using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteHttpRequest.Shared;

public class NullHttpContentJsonConverter : JsonConverter<HttpContent>
{
    public override HttpContent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return null;
    }

    public override void Write(Utf8JsonWriter writer, HttpContent value, JsonSerializerOptions options)
    {
        writer.WriteNullValue();
    }
}
