using LiteDB;

namespace Soso.Api;

public sealed class Store : IDisposable
{
    private readonly LiteDatabase database;
    public object Gate { get; } = new();
    public ILiteCollection<Account> Accounts => database.GetCollection<Account>("accounts");
    public ILiteCollection<Board> Boards => database.GetCollection<Board>("boards");
    public ILiteCollection<Ticket> Tickets => database.GetCollection<Ticket>("tickets");
    public ILiteCollection<ImageAsset> Images => database.GetCollection<ImageAsset>("images");
    public ILiteCollection<AccessToken> Tokens => database.GetCollection<AccessToken>("tokens");
    public ILiteCollection<DropboxBackupConfiguration> DropboxConfigurations => database.GetCollection<DropboxBackupConfiguration>("dropboxBackup");
    public ILiteCollection<BackupState> BackupStates => database.GetCollection<BackupState>("backupState");
    public string DatabasePath { get; }

    public Store(IConfiguration configuration)
    {
        var path = configuration["DataPath"] ?? "data";
        Directory.CreateDirectory(path);
        DatabasePath = Path.Combine(path, "soso.db");
        database = new LiteDatabase(DatabasePath);
        database.UtcDate = true;
        Accounts.EnsureIndex(account => account.Email, true);
        Tickets.EnsureIndex(ticket => ticket.BoardId);
        Tokens.EnsureIndex(token => token.UserId);
        lock (Gate)
        {
            if (BackupStates.FindById("main") is null)
            {
                BackupStates.Insert(new BackupState { LastModifiedAt = File.GetLastWriteTimeUtc(DatabasePath) });
            }
        }
    }

    public T Transaction<T>(Func<T> operation)
    {
        lock (Gate)
        {
            var started = database.BeginTrans();
            if (!started)
            {
                throw new InvalidOperationException("A transaction is already active.");
            }
            try
            {
                var result = operation();
                database.Commit();
                return result;
            }
            catch
            {
                database.Rollback();
                throw;
            }
        }
    }

    public void Dispose() => database.Dispose();

    public void MarkModified()
    {
        lock (Gate)
        {
            var state = BackupStates.FindById("main") ?? new BackupState();
            state.LastModifiedAt = DateTime.UtcNow;
            BackupStates.Upsert(state);
        }
    }

    public BackupState GetBackupState()
    {
        lock (Gate)
        {
            return BackupStates.FindById("main") ?? new BackupState();
        }
    }

    public DropboxBackupConfiguration? GetDropboxConfiguration()
    {
        lock (Gate)
        {
            return DropboxConfigurations.FindById("dropbox");
        }
    }

    public void SetDropboxConfiguration(string protectedCredentials)
    {
        lock (Gate)
        {
            DropboxConfigurations.Upsert(new DropboxBackupConfiguration { ProtectedCredentials = protectedCredentials });
            var state = BackupStates.FindById("main") ?? new BackupState();
            if (state.LastBackupAt is null && state.LastBackedUpModificationAt is null)
            {
                state.LastBackedUpModificationAt = state.LastModifiedAt;
            }
            state.LastError = null;
            state.NextBackupAttemptAt = null;
            BackupStates.Upsert(state);
        }
    }

    public void DeleteDropboxConfiguration()
    {
        lock (Gate)
        {
            DropboxConfigurations.Delete("dropbox");
            var state = BackupStates.FindById("main") ?? new BackupState();
            state.LastError = null;
            state.NextBackupAttemptAt = null;
            BackupStates.Upsert(state);
        }
    }

    public string CreateBackupSnapshot(out DateTime modifiedAt)
    {
        var path = Path.Combine(Path.GetTempPath(), $"soso-backup-{Guid.NewGuid():N}.db");
        lock (Gate)
        {
            modifiedAt = (BackupStates.FindById("main") ?? new BackupState()).LastModifiedAt;
            database.Checkpoint();
            File.Copy(DatabasePath, path);
        }
        return path;
    }

    public void RecordBackup(DateTime backedUpModificationAt)
    {
        lock (Gate)
        {
            var state = BackupStates.FindById("main") ?? new BackupState();
            state.LastBackupAt = DateTime.UtcNow;
            state.LastBackedUpModificationAt = backedUpModificationAt;
            state.LastError = null;
            state.NextBackupAttemptAt = null;
            BackupStates.Upsert(state);
        }
    }

    public void RecordBackupError(string message)
    {
        lock (Gate)
        {
            var state = BackupStates.FindById("main") ?? new BackupState();
            state.LastError = message;
            state.NextBackupAttemptAt = DateTime.UtcNow.AddMinutes(15);
            BackupStates.Upsert(state);
        }
    }
}
