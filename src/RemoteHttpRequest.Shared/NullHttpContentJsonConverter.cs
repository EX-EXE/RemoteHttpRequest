using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

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
