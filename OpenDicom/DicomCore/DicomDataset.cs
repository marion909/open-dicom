using System.Text;

namespace OpenDicom.DicomCore;

/// <summary>
/// An ordered list of DICOM data elements and sequences.
/// Supports reading Explicit VR Little Endian and Implicit VR Little Endian,
/// and writing Explicit VR Little Endian.
/// </summary>
public sealed class DicomDataset
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    // Ordered list preserves insertion order (important for DICOM sequences)
    private readonly List<DataElement> _elements = new();

    // ── Public API ───────────────────────────────────────────────────────

    /// <summary>Add or replace a string-valued element.</summary>
    public void Add(DicomTag tag, string vr, string value) =>
        Set(tag, vr, Utf8NoBom.GetBytes(value));

    /// <summary>Add or replace a US (uint16) element.</summary>
    public void AddUS(DicomTag tag, ushort value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Set(tag, DicomVR.US, bytes);
    }

    /// <summary>Add or replace an UL (uint32) element.</summary>
    public void AddUL(DicomTag tag, uint value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Set(tag, DicomVR.UL, bytes);
    }

    /// <summary>Add or replace an OB element (raw bytes).</summary>
    public void AddOB(DicomTag tag, byte[] value) => Set(tag, DicomVR.OB, value);

    /// <summary>Add or replace an OW element (raw bytes).</summary>
    public void AddOW(DicomTag tag, byte[] value) => Set(tag, DicomVR.OW, value);

    /// <summary>Add a sequence (tag + list of item-datasets).</summary>
    public void AddSequence(DicomTag tag, IEnumerable<DicomDataset> items) =>
        Set(tag, DicomVR.SQ, Array.Empty<byte>(), items.ToList());

    /// <summary>Add an empty sequence.</summary>
    public void AddEmptySequence(DicomTag tag) =>
        Set(tag, DicomVR.SQ, Array.Empty<byte>(), new List<DicomDataset>());

    public bool Contains(DicomTag tag) =>
        _elements.Any(e => e.Tag == tag);

    /// <summary>Returns null if tag not found.</summary>
    public string? GetString(DicomTag tag)
    {
        var el = _elements.FirstOrDefault(e => e.Tag == tag);
        if (el == null) return null;
        // Trim trailing null/space padding per DICOM standard
        return Utf8NoBom.GetString(el.Value).TrimEnd('\0', ' ');
    }

    public ushort GetUS(DicomTag tag)
    {
        var el = _elements.FirstOrDefault(e => e.Tag == tag);
        if (el == null || el.Value.Length < 2) return 0;
        return BitConverter.ToUInt16(el.Value, 0);
    }

    public int GetInt(DicomTag tag)
    {
        string? s = GetString(tag);
        return int.TryParse(s?.Trim(), out int v) ? v : 0;
    }

    /// <summary>Try to get sequence items.</summary>
    public bool TryGetSequence(DicomTag tag, out List<DicomDataset> items)
    {
        var el = _elements.FirstOrDefault(e => e.Tag == tag);
        if (el?.SequenceItems != null) { items = el.SequenceItems; return true; }
        items = new List<DicomDataset>();
        return false;
    }

    // ── Serialization ────────────────────────────────────────────────────

    /// <summary>
    /// Serialize this dataset as Explicit VR Little Endian bytes (no file meta).
    /// </summary>
    public byte[] ToBytes() => WriteDataset(this);

    private static byte[] WriteDataset(DicomDataset ds)
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
        foreach (var el in ds._elements)
            WriteElement(w, el);
        return ms.ToArray();
    }

    private static void WriteElement(BinaryWriter w, DataElement el)
    {
        w.Write(el.Tag.Group);
        w.Write(el.Tag.Element);

        if (el.VR == DicomVR.SQ)
        {
            // Write SQ with undefined length, items with undefined length
            w.Write(Encoding.ASCII.GetBytes(DicomVR.SQ));
            w.Write((ushort)0);                // reserved
            w.Write(0xFFFFFFFFu);              // undefined length
            if (el.SequenceItems != null)
            {
                foreach (var item in el.SequenceItems)
                {
                    // Item tag (FFFE,E000)
                    w.Write((ushort)0xFFFE);
                    w.Write((ushort)0xE000);
                    w.Write(0xFFFFFFFFu); // undefined item length

                    byte[] itemBytes = WriteDataset(item);
                    w.Write(itemBytes);

                    // Item delimitation (FFFE,E00D)
                    w.Write((ushort)0xFFFE);
                    w.Write((ushort)0xE00D);
                    w.Write(0u);
                }
            }
            // SQ delimitation (FFFE,E0DD)
            w.Write((ushort)0xFFFE);
            w.Write((ushort)0xE0DD);
            w.Write(0u);
            return;
        }

        // Explicit VR
        byte[] vrBytes = Encoding.ASCII.GetBytes(el.VR.PadRight(2).Substring(0, 2));
        w.Write(vrBytes);

        bool longVr = el.VR is DicomVR.OB or DicomVR.OW or DicomVR.UN or "UC" or "UR" or "UT";
        byte[] value = el.Value;
        // Even-length padding
        byte[] paddedValue = value;
        if (value.Length % 2 != 0)
        {
            paddedValue = new byte[value.Length + 1];
            Array.Copy(value, paddedValue, value.Length);
            paddedValue[value.Length] = el.VR is DicomVR.OB or DicomVR.OW ? (byte)0 : (byte)' ';
        }

        if (longVr)
        {
            w.Write((ushort)0);               // reserved
            w.Write((uint)paddedValue.Length);
        }
        else
        {
            w.Write((ushort)paddedValue.Length);
        }
        w.Write(paddedValue);
    }

    // ── Deserialization ──────────────────────────────────────────────────

    /// <summary>
    /// Parse a dataset from a byte array.
    /// If implicitVR = true, uses well-known tag VR dictionary for common tags.
    /// </summary>
    public static DicomDataset FromBytes(byte[] data, bool implicitVR = false)
    {
        using var ms = new MemoryStream(data);
        using var r  = new BinaryReader(ms);
        return ReadDataset(r, (long)data.Length, implicitVR);
    }

    public static DicomDataset ReadFromStream(Stream stream, long length, bool implicitVR)
    {
        using var r = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        return ReadDataset(r, length, implicitVR);
    }

    private static DicomDataset ReadDataset(BinaryReader r, long endOffset, bool implicitVR)
    {
        var ds = new DicomDataset();
        while (TryReadElement(r, endOffset, implicitVR, out var el) && el != null)
            ds._elements.Add(el);
        return ds;
    }

    private static bool TryReadElement(BinaryReader r, long endOffset, bool implicitVR,
        out DataElement? el)
    {
        el = null;
        if (r.BaseStream.Position + 4 > endOffset) return false;

        ushort group   = r.ReadUInt16();
        ushort element = r.ReadUInt16();
        var tag = new DicomTag(group, element);

        // Sequence/item delimiters — consume and signal caller
        if (group == 0xFFFE)
        {
            r.ReadUInt32(); // length (always 0)
            return true;   // return true but el = null → caller continues loop
        }

        string vr;
        uint length;

        if (implicitVR)
        {
            vr     = KnownVR(tag);
            length = r.ReadUInt32();
        }
        else
        {
            byte[] vrBytes = r.ReadBytes(2);
            vr = Encoding.ASCII.GetString(vrBytes);

            bool longVr = vr is DicomVR.OB or DicomVR.OW or "SQ" or "UC" or "UN" or "UR" or "UT";
            if (longVr)
            {
                r.ReadUInt16(); // reserved
                length = r.ReadUInt32();
            }
            else
            {
                length = r.ReadUInt16();
            }
        }

        if (vr == DicomVR.SQ || (length == 0xFFFFFFFF && vr == DicomVR.SQ))
        {
            // Read sequence items until SQ delimiter (FFFE,E0DD)
            var items = ReadSequenceItems(r, length, implicitVR);
            el = new DataElement(tag, DicomVR.SQ, Array.Empty<byte>(), items);
            return true;
        }

        if (length == 0xFFFFFFFF)
        {
            // Undefined-length non-SQ (e.g. encapsulated pixel data) – read until delimiter
            el = new DataElement(tag, vr, ReadUndefinedLength(r), null);
            return true;
        }

        byte[] value = length > 0 ? r.ReadBytes((int)length) : Array.Empty<byte>();
        el = new DataElement(tag, vr, value, null);
        return true;
    }

    private static List<DicomDataset> ReadSequenceItems(BinaryReader r, uint sqLength, bool implicitVR)
    {
        var items = new List<DicomDataset>();
        long sqEnd = sqLength == 0xFFFFFFFF
            ? long.MaxValue
            : r.BaseStream.Position + sqLength;

        while (r.BaseStream.Position < sqEnd && r.BaseStream.Position + 8 <= r.BaseStream.Length)
        {
            ushort g = r.ReadUInt16();
            ushort e = r.ReadUInt16();
            uint   l = r.ReadUInt32();

            if (g == 0xFFFE && e == 0xE0DD) break; // SQ delimiter

            if (g == 0xFFFE && e == 0xE000) // Item
            {
                if (l == 0xFFFFFFFF)
                {
                    var item = ReadDataset(r, long.MaxValue, implicitVR);
                    items.Add(item);
                    // marker consumed inside ReadDataset via FFFE,E00D delimiter
                }
                else
                {
                    long itemEnd = r.BaseStream.Position + l;
                    var item = ReadDataset(r, itemEnd, implicitVR);
                    items.Add(item);
                    r.BaseStream.Position = itemEnd; // skip unread item bytes
                }
            }
        }
        return items;
    }

    private static byte[] ReadUndefinedLength(BinaryReader r)
    {
        using var buf = new MemoryStream();
        while (r.BaseStream.Position + 4 <= r.BaseStream.Length)
        {
            ushort g = r.ReadUInt16();
            ushort e = r.ReadUInt16();
            if (g == 0xFFFE && (e == 0xE0DD || e == 0xE00D))
            {
                r.ReadUInt32(); break; // delimiter
            }
            // Not a delimiter — put back and read as data
            buf.WriteByte((byte)(g & 0xFF));
            buf.WriteByte((byte)(g >> 8));
            buf.WriteByte((byte)(e & 0xFF));
            buf.WriteByte((byte)(e >> 8));
        }
        return buf.ToArray();
    }

    // ── Known VR lookup for Implicit VR ──────────────────────────────────

    private static string KnownVR(DicomTag tag) => tag switch
    {
        var t when t == DicomTag.PatientID               => DicomVR.LO,
        var t when t == DicomTag.PatientName             => DicomVR.PN,
        var t when t == DicomTag.PatientBirthDate        => DicomVR.DA,
        var t when t == DicomTag.PatientSex              => DicomVR.CS,
        var t when t == DicomTag.AccessionNumber         => DicomVR.SH,
        var t when t == DicomTag.StudyInstanceUID        => DicomVR.UI,
        var t when t == DicomTag.SOPClassUID             => DicomVR.UI,
        var t when t == DicomTag.SOPInstanceUID          => DicomVR.UI,
        var t when t == DicomTag.Modality                => DicomVR.CS,
        var t when t == DicomTag.Rows                    => DicomVR.US,
        var t when t == DicomTag.Columns                 => DicomVR.US,
        var t when t == DicomTag.BitsAllocated           => DicomVR.US,
        var t when t == DicomTag.BitsStored              => DicomVR.US,
        var t when t == DicomTag.HighBit                 => DicomVR.US,
        var t when t == DicomTag.PixelRepresentation     => DicomVR.US,
        var t when t == DicomTag.SamplesPerPixel         => DicomVR.US,
        var t when t == DicomTag.PlanarConfiguration     => DicomVR.US,
        var t when t == DicomTag.PixelData               => DicomVR.OW,
        var t when t == DicomTag.ScheduledProcedureStepSequence => DicomVR.SQ,
        _ => DicomVR.LO  // sensible fallback for unknown tags
    };

    // ── Internal record ──────────────────────────────────────────────────

    private void Set(DicomTag tag, string vr, byte[] value,
        List<DicomDataset>? seqItems = null)
    {
        int idx = _elements.FindIndex(e => e.Tag == tag);
        var el  = new DataElement(tag, vr, value, seqItems);
        if (idx >= 0) _elements[idx] = el;
        else          _elements.Add(el);
    }

    internal IReadOnlyList<DataElement> Elements => _elements;

    internal sealed class DataElement
    {
        public DicomTag           Tag           { get; }
        public string             VR            { get; }
        public byte[]             Value         { get; }
        public List<DicomDataset>? SequenceItems { get; }

        public DataElement(DicomTag tag, string vr, byte[] value,
            List<DicomDataset>? seqItems)
        {
            Tag           = tag;
            VR            = vr;
            Value         = value;
            SequenceItems = seqItems;
        }
    }
}
