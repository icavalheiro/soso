using System.Security.Claims;

namespace Soso.Api;

public static class BackupEndpoints
{
    public static void MapBackup(this WebApplication app)
    {
        var backup = app.MapGroup("/api/admin/backup").RequireAuthorization("Admin");
        backup.MapGet("/", (Store store, DropboxBackupService service) =>
        {
            var configuration = store.GetDropboxConfiguration();
            var state = store.GetBackupState();
            return TypedResults.Ok(new
            {
                connected = configuration is not null,
                appKey = configuration is null ? "" : service.Unprotect(configuration.ProtectedCredentials).AppKey,
                lastBackupAt = state.LastBackupAt,
                lastError = state.LastError
            });
        });
        backup.MapPut("/", async (DropboxCredentialsRequest request, Store store, DropboxBackupService service, CancellationToken cancellationToken) =>
        {
            var credentials = new DropboxCredentials(request.AppKey.Trim(), request.AccessToken.Trim());
            await service.ValidateAsync(credentials, cancellationToken);
            store.SetDropboxConfiguration(service.Protect(credentials));
            return TypedResults.Ok(new { connected = true, appKey = credentials.AppKey, lastBackupAt = store.GetBackupState().LastBackupAt, lastError = (string?)null });
        });
        backup.MapDelete("/", (Store store) =>
        {
            store.DeleteDropboxConfiguration();
            return TypedResults.NoContent();
        });
        backup.MapPost("/run", async (DropboxBackupService service, CancellationToken cancellationToken) =>
        {
            await service.RunBackupAsync(cancellationToken, force: true);
            return TypedResults.Ok(new { status = "Backup requested." });
        });
    }
}
