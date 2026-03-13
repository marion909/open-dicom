namespace OpenDicom;

public sealed class DicomSettings
{
    public string AeTitle { get; set; } = "OPENDICOM";
    public int Port { get; set; } = 11112;
}

public sealed class PathSettings
{
    public string GdtInputFolder { get; set; } = @"C:\OpenDicom\gdt_in";
    public string GdtOutputFolder { get; set; } = @"C:\OpenDicom\gdt_out";
    public string DicomStorageFolder { get; set; } = @"C:\OpenDicom\storage";
}

public sealed class WorklistSettings
{
    public int EntryTtlHours { get; set; } = 24;
    public string DefaultModality { get; set; } = "*";
}

public sealed class AppSettings
{
    public DicomSettings Dicom { get; set; } = new();
    public PathSettings Paths { get; set; } = new();
    public WorklistSettings Worklist { get; set; } = new();
}
