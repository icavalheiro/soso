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
    public static readonly string[] AllowedIcons = ["columns", "briefcase", "house", "heart", "star", "rocket", "code", "book", "graduation-cap", "plane", "wallet", "target"];
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
        var canRead = board is not null && tokenAllows && (board.OwnerId == userId || board.Members.Contains(userId) || user.IsInRole("admin"));
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
        return store.Boards.FindAll().Where(board => TokenAllowsBoard(board.Id, user) && (board.OwnerId == id || board.Members.Contains(id) || user.IsInRole("admin"))).ToArray();
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
            var position = store.Tickets.Find(ticket => ticket.BoardId == boardId).Select(ticket => ticket.Position).DefaultIfEmpty(0).Max() + 1024;
            var ticket = new Ticket { BoardId = boardId, ColumnId = request.ColumnId, Title = Text(request.Title, 160), Position = position };
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
            return tickets.Select(ticket => UpdateTicket(boardId, ticket.TicketId, ticket.Update, user)).ToArray();
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
                .Where(ticket => ticket.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || ticket.Description.Contains(text, StringComparison.OrdinalIgnoreCase)
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
            ticket.Comments.Add(new TicketComment { AuthorId = UserId(user), Text = Text(text, 4000) });
            ticket.Revision++;
            store.Tickets.Update(ticket);
            return ticket;
        }
    }

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