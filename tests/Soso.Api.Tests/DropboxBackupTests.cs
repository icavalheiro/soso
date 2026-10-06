using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Soso.Api;
using Xunit;

namespace Soso.Api.Tests;

[Collection("LiteDB tests")]
public sealed class DropboxBackupTests
{
    [Fact]
    public async Task BackupUploadsUsingDropboxSnakeCaseSessionArguments()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "soso-dropbox-test-" + Guid.NewGuid().ToString("N"));
        var keysPath = Path.Combine(dataPath, "keys");
        Directory.CreateDirectory(keysPath);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = dataPath }).Build();
            using var store = new Store(configuration);
            var content = new byte[9 * 1024 * 1024];
            Random.Shared.NextBytes(content);
            store.Images.Insert(new ImageAsset { Id = Guid.NewGuid().ToString("N"), OwnerId = "test", Content = content });

            var handler = new DropboxUploadHandler();
            var clients = new TestHttpClientFactory(handler);
            var protection = DataProtectionProvider.Create(new DirectoryInfo(keysPath));
            var service = new DropboxBackupService(store, clients, protection, NullLogger<DropboxBackupService>.Instance);
            store.SetDropboxConfiguration(service.Protect(new DropboxCredentials("app-key", "access-token")));

            await service.RunBackupAsync(CancellationToken.None, force: true);

            Assert.Equal(3, handler.Requests.Count);
            Assert.Equal("/2/files/upload_session/start", handler.Requests[0].Path);
            Assert.Equal("false", handler.Requests[0].Arguments.RootElement.GetProperty("close").GetRawText());
            Assert.Equal(4 * 1024 * 1024, handler.Requests[0].ContentLength);
            Assert.Equal("/2/files/upload_session/append_v2", handler.Requests[1].Path);
            var cursor = handler.Requests[1].Arguments.RootElement.GetProperty("cursor");
            Assert.Equal("session-test", cursor.GetProperty("session_id").GetString());
            Assert.Equal(4 * 1024 * 1024, cursor.GetProperty("offset").GetInt64());
            Assert.Equal(4 * 1024 * 1024, handler.Requests[1].ContentLength);
            Assert.Equal("/2/files/upload_session/finish", handler.Requests[2].Path);
            var finish = handler.Requests[2].Arguments.RootElement;
            Assert.Equal("session-test", finish.GetProperty("cursor").GetProperty("session_id").GetString());
            Assert.Equal(handler.Requests[0].ContentLength + handler.Requests[1].ContentLength, finish.GetProperty("cursor").GetProperty("offset").GetInt64());
            Assert.Equal("/soso-backup.db", finish.GetProperty("commit").GetProperty("path").GetString());
            Assert.Equal("overwrite", finish.GetProperty("commit").GetProperty("mode").GetString());
            Assert.True(handler.Requests[2].ContentLength > 0);
        }
        finally
        {
            if (Directory.Exists(dataPath))
            {
                Directory.Delete(dataPath, recursive: true);
            }
        }
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class DropboxUploadHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var argumentsText = request.Headers.GetValues("Dropbox-API-Arg").Single();
            var content = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.RequestUri!.AbsolutePath, JsonDocument.Parse(argumentsText), content.LongLength));
            var response = request.RequestUri.AbsolutePath.EndsWith("/start", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"session_id\":\"session-test\"}", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            return response;
        }
    }

    private sealed record CapturedRequest(string Path, JsonDocument Arguments, long ContentLength);
}
