using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenDicom.Gdt;
using OpenDicom.Storage;

namespace OpenDicom.Watcher;

/// <summary>
/// Überwacht den GDT-Eingangsordner mittels FileSystemWatcher.
/// Neue .gdt-Dateien (Satzart 6301) werden geparst und als Worklist-Einträge registriert.
/// </summary>
public sealed class GdtWatcherService : BackgroundService
{
    private readonly AppSettings _settings;
    private readonly WorklistStore _worklistStore;
    private readonly ILogger<GdtWatcherService> _logger;

    // Debounce: Verhindert Doppelverarbeitung wenn eine Datei noch geschrieben wird
    private readonly ConcurrentFileDebouncer _debouncer;

    public GdtWatcherService(
        IOptions<AppSettings> settings,
        WorklistStore worklistStore,
        ILogger<GdtWatcherService> logger)
    {
        _settings = settings.Value;
        _worklistStore = worklistStore;
        _logger = logger;
        _debouncer = new ConcurrentFileDebouncer(TimeSpan.FromMilliseconds(400));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string inputFolder = _settings.Paths.GdtInputFolder;
        _logger.LogInformation("GDT Watcher started. Watching: {Folder}", inputFolder);

        // Bereits vorhandene Dateien beim Start verarbeiten
        foreach (string existingFile in Directory.GetFiles(inputFolder, "*.gdt"))
            await ProcessFileAsync(existingFile);

        using var watcher = new FileSystemWatcher(inputFolder, "*.gdt")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        var tcs = new TaskCompletionSource();
        stoppingToken.Register(() => tcs.TrySetResult());

        watcher.Created += async (_, e) => await _debouncer.DebounceAsync(e.FullPath,
            async () => await ProcessFileAsync(e.FullPath));
        watcher.Changed += async (_, e) => await _debouncer.DebounceAsync(e.FullPath,
            async () => await ProcessFileAsync(e.FullPath));

        await tcs.Task;
        _logger.LogInformation("GDT Watcher stopped.");
    }

    private async Task ProcessFileAsync(string filePath)
    {
        // Kurz warten bis der Schreiber die Datei freigibt
        await Task.Delay(200);

        string processedDir = Path.Combine(_settings.Paths.GdtInputFolder, "processed");
        string errorDir = Path.Combine(_settings.Paths.GdtInputFolder, "error");

        try
        {
            _logger.LogInformation("Processing GDT file: {File}", filePath);

            GdtRecord record = GdtParser.Parse(filePath);
            _worklistStore.AddEntry(record);

            // Datei archivieren
            string dest = Path.Combine(processedDir,
                $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Path.GetFileName(filePath)}");
            MoveFileSafe(filePath, dest);

            _logger.LogInformation(
                "GDT file processed successfully. Patient: {Name} ({Id})",
                record.PatientSurname, record.PatientId);
        }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020))
        {
            // Datei noch gesperrt – ignorieren, FileSystemWatcher wird erneut auflösen
            _logger.LogDebug("GDT file still locked, will retry: {File}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process GDT file: {File}", filePath);
            try
            {
                string dest = Path.Combine(errorDir,
                    $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Path.GetFileName(filePath)}");
                MoveFileSafe(filePath, dest);
            }
            catch (Exception moveEx)
            {
                _logger.LogWarning(moveEx, "Could not move erroneous GDT file to error folder.");
            }
        }
    }

    private static void MoveFileSafe(string source, string dest)
    {
        if (!File.Exists(source)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Move(source, dest, overwrite: true);
    }
}

/// <summary>
/// Einfacher Debouncer: ruft eine Aktion erst auf, nachdem eine Datei
/// für eine bestimmte Zeit nicht erneut gemeldet wurde.
/// </summary>
internal sealed class ConcurrentFileDebouncer
{
    private readonly TimeSpan _delay;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _pending = new();

    public ConcurrentFileDebouncer(TimeSpan delay) => _delay = delay;

    public async Task DebounceAsync(string key, Func<Task> action)
    {
        if (_pending.TryRemove(key, out var existing))
            existing.Cancel();

        var cts = new CancellationTokenSource();
        _pending[key] = cts;

        try
        {
            await Task.Delay(_delay, cts.Token);
            _pending.TryRemove(key, out _);
            await action();
        }
        catch (OperationCanceledException)
        {
            // Debounced – a later call will execute the action
        }
    }
}
