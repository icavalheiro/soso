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

    [Fact]
    public async Task BackupErrorIncludesDropboxPermissionGuidance()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "soso-dropbox-error-test-" + Guid.NewGuid().ToString("N"));
        var keysPath = Path.Combine(dataPath, "keys");
        Directory.CreateDirectory(keysPath);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = dataPath }).Build();
            using var store = new Store(configuration);
            var handler = new DropboxUploadHandler { ReturnMissingScopeError = true };
            var service = new DropboxBackupService(store, new TestHttpClientFactory(handler), DataProtectionProvider.Create(new DirectoryInfo(keysPath)), NullLogger<DropboxBackupService>.Instance);
            store.SetDropboxConfiguration(service.Protect(new DropboxCredentials("app-key", "access-token")));

            var error = await Assert.ThrowsAsync<ApiException>(() => service.RunBackupAsync(CancellationToken.None, force: true));

            Assert.Contains("Dropbox App Console", error.Message, StringComparison.Ordinal);
            Assert.Contains("generate a new access token", error.Message, StringComparison.Ordinal);
            Assert.Equal(error.Message, store.GetBackupState().LastError);
            Assert.Single(handler.Requests);
            Assert.True(store.GetBackupState().NextBackupAttemptAt > DateTime.UtcNow);
            await service.RunBackupAsync(CancellationToken.None);
            Assert.Single(handler.Requests);
        }
        finally
        {
            if (Directory.Exists(dataPath))
            {
                Directory.Delete(dataPath, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("start")]
    [InlineData("append_v2")]
    [InlineData("finish")]
    public async Task InterruptedUploadsRestartWithANewSession(string interruptedPhase)
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "soso-dropbox-retry-test-" + Guid.NewGuid().ToString("N"));
        var keysPath = Path.Combine(dataPath, "keys");
        Directory.CreateDirectory(keysPath);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = dataPath }).Build();
            using var store = new Store(configuration);
            store.Images.Insert(new ImageAsset { OwnerId = "test", Content = new byte[9 * 1024 * 1024] });
            var handler = new DropboxUploadHandler { FailOnceAt = interruptedPhase };
            using var service = new DropboxBackupService(store, new TestHttpClientFactory(handler), DataProtectionProvider.Create(new DirectoryInfo(keysPath)), NullLogger<DropboxBackupService>.Instance);
            store.SetDropboxConfiguration(service.Protect(new DropboxCredentials("app-key", "access-token")));

            await service.RunBackupAsync(CancellationToken.None, force: true);

            Assert.Equal(2, handler.Requests.Count(request => request.Path.EndsWith("/start", StringComparison.Ordinal)));
            var finalRequests = handler.Requests.TakeLast(3).ToArray();
            Assert.EndsWith("/start", finalRequests[0].Path);
            Assert.EndsWith("/append_v2", finalRequests[1].Path);
            Assert.EndsWith("/finish", finalRequests[2].Path);
            Assert.Equal("session-test-2", finalRequests[1].Arguments.RootElement.GetProperty("cursor").GetProperty("session_id").GetString());
            Assert.Equal(4 * 1024 * 1024, finalRequests[1].Arguments.RootElement.GetProperty("cursor").GetProperty("offset").GetInt64());
            Assert.NotNull(store.GetBackupState().LastBackupAt);
            Assert.Null(store.GetBackupState().LastError);
            Assert.Null(store.GetBackupState().NextBackupAttemptAt);
        }
        finally
        {
            Directory.Delete(dataPath, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistentUploadFailuresHaveBoundedRetriesAndAutomaticCooldown(bool returnServerError)
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "soso-dropbox-cooldown-test-" + Guid.NewGuid().ToString("N"));
        var keysPath = Path.Combine(dataPath, "keys");
        Directory.CreateDirectory(keysPath);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = dataPath }).Build();
            using var store = new Store(configuration);
            var handler = new DropboxUploadHandler { AlwaysFail = true, ReturnServerError = returnServerError };
            using var service = new DropboxBackupService(store, new TestHttpClientFactory(handler), DataProtectionProvider.Create(new DirectoryInfo(keysPath)), NullLogger<DropboxBackupService>.Instance);
            store.SetDropboxConfiguration(service.Protect(new DropboxCredentials("app-key", "access-token")));
            var state = store.GetBackupState();
            state.LastModifiedAt = DateTime.UtcNow.AddHours(-2);
            state.LastBackedUpModificationAt = null;
            store.BackupStates.Update(state);

            await service.RunBackupAsync(CancellationToken.None);

            Assert.Equal(3, handler.Requests.Count);
            Assert.Null(store.GetBackupState().LastBackupAt);
            Assert.NotNull(store.GetBackupState().LastError);
            Assert.True(store.GetBackupState().NextBackupAttemptAt > DateTime.UtcNow);
            await service.RunBackupAsync(CancellationToken.None);
            Assert.Equal(3, handler.Requests.Count);
            // A manual request bypasses the cooldown and a successful backup clears it.
            handler.AlwaysFail = false;
            await service.RunBackupAsync(CancellationToken.None, force: true);
            Assert.NotNull(store.GetBackupState().LastBackupAt);
            Assert.Null(store.GetBackupState().LastError);
            Assert.Null(store.GetBackupState().NextBackupAttemptAt);
        }
        finally
        {
            Directory.Delete(dataPath, recursive: true);
        }
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class DropboxUploadHandler : HttpMessageHandler
    {
        public bool ReturnMissingScopeError { get; init; }
        public string? FailOnceAt { get; init; }
        public bool AlwaysFail { get; set; }
        public bool ReturnServerError { get; init; }
        private bool interrupted;
        private int sessions;
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var argumentsText = request.Headers.GetValues("Dropbox-API-Arg").Single();
            Assert.True(request.Headers.ExpectContinue);
            var content = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.RequestUri!.AbsolutePath, JsonDocument.Parse(argumentsText), content.LongLength));
            if (request.RequestUri.AbsolutePath.EndsWith("/start", StringComparison.Ordinal))
            {
                sessions++;
            }
            if (AlwaysFail || !interrupted && FailOnceAt is not null && request.RequestUri.AbsolutePath.EndsWith("/" + FailOnceAt, StringComparison.Ordinal))
            {
                interrupted = true;
                if (ReturnServerError)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") };
                }
                throw new HttpRequestException("Connection reset by peer", new IOException("Unable to write data to the transport connection."));
            }
            if (ReturnMissingScopeError)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("{\"error\":{\".tag\":\"missing_scope\",\"required_scope\":\"files.content.write\"},\"error_summary\":\"missing_scope/\"}", Encoding.UTF8, "application/json")
                };
            }
            var response = request.RequestUri.AbsolutePath.EndsWith("/start", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { session_id = sessions == 1 ? "session-test" : $"session-test-{sessions}" }), Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            return response;
        }
    }

    private sealed record CapturedRequest(string Path, JsonDocument Arguments, long ContentLength);
}
