using Google.Protobuf;
using Grpc.Core;
using RemoteHttpRequest.Shared;
using System.Buffers;
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

        // MetaData
        var metaData = new Proto.HttpMeta()
        {
            Message = messageJson,
            ContentExists = false,
        };
        HttpHeaderUtility.AddHttpHeaders(metaData.RequestHeaders, request.Headers);
        if (request.Content != null)
        {
            metaData.ContentExists = true;
            HttpHeaderUtility.AddHttpHeaders(metaData.ContentHeaders, request.Content.Headers);
        }

        // Send Meta
        await streamingCall.RequestStream.WriteAsync(new Proto.HttpRequest() { Meta = metaData }, cancellationToken).ConfigureAwait(false);
        if (request.Content != null)
        {
            // Send Content
            using var writeStream = new WriteFuncStream(async (buffer, cancellationToken) =>
            {
                await streamingCall.RequestStream.WriteAsync(new Proto.HttpRequest()
                {
                    Content = ByteString.CopyFrom(buffer.Span),
                }, cancellationToken).ConfigureAwait(false);
            }, MaxWriteBytesSize);
            await request.Content.CopyToAsync(writeStream, cancellationToken).ConfigureAwait(false);
        }

        // Send Eof
        await streamingCall.RequestStream.WriteAsync(new Proto.HttpRequest() { Eof = true, }, cancellationToken).ConfigureAwait(false);

        // Response
        var meta = (Proto.HttpMeta?)default;
        using var memoryStream = new MemoryStream();
        var eof = false;
        await foreach (var message in streamingCall.ResponseStream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            switch (message.DataCase)
            {
                case Proto.HttpResponse.DataOneofCase.Meta:
                    meta = message.Meta;
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

        // Deserialize
        var response = System.Text.Json.JsonSerializer.Deserialize<HttpResponseMessage>(meta.Message);
        if (response == null)
        {
            throw new InvalidOperationException($"Deserialize Error. : {meta.Message}");
        }
        foreach (var header in meta.RequestHeaders)
        {
            response.Headers.Add(header.Key, header.Values);
        }
        if (meta.ContentExists)
        {
            response.Content = new ByteArrayContent(memoryStream.ToArray());
            foreach (var header in meta.ContentHeaders)
            {
                response.Content.Headers.Add(header.Key, header.Values);
            }
        }
        return response;
    }

}