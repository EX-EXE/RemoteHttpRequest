# RemoteHttpRequest
Use Grpc and HttpClientHandler to send and receive HTTP requests from C# HttpClient via an external server.

# Quick Start

## Client
Install by nuget
PM> Install-Package [RemoteHttpRequest.Client](https://www.nuget.org/packages/RemoteHttpRequest.Client/)
```csharp
// Grpc
var grpcChannel = GrpcChannel.ForAddress("https://localhost:8080");
var grpcClient = new HttpServiceClient(grpcChannel);

// HttpClient
var handler = new RemoteHttpClientHandler(grpcClient);
var httpClient = new HttpClient(handler);
// var response = await httpClient.SendAsync(message);
```

## Server (ASP.NET Core gRPC Service)
Install by nuget
PM> Install-Package [RemoteHttpRequest.Server](https://www.nuget.org/packages/RemoteHttpRequest.Server/)

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
builder.Services.AddHttpClient<RemoteHttpRequestService>();

var app = builder.Build();

app.MapGrpcService<RemoteHttpRequestService>();
app.Run();
```
