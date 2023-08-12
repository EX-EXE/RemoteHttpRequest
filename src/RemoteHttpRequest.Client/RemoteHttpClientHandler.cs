using Google.Protobuf;
using Grpc.Core;
using RemoteHttpRequest.Shared;
using System;
using System.Buffers;
using System.Net.Http.Headers;
using System.Reflection.Metadata;
using System.Text.Json;
using System.Threading.Channels;
using static RemoteHttpRequest.Proto.HttpService;

namespace RemoteHttpRequest.Client;

public class RemoteHttpClientHandler : HttpClientHandler
{
    public int MaxWriteBytesSize { get; set; } = 1024 * 1024;
    private readonly HttpServiceClient client;

    public RemoteHttpClientHandler(HttpServiceClient client)
    {
        this.client = client;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var streamingCall = client.Send();

        // Request
        var messageJson = System.Text.Json.JsonSerializer.Serialize(request, RemoteHttpRequestOptions.JsonSerializer);
        var contentHeaderJson = string.Empty;
        if (request.Content != null)
        {
            contentHeaderJson = System.Text.Json.JsonSerializer.Serialize(request.Content.Headers.ToArray(), RemoteHttpRequestOptions.JsonSerializer);
        }
        await streamingCall.RequestStream.WriteAsync(new Proto.HttpRequest()
        {
            Meta = new Proto.HttpMeta()
            {
                Message = messageJson,
                ContentHeader = contentHeaderJson,
            },
        }, cancellationToken).ConfigureAwait(false);
        if (request.Content != null)
        {
            using var writeStream = new WriteFuncStream(async (buffer, cancellationToken) =>
            {
                await streamingCall.RequestStream.WriteAsync(new Proto.HttpRequest()
                {
                    Content = ByteString.CopyFrom(buffer.Span),
                }, cancellationToken).ConfigureAwait(false);
            }, MaxWriteBytesSize);
            await request.Content.CopyToAsync(writeStream, cancellationToken).ConfigureAwait(false);
        }
        await streamingCall.RequestStream.WriteAsync(new Proto.HttpRequest()
        {
            Eof = true,
        }, cancellationToken).ConfigureAwait(false);

        // Response
        var meta = (HttpResponseMessage?)default;
        var contentHeader = string.Empty;
        using var memoryStream = new MemoryStream();
        var eof = false;
        await foreach (var message in streamingCall.ResponseStream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            switch (message.DataCase)
            {
                case Proto.HttpResponse.DataOneofCase.Meta:
                    meta = System.Text.Json.JsonSerializer.Deserialize<HttpResponseMessage>(message.Meta.Message);
                    contentHeader = message.Meta.ContentHeader;
                    break;
                case Proto.HttpResponse.DataOneofCase.Content:
                    await memoryStream.WriteAsync(message.Content.ToByteArray());
                    break;
                case Proto.HttpResponse.DataOneofCase.Eof:
                    eof = message.Eof;
                    break;
            }
        }
        if (meta == null)
        {
            throw new InvalidOperationException($"Not Receive. : {nameof(meta)}");
        }
        if (!eof)
        {
            throw new InvalidOperationException($"Not Receive. : {nameof(eof)}");
        }
        meta.Content = new ByteArrayContent(memoryStream.ToArray());
        if (string.IsNullOrEmpty(contentHeader))
        {
            var headers = System.Text.Json.JsonSerializer.Deserialize<IEnumerable<KeyValuePair<string, IEnumerable<string>>>>(contentHeader, RemoteHttpRequestOptions.JsonSerializer);
            if (headers == null)
            {
                throw new InvalidOperationException($"Deserialize Error : {nameof(HttpContentHeaders)}\n{contentHeader}");
            }
            foreach (var header in headers)
            {
                meta.Content.Headers.Add(header.Key, header.Value);
            }
        }
        return meta;
    }

}