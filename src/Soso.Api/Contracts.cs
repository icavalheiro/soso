using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Soso.Api;

/// <summary>Credentials for an existing account.</summary>
public sealed record LoginRequest([Required, EmailAddress, MaxLength(254)] string Email, [Required, MaxLength(128)] string Password);
/// <summary>Administrator-created account.</summary>
public sealed record CreateAccountRequest([Required, EmailAddress, MaxLength(254)] string Email, [Required, MaxLength(80)] string Name, [Required, MinLength(14), MaxLength(128)] string Password, bool IsAdmin);
/// <summary>Public account information.</summary>
public sealed record AccountResponse(string Id, string Email, string Name, bool IsAdmin, bool Disabled, string? AvatarId, string Theme, string Settings, string[]? BoardIds)
{
    public static AccountResponse From(Account account) => new(account.Id, account.Email, account.Name, account.IsAdmin, account.Disabled, account.AvatarId, account.Theme, account.Settings, account.BoardIds?.ToArray());
}
/// <summary>Editable personal preferences.</summary>
public sealed record ProfileRequest([Required, MaxLength(80)] string Name, [Required, RegularExpression("^(light|dark)$")] string Theme, [Required(AllowEmptyStrings = true), MaxLength(4000)] string Settings);
/// <summary>Password rotation with current-password verification.</summary>
public sealed record PasswordRequest([Required, MaxLength(128)] string CurrentPassword, [Required, MinLength(14), MaxLength(128)] string NewPassword);
/// <summary>Administrative account state and optional password reset.</summary>
public sealed record AccountStateRequest(bool Disabled, [MinLength(14), MaxLength(128)] string? Password, [MaxLength(1000)] string[]? BoardIds = null);
/// <summary>Board title and description.</summary>
public sealed record CreateBoardRequest([Required, MaxLength(80)] string Name, [Required(AllowEmptyStrings = true), MaxLength(2000)] string Description, [MaxLength(32)] string? Icon = null, [MaxLength(32)] string? Color = null);
/// <summary>Board configuration and membership.</summary>
public sealed record UpdateBoardRequest([Required, MaxLength(80)] string Name, [Required(AllowEmptyStrings = true), MaxLength(2000)] string Description, [Required, MaxLength(100)] string[] Members, [Required, MinLength(1), MaxLength(20)] ColumnRequest[] Columns, int Revision, [MaxLength(32)] string? Icon = null, [MaxLength(32)] string? Color = null);
/// <summary>Column definition.</summary>
public sealed record ColumnRequest([Required, MaxLength(32)] string Id, [Required, MaxLength(60)] string Name, bool IsDone);
/// <summary>New ticket in a board column.</summary>
public sealed record CreateTicketRequest([Required, MaxLength(160)] string Title, [Required, MaxLength(32)] string ColumnId, [MaxLength(12000)] string? Description = null, [MaxLength(8)] string[]? Tags = null, string? AssigneeId = null);
public sealed record McpCreateTicketRequest(
    [Required, MaxLength(160)] string Title,
    [Required, MaxLength(32)] string ColumnId,
    [Required, MaxLength(12000)][property: Description("Required work specification: context, expected behavior and acceptance criteria. Not a progress log or completion announcement.")] string Description,
    [Required, MinLength(1), MaxLength(8)][property: Description("Required lowercase work categories: bug, feature, design, docs, refactor, test, chore, research. Not status or priority.")] string[] Tags);
/// <summary>Editable ticket content with optimistic concurrency.</summary>
public sealed record UpdateTicketRequest([Required, MaxLength(160)] string Title, [Required(AllowEmptyStrings = true), MaxLength(12000)] string Description, [Required, MaxLength(32)] string ColumnId, [Required, RegularExpression("^(low|normal|high|urgent)$")] string Priority, string? AssigneeId, DateTimeOffset? DueDate, double Position, [Required, MaxLength(100)] SubtaskRequest[] Subtasks, [Required, MaxLength(8)] string[] Tags, bool Archived, int Revision);
public sealed record PatchTicketRequest(
    [property: JsonRequired, Description("Required current TICKET revision from get_board or a successful write. Never use the board revision.")] int Revision,
    [MaxLength(160)] string? Title = null,
    [MaxLength(12000)][property: Description("Work specification and acceptance criteria, not progress or completion. Omit to preserve; empty string explicitly clears.")] string? Description = null,
    [MaxLength(32)][property: Description("Status is the column. To complete, use a real column whose isDone is true. Omit to preserve status.")] string? ColumnId = null,
    [RegularExpression("^(low|normal|high|urgent)$")] string? Priority = null,
    [property: Description("Owner/member ID to assign. Omit or null preserves; use clearAssignee=true to unassign.")] string? AssigneeId = null,
    [property: Description("ISO 8601 timestamp with offset. Omit or null preserves; use clearDueDate=true to remove the deadline.")] DateTimeOffset? DueDate = null,
    double? Position = null,
    [MaxLength(100)][property: Description("Omit to preserve. Supplied array replaces the checklist; preserve existing IDs. [] clears.")] SubtaskRequest[]? Subtasks = null,
    [MaxLength(8)][property: Description("Omit to preserve. Supplied array replaces all tags; preserve unrelated tags. [] clears. Allowed: bug, feature, design, docs, refactor, test, chore, research.")] string[]? Tags = null,
    bool? Archived = null,
    [property: Description("Explicitly remove the assignee. Do not combine true with assigneeId.")] bool ClearAssignee = false,
    [property: Description("Explicitly remove the deadline. Do not combine true with dueDate.")] bool ClearDueDate = false);
public sealed record BatchTicketUpdateRequest([Required, MaxLength(32)] string TicketId, [Required] PatchTicketRequest Update);
public sealed record TicketSearchResponse(Ticket[] Tickets, int Total, int Offset, int Limit);
/// <summary>Editable checklist entry.</summary>
public sealed record SubtaskRequest([Required, MaxLength(32)] string Id, [Required, MaxLength(300)] string Title, bool Done);
/// <summary>New ticket comment.</summary>
public sealed record CommentRequest([Required, MaxLength(4000)] string Text);
/// <summary>Token label.</summary>
public sealed record TokenRequest([Required, MaxLength(80)] string Name);
/// <summary>Explicit board assignments for a personal MCP token.</summary>
public sealed record UpdateTokenBoardsRequest([Required, MaxLength(1000)] string[] BoardIds);
/// <summary>Dropbox app key and access token used for backups.</summary>
public sealed record DropboxCredentialsRequest([Required, MaxLength(200)] string AppKey, [Required, MaxLength(4000)] string AccessToken);
/// <summary>Board and its visible tickets.</summary>
public sealed record BoardResponse(Board Board, Ticket[] Tickets, PersonResponse[] Members);
/// <summary>Minimal member information without private preferences or email.</summary>
public sealed record PersonResponse(string Id, string Name, string? AvatarId);
