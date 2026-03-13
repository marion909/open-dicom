using System.Text;
using Microsoft.Extensions.Configuration.Ini;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using OpenDicom;
using OpenDicom.Dicom;
using OpenDicom.Logging;
using OpenDicom.Storage;
using OpenDicom.Watcher;

// Determine base directory (handles both development and single-file-publish)
string baseDir = AppContext.BaseDirectory;
string iniPath = Path.Combine(baseDir, "service.ini");
string logDir  = Path.Combine(baseDir, "logs");

// Windows-1252 für GDT-Encoding registrieren
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// First-run: create default INI if not present
EnsureIniExists(iniPath, baseDir);

// Bootstrap: write startup errors to file before host is built
Directory.CreateDirectory(logDir);
using var bootstrapLogger = new FileLoggerProvider(logDir);
var startupLog = bootstrapLogger.CreateLogger("Startup");
startupLog.LogInformation("OpenDicom starting up. Config: {IniPath}", iniPath);

try
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

    // ----------- Configuration -----------
    builder.Configuration.Sources.Clear();
    builder.Configuration.AddIniFile(iniPath, optional: true, reloadOnChange: false);

    // Bind typed settings
    builder.Services.Configure<AppSettings>(opts =>
    {
        opts.Dicom.AeTitle = builder.Configuration["Dicom:AeTitle"] ?? "OPENDICOM";
        if (int.TryParse(builder.Configuration["Dicom:Port"], out int port))
            opts.Dicom.Port = port;

        opts.Paths.GdtInputFolder = builder.Configuration["Paths:GdtInputFolder"]
            ?? @"C:\OpenDicom\gdt_in";
        opts.Paths.GdtOutputFolder = builder.Configuration["Paths:GdtOutputFolder"]
            ?? @"C:\OpenDicom\gdt_out";
        opts.Paths.DicomStorageFolder = builder.Configuration["Paths:DicomStorageFolder"]
            ?? @"C:\OpenDicom\storage";

        if (int.TryParse(builder.Configuration["Worklist:EntryTtlHours"], out int ttl))
            opts.Worklist.EntryTtlHours = ttl;
        opts.Worklist.DefaultModality = builder.Configuration["Worklist:DefaultModality"] ?? "*";
    });

    // ----------- Logging (File + Windows Event Log) -----------
    builder.Logging.ClearProviders();
    builder.Logging.AddProvider(new FileLoggerProvider(logDir));
    builder.Logging.AddEventLog(new EventLogSettings
    {
        SourceName = "OpenDicom",
        LogName    = "Application",
        Filter     = (_, level) => level >= LogLevel.Warning
    });
    builder.Logging.SetMinimumLevel(LogLevel.Information);
    builder.Logging.AddFilter("Microsoft", LogLevel.Warning);

    // ----------- Windows Service -----------
    builder.Services.AddWindowsService(opts => opts.ServiceName = "OpenDicom");

    // ----------- Application Services -----------
    builder.Services.AddSingleton<WorklistStore>();
    builder.Services.AddSingleton<DicomHandler>();
    builder.Services.AddHostedService<GdtWatcherService>();
    builder.Services.AddHostedService<DicomServerService>();

    IHost host = builder.Build();

    // Ensure required directories exist
    using (IServiceScope scope = host.Services.CreateScope())
    {
        AppSettings settings = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<AppSettings>>().Value;
        EnsureDirectoriesExist(settings);
    }

    await host.RunAsync();
}
catch (Exception ex)
{
    try { File.WriteAllText(Path.Combine(logDir, "crash.txt"), ex.ToString()); } catch { }
    startupLog.LogCritical(ex, "OpenDicom terminated unexpectedly");
    return 1;
}

return 0;


static void EnsureDirectoriesExist(AppSettings settings)
{
    foreach (string dir in new[]
    {
        settings.Paths.GdtInputFolder,
        Path.Combine(settings.Paths.GdtInputFolder, "processed"),
        Path.Combine(settings.Paths.GdtInputFolder, "error"),
        settings.Paths.GdtOutputFolder,
        settings.Paths.DicomStorageFolder
    })
    {
        Directory.CreateDirectory(dir);
    }
}

static void EnsureIniExists(string iniPath, string baseDir)
{
    if (File.Exists(iniPath)) return;

    // Determine sensible default data path next to the exe
    string dataRoot = Path.Combine(baseDir, "data");

    string defaultIni =
        $"""
        [Dicom]
        ; DICOM AE-Title des Servers (max. 16 Zeichen)
        AeTitle=OPENDICOM
        ; DICOM Port (Standard: 11112)
        Port=11112

        [Paths]
        ; Ordner, in dem das AIS .gdt-Dateien (Satzart 6301) ablegt
        GdtInputFolder={dataRoot}\gdt_in
        ; Ordner, in dem der Server .gdt-Antwortdateien (Satzart 6310) ablegt
        GdtOutputFolder={dataRoot}\gdt_out
        ; Ordner, in dem empfangene DICOM-Dateien gespeichert werden
        DicomStorageFolder={dataRoot}\storage

        [Worklist]
        ; Maximale Haltezeit eines Worklist-Eintrags in Stunden
        EntryTtlHours=24
        ; Standard-Modalitaet (* = alle)
        DefaultModality=*

        [Serilog]
        MinimumLevel=Information
        """;

    File.WriteAllText(iniPath, defaultIni, System.Text.Encoding.UTF8);
}
