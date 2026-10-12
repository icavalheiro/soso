namespace Soso.Api;

/// <summary>
/// MCP-only shaping for bulk ticket reads. Projects <see cref="Ticket"/> to the
/// requested fields without touching stored data or <see cref="BoardService"/>
/// permission logic. REST endpoints are unaffected.
/// </summary>
public static class TicketFieldProjection
{
    public static readonly string[] AllowedFields =
    [
        "id", "boardId", "columnId", "title", "description", "priority", "tags",
        "archived", "assigneeId", "dueDate", "position", "revision",
        "subtasks", "comments", "activity", "images", "videos",
    ];

    public static string AllowedList => string.Join(", ", AllowedFields);

    public static Ticket[] Project(Ticket[] tickets, string[]? fields)
    {
        if (fields is null)
        {
            return tickets;
        }
        var selected = Normalize(fields);
        return tickets.Select(ticket => Project(ticket, selected)).ToArray();
    }

    public static HashSet<string> Normalize(string[] fields)
    {
        if (fields.Length == 0)
        {
            throw new ApiException(400, $"fields must contain at least one of: {AllowedList}.");
        }
        var allowed = AllowedFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in fields)
        {
            var name = raw?.Trim() ?? "";
            if (name.Length == 0 || !allowed.Contains(name))
            {
                throw new ApiException(400, $"Unknown ticket field '{raw}'. Allowed fields: {AllowedList}.");
            }
            selected.Add(name);
        }
        if (selected.Count == 0)
        {
            throw new ApiException(400, $"fields must contain at least one of: {AllowedList}.");
        }
        // Always required for follow-up move_ticket/update_ticket calls.
        selected.Add("id");
        selected.Add("boardId");
        selected.Add("revision");
        return selected;
    }

    public static Ticket Project(Ticket ticket, HashSet<string> selected)
    {
        return new Ticket
        {
            Id = ticket.Id,
            BoardId = ticket.BoardId,
            ColumnId = selected.Contains("columnId") ? ticket.ColumnId : "",
            Title = selected.Contains("title") ? ticket.Title : "",
            Description = selected.Contains("description") ? ticket.Description : "",
            Priority = selected.Contains("priority") ? ticket.Priority : "",
            Tags = selected.Contains("tags") ? ticket.Tags : [],
            Archived = selected.Contains("archived") && ticket.Archived,
            AssigneeId = selected.Contains("assigneeId") ? ticket.AssigneeId : null,
            DueDate = selected.Contains("dueDate") ? ticket.DueDate : null,
            Position = selected.Contains("position") ? ticket.Position : 0,
            Revision = ticket.Revision,
            Subtasks = selected.Contains("subtasks") ? ticket.Subtasks : [],
            Comments = selected.Contains("comments") ? ticket.Comments : [],
            Activity = selected.Contains("activity") ? ticket.Activity : [],
            Images = selected.Contains("images") ? ticket.Images : [],
            Videos = selected.Contains("videos") ? ticket.Videos : [],
        };
    }
}
