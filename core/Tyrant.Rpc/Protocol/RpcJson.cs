using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tyrant.Rpc.Protocol;

public static class RpcJson
{
    /// <summary>
    /// Wire format: camelCase, enums as camelCase strings, non-ASCII text kept readable (lines are JSON, never HTML),
    /// and strict about missing required values and nulls in non-nullable properties.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };
}
