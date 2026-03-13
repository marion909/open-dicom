using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenDicom.Network;
using OpenDicom.Storage;

namespace OpenDicom.Dicom;

/// <summary>
/// BackgroundService that starts and manages the custom DICOM TCP server.
/// </summary>
public sealed class DicomServerService : BackgroundService
{
    private readonly AppSettings _settings;
    private readonly DicomHandler _handler;
    private readonly ILogger<DicomServerService> _logger;

    public DicomServerService(
        IOptions<AppSettings> settings,
        DicomHandler handler,
        ILogger<DicomServerService> logger)
    {
        _settings = settings.Value;
        _handler  = handler;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Starting DICOM server on port {Port} with AE-Title '{AeTitle}'",
            _settings.Dicom.Port, _settings.Dicom.AeTitle);

        await using var server = new DicomServer(
            _settings.Dicom.Port, _handler, _logger);

        try
        {
            server.Start();
            _logger.LogInformation("DICOM server is listening on port {Port}.", _settings.Dicom.Port);
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "DICOM server encountered a fatal error.");
            throw;
        }
        finally
        {
            _logger.LogInformation("DICOM server stopped.");
        }
    }
}
