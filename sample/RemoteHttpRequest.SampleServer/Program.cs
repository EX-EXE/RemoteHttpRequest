using RemoteHttpRequest.SampleServer.Services;
using RemoteHttpRequest.Server;
using RemoteHttpRequest.Shared;

namespace RemoteHttpRequest.SampleServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddGrpc();
            builder.Services.AddHttpClient<RemoteHttpRequestService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            app.MapGrpcService<RemoteHttpRequestService>();
            app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

            app.Run();
        }
    }
}