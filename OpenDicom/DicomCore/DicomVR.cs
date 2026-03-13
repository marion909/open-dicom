namespace OpenDicom.DicomCore;

/// <summary>String constants for DICOM Value Representations used by this server.</summary>
public static class DicomVR
{
    public const string AE = "AE"; // Application Entity    – max 16 chars
    public const string CS = "CS"; // Code String           – max 16 chars
    public const string DA = "DA"; // Date                  – YYYYMMDD
    public const string DS = "DS"; // Decimal String        – max 16 chars
    public const string IS = "IS"; // Integer String        – max 12 chars
    public const string LO = "LO"; // Long String           – max 64 chars
    public const string OB = "OB"; // Other Byte            – binary
    public const string OW = "OW"; // Other Word            – binary
    public const string PN = "PN"; // Person Name           – max 64 chars per component group
    public const string SH = "SH"; // Short String          – max 16 chars
    public const string SQ = "SQ"; // Sequence              – items
    public const string TM = "TM"; // Time                  – HHMMSS.ffffff
    public const string UI = "UI"; // Unique Identifier     – max 64 chars
    public const string UL = "UL"; // Unsigned Long         – 4 bytes
    public const string UN = "UN"; // Unknown               – binary
    public const string US = "US"; // Unsigned Short        – 2 bytes
}
