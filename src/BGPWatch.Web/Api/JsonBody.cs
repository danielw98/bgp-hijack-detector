using System.Text.Json;
using System.Text.Json.Nodes;

namespace BGPWatch.Web.Api;

/// <summary>The JSON object a request carries; an empty or unreadable body is an empty object.</summary>
public static class JsonBody
{
    public static async Task<JsonObject> ReadAsync(HttpRequest request, CancellationToken cancel)
    {
        using var reader = new StreamReader(request.Body);
        var text = await reader.ReadToEndAsync(cancel);
        try
        {
            return text.Trim() == "" ? [] : JsonNode.Parse(text) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The non-negative integers of an array field; null when the field is not such an array.</summary>
    public static List<long>? Numbers(JsonObject body, string field)
    {
        if (body[field] is not JsonArray array)
        {
            return null;
        }
        var numbers = new List<long>();
        foreach (var item in array)
        {
            if (item is not JsonValue value || !value.TryGetValue<long>(out var number) || number < 0)
            {
                return null;
            }
            numbers.Add(number);
        }
        return numbers;
    }
}
