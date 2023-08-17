using System.Text.Json;

namespace RemoteHttpRequest.Shared;

public static class RemoteHttpRequestOptions
{
    public static readonly JsonSerializerOptions JsonSerializer = CreateJsonSerializerOptions();

    private static JsonSerializerOptions CreateJsonSerializerOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new NullHttpContentJsonConverter());
        return options;
    }
}
