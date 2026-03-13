using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using OpenDicom.DicomCore;
using OpenDicom.Dicom;

namespace OpenDicom.Network;

/// <summary>
/// Handles a single DICOM TCP connection: performs UL association handshake,
/// receives P-DATA fragments, reassembles DIMSE commands, and dispatches
/// to DicomHandler for C-FIND, C-STORE, and C-ECHO.
/// </summary>
internal sealed class DicomConnection
{
    // DIMSE command fields (0000 group)
    private const uint CommandFieldCFind  = 0x0020;
    private const uint CommandFieldCStore = 0x0001;
    private const uint CommandFieldCEcho  = 0x0030;
    private const uint CommandFieldCFindRsp  = 0x8020;
    private const uint CommandFieldCStoreRsp = 0x8001;
    private const uint CommandFieldCEchoRsp  = 0x8030;
    private const uint CommandDataSetNone    = 0x0101;

    private readonly TcpClient   _client;
    private readonly DicomHandler _handler;
    private readonly ILogger     _log;

    // Association state
    private uint _maxSendLength = 65536;
    // pcId → accepted transfer syntax
    private readonly Dictionary<byte, string> _acceptedPCs = new();

    public DicomConnection(TcpClient client, DicomHandler handler, ILogger log)
    {
        _client  = client;
        _handler = handler;
        _log     = log;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var remote = _client.Client.RemoteEndPoint;
        _log.LogInformation("DICOM connection from {Remote}", remote);
        try
        {
            await using var stream = _client.GetStream();
            await HandleConnection(stream, ct);
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "DICOM connection from {Remote} closed with error", remote);
        }
        finally
        {
            _client.Dispose();
            _log.LogInformation("DICOM connection from {Remote} closed", remote);
        }
    }

    private async Task HandleConnection(NetworkStream stream, CancellationToken ct)
    {
        // ── Association negotiation ─────────────────────────────────────
        var pdu = await Pdu.ReadAsync(stream, ct);
        if (pdu == null || pdu.Value.Type != Pdu.AssociateRq)
        {
            _log.LogWarning("Expected A-ASSOCIATE-RQ, got {Type}", pdu?.Type);
            return;
        }

        var rq = AssociatePdu.ParseRQ(pdu.Value.Data);
        _log.LogInformation("A-ASSOCIATE-RQ from {Calling} → {Called}",
            rq.CallingAe, rq.CalledAe);
        _maxSendLength = rq.MaxPduLength > 0 ? rq.MaxPduLength : 65536;

        var accepted = new List<AcceptedContext>();
        foreach (var pc in rq.PresentationContexts)
        {
            string? ts = NegotiateTransferSyntax(pc);
            if (ts != null)
            {
                accepted.Add(new AcceptedContext { PcId = pc.Id, TransferSyntaxUid = ts });
                _acceptedPCs[pc.Id] = ts;
                _log.LogInformation("Accepted PC {PcId} {SopClass} with {Ts}",
                    pc.Id, pc.AbstractSyntaxUid, ts);
            }
        }

        if (accepted.Count == 0)
        {
            _log.LogWarning("No presentation contexts accepted — rejecting association");
            await Pdu.WriteAsync(stream, Pdu.AssociateRj, AssociatePdu.BuildRJ(), ct);
            return;
        }

        byte[] acPayload = AssociatePdu.BuildAC(rq.CalledAe, rq.CallingAe, accepted);
        await Pdu.WriteAsync(stream, Pdu.AssociateAc, acPayload, ct);
        _log.LogInformation("Association accepted with {Count} presentation context(s)", accepted.Count);

        // ── PDU loop ────────────────────────────────────────────────────
        // Per-PC reassembly state: track fragments and "last received" flags
        var cmdBufs  = new Dictionary<byte, List<byte>>();
        var dataBufs = new Dictionary<byte, List<byte>>();
        var cmdDone  = new HashSet<byte>(); // pcIds where last command fragment received
        var dataDone = new HashSet<byte>(); // pcIds where last dataset fragment received

        while (!ct.IsCancellationRequested)
        {
            var pkt = await Pdu.ReadAsync(stream, ct);
            if (pkt == null) break;

            if (pkt.Value.Type == Pdu.ReleaseRq)
            {
                await Pdu.WriteAsync(stream, Pdu.ReleaseRp, AssociatePdu.BuildReleaseRp(), ct);
                break;
            }

            if (pkt.Value.Type == Pdu.Abort) break;

            if (pkt.Value.Type != Pdu.PData) continue;

            foreach (var (pcId, fragment, isCommand, isLast) in Pdu.ParsePDataItems(pkt.Value.Data))
            {
                if (isCommand)
                {
                    if (!cmdBufs.ContainsKey(pcId)) cmdBufs[pcId] = new List<byte>();
                    cmdBufs[pcId].AddRange(fragment);
                    if (isLast) cmdDone.Add(pcId);
                }
                else
                {
                    if (!dataBufs.ContainsKey(pcId)) dataBufs[pcId] = new List<byte>();
                    dataBufs[pcId].AddRange(fragment);
                    if (isLast) dataDone.Add(pcId);
                }
            }

            // Dispatch any command whose last fragment has arrived
            foreach (byte pcId in cmdDone.ToList())
            {
                if (!cmdBufs.TryGetValue(pcId, out var cmdBuf)) continue;

                DicomDataset cmd;
                try { cmd = DicomDataset.FromBytes(cmdBuf.ToArray(), implicitVR: true); }
                catch { cmdBufs.Remove(pcId); cmdDone.Remove(pcId); continue; }

                // CommandDataSetType 0x0101 = no dataset; anything else = dataset follows
                ushort dataSetType = cmd.GetUS(DicomTag.CommandDataSetType);
                bool hasDataset    = dataSetType != CommandDataSetNone;

                // If dataset expected but its last fragment not yet received, wait
                if (hasDataset && !dataDone.Contains(pcId)) continue;

                // Ready — clean up reassembly state
                cmdBufs.Remove(pcId);
                cmdDone.Remove(pcId);

                DicomDataset? dataset = null;
                if (hasDataset && dataBufs.TryGetValue(pcId, out var dBuf))
                {
                    bool implicitVr = _acceptedPCs.TryGetValue(pcId, out string? ts)
                        && ts == DicomUids.ImplicitVRLittleEndian;
                    dataset = DicomDataset.FromBytes(dBuf.ToArray(), implicitVr);
                    dataBufs.Remove(pcId);
                    dataDone.Remove(pcId);
                }

                ushort cmdField = cmd.GetUS(DicomTag.CommandField);
                try
                {
                    await DispatchDimse(stream, pcId, cmd, dataset, ct);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error handling DIMSE command 0x{Cmd:X4}", cmdField);
                }
            }
        }
    }

    // ── DIMSE dispatch ───────────────────────────────────────────────────

    private async Task DispatchDimse(NetworkStream stream, byte pcId,
        DicomDataset cmd, DicomDataset? dataset, CancellationToken ct)
    {
        uint cmdField = cmd.GetUS(DicomTag.CommandField);
        uint msgId    = cmd.GetUS(DicomTag.MessageID);

        switch (cmdField)
        {
            case CommandFieldCEcho:
                _log.LogInformation("C-ECHO request (msg {MsgId})", msgId);
                await SendCEchoResponse(stream, pcId, msgId, ct);
                break;

            case CommandFieldCFind:
                string cfindSopClass = cmd.GetString(DicomTag.AffectedSOPClassUID) ?? "";
                _log.LogInformation("C-FIND request SOP={SopClass} msg={MsgId}", cfindSopClass, msgId);
                if (dataset != null)
                    await SendCFindResponses(stream, pcId, msgId, dataset, ct);
                break;

            case CommandFieldCStore:
                string sopClass    = cmd.GetString(DicomTag.AffectedSOPClassUID)    ?? "";
                string sopInstance = cmd.GetString(DicomTag.AffectedSOPInstanceUID) ?? "";
                _log.LogInformation("C-STORE {SopClass} / {SopInst}", sopClass, sopInstance);
                if (dataset != null)
                    _handler.HandleCStore(sopClass, sopInstance, dataset);
                await SendCStoreResponse(stream, pcId, msgId, sopInstance, ct);
                break;

            default:
                _log.LogWarning("Unsupported DIMSE command 0x{Cmd:X4}", cmdField);
                break;
        }
    }

    // ── C-ECHO ───────────────────────────────────────────────────────────

    private async Task SendCEchoResponse(NetworkStream stream, byte pcId, uint msgId, CancellationToken ct)
    {
        var rsp = new DicomDataset();
        rsp.Add(DicomTag.AffectedSOPClassUID,  DicomVR.UI, DicomUids.VerificationSOPClass);
        rsp.AddUS(DicomTag.CommandField,        (ushort)CommandFieldCEchoRsp);
        rsp.AddUS(DicomTag.MessageIDBeingRespondedTo, (ushort)msgId);
        rsp.AddUS(DicomTag.CommandDataSetType,  (ushort)CommandDataSetNone);
        rsp.AddUS(DicomTag.Status,              0x0000); // Success
        await SendCommandAsync(stream, pcId, rsp, ct);
    }

    // ── C-FIND ───────────────────────────────────────────────────────────

    private async Task SendCFindResponses(NetworkStream stream, byte pcId, uint msgId,
        DicomDataset query, CancellationToken ct)
    {
        var results = _handler.HandleCFind(query);
        foreach (var result in results)
        {
            // Pending response
            var rsp = BuildCFindRsp(msgId, 0xFF00 /*Pending*/);
            await SendCommandAsync(stream, pcId, rsp, ct);
            await SendDatasetAsync(stream, pcId, result, ct);
        }

        // Final success
        var final = BuildCFindRsp(msgId, 0x0000 /*Success*/);
        await SendCommandAsync(stream, pcId, final, ct);
    }

    private static DicomDataset BuildCFindRsp(uint msgId, ushort status)
    {
        var rsp = new DicomDataset();
        rsp.Add(DicomTag.AffectedSOPClassUID,  DicomVR.UI, DicomUids.ModalityWorklistFind);
        rsp.AddUS(DicomTag.CommandField,       (ushort)CommandFieldCFindRsp);
        rsp.AddUS(DicomTag.MessageIDBeingRespondedTo, (ushort)msgId);
        // 0xFF00 = Pending (has dataset), 0x0000 = Success (no dataset)
        rsp.AddUS(DicomTag.CommandDataSetType,
            status == 0xFF00 ? (ushort)0x0000 : (ushort)CommandDataSetNone);
        rsp.AddUS(DicomTag.Status, status);
        return rsp;
    }

    // ── C-STORE ──────────────────────────────────────────────────────────

    private async Task SendCStoreResponse(NetworkStream stream, byte pcId, uint msgId,
        string sopInstance, CancellationToken ct)
    {
        var rsp = new DicomDataset();
        rsp.Add(DicomTag.AffectedSOPClassUID,    DicomVR.UI, DicomUids.VerificationSOPClass);
        rsp.Add(DicomTag.AffectedSOPInstanceUID, DicomVR.UI, sopInstance);
        rsp.AddUS(DicomTag.CommandField,         (ushort)CommandFieldCStoreRsp);
        rsp.AddUS(DicomTag.MessageIDBeingRespondedTo, (ushort)msgId);
        rsp.AddUS(DicomTag.CommandDataSetType,   (ushort)CommandDataSetNone);
        rsp.AddUS(DicomTag.Status,               0x0000);
        await SendCommandAsync(stream, pcId, rsp, ct);
    }

    // ── PDU senders ──────────────────────────────────────────────────────

    private async Task SendCommandAsync(NetworkStream stream, byte pcId,
        DicomDataset cmd, CancellationToken ct)
    {
        byte[] bytes = cmd.ToBytes(); // ImplicitVR for command group (standard says Implicit)
        // But per DICOM PS3.7 §9.3, Command Datasets use Implicit VR — however
        // DicomDataset.ToBytes() writes Explicit VR LE. The command dataset MUST be
        // Implicit VR Little Endian, so we serialise it accordingly:
        bytes = SerializeImplicitVR(cmd);
        await SendFragmentsAsync(stream, pcId, bytes, isCommand: true, ct);
    }

    private async Task SendDatasetAsync(NetworkStream stream, byte pcId,
        DicomDataset ds, CancellationToken ct)
    {
        bool implicitVR = _acceptedPCs.TryGetValue(pcId, out string? ts)
            && ts == DicomUids.ImplicitVRLittleEndian;
        byte[] bytes = implicitVR ? ds.ToBytesImplicit() : ds.ToBytes();
        await SendFragmentsAsync(stream, pcId, bytes, isCommand: false, ct);
    }

    private async Task SendFragmentsAsync(NetworkStream stream, byte pcId,
        byte[] bytes, bool isCommand, CancellationToken ct)
    {
        int maxFragment  = (int)Math.Min(_maxSendLength - 6, 65530);
        int offset = 0;
        while (offset < bytes.Length)
        {
            int len    = Math.Min(maxFragment, bytes.Length - offset);
            bool isLast = offset + len >= bytes.Length;
            byte[] fragment = new byte[len];
            Array.Copy(bytes, offset, fragment, 0, len);
            byte[] item = Pdu.BuildPDataItem(pcId, fragment, isCommand, isLast);
            await Pdu.WriteAsync(stream, Pdu.PData, item, ct);
            offset += len;
        }
    }

    // ── Implicit VR serialiser for DIMSE command groups ──────────────────
    // DICOM PS3.7 §E.1: command datasets use Implicit VR LE regardless of transfer syntax.

    private static byte[] SerializeImplicitVR(DicomDataset ds)
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);
        foreach (var el in ds.Elements)
        {
            w.Write(el.Tag.Group);
            w.Write(el.Tag.Element);
            byte[] val = el.Value;
            if (val.Length % 2 != 0)
            {
                var padded = new byte[val.Length + 1];
                Array.Copy(val, padded, val.Length);
                val = padded;
            }
            w.Write((uint)val.Length);
            w.Write(val);
        }
        return ms.ToArray();
    }

    // ── Transfer syntax negotiation ──────────────────────────────────────

    private static string? NegotiateTransferSyntax(PresentationContext pc)
    {
        // Accept any SOP class (storage, worklist, echo) with Explicit VR LE preferred
        // then Implicit VR LE.
        foreach (string ts in pc.TransferSyntaxUids)
        {
            if (ts == DicomUids.ExplicitVRLittleEndian ||
                ts == DicomUids.ImplicitVRLittleEndian)
                return ts;
        }
        return null;
    }
}
