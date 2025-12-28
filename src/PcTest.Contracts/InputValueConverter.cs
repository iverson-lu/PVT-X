using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public sealed class InputValueConverter : JsonConverter<InputValue>
{
    public override InputValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return JsonParsing.ParseInputValue(doc.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, InputValue value, JsonSerializerOptions options)
    {
        if (value.EnvRef is not null)
        {
            JsonSerializer.Serialize(writer, value.EnvRef, options);
            return;
        }

        if (value.Literal.HasValue)
        {
            value.Literal.Value.WriteTo(writer);
            return;
        }

        writer.WriteNullValue();
    }
}
