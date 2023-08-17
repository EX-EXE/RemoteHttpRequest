using System.Net.Http.Headers;

namespace RemoteHttpRequest.Shared;

public static class HttpHeaderUtility
{
    public static void AddHttpHeaders(Google.Protobuf.Collections.RepeatedField<Proto.HttpHeader> addList, HttpHeaders headers)
    {
        foreach (var (key, values) in headers)
        {
            var header = new Proto.HttpHeader()
            {
                Key = key,
            };
            foreach (var value in values)
            {
                header.Values.Add(value);
            }
            addList.Add(header);
        }
    }
}
