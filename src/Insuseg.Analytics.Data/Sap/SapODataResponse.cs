using System.Text.Json.Serialization;

namespace Insuseg.Analytics.Data.Sap;

// Forma del sobre OData que envuelve las respuestas del Service Layer, ej.:
// { "odata.metadata": "...", "value": [ ... ], "odata.nextLink": "..." }
public class SapODataResponse<T>
{
    [JsonPropertyName("value")]
    public List<T> Value { get; set; } = [];

    [JsonPropertyName("odata.nextLink")]
    public string? NextLink { get; set; }
}
