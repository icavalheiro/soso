using System.ComponentModel.DataAnnotations;

namespace Soso.Api;

/// <summary>Credentials for an existing account.</summary>
public sealed record LoginRequest([Required, EmailAddress, MaxLength(254)] string Email, [Required, MaxLength(128)] string Password);
/// <summary>Administrator-created account.</summary>
public sealed record CreateAccountRequest([Required, EmailAddress, MaxLength(254)] string Email, [Required, MaxLength(80)] string Name, [Required, MinLength(14), MaxLength(128)] string Password, bool IsAdmin);
/// <summary>Public account information.</summary>
public sealed record AccountResponse(string Id, string Email, string Name, bool IsAdmin, bool Disabled, string? AvatarId, string Theme, string Settings)
{
    public static AccountResponse From(Account account) => new(account.Id, account.Email, account.Name, account.IsAdmin, account.Disabled, account.AvatarId, account.Theme, account.Settings);
}
/// <summary>Editable personal preferences.</summary>
public sealed record ProfileRequest([Required, MaxLength(80)] string Name, [Required, RegularExpression("^(light|dark)$")] string Theme, [Required(AllowEmptyStrings = true), MaxLength(4000)] string Settings);
/// <summary>Password rotation with current-password verification.</summary>
public sealed record PasswordRequest([Required, MaxLength(128)] string CurrentPassword, [Required, MinLength(14), MaxLength(128)] string NewPassword);
/// <summary>Administrative account state and optional password reset.</summary>
public sealed record AccountStateRequest(bool Disabled, [MinLength(14), MaxLength(128)] string? Password);
/// <summary>Board title and description.</summary>
public sealed record CreateBoardRequest([Required, MaxLength(80)] string Name, [Required(AllowEmptyStrings = true), MaxLength(2000)] string Description, [MaxLength(32)] string? Icon = null, [MaxLength(32)] string? Color = null);
/// <summary>Board configuration and membership.</summary>
public sealed record UpdateBoardRequest([Required, MaxLength(80)] string Name, [Required(AllowEmptyStrings = true), MaxLength(2000)] string Description, [Required, MaxLength(100)] string[] Members, [Required, MinLength(1), MaxLength(20)] ColumnRequest[] Columns, int Revision, [MaxLength(32)] string? Icon = null, [MaxLength(32)] string? Color = null);
/// <summary>Column definition.</summary>
public sealed record ColumnRequest([Required, MaxLength(32)] string Id, [Required, MaxLength(60)] string Name, bool IsDone);
/// <summary>New ticket in a board column.</summary>
public sealed record CreateTicketRequest([Required, MaxLength(160)] string Title, [Required, MaxLength(32)] string ColumnId);
/// <summary>Editable ticket content with optimistic concurrency.</summary>
public sealed record UpdateTicketRequest([Required, MaxLength(160)] string Title, [Required(AllowEmptyStrings = true), MaxLength(12000)] string Description, [Required, MaxLength(32)] string ColumnId, [Required, RegularExpression("^(low|normal|high|urgent)$")] string Priority, string? AssigneeId, DateTimeOffset? DueDate, double Position, [Required, MaxLength(100)] SubtaskRequest[] Subtasks, [Required, MaxLength(8)] string[] Tags, bool Archived, int Revision);
public sealed record BatchTicketUpdateRequest([Required, MaxLength(32)] string TicketId, [Required] UpdateTicketRequest Update);
public sealed record TicketSearchResponse(Ticket[] Tickets, int Total, int Offset, int Limit);
/// <summary>Editable checklist entry.</summary>
public sealed record SubtaskRequest([Required, MaxLength(32)] string Id, [Required, MaxLength(300)] string Title, bool Done);
/// <summary>New ticket comment.</summary>
public sealed record CommentRequest([Required, MaxLength(4000)] string Text);
/// <summary>Token label.</summary>
public sealed record TokenRequest([Required, MaxLength(80)] string Name);
/// <summary>Board and its visible tickets.</summary>
public sealed record BoardResponse(Board Board, Ticket[] Tickets, PersonResponse[] Members);
/// <summary>Minimal member information without private preferences or email.</summary>
public sealed record PersonResponse(string Id, string Name, string? AvatarId);