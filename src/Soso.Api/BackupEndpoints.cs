using System.Security.Claims;

namespace Soso.Api;

public static class BackupEndpoints
{
    public static void MapBackup(this WebApplication app)
    {
        var backup = app.MapGroup("/api/admin/backup").RequireAuthorization("Admin");
        backup.MapGet("/", Status);
        backup.MapPut("/", (DropboxAppKeyRequest request, Store store, DropboxBackupService service, HttpContext context) =>
        {
            var appKey = request.AppKey.Trim();
            var configuration = store.GetDropboxConfiguration();
            if (configuration is not null && configuration.AppKey != appKey && service.IsConnected(configuration))
            {
                // The saved connection belongs to a different Dropbox app and cannot be reused.
                store.DisconnectDropbox();
            }
            store.SaveDropboxAppKey(appKey);
            return TypedResults.Ok(Status(context, store, service));
        });
        backup.MapDelete("/", async (Store store, DropboxBackupService service, CancellationToken cancellationToken) =>
        {
            await service.TryRevokeAsync(cancellationToken);
            store.DisconnectDropbox();
            return TypedResults.NoContent();
        });
        backup.MapPost("/run", async (DropboxBackupService service, CancellationToken cancellationToken) =>
        {
            await service.RunBackupAsync(cancellationToken, force: true);
            return TypedResults.Ok(new { status = "Backup requested." });
        });
        backup.MapGet("/dropbox/authorize", (ClaimsPrincipal user, HttpContext context, DropboxBackupService service) =>
            TypedResults.Ok(new { url = service.CreateAuthorizationUrl(context, BoardService.UserId(user)) }));
        backup.MapGet("/dropbox/callback", async (string? code, string? state, string? error, DropboxBackupService service, CancellationToken cancellationToken) =>
            TypedResults.Redirect(await service.CompleteAuthorizationAsync(code, state, error, cancellationToken))).AllowAnonymous();
    }

    private static IResult Status(HttpContext context, Store store, DropboxBackupService service)
    {
        var configuration = store.GetDropboxConfiguration();
        var state = store.GetBackupState();
        return TypedResults.Ok(new
        {
            connected = configuration is not null && service.IsConnected(configuration),
            appKey = configuration is null ? "" : service.GetAppKey(configuration),
            redirectUri = DropboxBackupService.RedirectUri(context),
            lastBackupAt = state.LastBackupAt,
            lastError = state.LastError
        });
    }
}