using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExiledCms.TicketsService.Api.Infrastructure;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static JsonElement ParseElement(string? json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return document.RootElement.Clone();
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
