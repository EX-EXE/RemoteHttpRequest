using Grpc.Net.Client;
using RemoteHttpRequest.Client;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using static RemoteHttpRequest.Proto.HttpService;

namespace RemoteHttpRequest.SampleClient
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            //using var fs = new FileStream(@"", FileMode.Open, FileAccess.Read);
            //var streamContent = new StreamContent(fs);
            //streamContent.Headers.ContentDisposition =
            //       new ContentDispositionHeaderValue("form-data")
            //       {
            //           Name = "upfile",
            //           FileName = ""
            //       };



            var parameters = new Dictionary<string, string>()
            {
                { "name", "Test" },
            };
            var jsonString = System.Text.Json.JsonSerializer.Serialize(parameters);
            var content = new StringContent(jsonString, Encoding.UTF8, @"application/json");
            content.Headers.Add("Test", "Test2");

            var channel = GrpcChannel.ForAddress("https://localhost:7007");
            var client = new HttpServiceClient(channel);
            var httpClient = new HttpClient(new RemoteHttpClientHandler(client));
            
            //var httpClient = new HttpClient();
            var handler = new HttpRequestMessage()
            {
                RequestUri = new Uri(@"http://127.0.0.1:8080/post"),
                Content = content,
                Method = HttpMethod.Post,
            };
            handler.Headers.Add($"Authorization", "Bearer x");

            var response = await httpClient.SendAsync(handler);
            var readString = await response.Content.ReadAsStringAsync();
        }
    }
}