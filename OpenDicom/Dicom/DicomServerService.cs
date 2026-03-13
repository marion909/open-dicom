using FellowOakDicom.Network;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenDicom.Storage;

namespace OpenDicom.Dicom;

/// <summary>
/// BackgroundService, der den fo-dicom DICOM-Server startet und dessen Lifecycle verwaltet.
/// </summary>
public sealed class DicomServerService : BackgroundService
{
    private readonly AppSettings _settings;
    private readonly WorklistStore _worklistStore;
    private readonly ILogger<DicomServerService> _logger;

    private IDicomServer? _server;

    public DicomServerService(
        IOptions<AppSettings> settings,
        WorklistStore worklistStore,
        ILogger<DicomServerService> logger)
    {
        _settings = settings.Value;
        _worklistStore = worklistStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Dependencies in statische Felder der SCP-Klasse injizieren
        // (fo-dicom erzeugt SCP-Instanzen ohne DI-Container)
        CombinedDicomScp.SetDependencies(_worklistStore, _settings, _logger);

        _logger.LogInformation(
            "Starting DICOM server on port {Port} with AE-Title '{AeTitle}'",
            _settings.Dicom.Port, _settings.Dicom.AeTitle);

        try
        {
            _server = DicomServerFactory.Create<CombinedDicomScp>(
                ipAddress: "0.0.0.0",
                port: _settings.Dicom.Port);

            // Warten bis der Server tatsächlich lauscht
            int attempts = 0;
            while (!_server.IsListening && attempts++ < 50)
                await Task.Delay(100, stoppingToken);

            if (!_server.IsListening)
                throw new InvalidOperationException(
                    $"DICOM server failed to start listening on port {_settings.Dicom.Port}.");

            _logger.LogInformation(
                "DICOM server is listening on port {Port}.", _settings.Dicom.Port);

            // Laufen bis der Dienst gestoppt wird
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normales Shutdown
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "DICOM server encountered a fatal error.");
            throw;
        }
        finally
        {
            _server?.Dispose();
            _logger.LogInformation("DICOM server stopped.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DICOM server shutting down...");
        _server?.Dispose();
        await base.StopAsync(cancellationToken);
    }
}
