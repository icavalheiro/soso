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

    public Store(IConfiguration configuration)
    {
        var path = configuration["DataPath"] ?? "data";
        Directory.CreateDirectory(path);
        database = new LiteDatabase(Path.Combine(path, "soso.db"));
        database.UtcDate = true;
        Accounts.EnsureIndex(account => account.Email, true);
        Tickets.EnsureIndex(ticket => ticket.BoardId);
        Tokens.EnsureIndex(token => token.UserId);
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
}