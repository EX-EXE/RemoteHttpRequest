using Google.Protobuf;
using Microsoft.Extensions.Logging;
using RemoteHttpRequest.Proto;
using RemoteHttpRequest.Shared;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using Grpc.Core;
using System.Net.Http.Headers;
using System.Reflection.PortableExecutable;

namespace RemoteHttpRequest.Server;

public partial class RemoteHttpRequestService : Proto.HttpService.HttpServiceBase
{
    public int ChannelCapacity { get; set; } = 128;
    public int MaxWriteBytesSize { get; set; } = 1024 * 1024;

    private readonly ILogger<RemoteHttpRequestService> logger;
    private readonly HttpClient httpClient;
    public RemoteHttpRequestService(
        ILogger<RemoteHttpRequestService> logger,
        HttpClient httpClient)
    {
        this.logger = logger;
        this.httpClient = httpClient;
    }

    public override async Task Send(IAsyncStreamReader<HttpRequest> requestStream, IServerStreamWriter<HttpResponse> responseStream, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();

        // Meta
        if (!await requestStream.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Not Receive Request Message.");
        }
        var firstReceive = requestStream.Current;
        if (firstReceive.DataCase != HttpRequest.DataOneofCase.Meta)
        {
            throw new InvalidOperationException($"Incorrect Receive Order. : {firstReceive.DataCase}");
        }

        // Message
        var messageData = ReceiveRequestMessage(firstReceive);

        // Content
        var receiveTask = default(Task);
        var buffer = Channel.CreateBounded<ReadOnlyMemory<byte>>(ChannelCapacity);
        using var content = new ChannelReaderHttpContent(buffer.Reader);
        if (!string.IsNullOrEmpty(firstReceive.Meta.ContentHeader))
        {
            var headers = System.Text.Json.JsonSerializer.Deserialize<IEnumerable<KeyValuePair<string, IEnumerable<string>>>>(firstReceive.Meta.ContentHeader, RemoteHttpRequestOptions.JsonSerializer);
            if (headers == null)
            {
                throw new InvalidOperationException($"Deserialize Error : {nameof(HttpContentHeaders)}\n{firstReceive.Meta}");
            }

            // Buffer
            foreach (var header in headers)
            {
                content.Headers.Add(header.Key, header.Value);
            }

            // Send
            receiveTask = Task.Run(async () =>
            {
                await foreach (var message in requestStream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    switch (message.DataCase)
                    {
                        case Proto.HttpRequest.DataOneofCase.Content:
                            await buffer.Writer.WriteAsync(message.Content.Memory, cancellationToken: cancellationToken).ConfigureAwait(false);
                            break;
                        case Proto.HttpRequest.DataOneofCase.Eof:
                            buffer.Writer.Complete();
                            return;
                    }
                }
            }, cancellationToken);

            messageData.Content = content;
        }

        // Http Send
        using var responseData = await SendHttpRequestAsync(messageData, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (receiveTask != null)
        {
            await receiveTask;
        }

        // Response 
        await WriteResponseStream(responseData,responseStream,cancellationToken: cancellationToken).ConfigureAwait(false);
    }


    protected virtual HttpRequestMessage ReceiveRequestMessage(HttpRequest request)
    {
        // Message
        var messageData = System.Text.Json.JsonSerializer.Deserialize<HttpRequestMessage>(request.Meta.Message, RemoteHttpRequestOptions.JsonSerializer);
        if (messageData == null)
        {
            throw new InvalidOperationException($"Deserialize Error : {nameof(HttpRequestMessage)}\n{request.Meta}");
        }
        return messageData;
    }

    protected virtual Task<HttpResponseMessage> SendHttpRequestAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken)
    {
        return httpClient.SendAsync(httpRequestMessage, cancellationToken: cancellationToken);
    }

    protected virtual async ValueTask WriteResponseStream(
        HttpResponseMessage httpResponseMessage,
        IServerStreamWriter<HttpResponse> responseStream,
        CancellationToken cancellationToken)
    {
        // Message
        var responseMetaData = new HttpMeta()
        {
            Message = System.Text.Json.JsonSerializer.Serialize(httpResponseMessage, RemoteHttpRequestOptions.JsonSerializer),
            ContentHeader = string.Empty
        };

        // Content Header
        if (httpResponseMessage.Content != null)
        {
            responseMetaData.ContentHeader = System.Text.Json.JsonSerializer.Serialize(
                httpResponseMessage.Content.Headers.ToArray(), 
                RemoteHttpRequestOptions.JsonSerializer);
        }
        await responseStream.WriteAsync(new HttpResponse() { Meta = responseMetaData }, cancellationToken).ConfigureAwait(false);

        // Content
        if (httpResponseMessage.Content != null)
        {
            using var writeStream = new WriteFuncStream(async (buffer, cancellationToken) =>
            {
                await responseStream.WriteAsync(new Proto.HttpResponse()
                {
                    Content = ByteString.CopyFrom(buffer.Span),
                }, cancellationToken).ConfigureAwait(false);
            }, MaxWriteBytesSize);
            await httpResponseMessage.Content.CopyToAsync(writeStream, cancellationToken).ConfigureAwait(false);
        }

        // Eof
        await responseStream.WriteAsync(new Proto.HttpResponse()
        {
            Eof = true,
        }, cancellationToken).ConfigureAwait(false);
    }

    class ChannelReaderHttpContent : HttpContent
    {
        private ChannelReader<ReadOnlyMemory<byte>> channel;

        public ChannelReaderHttpContent(
            ChannelReader<ReadOnlyMemory<byte>> channel)
        {
            this.channel = channel;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            ReadOnlyMemory<byte> buffer;
            while (await channel.WaitToReadAsync().ConfigureAwait(false))
            {
                while (channel.TryRead(out buffer))
                {
                    await stream.WriteAsync(buffer).ConfigureAwait(false);
                }
            }
            stream.Flush();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1L;
            return false;
        }
    }

}