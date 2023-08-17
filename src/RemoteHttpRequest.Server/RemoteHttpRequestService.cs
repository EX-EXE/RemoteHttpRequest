using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using RemoteHttpRequest.Proto;
using RemoteHttpRequest.Shared;
using System.Net;
using System.Threading.Channels;

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
        var receiveData = requestStream.Current;
        if (receiveData.DataCase != HttpRequest.DataOneofCase.Meta)
        {
            throw new InvalidOperationException($"Incorrect Receive Order. : {receiveData.DataCase}");
        }
        var receiveMetaData = receiveData.Meta;

        // Message
        var requestMessage = ReceiveRequestMessage(receiveMetaData, context);

        // Content
        var buffer = Channel.CreateBounded<ReadOnlyMemory<byte>>(ChannelCapacity);
        using var content = new ChannelReaderHttpContent(buffer.Reader);
        var receiveTask = default(Task);
        if (receiveMetaData.ContentExists)
        {
            requestMessage.Content = content;
            // Receive Content Header
            foreach (var header in receiveData.Meta.ContentHeaders)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Values);
            }
            // Receive Content Data
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

        }
        // Http Send
        using var responseData = await SendHttpRequestAsync(requestMessage, context, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (receiveTask != null)
        {
            await receiveTask.ConfigureAwait(false);
        }
        // Response 
        await WriteResponseStream(responseData, responseStream, context, cancellationToken: cancellationToken).ConfigureAwait(false);
    }


    protected virtual HttpRequestMessage ReceiveRequestMessage(HttpMeta metaData, ServerCallContext context)
    {
        // Message
        var requestMessage = System.Text.Json.JsonSerializer.Deserialize<HttpRequestMessage>(metaData.Message, RemoteHttpRequestOptions.JsonSerializer);
        if (requestMessage == null)
        {
            throw new InvalidOperationException($"Deserialize Error : {nameof(HttpRequestMessage)}\n{metaData.Message}");
        }
        foreach (var header in metaData.RequestHeaders)
        {
            requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Values);
        }
        return requestMessage;
    }

    protected virtual Task<HttpResponseMessage> SendHttpRequestAsync(HttpRequestMessage httpRequestMessage, ServerCallContext context, CancellationToken cancellationToken)
    {
        return httpClient.SendAsync(httpRequestMessage, cancellationToken: cancellationToken);
    }

    protected virtual async ValueTask WriteResponseStream(
        HttpResponseMessage httpResponseMessage,
        IServerStreamWriter<HttpResponse> responseStream,
        ServerCallContext context,
        CancellationToken cancellationToken)
    {
        // Meta
        var responseMetaData = new HttpMeta()
        {
            Message = System.Text.Json.JsonSerializer.Serialize(httpResponseMessage, RemoteHttpRequestOptions.JsonSerializer),
            ContentExists = false
        };
        HttpHeaderUtility.AddHttpHeaders(responseMetaData.RequestHeaders, httpResponseMessage.Headers);

        // Meta Content
        if (httpResponseMessage.Content != null)
        {
            responseMetaData.ContentExists = true;
            HttpHeaderUtility.AddHttpHeaders(responseMetaData.ContentHeaders, httpResponseMessage.Content.Headers);
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
        await responseStream.WriteAsync(new Proto.HttpResponse() { Eof = true, }, cancellationToken).ConfigureAwait(false);
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