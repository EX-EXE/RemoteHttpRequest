using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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
