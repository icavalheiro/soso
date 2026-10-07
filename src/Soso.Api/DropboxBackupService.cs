using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Soso.Api;

public sealed record DropboxCredentials(string AppKey, string RefreshToken, string AccessToken, DateTimeOffset AccessTokenExpiresAt);

public sealed record DropboxAuthorizationState(string UserId, string AppKey, string CodeVerifier, string RedirectUri, DateTimeOffset ExpiresAt);

public sealed class DropboxBackupService(Store store, IHttpClientFactory clients, IDataProtectionProvider protection, ILogger<DropboxBackupService> logger) : BackgroundService
{
    private static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions DropboxApiJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly IDataProtector protector = protection.CreateProtector("Soso.DropboxBackup.Credentials.v1");
    private readonly IDataProtector authorizationProtector = protection.CreateProtector("Soso.DropboxBackup.Authorization.v1");
    private readonly SemaphoreSlim backupGate = new(1, 1);
    private readonly SemaphoreSlim refreshGate = new(1, 1);

    public string Protect(DropboxCredentials credentials) => protector.Protect(JsonSerializer.Serialize(credentials, JsonOptions));

    public DropboxCredentials Unprotect(string value) => TryUnprotect(value)
        ?? throw new ApiException(500, "Dropbox credentials could not be read. Reconnect Dropbox.");

    private DropboxCredentials? TryUnprotect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<DropboxCredentials>(protector.Unprotect(value), JsonOptions);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    public string GetAppKey(DropboxBackupConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.AppKey))
        {
            return configuration.AppKey;
        }
        // Credentials saved before the app key was stored separately still carry it.
        return TryUnprotect(configuration.ProtectedCredentials)?.AppKey ?? "";
    }

    public bool IsConnected(DropboxBackupConfiguration configuration)
        => !string.IsNullOrWhiteSpace(TryUnprotect(configuration.ProtectedCredentials)?.RefreshToken);

    public static string RedirectUri(HttpContext context)
        => $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}/api/admin/backup/dropbox/callback";

    public string CreateAuthorizationUrl(HttpContext context, string userId)
    {
        var configuration = store.GetDropboxConfiguration();
        var appKey = configuration is null ? "" : GetAppKey(configuration);
        if (string.IsNullOrWhiteSpace(appKey))
        {
            throw new ApiException(400, "Enter the Dropbox app key before connecting.");
        }
        var redirectUri = RedirectUri(context);
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = authorizationProtector.Protect(JsonSerializer.Serialize(
            new DropboxAuthorizationState(userId, appKey, verifier, redirectUri, DateTimeOffset.UtcNow.Add(AuthorizationLifetime)), JsonOptions));
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = appKey,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["token_access_type"] = "offline",
            ["state"] = state
        };
        var query = string.Join("&", parameters.Select(parameter => $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));
        return $"https://www.dropbox.com/oauth2/authorize?{query}";
    }

    public async Task<string> CompleteAuthorizationAsync(string? code, string? state, string? error, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return error == "access_denied" ? "/?dropbox=cancelled" : "/?dropbox=error";
        }
        DropboxAuthorizationState? pending;
        try
        {
            pending = string.IsNullOrWhiteSpace(state)
                ? null
                : JsonSerializer.Deserialize<DropboxAuthorizationState>(authorizationProtector.Unprotect(state), JsonOptions);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            pending = null;
        }
        if (pending is null || pending.ExpiresAt < DateTimeOffset.UtcNow || string.IsNullOrWhiteSpace(code))
        {
            logger.LogWarning("Dropbox authorization callback was rejected because its state was missing, expired or invalid.");
            return "/?dropbox=error";
        }
        if (store.Accounts.FindById(pending.UserId) is not { IsAdmin: true, Disabled: false })
        {
            logger.LogWarning("Dropbox authorization callback was rejected because its initiating administrator is no longer active.");
            return "/?dropbox=error";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/oauth2/token");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["client_id"] = pending.AppKey,
            ["redirect_uri"] = pending.RedirectUri,
            ["code_verifier"] = pending.CodeVerifier
        });
        using var response = await clients.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await ReadDropboxErrorAsync(response, cancellationToken);
            logger.LogWarning("Dropbox code exchange failed with HTTP {StatusCode}; error {DropboxError}", (int)response.StatusCode, detail ?? "no error details");
            return "/?dropbox=error";
        }
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var refreshToken = document.RootElement.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null;
        var accessToken = document.RootElement.TryGetProperty("access_token", out var access) ? access.GetString() : null;
        if (string.IsNullOrWhiteSpace(refreshToken) || string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogWarning("Dropbox code exchange did not return an offline refresh token.");
            return "/?dropbox=error";
        }
        var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds) ? seconds : 14400;
        var credentials = new DropboxCredentials(pending.AppKey, refreshToken, accessToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
        store.SaveDropboxConnection(pending.AppKey, Protect(credentials));
        return "/?dropbox=connected";
    }

    public async Task TryRevokeAsync(CancellationToken cancellationToken)
    {
        var configuration = store.GetDropboxConfiguration();
        var credentials = configuration is null ? null : TryUnprotect(configuration.ProtectedCredentials);
        if (credentials is null || string.IsNullOrWhiteSpace(credentials.RefreshToken))
        {
            return;
        }
        try
        {
            var active = await GetAccessTokenAsync(credentials, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/auth/token/revoke");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", active.AccessToken);
            using var response = await clients.CreateClient().SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Dropbox token revocation returned HTTP {StatusCode}.", (int)response.StatusCode);
            }
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning(exception, "Dropbox token could not be revoked; the saved connection is still removed.");
        }
    }

    private async Task<DropboxCredentials> GetAccessTokenAsync(DropboxCredentials credentials, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(credentials.AccessToken) && credentials.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return credentials;
        }
        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            var configuration = store.GetDropboxConfiguration();
            var current = configuration is null ? null : TryUnprotect(configuration.ProtectedCredentials);
            if (current is not null && current.RefreshToken == credentials.RefreshToken
                && !string.IsNullOrWhiteSpace(current.AccessToken) && current.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return current;
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/oauth2/token");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = credentials.RefreshToken,
                ["client_id"] = credentials.AppKey
            });
            using var response = await clients.CreateClient().SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await ReadDropboxErrorAsync(response, cancellationToken);
                logger.LogWarning("Dropbox token refresh failed with HTTP {StatusCode}; error {DropboxError}", (int)response.StatusCode, detail ?? "no error details");
                throw new ApiException(400, "Dropbox rejected the saved connection. Reconnect Dropbox.");
            }
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var accessToken = document.RootElement.TryGetProperty("access_token", out var access) ? access.GetString() : null;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new ApiException(502, "Dropbox did not return a refreshed access token. Reconnect Dropbox.");
            }
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds) ? seconds : 14400;
            var refreshed = credentials with { AccessToken = accessToken, AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn) };
            store.UpdateDropboxCredentials(Protect(refreshed));
            return refreshed;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async Task RunBackupAsync(CancellationToken cancellationToken, bool force = false)
    {
        if (force)
        {
            await backupGate.WaitAsync(cancellationToken);
        }
        else if (!await backupGate.WaitAsync(0, cancellationToken))
        {
            return;
        }
        string? snapshotPath = null;
        try
        {
            var configuration = store.GetDropboxConfiguration();
            if (configuration is null || !IsConnected(configuration))
            {
                if (force)
                {
                    throw new ApiException(400, "Connect Dropbox before requesting a backup.");
                }
                return;
            }
            var state = store.GetBackupState();
            var now = DateTime.UtcNow;
            if (!force && state.NextBackupAttemptAt > now)
            {
                return;
            }
            var credentials = await GetAccessTokenAsync(Unprotect(configuration.ProtectedCredentials), cancellationToken);
            var changedSinceBackup = state.LastBackedUpModificationAt is null || state.LastModifiedAt > state.LastBackedUpModificationAt;
            var quietForTenMinutes = changedSinceBackup && now - state.LastModifiedAt >= TimeSpan.FromMinutes(10);
            var lastSaveAt = state.LastBackupAt ?? state.LastBackedUpModificationAt ?? state.LastModifiedAt;
            var hourlyFallback = changedSinceBackup && now - lastSaveAt >= TimeSpan.FromHours(1);
            if (!force && !quietForTenMinutes && !hourlyFallback)
            {
                return;
            }

            snapshotPath = store.CreateBackupSnapshot(out var backedUpModificationAt);
            await UploadWithRetryAsync(credentials, snapshotPath, cancellationToken);
            store.RecordBackup(backedUpModificationAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var message = exception switch
            {
                ApiException => exception.Message,
                HttpRequestException { StatusCode: not null } => exception.Message,
                HttpRequestException => "The connection to Dropbox's upload server was interrupted. Check that the server/container can make outbound HTTPS connections to content.dropboxapi.com on port 443, then try again.",
                OperationCanceledException => "The Dropbox upload timed out. Check the server's outbound connection and try again.",
                IOException => "Could not read or create the database backup file. Check the server's data directory and available disk space.",
                _ => "Dropbox backup failed. Check the server logs for details."
            };
            if (exception is ApiException or HttpRequestException or OperationCanceledException)
            {
                logger.LogWarning("Dropbox backup failed: {Reason} Automatic retry in 15 minutes.", message);
            }
            else
            {
                logger.LogError(exception, "Dropbox backup failed.");
            }
            store.RecordBackupError(message);
            if (force)
            {
                throw exception is ApiException ? exception : new ApiException(502, message);
            }
        }
        finally
        {
            if (snapshotPath is not null)
            {
                try { File.Delete(snapshotPath); } catch (IOException) { }
            }
            backupGate.Release();
        }
    }

    private async Task UploadWithRetryAsync(DropboxCredentials credentials, string path, CancellationToken cancellationToken)
    {
        // A lost response can mean a chunk was accepted. Restart with a fresh session
        // instead of blindly replaying an append at a potentially obsolete offset.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await UploadAsync(credentials, path, cancellationToken);
                return;
            }
            catch (Exception exception) when (attempt < 3 && !cancellationToken.IsCancellationRequested
                && exception is HttpRequestException or OperationCanceledException)
            {
                logger.LogWarning("Dropbox upload interrupted on attempt {Attempt}; restarting with a fresh upload session.", attempt);
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }
    }

    private async Task UploadAsync(DropboxCredentials credentials, string path, CancellationToken cancellationToken)
    {
        const int chunkSize = 4 * 1024 * 1024;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, chunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length == 0)
        {
            throw new ApiException(500, "The database backup file is empty; Dropbox upload was not started.");
        }

        var firstChunkLength = checked((int)Math.Min(stream.Length, chunkSize));
        var firstChunk = new byte[firstChunkLength];
        await stream.ReadExactlyAsync(firstChunk, cancellationToken);

        using var startResponse = await SendUploadChunkAsync(
            credentials,
            "upload_session/start",
            "starting upload session",
            new { close = false },
            firstChunk,
            cancellationToken);
        using var startDocument = JsonDocument.Parse(await startResponse.Content.ReadAsStringAsync(cancellationToken));
        var sessionId = startDocument.RootElement.GetProperty("session_id").GetString()
            ?? throw new ApiException(502, "Dropbox did not return an upload session ID.");

        long offset = firstChunkLength;
        while (stream.Length - offset > chunkSize)
        {
            var chunk = new byte[chunkSize];
            await stream.ReadExactlyAsync(chunk, cancellationToken);
            using var appendResponse = await SendUploadChunkAsync(
                credentials,
                "upload_session/append_v2",
                $"uploading chunk at byte offset {offset}",
                new { cursor = new { session_id = sessionId, offset }, close = false },
                chunk,
                cancellationToken);
            offset += chunk.Length;
        }

        var remaining = checked((int)(stream.Length - offset));
        var finalChunk = new byte[remaining];
        await stream.ReadExactlyAsync(finalChunk, cancellationToken);
        using var finishResponse = await SendUploadChunkAsync(
            credentials,
            "upload_session/finish",
            "finishing upload session",
            new
            {
                cursor = new { session_id = sessionId, offset },
                commit = new { path = "/soso-backup.db", mode = "overwrite", autorename = false, mute = true, strict_conflict = false }
            },
            finalChunk,
            cancellationToken);
    }

    private async Task<HttpResponseMessage> SendUploadChunkAsync(DropboxCredentials credentials, string endpoint, string phase, object arguments, byte[] content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://content.dropboxapi.com/2/files/{endpoint}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.ExpectContinue = true;
        request.Headers.TryAddWithoutValidation("Dropbox-API-Arg", JsonSerializer.Serialize(arguments, DropboxApiJsonOptions));
        request.Content = new ByteArrayContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response = await clients.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await ReadDropboxErrorAsync(response, cancellationToken);
            var requestId = response.Headers.TryGetValues("X-Dropbox-Request-Id", out var requestIds) ? requestIds.FirstOrDefault() : null;
            logger.LogWarning("Dropbox returned HTTP {StatusCode} while {Phase}; request ID {DropboxRequestId}; error {DropboxError}",
                (int)response.StatusCode, phase, requestId ?? "unavailable", detail ?? "no error details");
            var message = response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized when detail?.Contains("missing_scope", StringComparison.OrdinalIgnoreCase) == true
                    || detail?.Contains("files.content.write", StringComparison.OrdinalIgnoreCase) == true
                    => "Dropbox is missing the files.content.write permission for the saved connection. Enable it in the Dropbox App Console, then reconnect Dropbox in Backup settings.",
                System.Net.HttpStatusCode.Unauthorized => "Dropbox rejected the saved connection. Reconnect Dropbox in Backup settings.",
                System.Net.HttpStatusCode.Forbidden => "Dropbox denied the upload. Ensure the app has the files.content.write permission, then reconnect Dropbox in Backup settings.",
                (System.Net.HttpStatusCode)507 => "Dropbox storage is full. Free up Dropbox space and try again.",
                System.Net.HttpStatusCode.BadRequest when detail?.Contains("files.content.write", StringComparison.OrdinalIgnoreCase) == true
                    => "The Dropbox app is missing the files.content.write permission. Enable it in the Dropbox App Console under Permissions, then reconnect Dropbox in Backup settings.",
                _ when detail is not null => $"Dropbox returned HTTP {(int)response.StatusCode} while {phase}: {detail}{(requestId is null ? "" : $" (request ID {requestId})")}",
                _ => $"Dropbox could not store the backup (HTTP {(int)response.StatusCode} while {phase}). Check the app permissions and available Dropbox space.{(requestId is null ? "" : $" Dropbox request ID: {requestId}.")}"
            };
            response.Dispose();
            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500 && (int)response.StatusCode != 507)
            {
                throw new HttpRequestException(message, null, response.StatusCode);
            }
            throw new ApiException(502, message);
        }
        return response;
    }

    private static async Task<string?> ReadDropboxErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("user_message", out var userMessage)
                && userMessage.ValueKind == JsonValueKind.Object
                && userMessage.TryGetProperty("text", out var userMessageText)
                && userMessageText.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(userMessageText.GetString()))
            {
                var text = userMessageText.GetString()!;
                return text.Length > 500 ? text[..500] : text;
            }
            if (document.RootElement.TryGetProperty("error_summary", out var summary) && summary.ValueKind == JsonValueKind.String)
            {
                var summaryText = summary.GetString();
                if (!string.IsNullOrWhiteSpace(summaryText) && summaryText != "other/...")
                {
                    return summaryText.Length > 300 ? summaryText[..300] : summaryText;
                }
            }
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var errorTags = GetDropboxErrorTags(error);
                if (errorTags.Count > 0)
                {
                    var requiredScope = GetDropboxRequiredScope(error);
                    return requiredScope is null
                        ? string.Join("/", errorTags)
                        : $"{string.Join("/", errorTags)}/{requiredScope}";
                }
            }
            return document.RootElement.TryGetProperty("error_summary", out var fallback) && fallback.ValueKind == JsonValueKind.String
                ? fallback.GetString() is { Length: > 300 } value ? value[..300] : fallback.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string> GetDropboxErrorTags(JsonElement element)
    {
        var tags = new List<string>();
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(".tag", out var tag) && tag.ValueKind == JsonValueKind.String && tag.GetString() is { } value)
            {
                tags.Add(value);
            }
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name != ".tag")
                {
                    tags.AddRange(GetDropboxErrorTags(property.Value));
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                tags.AddRange(GetDropboxErrorTags(item));
            }
        }
        return tags;
    }

    private static string? GetDropboxRequiredScope(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("required_scope", out var scope) && scope.ValueKind == JsonValueKind.String)
            {
                return scope.GetString();
            }
            foreach (var property in element.EnumerateObject())
            {
                var nestedScope = GetDropboxRequiredScope(property.Value);
                if (nestedScope is not null)
                {
                    return nestedScope;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nestedScope = GetDropboxRequiredScope(item);
                if (nestedScope is not null)
                {
                    return nestedScope;
                }
            }
        }
        return null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunBackupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }
}
