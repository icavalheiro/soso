using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
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
            store.SaveDropboxConnection("app-key", service.Protect(new DropboxCredentials("app-key", "refresh-token", "access-token", DateTimeOffset.UtcNow.AddHours(4))));

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
            Assert.Empty(handler.TokenRequests);
            Assert.All(handler.Requests, request => Assert.Equal("Bearer access-token", request.Authorization));
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
            store.SaveDropboxConnection("app-key", service.Protect(new DropboxCredentials("app-key", "refresh-token", "access-token", DateTimeOffset.UtcNow.AddHours(4))));

            var error = await Assert.ThrowsAsync<ApiException>(() => service.RunBackupAsync(CancellationToken.None, force: true));

            Assert.Contains("Dropbox App Console", error.Message, StringComparison.Ordinal);
            Assert.Contains("reconnect Dropbox", error.Message, StringComparison.Ordinal);
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
            store.SaveDropboxConnection("app-key", service.Protect(new DropboxCredentials("app-key", "refresh-token", "access-token", DateTimeOffset.UtcNow.AddHours(4))));

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
            store.SaveDropboxConnection("app-key", service.Protect(new DropboxCredentials("app-key", "refresh-token", "access-token", DateTimeOffset.UtcNow.AddHours(4))));
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

    [Fact]
    public async Task BackupRefreshesAnExpiredAccessTokenAndPersistsIt()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "soso-dropbox-refresh-test-" + Guid.NewGuid().ToString("N"));
        var keysPath = Path.Combine(dataPath, "keys");
        Directory.CreateDirectory(keysPath);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = dataPath }).Build();
            using var store = new Store(configuration);
            var handler = new DropboxUploadHandler();
            using var service = new DropboxBackupService(store, new TestHttpClientFactory(handler), DataProtectionProvider.Create(new DirectoryInfo(keysPath)), NullLogger<DropboxBackupService>.Instance);
            store.SaveDropboxConnection("app-key", service.Protect(new DropboxCredentials("app-key", "refresh-test", "expired-access-token", DateTimeOffset.UtcNow.AddMinutes(-1))));

            await service.RunBackupAsync(CancellationToken.None, force: true);

            var tokenRequest = Assert.Single(handler.TokenRequests);
            Assert.Contains("grant_type=refresh_token", tokenRequest.Body, StringComparison.Ordinal);
            Assert.Contains("refresh_token=refresh-test", tokenRequest.Body, StringComparison.Ordinal);
            Assert.All(handler.Requests, request => Assert.Equal("Bearer refreshed-access-token", request.Authorization));
            var saved = service.Unprotect(store.GetDropboxConfiguration()!.ProtectedCredentials);
            Assert.Equal("refreshed-access-token", saved.AccessToken);
            Assert.Equal("refresh-test", saved.RefreshToken);
            Assert.True(saved.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddHours(3));
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
    public void AuthorizationUrlUsesPkceOfflineAccessAndTheCallbackRedirectUri()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "soso-dropbox-authorize-test-" + Guid.NewGuid().ToString("N"));
        var keysPath = Path.Combine(dataPath, "keys");
        Directory.CreateDirectory(keysPath);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = dataPath }).Build();
            using var store = new Store(configuration);
            var service = new DropboxBackupService(store, new TestHttpClientFactory(new DropboxUploadHandler()), DataProtectionProvider.Create(new DirectoryInfo(keysPath)), NullLogger<DropboxBackupService>.Instance);
            store.SaveDropboxAppKey("app-key");

            var url = service.CreateAuthorizationUrl(BrowserContext(), "admin");

            Assert.StartsWith("https://www.dropbox.com/oauth2/authorize?", url, StringComparison.Ordinal);
            var query = QueryHelpers.ParseQuery(new Uri(url).Query);
            Assert.Equal("app-key", query["client_id"]);
            Assert.Equal("code", query["response_type"]);
            Assert.Equal("offline", query["token_access_type"]);
            Assert.Equal("S256", query["code_challenge_method"]);
            Assert.Equal("https://soso.example.test/api/admin/backup/dropbox/callback", query["redirect_uri"]);
            Assert.False(string.IsNullOrWhiteSpace(query["code_challenge"]));
            Assert.False(string.IsNullOrWhiteSpace(query["state"]));
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
    public async Task CallbackExchangesTheCodeAndStoresAnOfflineRefreshToken()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "soso-dropbox-callback-test-" + Guid.NewGuid().ToString("N"));
        var keysPath = Path.Combine(dataPath, "keys");
        Directory.CreateDirectory(keysPath);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = dataPath }).Build();
            using var store = new Store(configuration);
            var handler = new DropboxUploadHandler();
            var service = new DropboxBackupService(store, new TestHttpClientFactory(handler), DataProtectionProvider.Create(new DirectoryInfo(keysPath)), NullLogger<DropboxBackupService>.Instance);
            store.Accounts.Insert(new Account { Id = "admin", Email = "admin@example.test", IsAdmin = true });
            store.SaveDropboxAppKey("app-key");
            var query = QueryHelpers.ParseQuery(new Uri(service.CreateAuthorizationUrl(BrowserContext(), "admin")).Query);

            var result = await service.CompleteAuthorizationAsync("auth-code", query["state"], null, CancellationToken.None);

            Assert.Equal("/?dropbox=connected", result);
            var tokenRequest = Assert.Single(handler.TokenRequests);
            Assert.Contains("grant_type=authorization_code", tokenRequest.Body, StringComparison.Ordinal);
            Assert.Contains("code=auth-code", tokenRequest.Body, StringComparison.Ordinal);
            Assert.Contains("client_id=app-key", tokenRequest.Body, StringComparison.Ordinal);
            var exchange = QueryHelpers.ParseQuery("?" + tokenRequest.Body);
            var verifier = exchange["code_verifier"].ToString();
            Assert.False(string.IsNullOrWhiteSpace(verifier));
            var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Assert.Equal(query["code_challenge"], challenge);
            var saved = service.Unprotect(store.GetDropboxConfiguration()!.ProtectedCredentials);
            Assert.Equal("app-key", saved.AppKey);
            Assert.Equal("refresh-test", saved.RefreshToken);
            Assert.Equal("access-test", saved.AccessToken);
            Assert.True(service.IsConnected(store.GetDropboxConfiguration()!));
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
    public async Task CallbackRejectsCancelledTamperedAndNonAdminAuthorizations()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "soso-dropbox-reject-test-" + Guid.NewGuid().ToString("N"));
        var keysPath = Path.Combine(dataPath, "keys");
        Directory.CreateDirectory(keysPath);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = dataPath }).Build();
            using var store = new Store(configuration);
            var service = new DropboxBackupService(store, new TestHttpClientFactory(new DropboxUploadHandler()), DataProtectionProvider.Create(new DirectoryInfo(keysPath)), NullLogger<DropboxBackupService>.Instance);
            store.SaveDropboxAppKey("app-key");

            Assert.Equal("/?dropbox=cancelled", await service.CompleteAuthorizationAsync(null, null, "access_denied", CancellationToken.None));
            Assert.Equal("/?dropbox=error", await service.CompleteAuthorizationAsync("code", "tampered-state", null, CancellationToken.None));
            // A valid state for an account that is no longer an active administrator must not connect.
            store.Accounts.Insert(new Account { Id = "member", Email = "member@example.test", IsAdmin = false });
            var query = QueryHelpers.ParseQuery(new Uri(service.CreateAuthorizationUrl(BrowserContext(), "member")).Query);
            Assert.Equal("/?dropbox=error", await service.CompleteAuthorizationAsync("code", query["state"], null, CancellationToken.None));
            Assert.Null(store.GetDropboxConfiguration()!.ProtectedCredentials);
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
    public async Task LegacyAccessTokenCredentialsAreTreatedAsDisconnectedButKeepTheAppKey()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "soso-dropbox-legacy-test-" + Guid.NewGuid().ToString("N"));
        var keysPath = Path.Combine(dataPath, "keys");
        Directory.CreateDirectory(keysPath);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = dataPath }).Build();
            using var store = new Store(configuration);
            var protection = DataProtectionProvider.Create(new DirectoryInfo(keysPath));
            var service = new DropboxBackupService(store, new TestHttpClientFactory(new DropboxUploadHandler()), protection, NullLogger<DropboxBackupService>.Instance);
            var legacy = protection.CreateProtector("Soso.DropboxBackup.Credentials.v1").Protect("""{"appKey":"legacy-key","accessToken":"legacy-token"}""");
            store.SaveDropboxConnection("", legacy);

            var saved = store.GetDropboxConfiguration()!;
            Assert.Equal("legacy-key", service.GetAppKey(saved));
            Assert.False(service.IsConnected(saved));
            var error = await Assert.ThrowsAsync<ApiException>(() => service.RunBackupAsync(CancellationToken.None, force: true));
            Assert.Contains("Connect Dropbox", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(dataPath))
            {
                Directory.Delete(dataPath, recursive: true);
            }
        }
    }

    private static DefaultHttpContext BrowserContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("soso.example.test");
        return context;
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
        public List<CapturedTokenRequest> TokenRequests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/oauth2/token")
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                TokenRequests.Add(new CapturedTokenRequest(request.RequestUri.AbsolutePath, body));
                var accessToken = body.Contains("grant_type=refresh_token", StringComparison.Ordinal) ? "refreshed-access-token" : "access-test";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { access_token = accessToken, token_type = "bearer", expires_in = 14400, refresh_token = "refresh-test" }), Encoding.UTF8, "application/json")
                };
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/token/revoke", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            }
            var argumentsText = request.Headers.GetValues("Dropbox-API-Arg").Single();
            Assert.True(request.Headers.ExpectContinue);
            var content = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.RequestUri.AbsolutePath, JsonDocument.Parse(argumentsText), content.LongLength, request.Headers.Authorization?.ToString()));
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

    private sealed record CapturedRequest(string Path, JsonDocument Arguments, long ContentLength, string? Authorization);
    private sealed record CapturedTokenRequest(string Path, string Body);
}