using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Soso.Api;

public sealed record DropboxCredentials(string AppKey, string AccessToken);

public sealed class DropboxBackupService(Store store, IHttpClientFactory clients, IDataProtectionProvider protection, ILogger<DropboxBackupService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions DropboxApiJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly IDataProtector protector = protection.CreateProtector("Soso.DropboxBackup.Credentials.v1");
    private readonly SemaphoreSlim backupGate = new(1, 1);

    public string Protect(DropboxCredentials credentials) => protector.Protect(JsonSerializer.Serialize(credentials, JsonOptions));

    public DropboxCredentials Unprotect(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<DropboxCredentials>(protector.Unprotect(value), JsonOptions)
                ?? throw new ApiException(500, "Dropbox credentials could not be read.");
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            throw new ApiException(500, "Dropbox credentials could not be read. Reconnect Dropbox.");
        }
    }

    public async Task ValidateAsync(DropboxCredentials credentials, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentials.AppKey) || string.IsNullOrWhiteSpace(credentials.AccessToken))
        {
            throw new ApiException(400, "Enter both the Dropbox app key and access token.");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/users/get_current_account");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Content = new StringContent("null", Encoding.UTF8, "application/json");
        using var response = await clients.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException(400, "Dropbox rejected the access token. Check the token and its permissions.");
        }
    }

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
            if (configuration is null)
            {
                if (force)
                {
                    throw new ApiException(400, "Connect Dropbox before requesting a backup.");
                }
                return;
            }
            var credentials = Unprotect(configuration.ProtectedCredentials);
            var state = store.GetBackupState();
            var now = DateTime.UtcNow;
            var changedSinceBackup = state.LastBackedUpModificationAt is null || state.LastModifiedAt > state.LastBackedUpModificationAt;
            var quietForTenMinutes = changedSinceBackup && now - state.LastModifiedAt >= TimeSpan.FromMinutes(10);
            var lastSaveAt = state.LastBackupAt ?? state.LastBackedUpModificationAt ?? state.LastModifiedAt;
            var hourlyFallback = changedSinceBackup && now - lastSaveAt >= TimeSpan.FromHours(1);
            if (!force && !quietForTenMinutes && !hourlyFallback)
            {
                return;
            }

            snapshotPath = store.CreateBackupSnapshot(out var backedUpModificationAt);
            await UploadAsync(credentials, snapshotPath, cancellationToken);
            store.RecordBackup(backedUpModificationAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Dropbox backup failed.");
            var message = exception switch
            {
                ApiException => exception.Message,
                HttpRequestException => "The connection to Dropbox's upload server was interrupted. Check that the server/container can make outbound HTTPS connections to content.dropboxapi.com on port 443, then try again.",
                IOException => "Could not read or create the database backup file. Check the server's data directory and available disk space.",
                _ => "Dropbox backup failed. Check the server logs for details."
            };
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
                    => "Dropbox is missing the files.content.write permission for this access token. Enable it in the Dropbox App Console, generate a new access token, then replace the saved token in Backup settings.",
                System.Net.HttpStatusCode.Unauthorized => "Dropbox rejected the access token. Reconnect with a valid token.",
                System.Net.HttpStatusCode.Forbidden => "Dropbox denied the upload. Ensure the app has the files.content.write permission and reconnect after changing permissions.",
                (System.Net.HttpStatusCode)507 => "Dropbox storage is full. Free up Dropbox space and try again.",
                System.Net.HttpStatusCode.BadRequest when detail?.Contains("files.content.write", StringComparison.OrdinalIgnoreCase) == true
                    => "The Dropbox app is missing the files.content.write permission. Enable it in the Dropbox App Console under Permissions, then generate a new access token and reconnect.",
                _ when detail is not null => $"Dropbox returned HTTP {(int)response.StatusCode} while {phase}: {detail}{(requestId is null ? "" : $" (request ID {requestId})")}",
                _ => $"Dropbox could not store the backup (HTTP {(int)response.StatusCode} while {phase}). Check the app permissions and available Dropbox space.{(requestId is null ? "" : $" Dropbox request ID: {requestId}.")}"
            };
            response.Dispose();
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
