using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace OpenDicom.Logging;

/// <summary>
/// Rolling daily file logger – zeigt täglich eine neue Datei an (opendicom-YYYYMMDD.log).
/// Thread-sicher über eine dedizierte Schreib-Queue.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minLevel;
    private readonly BlockingCollection<string> _queue = new(4096);
    private readonly Thread _writerThread;
    private string _currentPath = string.Empty;
    private StreamWriter? _writer;

    public FileLoggerProvider(string directory, LogLevel minLevel = LogLevel.Information)
    {
        _directory = directory;
        _minLevel  = minLevel;
        Directory.CreateDirectory(directory);

        _writerThread = new Thread(WriteLoop) { IsBackground = true, Name = "FileLogger" };
        _writerThread.Start();
    }

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(categoryName, _minLevel, Enqueue);

    private void Enqueue(string line) => _queue.TryAdd(line);

    private void WriteLoop()
    {
        foreach (string line in _queue.GetConsumingEnumerable())
        {
            try
            {
                string path = Path.Combine(_directory,
                    $"opendicom-{DateTime.Now:yyyyMMdd}.log");

                if (path != _currentPath)
                {
                    _writer?.Flush();
                    _writer?.Dispose();
                    _writer = new StreamWriter(
                        new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                        System.Text.Encoding.UTF8)
                    {
                        AutoFlush = true
                    };
                    _currentPath = path;
                }

                _writer?.WriteLine(line);
            }
            catch { /* swallow – logging must never crash the app */ }
        }

        _writer?.Flush();
        _writer?.Dispose();
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _writerThread.Join(TimeSpan.FromSeconds(3));
        _queue.Dispose();
    }
}

internal sealed class FileLogger : ILogger
{
    private readonly string _category;
    private readonly LogLevel _minLevel;
    private readonly Action<string> _enqueue;

    public FileLogger(string category, LogLevel minLevel, Action<string> enqueue)
    {
        _category = category;
        _minLevel  = minLevel;
        _enqueue   = enqueue;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        string level = logLevel switch
        {
            LogLevel.Trace       => "TRC",
            LogLevel.Debug       => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning     => "WRN",
            LogLevel.Error       => "ERR",
            LogLevel.Critical    => "CRT",
            _                    => "   "
        };

        string shortCategory = _category.Length > 30
            ? "…" + _category[^29..]
            : _category;

        string msg = formatter(state, exception);
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {shortCategory}: {msg}";
        if (exception != null)
            line += Environment.NewLine + exception;

        _enqueue(line);
    }
}
