using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenDicom.Gdt;
using OpenDicom.Storage;

namespace OpenDicom.Dicom;

/// <summary>
/// Kombinierter DICOM-SCP: Implementiert C-FIND (Modality Worklist) und C-STORE.
/// Eine Instanz pro Clientverbindung (fo-dicom erzeugt eine neue Instanz pro Association).
/// </summary>
public sealed class CombinedDicomScp : DicomService,
    IDicomServiceProvider,
    IDicomCFindProvider,
    IDicomCStoreProvider
{
    private static readonly DicomTransferSyntax[] AcceptedTransferSyntaxes =
    {
        DicomTransferSyntax.ExplicitVRLittleEndian,
        DicomTransferSyntax.ImplicitVRLittleEndian,
        DicomTransferSyntax.ExplicitVRBigEndian,
    };

    // Statische Dependencies – werden einmalig von DicomServerService gesetzt
    private static WorklistStore? _worklistStore;
    private static AppSettings? _appSettings;
    private static ILogger? _staticLogger;

    internal static void SetDependencies(
        WorklistStore worklistStore,
        AppSettings appSettings,
        ILogger logger)
    {
        _worklistStore = worklistStore;
        _appSettings = appSettings;
        _staticLogger = logger;
    }

    public CombinedDicomScp(
        INetworkStream stream,
        Encoding fallbackEncoding,
        ILogger logger,
        DicomServiceDependencies dependencies)
        : base(stream, fallbackEncoding, logger, dependencies) { }

    // =====================================================================
    //  IDicomServiceProvider
    // =====================================================================

    public async Task OnReceiveAssociationRequestAsync(DicomAssociation association)
    {
        _staticLogger?.LogDebug(
            "Association request from AE: {CallingAE} → {CalledAE}",
            association.CallingAE, association.CalledAE);

        foreach (DicomPresentationContext pc in association.PresentationContexts)
        {
            // Modality Worklist C-FIND
            if (pc.AbstractSyntax == DicomUID.ModalityWorklistInformationModelFind)
            {
                pc.AcceptTransferSyntaxes(AcceptedTransferSyntaxes);
                continue;
            }

            // Alle Storage SOP Classes annehmen
            if (pc.AbstractSyntax.StorageCategory != DicomStorageCategory.None)
            {
                pc.AcceptTransferSyntaxes(AcceptedTransferSyntaxes);
                continue;
            }

            pc.SetResult(DicomPresentationContextResult.RejectAbstractSyntaxNotSupported);
        }

        await SendAssociationAcceptAsync(association);
    }

    public async Task OnReceiveAssociationReleaseRequestAsync()
    {
        await SendAssociationReleaseResponseAsync();
    }

    public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason)
    {
        _staticLogger?.LogWarning("DICOM Association aborted. Source={Source} Reason={Reason}",
            source, reason);
    }

    public void OnConnectionClosed(Exception? exception)
    {
        if (exception != null)
            _staticLogger?.LogDebug(exception, "DICOM connection closed with exception.");
    }

    // =====================================================================
    //  IDicomCFindProvider – Modality Worklist
    // =====================================================================

    public async IAsyncEnumerable<DicomCFindResponse> OnCFindRequestAsync(DicomCFindRequest request)
    {
        if (_worklistStore == null || _appSettings == null)
        {
            yield return new DicomCFindResponse(request, DicomStatus.ProcessingFailure);
            yield break;
        }

        _staticLogger?.LogInformation("C-FIND Worklist query received.");

        IEnumerable<WorklistEntry> matches = _worklistStore.FindEntries(request.Dataset);
        int count = 0;

        foreach (WorklistEntry entry in matches)
        {
            DicomDataset dataset = entry.ToWorklistDataset(_appSettings.Dicom.AeTitle);
            _staticLogger?.LogDebug(
                "C-FIND match: AccessionNumber={Acc} Patient={Name}",
                entry.AccessionNumber, entry.PatientName);

            yield return new DicomCFindResponse(request, DicomStatus.Pending) { Dataset = dataset };
            count++;
            await Task.Yield(); // Erlaubt async-Interleaving
        }

        _staticLogger?.LogInformation("C-FIND completed. Matches returned: {Count}", count);
        yield return new DicomCFindResponse(request, DicomStatus.Success);
    }

    // =====================================================================
    //  IDicomCStoreProvider – Bildempfang
    // =====================================================================

    public async Task<DicomCStoreResponse> OnCStoreRequestAsync(DicomCStoreRequest request)
    {
        if (_worklistStore == null || _appSettings == null)
            return new DicomCStoreResponse(request, DicomStatus.ProcessingFailure);

        try
        {
            string patientId = GetStringOrEmpty(request.Dataset, DicomTag.PatientID);
            string accessionNumber = GetStringOrEmpty(request.Dataset, DicomTag.AccessionNumber);
            string sopInstanceUid = request.SOPInstanceUID?.UID ?? DicomUID.Generate().UID;
            string modalityStr = GetStringOrEmpty(request.Dataset, DicomTag.Modality);

            // Speicherpfad: storage/{PatientId}/{Datum}/{SopInstanceUid}.dcm
            string datePart = DateTime.Today.ToString("yyyyMMdd");
            string safePatientId = SanitizeFolder(string.IsNullOrWhiteSpace(patientId) ? "UNKNOWN" : patientId);
            string storageDir = Path.Combine(
                _appSettings.Paths.DicomStorageFolder,
                safePatientId,
                datePart);

            Directory.CreateDirectory(storageDir);
            string dcmFilePath = Path.Combine(storageDir, $"{sopInstanceUid}.dcm");

            await request.File.SaveAsync(dcmFilePath);

            // DICOM-Pixel als PNG extrahieren (für GDT FK 8132 und direkte Betrachtung)
            string imagePath = SaveDicomAsPng(request.Dataset, dcmFilePath) ?? dcmFilePath;

            _staticLogger?.LogInformation(
                "C-STORE: Saved DICOM file. PatientId={PId} AccessionNumber={Acc} File={Path} Image={Img}",
                patientId, accessionNumber, dcmFilePath, imagePath);

            // Worklist-Eintrag suchen und GDT-Antwort schreiben
            WorklistEntry? entry = null;
            if (!string.IsNullOrWhiteSpace(accessionNumber))
                entry = _worklistStore.FindByAccession(accessionNumber);

            entry ??= _worklistStore.FindByPatientId(patientId);

            if (entry != null)
            {
                entry.MarkCompleted(dcmFilePath);
                WriteGdtResponse(entry, imagePath);  // GDT zeigt auf PNG, nicht DCM
            }
            else
            {
                _staticLogger?.LogWarning(
                    "No matching worklist entry found for PatientId={PId} AccessionNumber={Acc}. " +
                    "No GDT response written.",
                    patientId, accessionNumber);
            }

            return new DicomCStoreResponse(request, DicomStatus.Success);
        }
        catch (Exception ex)
        {
            _staticLogger?.LogError(ex, "C-STORE request failed.");
            return new DicomCStoreResponse(request, DicomStatus.ProcessingFailure);
        }
    }

    public Task OnCStoreRequestExceptionAsync(string tempFileName, Exception e)
    {
        _staticLogger?.LogError(e, "C-STORE exception for temp file: {TempFile}", tempFileName);
        try { if (File.Exists(tempFileName)) File.Delete(tempFileName); } catch { /* ignore */ }
        return Task.CompletedTask;
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private void WriteGdtResponse(WorklistEntry entry, string dcmFilePath)
    {
        try
        {
            string outputPath = GdtWriter.Write(
                entry.SourceGdtRecord,
                dcmFilePath,
                _appSettings!.Paths.GdtOutputFolder,
                _appSettings.Dicom.AeTitle);

            _staticLogger?.LogInformation(
                "GDT response written: {GdtPath}", outputPath);
        }
        catch (Exception ex)
        {
            _staticLogger?.LogError(ex, "Failed to write GDT response for patient {PId}.",
                entry.PatientId);
        }
    }

    private static string GetStringOrEmpty(DicomDataset dataset, DicomTag tag)
    {
        try { return dataset.GetString(tag) ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string SanitizeFolder(string input)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(input.Select(c => invalid.Contains(c) ? '_' : c));
    }

    /// <summary>
    /// Extrahiert den ersten Frame aus einem DICOM-Datensatz und speichert ihn als PNG.
    /// Unterstützt 8-Bit-Graustufen, 16-Bit-Graustufen (normalisiert) und 24-Bit-RGB.
    /// Gibt den PNG-Pfad zurück, oder null bei Fehler.
    /// </summary>
    private static string? SaveDicomAsPng(DicomDataset dataset, string dcmFilePath)
    {
        try
        {
            var pixelData = DicomPixelData.Create(dataset);
            if (pixelData.NumberOfFrames == 0) return null;

            int rows           = dataset.GetValue<int>(DicomTag.Rows, 0);
            int cols           = dataset.GetValue<int>(DicomTag.Columns, 0);
            int bitsAllocated  = dataset.GetValue<int>(DicomTag.BitsAllocated, 0);
            int samplesPerPixel= dataset.GetValue<int>(DicomTag.SamplesPerPixel, 0);
            int planarConfig   = dataset.Contains(DicomTag.PlanarConfiguration)
                                     ? dataset.GetValue<int>(DicomTag.PlanarConfiguration, 0)
                                     : 0;
            if (rows <= 0 || cols <= 0) return null;

            byte[] frameBytes = pixelData.GetFrame(0).Data;
            string pngPath    = Path.ChangeExtension(dcmFilePath, ".png");

            Bitmap bmp;

            if (samplesPerPixel == 1 && bitsAllocated == 8)
            {
                // ── 8-Bit Graustufen ──────────────────────────────────────
                bmp = new Bitmap(cols, rows, PixelFormat.Format8bppIndexed);
                var palette = bmp.Palette;
                for (int i = 0; i < 256; i++)
                    palette.Entries[i] = Color.FromArgb(i, i, i);
                bmp.Palette = palette;

                var bd = bmp.LockBits(new Rectangle(0, 0, cols, rows),
                                      ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
                int stride = bd.Stride;
                byte[] buf = new byte[rows * stride];
                for (int r = 0; r < rows; r++)
                    Array.Copy(frameBytes, r * cols, buf, r * stride, cols);
                Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
                bmp.UnlockBits(bd);
            }
            else if (samplesPerPixel == 1 && bitsAllocated == 16)
            {
                // ── 16-Bit Graustufen → auf 8 Bit normalisieren ───────────
                int pixelCount = rows * cols;
                ushort[] raw16 = new ushort[pixelCount];
                Buffer.BlockCopy(frameBytes, 0, raw16, 0, Math.Min(frameBytes.Length, pixelCount * 2));

                ushort min = raw16[0], max = raw16[0];
                foreach (ushort v in raw16) { if (v < min) min = v; if (v > max) max = v; }
                float range = max > min ? max - min : 1f;

                bmp = new Bitmap(cols, rows, PixelFormat.Format8bppIndexed);
                var palette = bmp.Palette;
                for (int i = 0; i < 256; i++)
                    palette.Entries[i] = Color.FromArgb(i, i, i);
                bmp.Palette = palette;

                var bd = bmp.LockBits(new Rectangle(0, 0, cols, rows),
                                      ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
                int stride = bd.Stride;
                byte[] buf = new byte[rows * stride];
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                        buf[r * stride + c] = (byte)((raw16[r * cols + c] - min) / range * 255f);
                Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
                bmp.UnlockBits(bd);
            }
            else if (samplesPerPixel == 3 && bitsAllocated == 8)
            {
                // ── 24-Bit RGB (interleaved oder planar) → BGR für GDI+ ───
                bmp = new Bitmap(cols, rows, PixelFormat.Format24bppRgb);
                var bd = bmp.LockBits(new Rectangle(0, 0, cols, rows),
                                      ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                int stride = bd.Stride;
                byte[] buf = new byte[rows * stride];

                if (planarConfig == 0) // interleaved RGBRGB...
                {
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                        {
                            int s = (r * cols + c) * 3;
                            int d = r * stride + c * 3;
                            buf[d]     = frameBytes[s + 2]; // B
                            buf[d + 1] = frameBytes[s + 1]; // G
                            buf[d + 2] = frameBytes[s];     // R
                        }
                }
                else // planar RRR...GGG...BBB...
                {
                    int plane = rows * cols;
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                        {
                            int i = r * cols + c;
                            int d = r * stride + c * 3;
                            buf[d]     = frameBytes[plane * 2 + i]; // B
                            buf[d + 1] = frameBytes[plane     + i]; // G
                            buf[d + 2] = frameBytes[i];             // R
                        }
                }
                Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
                bmp.UnlockBits(bd);
            }
            else
            {
                return null; // Unbekanntes Format
            }

            bmp.Save(pngPath, ImageFormat.Png);
            bmp.Dispose();
            return pngPath;
        }
        catch (Exception ex)
        {
            _staticLogger?.LogWarning(ex, "Could not extract image from DICOM to PNG.");
            return null;
        }
    }
}
