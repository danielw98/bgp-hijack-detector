using System.Net;
using System.Text.Json.Nodes;
using BGPWatch.Core.Application;
using BGPWatch.Core.Domain;
using BGPWatch.Web.Watch;
using Microsoft.Extensions.Logging;

namespace BGPWatch.Tests;

/// <summary>The webhook: the notification posted as JSON, one more try after a failure, the URL kept out of the log.</summary>
public class WebhookTests
{
    private static readonly Uri Url = new("https://example.org/hooks/secret-token");
    private static readonly DateTime At = new(2026, 11, 6, 18, 0, 0, DateTimeKind.Utc);

    private sealed class Endpoint(params HttpStatusCode[] answers) : HttpMessageHandler
    {
        public List<(Uri? Url, string Body, string? MediaType)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Calls.Add((request.RequestUri, await request.Content!.ReadAsStringAsync(cancel), request.Content.Headers.ContentType?.MediaType));
            return new HttpResponseMessage(answers[Math.Min(Calls.Count, answers.Length) - 1]);
        }
    }

    private sealed class Memory : ILogger
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Lines.Add(formatter(state, exception));
        }
    }

    private static WebhookNotifier Hook(Endpoint endpoint, Memory log)
    {
        return new WebhookNotifier(new HttpClient(endpoint), "ops", Url, log, TimeSpan.Zero);
    }

    private static Notification Opened()
    {
        var incident = new OpenIncident("203.0.113.0/24", 65003, [65001], At, At, 2, ["rrc21 192.0.2.1"], ["203.0.113.0/25"], ["subprefix_hijack"], "detail");
        return Notification.Opened("ris", incident);
    }

    [Fact]
    public async Task TheNotificationIsPostedAsSnakeCaseJson()
    {
        var endpoint = new Endpoint(HttpStatusCode.OK);
        await Hook(endpoint, new Memory()).NotifyAsync(Opened(), CancellationToken.None);
        var call = Assert.Single(endpoint.Calls);
        Assert.Equal((Url, "application/json"), (call.Url, call.MediaType));
        var body = JsonNode.Parse(call.Body)!;
        Assert.Equal(("opened", "ris", "2026-11-06T18:00:00Z"), (body["kind"]!.GetValue<string>(), body["source"]!.GetValue<string>(), body["at"]!.GetValue<string>()));
        Assert.Equal("detail; seen by one vantage point", body["text"]!.GetValue<string>());
        var incident = body["incident"]!;
        Assert.Equal(("203.0.113.0/24", 65003L, 2), (incident["prefix"]!.GetValue<string>(), incident["origin"]!.GetValue<long>(), incident["alerts"]!.GetValue<int>()));
        Assert.Equal([65001L], incident["expected_origins"]!.AsArray().Select(a => a!.GetValue<long>()));
        Assert.Equal(["subprefix_hijack"], incident["alert_types"]!.AsArray().Select(a => a!.GetValue<string>()));
        Assert.Equal("2026-11-06T18:00:00Z", incident["first_seen"]!.GetValue<string>());
    }

    [Fact]
    public async Task AFailedPostIsTriedOnceMoreAndTheLogNamesTheHookNotTheUrl()
    {
        var endpoint = new Endpoint(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var log = new Memory();
        await Hook(endpoint, log).NotifyAsync(Opened(), CancellationToken.None);
        Assert.Equal(2, endpoint.Calls.Count);
        var line = Assert.Single(log.Lines);
        Assert.Contains("ops", line, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoFailuresRaiseTheError()
    {
        var endpoint = new Endpoint(HttpStatusCode.BadGateway);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Hook(endpoint, new Memory()).NotifyAsync(Opened(), CancellationToken.None));
        Assert.Equal(2, endpoint.Calls.Count);
        Assert.Equal("webhook ops answered 502", error.Message);
    }

    [Fact]
    public async Task ATestNotificationHasNoIncident()
    {
        var endpoint = new Endpoint(HttpStatusCode.OK);
        await Hook(endpoint, new Memory()).NotifyAsync(Notification.Test(At), CancellationToken.None);
        var body = JsonNode.Parse(Assert.Single(endpoint.Calls).Body)!;
        Assert.Equal("test", body["kind"]!.GetValue<string>());
        Assert.Null(body["incident"]);
    }
}
