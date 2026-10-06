using System.Security.Claims;
using System.ComponentModel.DataAnnotations;

namespace Soso.Api;

public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class BoardService(Store store)
{
    public static readonly string[] AllowedTags = ["bug", "feature", "design", "docs", "refactor", "test", "chore", "research"];
    public static readonly string[] AllowedIcons = ["columns", "briefcase", "house", "heart", "star", "rocket", "code", "book", "graduation-cap", "plane", "wallet", "target", "calendar", "shopping", "music", "fitness", "team", "ideas", "nature", "coffee"];
    public static readonly string[] AllowedColors = ["teal", "blue", "cyan", "green", "grape", "pink", "orange", "gray"];
    public static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new ApiException(401, "Sign in required.");

    private bool TokenAllowsBoard(string boardId, ClaimsPrincipal user)
    {
        var tokenId = user.FindFirstValue(McpAuthentication.TokenClaim);
        if (tokenId is null)
        {
            return true;
        }
        var token = store.Tokens.FindById(tokenId);
        var ownsToken = token?.UserId == UserId(user);
        var isActive = token?.ExpiresAt > DateTime.UtcNow;
        var isAssigned = token?.BoardIds.Contains(boardId) == true;
        return ownsToken && isActive && isAssigned;
    }

    public Board RequireBoard(string id, ClaimsPrincipal user, bool ownerOnly = false)
    {
        var board = store.Boards.FindById(id);
        var userId = UserId(user);
        var tokenAllows = TokenAllowsBoard(id, user);
        var account = store.Accounts.FindById(userId);
        var canRead = board is not null && tokenAllows && CanRead(board, account, userId, user);
        if (!canRead)
        {
            throw new ApiException(404, "Board not found.");
        }
        var canManage = board!.OwnerId == userId || user.IsInRole("admin");
        if (ownerOnly && !canManage)
        {
            throw new ApiException(403, "Only the board owner can change its configuration.");
        }
        return board;
    }

    public Board[] List(ClaimsPrincipal user)
    {
        var id = UserId(user);
        var account = store.Accounts.FindById(id);
        return store.Boards.FindAll().Where(board => TokenAllowsBoard(board.Id, user) && CanRead(board, account, id, user)).ToArray();
    }

    private static bool CanRead(Board board, Account? account, string userId, ClaimsPrincipal user)
    {
        if (user.IsInRole("admin"))
        {
            return true;
        }
        if (account?.BoardIds is not null)
        {
            return account.BoardIds.Contains(board.Id);
        }
        return board.OwnerId == userId || board.Members.Contains(userId);
    }

    public BoardResponse Get(string id, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            var board = RequireBoard(id, user);
            var ids = board.Members.Append(board.OwnerId).ToHashSet();
            var members = store.Accounts.FindAll().Where(account => ids.Contains(account.Id)).Select(account => new PersonResponse(account.Id, account.Name, account.AvatarId)).ToArray();
            return new(board, store.Tickets.Find(ticket => ticket.BoardId == id).OrderBy(ticket => ticket.Position).ToArray(), members);
        }
    }

    public Board Create(CreateBoardRequest request, ClaimsPrincipal user)
    {
        var board = new Board { Name = Text(request.Name, 80), Description = request.Description.Trim(), Icon = CheckIcon(request.Icon ?? "columns"), Color = CheckColor(request.Color ?? "teal"), OwnerId = UserId(user), Columns = [new() { Name = "To do" }, new() { Name = "In progress" }, new() { Name = "Done", IsDone = true }] };
        lock (store.Gate)
        {
            store.Boards.Insert(board);
        }
        return board;
    }

    public Board Update(string id, UpdateBoardRequest request, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            var board = RequireBoard(id, user, true);
            CheckRevision(board.Revision, request.Revision);
            var columnIds = request.Columns.Select(column => column.Id).ToHashSet();
            var invalidColumns = columnIds.Count != request.Columns.Length || request.Columns.Any(column => !Guid.TryParseExact(column.Id, "N", out _));
            var hasOrphanedTickets = store.Tickets.Find(ticket => ticket.BoardId == id).Any(ticket => !columnIds.Contains(ticket.ColumnId));
            if (invalidColumns || hasOrphanedTickets)
            {
                throw new ApiException(400, "Columns must have unique IDs; move tickets before deleting a column.");
            }
            var invalidMembers = request.Members.Any(member => store.Accounts.FindById(member) is not { Disabled: false });
            if (invalidMembers)
            {
                throw new ApiException(400, "Unknown or disabled member.");
            }
            board.Name = Text(request.Name, 80);
            board.Description = request.Description.Trim();
            board.Icon = CheckIcon(request.Icon ?? board.Icon);
            board.Color = CheckColor(request.Color ?? board.Color);
            board.Members = request.Members.Distinct().ToList();
            board.Columns = request.Columns.Select(column => new BoardColumn { Id = column.Id, Name = Text(column.Name, 60), IsDone = column.IsDone }).ToList();
            board.Revision++;
            store.Boards.Update(board);
            return board;
        }
    }

    public Ticket RequireTicket(string boardId, string ticketId, ClaimsPrincipal user)
    {
        RequireBoard(boardId, user);
        var ticket = store.Tickets.FindById(ticketId);
        var missing = ticket is null || ticket.BoardId != boardId;
        if (missing)
        {
            throw new ApiException(404, "Ticket not found.");
        }
        return ticket!;
    }

    public Ticket CreateTicket(string boardId, CreateTicketRequest request, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            ValidateInput(request);
            var board = RequireBoard(boardId, user);
            CheckColumn(board, request.ColumnId);
            var validAssignee = request.AssigneeId is null || request.AssigneeId == board.OwnerId || board.Members.Contains(request.AssigneeId);
            var tags = request.Tags ?? [];
            var validTags = tags.All(AllowedTags.Contains);
            if (!validAssignee)
            {
                throw new ApiException(400, "Invalid ticket assignee.");
            }
            if (!validTags)
            {
                throw new ApiException(400, "Tags must classify software work: bug, feature, design, docs, refactor, test, chore or research.");
            }
            var position = store.Tickets.Find(ticket => ticket.BoardId == boardId).Select(ticket => ticket.Position).DefaultIfEmpty(0).Max() + 1024;
            var ticket = new Ticket { BoardId = boardId, ColumnId = request.ColumnId, Title = Text(request.Title, 160), Description = request.Description?.Trim() ?? "", Tags = tags.Distinct().ToList(), AssigneeId = request.AssigneeId, Position = position };
            Track(ticket, user, "created");
            store.Tickets.Insert(ticket);
            return ticket;
        }
    }

    public Ticket UpdateTicket(string boardId, string ticketId, UpdateTicketRequest request, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            ValidateInput(request);
            foreach (var task in request.Subtasks)
            {
                ValidateInput(task);
            }
            var board = RequireBoard(boardId, user);
            var ticket = RequireTicket(boardId, ticketId, user);
            CheckRevision(ticket.Revision, request.Revision);
            CheckColumn(board, request.ColumnId);
            var validAssignee = request.AssigneeId is null || request.AssigneeId == board.OwnerId || board.Members.Contains(request.AssigneeId);
            var validPosition = double.IsFinite(request.Position) && Math.Abs(request.Position) < 1e12;
            var validPriority = new[] { "low", "normal", "high", "urgent" }.Contains(request.Priority);
            var validSubtasks = request.Subtasks.Length <= 100 && request.Subtasks.Select(task => task.Id).Distinct().Count() == request.Subtasks.Length;
            var validTags = request.Tags.Length <= 8 && request.Tags.All(AllowedTags.Contains);
            if (!validAssignee || !validPosition || !validPriority || !validSubtasks || !validTags)
            {
                throw new ApiException(400, "Invalid ticket properties.");
            }
            TrackChange(ticket, user, "title", ticket.Title, request.Title);
            TrackChange(ticket, user, "description", ticket.Description, request.Description);
            TrackChange(ticket, user, "status", ColumnName(board, ticket.ColumnId), ColumnName(board, request.ColumnId));
            TrackChange(ticket, user, "priority", ticket.Priority, request.Priority);
            TrackChange(ticket, user, "assignee", PersonName(ticket.AssigneeId), PersonName(request.AssigneeId));
            TrackChange(ticket, user, "due_date", ticket.DueDate?.ToString("O"), request.DueDate?.UtcDateTime.ToString("O"));
            TrackChange(ticket, user, "position", ticket.Position.ToString("G17"), request.Position.ToString("G17"));
            TrackChange(ticket, user, "tags", string.Join(", ", ticket.Tags), string.Join(", ", request.Tags.Distinct()));
            TrackChange(ticket, user, "archived", ticket.Archived.ToString(), request.Archived.ToString());
            TrackChange(ticket, user, "subtasks", SubtasksValue(ticket.Subtasks), SubtasksValue(request.Subtasks));
            ticket.Title = Text(request.Title, 160);
            ticket.Description = request.Description.Trim();
            ticket.ColumnId = request.ColumnId;
            ticket.Priority = request.Priority;
            ticket.Tags = request.Tags.Distinct().ToList();
            ticket.Archived = request.Archived;
            ticket.AssigneeId = request.AssigneeId;
            ticket.DueDate = request.DueDate?.UtcDateTime;
            ticket.Position = request.Position;
            ticket.Subtasks = request.Subtasks.Select(task => new Subtask { Id = task.Id, Title = Text(task.Title, 300), Done = task.Done }).ToList();
            ticket.Revision++;
            store.Tickets.Update(ticket);
            return ticket;
        }
    }

    public Ticket PatchTicket(string boardId, string ticketId, PatchTicketRequest request, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            ValidateInput(request);
            var ticket = RequireTicket(boardId, ticketId, user);
            CheckRevision(ticket.Revision, request.Revision);
            var conflictingAssignee = request.ClearAssignee && request.AssigneeId is not null;
            var conflictingDueDate = request.ClearDueDate && request.DueDate is not null;
            if (conflictingAssignee || conflictingDueDate)
            {
                throw new ApiException(400, "Do not supply a value and its clear flag together.");
            }
            var dueDate = ticket.DueDate is null ? (DateTimeOffset?)null : new DateTimeOffset(DateTime.SpecifyKind(ticket.DueDate.Value, DateTimeKind.Utc));
            var subtasks = request.Subtasks ?? ticket.Subtasks.Select(task => new SubtaskRequest(task.Id, task.Title, task.Done)).ToArray();
            var update = new UpdateTicketRequest(
                request.Title ?? ticket.Title,
                request.Description ?? ticket.Description ?? "",
                request.ColumnId ?? ticket.ColumnId,
                request.Priority ?? ticket.Priority,
                request.ClearAssignee ? null : request.AssigneeId ?? ticket.AssigneeId,
                request.ClearDueDate ? null : request.DueDate ?? dueDate,
                request.Position ?? ticket.Position,
                subtasks,
                request.Tags ?? ticket.Tags.ToArray(),
                request.Archived ?? ticket.Archived,
                request.Revision);
            return UpdateTicket(boardId, ticketId, update, user);
        }
    }

    public Ticket[] CreateTickets(string boardId, CreateTicketRequest[] tickets, ClaimsPrincipal user)
    {
        CheckBatch(tickets);
        return store.Transaction(() =>
        {
            RequireBoard(boardId, user);
            return tickets.Select(ticket => CreateTicket(boardId, ticket, user)).ToArray();
        });
    }

    public Ticket[] UpdateTickets(string boardId, BatchTicketUpdateRequest[] tickets, ClaimsPrincipal user)
    {
        CheckBatch(tickets);
        foreach (var ticket in tickets)
        {
            ValidateInput(ticket);
        }
        var uniqueTickets = tickets.Select(ticket => ticket.TicketId).Distinct().Count() == tickets.Length;
        if (!uniqueTickets)
        {
            throw new ApiException(400, "Batch ticket IDs must be unique.");
        }
        return store.Transaction(() =>
        {
            RequireBoard(boardId, user);
            return tickets.Select(ticket => PatchTicket(boardId, ticket.TicketId, ticket.Update, user)).ToArray();
        });
    }

    public TicketSearchResponse SearchTickets(string query, ClaimsPrincipal user, string? boardId = null, bool includeArchived = false, int offset = 0, int limit = 50)
    {
        var text = Text(query, 200);
        var validPage = offset >= 0 && limit is >= 1 and <= 100;
        if (!validPage)
        {
            throw new ApiException(400, "Offset must be nonnegative and limit between 1 and 100.");
        }
        lock (store.Gate)
        {
            var boardIds = boardId is null ? List(user).Select(board => board.Id).ToHashSet() : new HashSet<string> { RequireBoard(boardId, user).Id };
            var matches = store.Tickets.FindAll()
                .Where(ticket => boardIds.Contains(ticket.BoardId))
                .Where(ticket => includeArchived || !ticket.Archived)
                .Where(ticket => ticket.Id.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || ticket.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || ticket.Description?.Contains(text, StringComparison.OrdinalIgnoreCase) == true
                    || ticket.Tags.Any(tag => tag.Contains(text, StringComparison.OrdinalIgnoreCase))
                    || ticket.Subtasks.Any(task => task.Title.Contains(text, StringComparison.OrdinalIgnoreCase))
                    || ticket.Comments.Any(comment => comment.Text.Contains(text, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(ticket => ticket.BoardId, StringComparer.Ordinal)
                .ThenBy(ticket => ticket.Position)
                .ThenBy(ticket => ticket.Id, StringComparer.Ordinal)
                .ToArray();
            return new(matches.Skip(offset).Take(limit).ToArray(), matches.Length, offset, limit);
        }
    }

    private static void CheckBatch<T>(T[]? tickets)
    {
        var validSize = tickets is { Length: >= 1 and <= 100 };
        if (!validSize)
        {
            throw new ApiException(400, "A batch must contain between 1 and 100 tickets.");
        }
    }

    private static void ValidateInput(object? input)
    {
        var errors = new List<ValidationResult>();
        var valid = input is not null && Validator.TryValidateObject(input, new ValidationContext(input), errors, true);
        if (!valid)
        {
            throw new ApiException(400, "Invalid ticket input.");
        }
    }

    public Ticket Comment(string boardId, string ticketId, string text, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            var ticket = RequireTicket(boardId, ticketId, user);
            if (ticket.Comments.Count >= 1000)
            {
                throw new ApiException(400, "Comment limit reached.");
            }
            var content = Text(text, 4000);
            ticket.Comments.Add(new TicketComment { AuthorId = UserId(user), Text = content });
            Track(ticket, user, "comment_added", "comment", null, content);
            ticket.Revision++;
            store.Tickets.Update(ticket);
            return ticket;
        }
    }

    public Ticket DeleteComment(string boardId, string ticketId, string commentId, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            var ticket = RequireTicket(boardId, ticketId, user);
            var comment = ticket.Comments.FirstOrDefault(item => item.Id == commentId) ?? throw new ApiException(404, "Comment not found.");
            var canDelete = comment.AuthorId == UserId(user) || user.IsInRole("admin");
            if (!canDelete)
            {
                throw new ApiException(403, "You can only delete your own comments.");
            }
            ticket.Comments.Remove(comment);
            Track(ticket, user, "comment_deleted", "comment", comment.Text);
            ticket.Revision++;
            store.Tickets.Update(ticket);
            return ticket;
        }
    }

    public Ticket AddImage(string boardId, string ticketId, ImageAsset image, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            var ticket = RequireTicket(boardId, ticketId, user);
            if (ticket.Images.Count >= 6)
            {
                throw new ApiException(400, "A ticket supports up to six images.");
            }
            store.Images.Insert(image);
            ticket.Images.Add(image.Id);
            Track(ticket, user, "image_added", "image", null, image.Id);
            ticket.Revision++;
            store.Tickets.Update(ticket);
            return ticket;
        }
    }

    public Ticket RemoveImage(string boardId, string ticketId, string imageId, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            var ticket = RequireTicket(boardId, ticketId, user);
            if (!ticket.Images.Remove(imageId))
            {
                throw new ApiException(404, "Image not found.");
            }
            store.Images.Delete(imageId);
            Track(ticket, user, "image_removed", "image", imageId);
            ticket.Revision++;
            store.Tickets.Update(ticket);
            return ticket;
        }
    }

    private void TrackChange(Ticket ticket, ClaimsPrincipal user, string field, string? oldValue, string? newValue)
    {
        if (oldValue != newValue)
        {
            Track(ticket, user, "field_changed", field, oldValue, newValue);
        }
    }

    private void Track(Ticket ticket, ClaimsPrincipal user, string action, string? field = null, string? oldValue = null, string? newValue = null)
    {
        var actorId = UserId(user);
        var actor = store.Accounts.FindById(actorId);
        ticket.Activity.Add(new TicketActivity { ActorId = actorId, ActorName = actor?.Name ?? actorId, Action = action, Field = field, OldValue = oldValue, NewValue = newValue });
    }

    private string? PersonName(string? id) => id is null ? null : store.Accounts.FindById(id)?.Name ?? id;

    private static string ColumnName(Board board, string id) => board.Columns.First(column => column.Id == id).Name;

    private static string SubtasksValue(IEnumerable<Subtask> subtasks) => string.Join("; ", subtasks.Select(task => $"{(task.Done ? "[x]" : "[ ]")} {task.Title}"));
    private static string SubtasksValue(IEnumerable<SubtaskRequest> subtasks) => string.Join("; ", subtasks.Select(task => $"{(task.Done ? "[x]" : "[ ]")} {task.Title}"));

    public static string Text(string value, int maximum)
    {
        var invalid = string.IsNullOrWhiteSpace(value) || value.Length > maximum;
        if (invalid)
        {
            throw new ApiException(400, $"Text must contain between 1 and {maximum} characters.");
        }
        return value.Trim();
    }

    private static string CheckColor(string color)
    {
        var allowed = AllowedColors.Contains(color);
        if (!allowed)
        {
            throw new ApiException(400, "Unknown board color.");
        }
        return color;
    }

    private static string CheckIcon(string icon)
    {
        var allowed = AllowedIcons.Contains(icon);
        if (!allowed)
        {
            throw new ApiException(400, "Unknown board icon.");
        }
        return icon;
    }

    private static void CheckRevision(int current, int expected)
    {
        if (current != expected)
        {
            throw new ApiException(409, "This item changed. Reload before saving.");
        }
    }

    private static void CheckColumn(Board board, string id)
    {
        var exists = board.Columns.Any(column => column.Id == id);
        if (!exists)
        {
            throw new ApiException(400, "Unknown column.");
        }
    }
}
