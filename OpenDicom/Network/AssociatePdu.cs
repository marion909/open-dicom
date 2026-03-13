using System.Text;
using OpenDicom.DicomCore;

namespace OpenDicom.Network;

/// <summary>
/// Parsed representation of an A-ASSOCIATE-RQ PDU
/// and builder for an A-ASSOCIATE-AC PDU.
///
/// A-ASSOCIATE-RQ layout (after the 6-byte PDU header):
///   [protocol-version:2BE][reserved:2][called-AE:16][calling-AE:16][reserved:32]
///   [variable items…]
///
/// Variable items use the sub-item format: [type:1][reserved:1][length:2BE][data:N]
/// </summary>
internal sealed class AssociatePdu
{
    // ── Parsed fields ────────────────────────────────────────────────────
    public string CalledAe   { get; private set; } = "";
    public string CallingAe  { get; private set; } = "";

    /// <summary>Requested presentation contexts: [{id, abstractSyntaxUid, [transferSyntaxUids]}]</summary>
    public List<PresentationContext> PresentationContexts { get; } = new();

    /// <summary>Max PDU size requested by the SCU.</summary>
    public uint MaxPduLength { get; private set; } = 65536;

    // ── Parse A-ASSOCIATE-RQ ─────────────────────────────────────────────

    public static AssociatePdu ParseRQ(byte[] data)
    {
        var pdu = new AssociatePdu();
        if (data.Length < 68) return pdu;

        // Protocol version is data[0..1], skip it
        pdu.CalledAe  = Encoding.ASCII.GetString(data,  4, 16).Trim();
        pdu.CallingAe = Encoding.ASCII.GetString(data, 20, 16).Trim();
        // 32 bytes reserved at offset 36

        int pos = 68; // start of variable items
        while (pos + 4 <= data.Length)
        {
            byte itemType = data[pos];
            // data[pos+1] reserved
            int itemLen = (data[pos + 2] << 8) | data[pos + 3];
            pos += 4;
            if (pos + itemLen > data.Length) break;

            switch (itemType)
            {
                case 0x10: // Application Context
                    break;

                case 0x20: // Presentation Context RQ
                    if (itemLen >= 4)
                    {
                        byte pcId = data[pos];
                        var pc = ParsePresentationContextRQ(data, pos, itemLen);
                        pc.Id = pcId;
                        pdu.PresentationContexts.Add(pc);
                    }
                    break;

                case 0x50: // User Information
                    ParseUserInfo(data, pos, itemLen, out uint maxPdu);
                    pdu.MaxPduLength = maxPdu;
                    break;
            }
            pos += itemLen;
        }
        return pdu;
    }

    private static PresentationContext ParsePresentationContextRQ(byte[] data, int start, int len)
    {
        var pc = new PresentationContext();
        // start+0 = pcId, start+1 reserved, start+2+3 reserved
        int pos = start + 4;
        int end = start + len;
        while (pos + 4 <= end)
        {
            byte subType = data[pos];
            int  subLen  = (data[pos + 2] << 8) | data[pos + 3];
            pos += 4;
            if (subType == 0x30) // Abstract Syntax
                pc.AbstractSyntaxUid = Encoding.ASCII.GetString(data, pos, subLen).Trim('\0', ' ');
            else if (subType == 0x40) // Transfer Syntax
                pc.TransferSyntaxUids.Add(Encoding.ASCII.GetString(data, pos, subLen).Trim('\0', ' '));
            pos += subLen;
        }
        return pc;
    }

    private static void ParseUserInfo(byte[] data, int start, int len, out uint maxPdu)
    {
        maxPdu = 65536;
        int pos = start;
        int end = start + len;
        while (pos + 4 <= end)
        {
            byte subType = data[pos];
            int  subLen  = (data[pos + 2] << 8) | data[pos + 3];
            pos += 4;
            if (subType == 0x51 && subLen >= 4) // Max PDU length
            {
                maxPdu = (uint)((data[pos]     << 24) | (data[pos + 1] << 16) |
                                (data[pos + 2] << 8)  |  data[pos + 3]);
            }
            pos += subLen;
        }
    }

    // ── Build A-ASSOCIATE-AC ─────────────────────────────────────────────

    /// <summary>
    /// Build an A-ASSOCIATE-AC payload (without the 6-byte PDU header).
    /// Accepted PCs get reason 0x00, rejected get reason 0x03.
    /// </summary>
    public static byte[] BuildAC(string calledAe, string callingAe,
        IReadOnlyList<AcceptedContext> accepted)
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        // Protocol version = 0x0001
        w.Write((byte)0x00); w.Write((byte)0x01);
        w.Write(new byte[2]); // reserved
        WriteAeTitle(w, calledAe,  16);
        WriteAeTitle(w, callingAe, 16);
        w.Write(new byte[32]); // reserved

        // Application Context item (0x10)
        WriteStringItem(w, 0x10, "1.2.840.10008.3.1.1.1");

        // Accepted Presentation Context items (0x21)
        foreach (var ac in accepted)
            WritePresentationContextAC(w, ac.PcId, 0x00, ac.TransferSyntaxUid);

        // User Information (0x50) — max PDU length
        WriteUserInfo(w, 0x40000); // 256 KB

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Build an A-ASSOCIATE-RJ payload.</summary>
    public static byte[] BuildRJ(byte result = 0x01, byte source = 0x01, byte reason = 0x01)
    {
        // result: 1=rejected permanent, source: 1=user, reason: 1=no reason
        return new byte[] { 0x00, result, source, reason };
    }

    // ── Build A-RELEASE-RP ───────────────────────────────────────────────
    public static byte[] BuildReleaseRp() => new byte[4]; // all zeroes

    // ── Helpers ──────────────────────────────────────────────────────────

    private static void WriteAeTitle(BinaryWriter w, string ae, int fieldLen)
    {
        byte[] buf = new byte[fieldLen];
        byte[] src = Encoding.ASCII.GetBytes(ae.PadRight(fieldLen));
        Array.Copy(src, buf, Math.Min(src.Length, fieldLen));
        w.Write(buf);
    }

    private static void WriteStringItem(BinaryWriter w, byte itemType, string value)
    {
        byte[] val = Encoding.ASCII.GetBytes(value);
        // Even-length padding
        if (val.Length % 2 != 0) val = val.Concat(new byte[] { 0x00 }).ToArray();
        w.Write(itemType);
        w.Write((byte)0); // reserved
        w.Write((byte)(val.Length >> 8));
        w.Write((byte)(val.Length));
        w.Write(val);
    }

    private static void WritePresentationContextAC(BinaryWriter w, byte pcId,
        byte result, string transferSyntaxUid)
    {
        // inner = [pcId:1][reserved:1][result:1][reserved:1] + transfer syntax sub-item
        using var inner = new MemoryStream();
        using var iw    = new BinaryWriter(inner, Encoding.ASCII, leaveOpen: true);
        iw.Write(pcId);
        iw.Write((byte)0);
        iw.Write(result);
        iw.Write((byte)0);

        byte[] tsVal = Encoding.ASCII.GetBytes(transferSyntaxUid);
        if (tsVal.Length % 2 != 0) tsVal = tsVal.Concat(new byte[] { 0x00 }).ToArray();
        iw.Write((byte)0x40); // Transfer Syntax sub-item
        iw.Write((byte)0);
        iw.Write((byte)(tsVal.Length >> 8));
        iw.Write((byte)(tsVal.Length));
        iw.Write(tsVal);

        iw.Flush();
        byte[] innerBytes = inner.ToArray();

        w.Write((byte)0x21); // PC-AC item type
        w.Write((byte)0);
        w.Write((byte)(innerBytes.Length >> 8));
        w.Write((byte)(innerBytes.Length));
        w.Write(innerBytes);
    }

    private static void WriteUserInfo(BinaryWriter w, uint maxPduLength)
    {
        // Max PDU length sub-item (0x51): 4 bytes
        using var inner = new MemoryStream();
        using var iw    = new BinaryWriter(inner, Encoding.ASCII, leaveOpen: true);
        iw.Write((byte)0x51);
        iw.Write((byte)0);
        iw.Write((byte)0x00); iw.Write((byte)0x04); // length = 4
        iw.Write((byte)(maxPduLength >> 24));
        iw.Write((byte)(maxPduLength >> 16));
        iw.Write((byte)(maxPduLength >> 8));
        iw.Write((byte)(maxPduLength));
        iw.Flush();
        byte[] innerBytes = inner.ToArray();

        w.Write((byte)0x50);
        w.Write((byte)0);
        w.Write((byte)(innerBytes.Length >> 8));
        w.Write((byte)(innerBytes.Length));
        w.Write(innerBytes);
    }
}

internal sealed class PresentationContext
{
    public byte   Id                   { get; set; }
    public string AbstractSyntaxUid    { get; set; } = "";
    public List<string> TransferSyntaxUids { get; } = new();
}

internal sealed class AcceptedContext
{
    public byte   PcId            { get; init; }
    public string TransferSyntaxUid { get; init; } = "";
}
