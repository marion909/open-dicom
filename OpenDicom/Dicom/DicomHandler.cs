using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenDicom.DicomCore;
using OpenDicom.Gdt;
using OpenDicom.Storage;

namespace OpenDicom.Dicom;

/// <summary>
/// Handles DICOM C-FIND (Modality Worklist) and C-STORE requests.
/// This is the protocol-independent DICOM application layer.
/// </summary>
public sealed class DicomHandler
{
    private readonly WorklistStore _worklistStore;
    private readonly AppSettings   _settings;
    private readonly ILogger<DicomHandler> _log;

    public DicomHandler(
        WorklistStore worklistStore,
        IOptions<AppSettings> settings,
        ILogger<DicomHandler> log)
    {
        _worklistStore = worklistStore;
        _settings      = settings.Value;
        _log           = log;
    }

    // ── C-FIND ───────────────────────────────────────────────────────────

    /// <summary>
    /// Execute a Modality Worklist C-FIND query.
    /// Returns one DicomDataset per matching worklist entry.
    /// </summary>
    public IReadOnlyList<DicomDataset> HandleCFind(DicomDataset query)
    {
        _log.LogInformation("C-FIND Worklist query received");
        var entries = _worklistStore.FindEntries(query);
        var results = new List<DicomDataset>();
        foreach (var entry in entries)
        {
            results.Add(entry.ToWorklistDataset(_settings.Dicom.AeTitle));
            _log.LogDebug("C-FIND match: AccessionNumber={Acc} Patient={Name}",
                entry.AccessionNumber, entry.PatientName);
        }
        _log.LogInformation("C-FIND completed. Matches returned: {Count}", results.Count);
        return results;
    }

    // ── C-STORE ──────────────────────────────────────────────────────────

    /// <summary>
    /// Persist a received DICOM dataset, optionally write a GDT response.
    /// </summary>
    public void HandleCStore(string sopClassUid, string sopInstanceUid, DicomDataset dataset)
    {
        try
        {
            string patientId      = dataset.GetString(DicomTag.PatientID)      ?? "";
            string accessionNumber = dataset.GetString(DicomTag.AccessionNumber) ?? "";

            // Build storage path: storage/{PatientId}/{yyyyMMdd}/{sopInstanceUid}.dcm
            string datePart       = DateTime.Today.ToString("yyyyMMdd");
            string safePatientId  = SanitizeFolder(string.IsNullOrWhiteSpace(patientId) ? "UNKNOWN" : patientId);
            string storageDir     = Path.Combine(
                _settings.Paths.DicomStorageFolder, safePatientId, datePart);

            Directory.CreateDirectory(storageDir);
            string dcmPath = Path.Combine(storageDir, $"{sopInstanceUid}.dcm");

            // Write Part-10 DICOM file
            using (var fs = new FileStream(dcmPath, FileMode.Create, FileAccess.Write, FileShare.None))
                DicomFileWriter.Write(fs, sopClassUid, sopInstanceUid, dataset);

            // Extract PNG for GDT FK 8132
            string imagePath = SaveDicomAsPng(dataset, dcmPath) ?? dcmPath;

            _log.LogInformation(
                "C-STORE: saved DICOM file. PatientId={PId} AccessionNumber={Acc} File={Path} Image={Img}",
                patientId, accessionNumber, dcmPath, imagePath);

            // Match worklist entry
            WorklistEntry? entry = null;
            if (!string.IsNullOrWhiteSpace(accessionNumber))
                entry = _worklistStore.FindByAccession(accessionNumber);
            entry ??= _worklistStore.FindByPatientId(patientId);

            if (entry != null)
            {
                entry.MarkCompleted(dcmPath);
                WriteGdtResponse(entry, imagePath);
            }
            else
            {
                _log.LogWarning(
                    "No matching worklist entry for PatientId={PId} AccessionNumber={Acc} — no GDT response",
                    patientId, accessionNumber);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "C-STORE failed for SOPInstance={Inst}", sopInstanceUid);
        }
    }

    // ── GDT response ─────────────────────────────────────────────────────

    private void WriteGdtResponse(WorklistEntry entry, string imagePath)
    {
        try
        {
            string outputPath = GdtWriter.Write(
                entry.SourceGdtRecord,
                imagePath,
                _settings.Paths.GdtOutputFolder,
                _settings.Dicom.AeTitle);
            _log.LogInformation("GDT response written: {GdtPath}", outputPath);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to write GDT response for patient {PId}", entry.PatientId);
        }
    }

    // ── PNG extraction ───────────────────────────────────────────────────

    /// <summary>
    /// Extract the first frame from the DICOM pixel data and save as PNG.
    /// Supports 8-bit grayscale, 16-bit grayscale (normalised), and 24-bit RGB.
    /// Returns the PNG path, or null on failure.
    /// </summary>
    private string? SaveDicomAsPng(DicomDataset dataset, string dcmFilePath)
    {
        try
        {
            byte[]? pixelBytes = dataset.GetString(DicomTag.PixelData) != null
                ? null // string path only works for text; let's read raw
                : null;

            // Access raw pixel bytes from the OW/OB element
            var elements = dataset.Elements; // internal accessor
            var pixelEl  = elements.FirstOrDefault(e => e.Tag == DicomTag.PixelData);
            if (pixelEl == null) return null;
            byte[] frameBytes = pixelEl.Value;
            if (frameBytes.Length == 0) return null;

            int rows            = dataset.GetUS(DicomTag.Rows);
            int cols            = dataset.GetUS(DicomTag.Columns);
            int bitsAllocated   = dataset.GetUS(DicomTag.BitsAllocated);
            int samplesPerPixel = dataset.GetUS(DicomTag.SamplesPerPixel);
            int planarConfig    = dataset.GetUS(DicomTag.PlanarConfiguration);

            if (rows <= 0 || cols <= 0) return null;

            string pngPath = Path.ChangeExtension(dcmFilePath, ".png");
            Bitmap bmp;

            if (samplesPerPixel == 1 && bitsAllocated == 8)
            {
                bmp = new Bitmap(cols, rows, PixelFormat.Format8bppIndexed);
                var palette = bmp.Palette;
                for (int i = 0; i < 256; i++)
                    palette.Entries[i] = Color.FromArgb(i, i, i);
                bmp.Palette = palette;

                var bd     = bmp.LockBits(new Rectangle(0, 0, cols, rows),
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

                var bd     = bmp.LockBits(new Rectangle(0, 0, cols, rows),
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
                bmp = new Bitmap(cols, rows, PixelFormat.Format24bppRgb);
                var bd     = bmp.LockBits(new Rectangle(0, 0, cols, rows),
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
                return null; // unknown format
            }

            bmp.Save(pngPath, ImageFormat.Png);
            bmp.Dispose();
            return pngPath;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not extract PNG from DICOM pixel data");
            return null;
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string SanitizeFolder(string input)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(input.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
