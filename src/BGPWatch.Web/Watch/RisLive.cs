using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using BGPWatch.Core.Domain;

namespace BGPWatch.Web.Watch;

/// <summary>One RIS Live subscription: a prefix with its more specifics, at one collector or at all of them.</summary>
public sealed record RisSubscription(string Prefix, string? Host = null)
{
    private static readonly JsonSerializerOptions Compact = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>The ris_subscribe message, acknowledged by the server.</summary>
    public string ToJson()
    {
        var data = new { prefix = Prefix, moreSpecific = true, type = "UPDATE", host = Host, socketOptions = new { acknowledge = true } };
        return JsonSerializer.Serialize(new { type = "ris_subscribe", data }, Compact);
    }
}

public enum RisFrameKind
{
    Update,
    SubscribeOk,
    Pong,
    Error,
    Other,
}

/// <summary>One frame of the stream, decoded: the routes of an UPDATE, or what else the server said.</summary>
public sealed record RisFrame(RisFrameKind Kind, IReadOnlyList<Announcement> Routes, string Message)
{
    /// <summary>
    /// Decodes a frame. The prefixes of an UPDATE become announcements seen at the
    /// collector's time from the vantage point "rrcNN peer" (the collector's first host
    /// label, as the looking glass names it); a path ending in an AS_SET is flattened in
    /// order, as the MRT parser does.
    /// </summary>
    public static RisFrame Parse(string json)
    {
        var frame = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("a RIS Live frame is a JSON object");
        var data = frame["data"] as JsonObject;
        return frame["type"]?.GetValue<string>() switch
        {
            "ris_message" when data?["type"]?.GetValue<string>() == "UPDATE" => new RisFrame(RisFrameKind.Update, RoutesOf(data), ""),
            "ris_message" => new RisFrame(RisFrameKind.Other, [], ""),
            "ris_subscribe_ok" => new RisFrame(RisFrameKind.SubscribeOk, [], ""),
            "pong" => new RisFrame(RisFrameKind.Pong, [], ""),
            "ris_error" => new RisFrame(RisFrameKind.Error, [], data?["message"]?.GetValue<string>() ?? "unknown error"),
            _ => new RisFrame(RisFrameKind.Other, [], ""),
        };
    }

    private static List<Announcement> RoutesOf(JsonObject data)
    {
        var seenAt = DateTime.UnixEpoch.AddSeconds(data["timestamp"]?.GetValue<double>() ?? 0);
        var host = (data["host"]?.GetValue<string>() ?? "ris").Split('.')[0];
        var peer = $"{host} {data["peer"]?.GetValue<string>()}";
        var path = Flatten(data["path"]).ToArray();
        var routes = new List<Announcement>();
        foreach (var group in data["announcements"]?.AsArray() ?? [])
        {
            var nextHop = IPAddress.TryParse(group?["next_hop"]?.GetValue<string>(), out var hop) ? hop.ToString() : null;
            foreach (var prefix in group?["prefixes"]?.AsArray() ?? [])
            {
                routes.Add(Announcement.Of(Prefixes.ParseLoose(prefix!.GetValue<string>()), path, peer, nextHop, seenAt));
            }
        }
        foreach (var prefix in data["withdrawals"]?.AsArray() ?? [])
        {
            routes.Add(Announcement.Withdrawal(Prefixes.ParseLoose(prefix!.GetValue<string>()), peer, seenAt));
        }
        return routes;
    }

    private static IEnumerable<long> Flatten(JsonNode? path)
    {
        return path switch
        {
            JsonArray members => members.SelectMany(Flatten),
            JsonValue value => [value.GetValue<long>()],
            _ => [],
        };
    }
}

/// <summary>How the stream is doing, for the page and the status line.</summary>
public sealed record FeedStatus(bool Connected, DateTime? Since, DateTime? LastMessageAt, long Messages, int Reconnects, int Subscriptions, string? LastError);

/// <summary>
/// The RIS Live client: one WebSocket to the RIPE NCC's stream, subscribed to every
/// watched prefix with its more specifics; the routes of every UPDATE go into the sink as
/// they arrive. A failure, a silent socket or a changed subscription set closes the
/// socket, and it is opened again after a pause that doubles up to MaxPause; a change of
/// subscriptions reconnects at once. Without subscriptions there is no socket.
/// </summary>
public sealed class RisLive(string client, ChannelWriter<List<Announcement>> sink, ILogger logger)
{
    public static readonly Uri Endpoint = new("wss://ris-live.ripe.net/v1/ws/");
    public static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(75);
    public static readonly TimeSpan MaxPause = TimeSpan.FromSeconds(60);
    private const string Ping = """{"type":"ping","data":null}""";
    private const int ReceiveBuffer = 64 * 1024;

    private readonly object sync = new();
    private IReadOnlyList<RisSubscription> wanted = [];
    private ClientWebSocket? socket;
    private bool resubscribe;
    private bool connected;
    private DateTime? since;
    private DateTime? lastMessageAt;
    private DateTime lastFrameAt;
    private long messages;
    private int reconnects;
    private string? lastError;
    private DateTime lastDecodeWarning = DateTime.MinValue;

    public FeedStatus Status
    {
        get
        {
            lock (sync)
            {
                return new FeedStatus(connected, since, lastMessageAt, messages, reconnects, wanted.Count, lastError);
            }
        }
    }

    /// <summary>Sets the subscriptions; a change while connected reconnects with the new set.</summary>
    public void Subscribe(IReadOnlyList<RisSubscription> subscriptions)
    {
        ClientWebSocket? open;
        lock (sync)
        {
            if (wanted.SequenceEqual(subscriptions))
            {
                return;
            }
            wanted = subscriptions;
            open = socket;
            resubscribe = open is not null;
        }
        open?.Abort();
    }

    public async Task RunAsync(CancellationToken stop)
    {
        var pause = TimeSpan.FromSeconds(1);
        while (!stop.IsCancellationRequested)
        {
            IReadOnlyList<RisSubscription> subscriptions;
            lock (sync)
            {
                subscriptions = wanted;
            }
            if (subscriptions.Count == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stop);
                continue;
            }
            var attempt = DateTime.UtcNow;
            try
            {
                await SessionAsync(subscriptions, stop);
            }
            catch (Exception e) when (e is not OperationCanceledException || !stop.IsCancellationRequested)
            {
                if (stop.IsCancellationRequested)
                {
                    break;
                }
                if (Resubscribing())
                {
                    pause = TimeSpan.FromSeconds(1);
                    continue;
                }
                if (CameUpSince(attempt))
                {
                    pause = TimeSpan.FromSeconds(1);
                }
                var reason = Silent() ? $"no answer for {SilenceLimit.TotalSeconds:0} s" : e.Message;
                lock (sync)
                {
                    lastError = reason;
                }
                logger.LogWarning("RIS Live: {Reason}; connecting again in {Pause} s", reason, pause.TotalSeconds);
                await Task.Delay(pause, stop);
                pause = TimeSpan.FromTicks(Math.Min(pause.Ticks * 2, MaxPause.Ticks));
            }
        }
    }

    private async Task SessionAsync(IReadOnlyList<RisSubscription> subscriptions, CancellationToken stop)
    {
        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = PingEvery;
        try
        {
            await ws.ConnectAsync(new Uri($"{Endpoint}?client={Uri.EscapeDataString(client)}"), stop);
            lock (sync)
            {
                socket = ws;
                connected = true;
                since = DateTime.UtcNow;
                lastFrameAt = DateTime.UtcNow;
                lastError = null;
            }
            foreach (var subscription in subscriptions)
            {
                await SendAsync(ws, subscription.ToJson(), stop);
            }
            logger.LogInformation("RIS Live connected, {Count} subscriptions", subscriptions.Count);
            using var session = CancellationTokenSource.CreateLinkedTokenSource(stop);
            var pinger = PingAsync(ws, session.Token);
            try
            {
                await ReceiveAsync(ws, session.Token);
            }
            finally
            {
                session.Cancel();
                await pinger.ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
        finally
        {
            lock (sync)
            {
                socket = null;
                connected = false;
                reconnects++;
            }
        }
    }

    private async Task ReceiveAsync(ClientWebSocket ws, CancellationToken cancel)
    {
        var buffer = new byte[ReceiveBuffer];
        using var message = new MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, cancel);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new IOException($"RIS Live closed the stream: {result.CloseStatusDescription ?? "no reason"}");
            }
            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }
            lock (sync)
            {
                lastFrameAt = DateTime.UtcNow;
            }
            if (Decode(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length)) is { } frame)
            {
                Accept(frame);
            }
            message.SetLength(0);
        }
    }

    /// <summary>The frame decoded; one that does not decode is dropped, and logged at most once a minute.</summary>
    private RisFrame? Decode(string json)
    {
        try
        {
            return RisFrame.Parse(json);
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or ArgumentException)
        {
            var now = DateTime.UtcNow;
            if (now - lastDecodeWarning > TimeSpan.FromMinutes(1))
            {
                lastDecodeWarning = now;
                logger.LogWarning("RIS Live frame dropped: {Reason}", e.Message);
            }
            return null;
        }
    }

    private void Accept(RisFrame frame)
    {
        lock (sync)
        {
            if (frame.Kind == RisFrameKind.Update)
            {
                messages++;
                lastMessageAt = DateTime.UtcNow;
            }
            if (frame.Kind == RisFrameKind.Error)
            {
                lastError = frame.Message;
            }
        }
        if (frame.Kind == RisFrameKind.Error)
        {
            logger.LogWarning("RIS Live error: {Message}", frame.Message);
        }
        if (frame.Routes.Count > 0)
        {
            sink.TryWrite([.. frame.Routes]);
        }
    }

    private async Task PingAsync(ClientWebSocket ws, CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            await Task.Delay(PingEvery, cancel);
            DateTime last;
            lock (sync)
            {
                last = lastFrameAt;
            }
            if (DateTime.UtcNow - last > SilenceLimit)
            {
                ws.Abort();
                return;
            }
            await SendAsync(ws, Ping, cancel);
        }
    }

    private static Task SendAsync(ClientWebSocket ws, string text, CancellationToken cancel)
    {
        return ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, cancel);
    }

    private bool Resubscribing()
    {
        lock (sync)
        {
            var wanted = resubscribe;
            resubscribe = false;
            return wanted;
        }
    }

    private bool CameUpSince(DateTime attempt)
    {
        lock (sync)
        {
            return since is { } up && up >= attempt;
        }
    }

    private bool Silent()
    {
        lock (sync)
        {
            return DateTime.UtcNow - lastFrameAt > SilenceLimit;
        }
    }
}
