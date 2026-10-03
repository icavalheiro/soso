namespace Soso.Api;

public sealed class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool Disabled { get; set; }
    public int FailedLogins { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public string? AvatarId { get; set; }
    public string Theme { get; set; } = "light";
    public string Settings { get; set; } = "";
}

public sealed class Board
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public List<string> Members { get; set; } = [];
    public List<BoardColumn> Columns { get; set; } = [];
    public int Revision { get; set; }
}

public sealed class BoardColumn
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool IsDone { get; set; }
}

public sealed class Ticket
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BoardId { get; set; } = "";
    public string ColumnId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Priority { get; set; } = "normal";
    public List<string> Tags { get; set; } = [];
    public bool Archived { get; set; }
    public string? AssigneeId { get; set; }
    public DateTime? DueDate { get; set; }
    public double Position { get; set; }
    public int Revision { get; set; }
    public List<Subtask> Subtasks { get; set; } = [];
    public List<TicketComment> Comments { get; set; } = [];
    public List<string> Images { get; set; } = [];
}

public sealed class Subtask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

public sealed class TicketComment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AuthorId { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ImageAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OwnerId { get; set; } = "";
    public string? BoardId { get; set; }
    public byte[] Content { get; set; } = [];
}

public sealed class AccessToken
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}