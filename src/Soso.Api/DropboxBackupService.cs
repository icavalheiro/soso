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
        if (!await backupGate.WaitAsync(0, cancellationToken))
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
            store.RecordBackupError(exception is ApiException ? exception.Message : "Dropbox backup failed. Check the connection and server logs.");
            if (force)
            {
                throw;
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
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://content.dropboxapi.com/2/files/upload");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.TryAddWithoutValidation("Dropbox-API-Arg", JsonSerializer.Serialize(new
        {
            path = "/soso-backup.db",
            mode = "overwrite",
            autorename = false,
            mute = true,
            strict_conflict = false
        }));
        request.Content = new ByteArrayContent(await File.ReadAllBytesAsync(path, cancellationToken));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await clients.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException(502, "Dropbox could not store the backup. Check the app permissions and available Dropbox space.");
        }
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
