using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using BGPWatch.Core.Application;
using BGPWatch.Core.Domain;
using BGPWatch.Web.Native;

namespace BGPWatch.Web.Bmp;

/// <summary>
/// The BMP station: accepts the lab routers' BMP sessions and runs the routes of every
/// Loc-RIB route monitoring message through the pipeline, one message at a time, in
/// arrival order. A row's time is the moment its message arrived.
/// </summary>
public sealed class BmpStation(LabPipeline lab, ILogger<BmpStation> logger) : BackgroundService
{
    public const int Port = 5000;

    private readonly ConcurrentDictionary<string, TcpClient> sessions = new();
    private readonly Channel<List<Announcement>> queue =
        Channel.CreateUnbounded<List<Announcement>>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>The addresses of the routers whose BMP session is up.</summary>
    public IReadOnlyCollection<string> Reporting => [.. sessions.Keys];

    /// <summary>Closes every session; each router reconnects and sends its Loc-RIB again.</summary>
    public void DropSessions()
    {
        foreach (var client in sessions.Values)
        {
            client.Close();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await lab.Pipeline.StartAsync(stop);
        var listener = new TcpListener(IPAddress.Any, Port);
        listener.Start();
        logger.LogInformation("BMP station listening on port {Port}", Port);
        var consumer = ConsumeAsync(stop);
        try
        {
            while (true)
            {
                _ = ServeAsync(await listener.AcceptTcpClientAsync(stop), stop);
            }
        }
        catch (OperationCanceledException)
        {
            listener.Stop();
        }
        await consumer;
    }

    private async Task ServeAsync(TcpClient client, CancellationToken stop)
    {
        var address = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.MapToIPv4().ToString();
        sessions[address] = client;
        logger.LogInformation("BMP session from {Address}", address);
        try
        {
            await using var stream = client.GetStream();
            while (await BmpMessage.ReadAsync(stream, stop) is { Type: not BmpType.Termination } message)
            {
                await AcceptAsync(message, address, stop);
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ObjectDisposedException or OperationCanceledException)
        {
            logger.LogInformation("BMP session from {Address} ended: {Reason}", address, e.Message);
        }
        finally
        {
            sessions.TryRemove(KeyValuePair.Create(address, client));
            client.Dispose();
        }
    }

    private async Task AcceptAsync(BmpMessage message, string address, CancellationToken stop)
    {
        if (message.Type == BmpType.Initiation)
        {
            logger.LogInformation("BMP router {Address} is {SysName}", address, message.SysName());
            return;
        }
        try
        {
            var routes = message.LocRibRoutes(DateTime.UtcNow);
            if (routes.Count > 0)
            {
                await queue.Writer.WriteAsync(routes, stop);
            }
        }
        catch (MalformedUpdateException e)
        {
            logger.LogWarning("BMP message from {Address} skipped: {Reason}", address, e.Message);
        }
    }

    private async Task ConsumeAsync(CancellationToken stop)
    {
        try
        {
            await foreach (var routes in queue.Reader.ReadAllAsync(stop))
            {
                await StoreAsync(routes, stop);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("BMP station stopped");
        }
    }

    private async Task StoreAsync(List<Announcement> routes, CancellationToken stop)
    {
        try
        {
            await lab.Pipeline.ProcessAsync(routes, stop);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "storing {Count} routes failed", routes.Count);
        }
    }
}

/// <summary>The pipeline of the live lab, whose rows carry the source "lab".</summary>
public sealed class LabPipeline(DetectorConfig config, IRouteLog log, ILogger<LabPipeline> logger)
{
    public const string Source = "lab";

    public Pipeline Pipeline { get; } = new(config, log, logger, Source);
}
