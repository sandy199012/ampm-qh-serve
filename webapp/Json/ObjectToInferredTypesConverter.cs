using System.Text.Json;
using System.Text.Json.Serialization;

namespace AMPMWeb.Json;

// Fixes a data-corruption bug: ASP.NET Core's default [FromBody] binder uses
// System.Text.Json, which deserializes "object" values (e.g. inside a
// Dictionary<string,object?>) into JsonElement structs instead of plain
// strings/bools/numbers. When that Dictionary is later saved via
// Newtonsoft's JsonConvert.SerializeObject (used everywhere else in this
// app), a boxed JsonElement has no known shape to Newtonsoft, so it gets
// reflected into just its one public property — producing garbage like
// {"ValueKind":3} in place of the real string, with the original value lost.
//
// Registering this converter for "object?" makes System.Text.Json return
// native CLR types (string/bool/long/double/null) instead of JsonElement,
// so values round-trip correctly through Newtonsoft afterwards.
public class ObjectToInferredTypesConverter : JsonConverter<object?>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True: return true;
            case JsonTokenType.False: return false;
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var l)) return l;
                return reader.GetDouble();
            case JsonTokenType.String: return reader.GetString();
            case JsonTokenType.Null: return null;
            default:
                using (var doc = JsonDocument.ParseValue(ref reader))
                    return doc.RootElement.Clone();
        }
    }

    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, value?.GetType() ?? typeof(object));
}
