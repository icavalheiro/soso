using System.Security.Claims;
using System.Security.Cryptography;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;

namespace Soso.Api;

public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class BoardService(Store store, BoardEventHub? events = null)
{
    public static readonly string[] AllowedTags = ["bug", "feature", "design", "docs", "refactor", "test", "chore", "research"];
    public static readonly string[] AllowedPriorities = ["low", "normal", "high", "urgent"];
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
            board.Slug = UniqueSlug(Slugify(board.Name));
            var code = NormalizeCode(request.Code, board.Name);
            if (!string.IsNullOrWhiteSpace(request.Code))
            {
                CheckCodeUnique(code, null);
            }
            else
            {
                code = UniqueCode(code, null);
            }
            board.Code = code;
            board.NextTicketNumber = 1;
            store.Boards.Insert(board);
            var audience = BoardAudience(board);
            store.AfterCommit(() => events?.Publish(board.Id, audience), $"board:{board.Id}");
        }
        return board;
    }

    public Board Update(string id, UpdateBoardRequest request, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            var board = RequireBoard(id, user, true);
            var previousAudience = BoardAudience(board);
            CheckRevision(board.Revision, request.Revision);
            var columnIds = request.Columns.Select(column => column.Id).ToHashSet();
            var invalidColumns = columnIds.Count != request.Columns.Length || request.Columns.Any(column => !Guid.TryParseExact(column.Id, "N", out _));
            var tickets = store.Tickets.Find(ticket => ticket.BoardId == id).ToArray();
            if (invalidColumns)
            {
                throw new ApiException(400, "Columns must have unique valid IDs.");
            }
            if (tickets.Any(ticket => !ticket.Archived && !columnIds.Contains(ticket.ColumnId)))
            {
                throw new ApiException(400, "Move active tickets before deleting a column.");
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
            if (request.Code is not null)
            {
                var code = NormalizeCode(request.Code, board.Name);
                CheckCodeUnique(code, board.Id);
                board.Code = code;
            }
            else if (string.IsNullOrWhiteSpace(board.Code))
            {
                board.Code = UniqueCode(NormalizeCode(null, board.Name), board.Id);
            }
            board.Members = request.Members.Distinct().ToList();
            var columns = request.Columns.Select(column => new BoardColumn { Id = column.Id, Name = Text(column.Name, 60), IsDone = column.IsDone }).ToList();
            var archivedColumnIds = tickets.Where(ticket => ticket.Archived).Select(ticket => ticket.ColumnId).ToHashSet();
            board.RemovedColumns.RemoveAll(item => columnIds.Contains(item.Column.Id) || !archivedColumnIds.Contains(item.Column.Id));
            board.RemovedColumns.AddRange(board.Columns.Select((column, index) => new RemovedBoardColumn { Column = column, Position = index })
                .Where(item => !columnIds.Contains(item.Column.Id) && archivedColumnIds.Contains(item.Column.Id)));
            board.Columns = columns;
            board.Revision++;
            store.Boards.Update(board);
            var currentAudience = BoardAudience(board);
            store.AfterCommit(() => events?.Publish(board.Id, currentAudience.Concat(previousAudience).Distinct(StringComparer.Ordinal)), $"board:{board.Id}");
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

    public TicketActivity[] GetTicketHistory(string boardId, string ticketId, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            return RequireTicket(boardId, ticketId, user).Activity.ToArray();
        }
    }

    public Ticket CreateTicket(string boardId, CreateTicketRequest request, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            ValidateInput(request);
            var subtasks = request.Subtasks ?? [];
            if (subtasks.Length > 100 || subtasks.Any(task => task is null))
            {
                throw new ApiException(400, "A ticket supports up to 100 valid subtasks.");
            }
            foreach (var task in subtasks)
            {
                ValidateInput(task);
            }
            var board = RequireBoard(boardId, user);
            CheckColumn(board, request.ColumnId);
            var validAssignee = request.AssigneeId is null || request.AssigneeId == board.OwnerId || board.Members.Contains(request.AssigneeId);
            var tags = request.Tags ?? [];
            var validTags = tags.All(AllowedTags.Contains);
            var validPriority = AllowedPriorities.Contains(request.Priority);
            if (!validAssignee)
            {
                throw new ApiException(400, "Invalid ticket assignee.");
            }
            if (!validPriority)
            {
                throw new ApiException(400, "Invalid ticket priority.");
            }
            if (!validTags)
            {
                throw new ApiException(400, "Tags must classify software work: bug, feature, design, docs, refactor, test, chore or research.");
            }
            var position = store.Tickets.Find(ticket => ticket.BoardId == boardId).Select(ticket => ticket.Position).DefaultIfEmpty(0).Max() + 1024;
            var validSubtasks = subtasks.Select(task => new Subtask { Id = task.Id, Title = Text(task.Title, 300), Done = task.Done }).ToList();
            if (board.NextTicketNumber < 1)
            {
                board.NextTicketNumber = store.Tickets.Find(ticket => ticket.BoardId == boardId).Select(ticket => ticket.Number).DefaultIfEmpty(0).Max() + 1;
            }
            var ticket = new Ticket { BoardId = boardId, ColumnId = request.ColumnId, Number = board.NextTicketNumber++, Title = Text(request.Title, 160), Description = request.Description?.Trim() ?? "", Priority = request.Priority, Tags = tags.Distinct().ToList(), AssigneeId = request.AssigneeId, Position = position, Subtasks = validSubtasks };
            Track(ticket, user, "created");
            store.Tickets.Insert(ticket);
            store.Boards.Update(board);
            PublishChange(boardId);
            return ticket;
        }
    }

    public Ticket UpdateTicket(string boardId, string ticketId, UpdateTicketRequest request, ClaimsPrincipal user)
    {
        return store.Transaction(() =>
        {
            ValidateInput(request);
            foreach (var task in request.Subtasks)
            {
                ValidateInput(task);
            }
            var board = RequireBoard(boardId, user);
            var ticket = RequireTicket(boardId, ticketId, user);
            CheckRevision(ticket.Revision, request.Revision);
            var targetColumn = board.Columns.FirstOrDefault(column => column.Id == request.ColumnId);
            var removedColumn = board.RemovedColumns.FirstOrDefault(item => item.Column.Id == request.ColumnId);
            var keepsRemovedColumn = ticket.Archived && ticket.ColumnId == request.ColumnId && removedColumn is not null;
            if (targetColumn is null && !keepsRemovedColumn)
            {
                throw new ApiException(400, "Unknown column.");
            }
            var validAssignee = request.AssigneeId is null || request.AssigneeId == board.OwnerId || board.Members.Contains(request.AssigneeId);
            var validPosition = double.IsFinite(request.Position) && Math.Abs(request.Position) < 1e12;
            var validPriority = AllowedPriorities.Contains(request.Priority);
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
            if (targetColumn is null && !request.Archived)
            {
                board.Columns.Insert(Math.Min(removedColumn!.Position, board.Columns.Count), removedColumn.Column);
                board.RemovedColumns.Remove(removedColumn);
                board.Revision++;
                store.Boards.Update(board);
            }
            store.Tickets.Update(ticket);
            PublishChange(boardId);
            return ticket;
        }, joinExisting: true);
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

    /// <summary>Archives every active ticket of one active column in a single transaction. Returns the tickets archived by this call.</summary>
    public Ticket[] ArchiveColumnTickets(string boardId, string columnId, ClaimsPrincipal user)
    {
        return store.Transaction(() =>
        {
            var board = RequireBoard(boardId, user);
            if (board.Columns.All(column => column.Id != columnId))
            {
                throw new ApiException(404, "Column not found.");
            }
            var tickets = store.Tickets.Find(ticket => ticket.BoardId == boardId && ticket.ColumnId == columnId && !ticket.Archived)
                .OrderBy(ticket => ticket.Position).ThenBy(ticket => ticket.Id, StringComparer.Ordinal)
                .ToArray();
            foreach (var ticket in tickets)
            {
                TrackChange(ticket, user, "archived", "False", "True");
                ticket.Archived = true;
                ticket.Revision++;
                store.Tickets.Update(ticket);
            }
            if (tickets.Length > 0)
            {
                PublishChange(boardId);
            }
            return tickets;
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
            var codes = store.Boards.FindAll().Where(board => boardIds.Contains(board.Id)).ToDictionary(board => board.Id, board => board.Code ?? "");
            var matches = store.Tickets.FindAll()
                .Where(ticket => boardIds.Contains(ticket.BoardId))
                .Where(ticket => includeArchived || !ticket.Archived)
                .Where(ticket => ticket.Id.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || ticket.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || ticket.Description?.Contains(text, StringComparison.OrdinalIgnoreCase) == true
                    || ticket.Tags.Any(tag => tag.Contains(text, StringComparison.OrdinalIgnoreCase))
                    || ticket.Subtasks.Any(task => task.Title.Contains(text, StringComparison.OrdinalIgnoreCase))
                    || ticket.Comments.Any(comment => comment.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
                    || (codes.TryGetValue(ticket.BoardId, out var code) && !string.IsNullOrEmpty(code) && TicketKey(code, ticket.Number).Contains(text, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(ticket => ticket.BoardId, StringComparer.Ordinal)
                .ThenBy(ticket => ticket.Position)
                .ThenBy(ticket => ticket.Id, StringComparer.Ordinal)
                .ToArray();
            return new(matches.Skip(offset).Take(limit).ToArray(), matches.Length, offset, limit);
        }
    }

    /// <summary>Assigns a URL slug to boards created before slugs existed. Safe to call on every startup.</summary>
    public void EnsureSlugs()
    {
        lock (store.Gate)
        {
            foreach (var board in store.Boards.FindAll().Where(board => string.IsNullOrEmpty(board.Slug)).ToArray())
            {
                board.Slug = UniqueSlug(Slugify(board.Name));
                store.Boards.Update(board);
            }
        }
    }

    /// <summary>Backfills human-readable ticket keys for boards/tickets created before codes existed. Safe to call on every startup.</summary>
    public void EnsureTicketKeys()
    {
        lock (store.Gate)
        {
            foreach (var board in store.Boards.FindAll().ToArray())
            {
                var changed = false;
                if (string.IsNullOrWhiteSpace(board.Code))
                {
                    board.Code = UniqueCode(NormalizeCode(null, board.Name), board.Id);
                    changed = true;
                }
                var tickets = store.Tickets.Find(ticket => ticket.BoardId == board.Id).OrderBy(ticket => ticket.Position).ThenBy(ticket => ticket.Id, StringComparer.Ordinal).ToArray();
                var max = tickets.Select(ticket => ticket.Number).DefaultIfEmpty(0).Max();
                foreach (var ticket in tickets.Where(ticket => ticket.Number < 1))
                {
                    ticket.Number = ++max;
                    store.Tickets.Update(ticket);
                }
                if (board.NextTicketNumber < 1)
                {
                    board.NextTicketNumber = max + 1;
                    changed = true;
                }
                else if (board.NextTicketNumber <= max)
                {
                    board.NextTicketNumber = max + 1;
                    changed = true;
                }
                if (changed)
                {
                    store.Boards.Update(board);
                }
            }
        }
    }

    /// <summary>Formats a human-readable ticket key, e.g. SOS-00001. Expands beyond 5 digits as needed.</summary>
    public static string TicketKey(string code, int number) => $"{code}-{number:D5}";

    public static string TicketKey(Board board, Ticket ticket) => TicketKey(board.Code, ticket.Number);

    /// <summary>Normalizes a user-supplied board code or derives a 3-letter code from the board name (first 3 ASCII letters, diacritics removed).</summary>
    public static string NormalizeCode(string? code, string name)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            var normalized = new string(code.Trim().ToUpperInvariant().Where(char.IsAsciiLetter).ToArray());
            if (normalized.Length is >= 2 and <= 10)
            {
                return normalized;
            }
            throw new ApiException(400, "Board code must contain 2 to 10 letters (A-Z).");
        }
        var clean = RemoveDiacritics(name.Trim()).ToUpperInvariant();
        var letters = new string(clean.Where(char.IsAsciiLetter).ToArray());
        if (letters.Length == 0)
        {
            return "SOS";
        }
        return letters.Length >= 3 ? letters[..3] : letters.PadRight(3, 'X');
    }

    private static string RemoveDiacritics(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private bool IsCodeTaken(string candidate, string? excludeId) => store.Boards.FindAll()
        .Any(board => board.Id != excludeId && string.Equals(board.Code, candidate, StringComparison.OrdinalIgnoreCase));

    private void CheckCodeUnique(string candidate, string? excludeId)
    {
        if (IsCodeTaken(candidate, excludeId))
        {
            throw new ApiException(400, "This board code is already in use. Choose another one.");
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
            PublishChange(boardId);
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
            PublishChange(boardId);
            return ticket;
        }
    }

    public Ticket AddImage(string boardId, string ticketId, ImageAsset image, ClaimsPrincipal user, bool isVideo = false)
    {
        lock (store.Gate)
        {
            var ticket = RequireTicket(boardId, ticketId, user);
            if (ticket.Images.Count + ticket.Videos.Count >= 6)
            {
                throw new ApiException(400, "A ticket supports up to six attachments.");
            }
            store.Images.Insert(image);
            if (isVideo)
            {
                ticket.Videos.Add(image.Id);
            }
            else
            {
                ticket.Images.Add(image.Id);
            }
            Track(ticket, user, "image_added", "image", null, image.Id);
            ticket.Revision++;
            store.Tickets.Update(ticket);
            PublishChange(boardId);
            return ticket;
        }
    }

    public Ticket RemoveImage(string boardId, string ticketId, string imageId, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            var ticket = RequireTicket(boardId, ticketId, user);
            if (!ticket.Images.Remove(imageId) && !ticket.Videos.Remove(imageId))
            {
                throw new ApiException(404, "Image not found.");
            }
            store.Images.Delete(imageId);
            store.VideoLinks.DeleteMany(link => link.VideoId == imageId);
            Track(ticket, user, "image_removed", "image", imageId);
            ticket.Revision++;
            store.Tickets.Update(ticket);
            PublishChange(boardId);
            return ticket;
        }
    }

    public AttachmentInfo[] ListAttachments(string boardId, string ticketId, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            var ticket = RequireTicket(boardId, ticketId, user);
            var infos = new List<AttachmentInfo>();
            foreach (var id in ticket.Images)
            {
                var asset = store.Images.FindById(id);
                if (asset is not null)
                {
                    infos.Add(new(id, "image", asset.ContentType, asset.Content.LongLength, null));
                }
            }
            foreach (var id in ticket.Videos)
            {
                var asset = store.Images.FindById(id);
                if (asset is not null)
                {
                    infos.Add(new(id, "video", asset.ContentType, asset.Content.LongLength, asset.Thumbnail.LongLength));
                }
            }
            return infos.ToArray();
        }
    }

    public ImageAsset TicketImage(string boardId, string ticketId, string imageId, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            var ticket = RequireTicket(boardId, ticketId, user);
            var attached = ticket.Images.Contains(imageId);
            var asset = attached ? store.Images.FindById(imageId) : null;
            if (asset is null)
            {
                throw new ApiException(404, "Image not found.");
            }
            return asset;
        }
    }

    public ImageAsset TicketVideo(string boardId, string ticketId, string videoId, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            var ticket = RequireTicket(boardId, ticketId, user);
            var attached = ticket.Videos.Contains(videoId);
            var asset = attached ? store.Images.FindById(videoId) : null;
            if (asset is null)
            {
                throw new ApiException(404, "Video not found.");
            }
            return asset;
        }
    }

    /// <summary>Mints a single-use video download grant valid for one hour. Only the secret hash is stored.</summary>
    public (string Secret, VideoLink Link) CreateVideoLink(string boardId, string ticketId, string videoId, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            var ticket = RequireTicket(boardId, ticketId, user);
            if (!ticket.Videos.Contains(videoId) || store.Images.FindById(videoId) is null)
            {
                throw new ApiException(404, "Video not found.");
            }
            PurgeExpiredVideoLinks();
            var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var link = new VideoLink
            {
                Id = AuthEndpoints.HashToken(secret),
                VideoId = videoId,
                TicketId = ticketId,
                BoardId = boardId,
                OwnerId = UserId(user),
                ExpiresAt = DateTime.UtcNow.AddHours(1),
            };
            store.VideoLinks.Insert(link);
            return (secret, link);
        }
    }

    /// <summary>Atomically consumes a video download grant and returns the video bytes.</summary>
    public (byte[] Content, string ContentType) RedeemVideoLink(string token)
    {
        lock (store.Gate)
        {
            var link = token.Length <= 256 ? store.VideoLinks.FindById(AuthEndpoints.HashToken(token)) : null;
            if (link is null || link.Used || link.ExpiresAt <= DateTime.UtcNow)
            {
                throw new ApiException(404, "Video link not found or expired.");
            }
            var ticket = store.Tickets.FindById(link.TicketId);
            var attached = ticket is not null && ticket.BoardId == link.BoardId && ticket.Videos.Contains(link.VideoId);
            var video = attached ? store.Images.FindById(link.VideoId) : null;
            if (video is null)
            {
                throw new ApiException(404, "Video link not found or expired.");
            }
            link.Used = true;
            store.VideoLinks.Update(link);
            PurgeExpiredVideoLinks();
            return (video.Content, video.ContentType);
        }
    }

    private void PurgeExpiredVideoLinks()
    {
        var now = DateTime.UtcNow;
        store.VideoLinks.DeleteMany(link => link.ExpiresAt <= now);
    }

    public void DeleteBoard(string id, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            var board = RequireBoard(id, user, true);
            var audience = BoardAudience(board);
            store.Tickets.DeleteMany(ticket => ticket.BoardId == id);
            store.Images.DeleteMany(image => image.BoardId == id);
            store.VideoLinks.DeleteMany(link => link.BoardId == id);
            store.Boards.Delete(id);
            store.AfterCommit(() => events?.Publish(id, audience), $"board:{id}");
        }
    }

    public void DeleteTicket(string boardId, string ticketId, ClaimsPrincipal user)
    {
        lock (store.Gate)
        {
            var ticket = RequireTicket(boardId, ticketId, user);
            foreach (var image in ticket.Images.Concat(ticket.Videos))
            {
                store.Images.Delete(image);
            }
            store.VideoLinks.DeleteMany(link => link.TicketId == ticketId);
            store.Tickets.Delete(ticketId);
            PublishChange(boardId);
        }
    }

    private void PublishChange(string boardId, IEnumerable<string>? previousAudience = null)
    {
        if (events is null)
        {
            return;
        }
        string[] audience;
        lock (store.Gate)
        {
            var board = store.Boards.FindById(boardId);
            audience = board is null ? previousAudience?.Distinct(StringComparer.Ordinal).ToArray() ?? []
                : BoardAudience(board).Concat(previousAudience ?? []).Distinct(StringComparer.Ordinal).ToArray();
        }
        store.AfterCommit(() => events.Publish(boardId, audience), $"board:{boardId}");
    }

    private string[] BoardAudience(Board board) => store.Accounts.FindAll()
        .Where(account => !account.Disabled && (account.IsAdmin || (account.BoardIds is not null
            ? account.BoardIds.Contains(board.Id)
            : account.Id == board.OwnerId || board.Members.Contains(account.Id))))
        .Select(account => account.Id).Distinct(StringComparer.Ordinal).ToArray();

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

    private static string ColumnName(Board board, string id) => board.Columns.FirstOrDefault(column => column.Id == id)?.Name
        ?? board.RemovedColumns.FirstOrDefault(item => item.Column.Id == id)?.Column.Name ?? id;

    private static string SubtasksValue(IEnumerable<Subtask> subtasks) => string.Join("; ", subtasks.Select(task => $"{(task.Done ? "[x]" : "[ ]")} {task.Title}"));
    private static string SubtasksValue(IEnumerable<SubtaskRequest> subtasks) => string.Join("; ", subtasks.Select(task => $"{(task.Done ? "[x]" : "[ ]")} {task.Title}"));

    /// <summary>Builds a URL-safe lowercase slug from a board name, e.g. "OtxIdle" -> "otxidle".</summary>
    public static string Slugify(string name)
    {
        var normalized = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var pendingSeparator = false;
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            var isLetterOrDigit = character is >= 'a' and <= 'z' or >= '0' and <= '9';
            if (isLetterOrDigit)
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }
                pendingSeparator = false;
                builder.Append(character);
            }
            else
            {
                pendingSeparator = builder.Length > 0;
            }
        }
        var slug = builder.ToString();
        if (slug.Length > 60)
        {
            slug = slug[..60].TrimEnd('-');
        }
        return slug.Length == 0 ? "board" : slug;
    }

    private string UniqueSlug(string baseSlug)
    {
        var taken = store.Boards.FindAll()
            .Select(board => board.Slug)
            .Where(slug => !string.IsNullOrEmpty(slug))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(baseSlug))
        {
            return baseSlug;
        }
        for (var suffix = 2; suffix <= 10000; suffix++)
        {
            var candidate = $"{baseSlug}-{suffix}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
        return $"{baseSlug}-{Guid.NewGuid():N}";
    }

    private string UniqueCode(string candidate, string? excludeId)
    {
        var taken = store.Boards.FindAll()
            .Where(board => board.Id != excludeId)
            .Select(board => board.Code)
            .Where(code => !string.IsNullOrEmpty(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(candidate))
        {
            return candidate;
        }
        for (var suffix = 2; suffix <= 10000; suffix++)
        {
            var suffixed = $"{candidate}{suffix}";
            var trimmed = suffixed.Length <= 10 ? suffixed : suffixed[^10..];
            if (!taken.Contains(trimmed))
            {
                return trimmed;
            }
        }
        return $"{candidate}{Guid.NewGuid():N}"[^10..].ToUpperInvariant();
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
