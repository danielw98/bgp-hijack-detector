using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BGPWatch.Core.Application;

namespace BGPWatch.Web.Watch;

/// <summary>A notification as the webhook posts it and the CLI prints it: one snake_case JSON object.</summary>
public static class NotificationJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(Notification notification)
    {
        return JsonSerializer.Serialize(notification, Options);
    }
}

/// <summary>
/// A webhook: the notification posted as JSON to one URL, with one more try after a
/// pause when the first fails. Failures are logged under the webhook's name only, since
/// a URL may carry a secret.
/// </summary>
public sealed class WebhookNotifier(HttpClient http, string name, Uri url, ILogger logger, TimeSpan retryAfter) : INotifier
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public WebhookNotifier(HttpClient http, string name, Uri url, ILogger logger)
        : this(http, name, url, logger, RetryAfter)
    {
    }

    public string Name => name;

    public async Task NotifyAsync(Notification notification, CancellationToken cancel)
    {
        var json = NotificationJson.Serialize(notification);
        try
        {
            await PostAsync(json, cancel);
        }
        catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !cancel.IsCancellationRequested))
        {
            logger.LogWarning("webhook {Name}: {Reason}; one more try in {Seconds} s", name, e.Message, retryAfter.TotalSeconds);
            await Task.Delay(retryAfter, cancel);
            await PostAsync(json, cancel);
        }
    }

    private async Task PostAsync(string json, CancellationToken cancel)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(url, content, cancel);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"webhook {name} answered {(int)response.StatusCode}");
        }
    }
}
