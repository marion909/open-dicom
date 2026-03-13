using System.Text;

namespace OpenDicom.DicomCore;

/// <summary>
/// Writes a DICOM Part 10 file (preamble + DICM + File Meta + dataset).
/// The payload dataset is stored with Explicit VR Little Endian transfer syntax.
/// </summary>
public static class DicomFileWriter
{
    private static readonly byte[] DicmMagic = Encoding.ASCII.GetBytes("DICM");

    /// <summary>
    /// Write a DICOM file to <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">Target (file) stream.</param>
    /// <param name="sopClassUid">SOP Class UID (e.g. CT Image Storage).</param>
    /// <param name="sopInstanceUid">SOP Instance UID.</param>
    /// <param name="dataset">
    ///   Patient / study / image dataset — must NOT include Group 0002 tags.
    ///   Pixel data should already be present as OB or OW element.
    /// </param>
    public static void Write(Stream stream, string sopClassUid, string sopInstanceUid,
        DicomDataset dataset)
    {
        using var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // 128-byte preamble (all zeroes)
        w.Write(new byte[128]);

        // "DICM" magic
        w.Write(DicmMagic);

        // File Meta Information (Group 0002) — always Explicit VR LE
        byte[] meta = BuildFileMeta(sopClassUid, sopInstanceUid);
        w.Write(meta);

        // Dataset (Explicit VR LE)
        byte[] dsBytes = dataset.ToBytes();
        w.Write(dsBytes);

        w.Flush();
    }

    // ── File Meta builder ────────────────────────────────────────────────

    private static byte[] BuildFileMeta(string sopClassUid, string sopInstanceUid)
    {
        // Build inner tags first so we know the total length
        using var inner = new MemoryStream();
        using var iw    = new BinaryWriter(inner, Encoding.ASCII, leaveOpen: true);

        // (0002,0001) FileMetaInformationVersion  = OB \x00\x01
        WriteExplicitTag(iw, DicomTag.FileMetaInformationVersion,
            DicomVR.OB, new byte[] { 0x00, 0x01 });

        // (0002,0002) MediaStorageSOPClassUID
        WriteExplicitTag(iw, DicomTag.MediaStorageSOPClassUID,
            DicomVR.UI, AsciiBytes(PadUid(sopClassUid)));

        // (0002,0003) MediaStorageSOPInstanceUID
        WriteExplicitTag(iw, DicomTag.MediaStorageSOPInstanceUID,
            DicomVR.UI, AsciiBytes(PadUid(sopInstanceUid)));

        // (0002,0010) TransferSyntaxUID  = Explicit VR Little Endian
        WriteExplicitTag(iw, DicomTag.TransferSyntaxUID,
            DicomVR.UI, AsciiBytes(PadUid(DicomUids.ExplicitVRLittleEndian)));

        // (0002,0012) ImplementationClassUID
        WriteExplicitTag(iw, DicomTag.ImplementationClassUID,
            DicomVR.UI, AsciiBytes(PadUid(DicomUids.ImplementationClassUID)));

        // (0002,0013) ImplementationVersionName
        WriteExplicitTag(iw, DicomTag.ImplementationVersionName,
            DicomVR.SH, AsciiBytes(DicomUids.ImplementationVersionName));

        iw.Flush();
        byte[] innerBytes = inner.ToArray();

        // Now write the Group Length tag (0002,0000) = UL value = innerBytes.Length
        using var outer = new MemoryStream();
        using var ow    = new BinaryWriter(outer, Encoding.ASCII, leaveOpen: true);

        WriteExplicitTag(ow, DicomTag.FileMetaInformationGroupLength,
            DicomVR.UL, BitConverter.GetBytes((uint)innerBytes.Length));
        ow.Write(innerBytes);
        ow.Flush();
        return outer.ToArray();
    }

    private static void WriteExplicitTag(BinaryWriter w, DicomTag tag, string vr, byte[] value)
    {
        w.Write(tag.Group);
        w.Write(tag.Element);

        byte[] vrBytes = Encoding.ASCII.GetBytes(vr.PadRight(2).Substring(0, 2));
        w.Write(vrBytes);

        bool longVr = vr is DicomVR.OB or DicomVR.OW or "UN";
        byte[] paddedValue = value;
        if (value.Length % 2 != 0)
        {
            paddedValue = new byte[value.Length + 1];
            Array.Copy(value, paddedValue, value.Length);
            // UI gets NUL padding; others space
            paddedValue[value.Length] = vr == DicomVR.UI ? (byte)0x00 : (byte)0x20;
        }

        if (longVr)
        {
            w.Write((ushort)0);
            w.Write((uint)paddedValue.Length);
        }
        else
        {
            w.Write((ushort)paddedValue.Length);
        }

        w.Write(paddedValue);
    }

    // UI values: if odd length, append NUL (already handled in WriteExplicitTag)
    private static string PadUid(string uid) => uid;

    private static byte[] AsciiBytes(string s) => Encoding.ASCII.GetBytes(s);
}
