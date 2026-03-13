using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using OpenDicom.Dicom;

namespace OpenDicom.Network;

/// <summary>
/// Listens on a TCP port and spawns a <see cref="DicomConnection"/> per client.
/// Created and controlled by DicomServerService.
/// </summary>
internal sealed class DicomServer : IAsyncDisposable
{
    private readonly TcpListener  _listener;
    private readonly DicomHandler _handler;
    private readonly ILogger      _log;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    public DicomServer(int port, DicomHandler handler, ILogger log)
    {
        _handler  = handler;
        _log      = log;
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    }

    public void Start()
    {
        _listener.Start();
        _log.LogInformation("DICOM server listening on port {Port}",
            ((IPEndPoint)_listener.LocalEndpoint).Port);
        _acceptLoop = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error accepting DICOM connection");
                continue;
            }

            client.ReceiveTimeout = 300_000; // 5 min
            client.SendTimeout    = 300_000;

            var conn = new DicomConnection(client, _handler, _log);
            // Fire-and-forget: each connection is independent
            _ = Task.Run(() => conn.RunAsync(ct), ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        if (_acceptLoop != null)
        {
            try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { /* expected */ }
        }
        _cts.Dispose();
    }
}
