using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Soso.Api;
using Xunit;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Soso.Api.Tests;

public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly string dataPath = Path.Combine(Path.GetTempPath(), "soso-tests-" + Guid.NewGuid().ToString("N"));
    public const string Password = "Test-only-password-2026!";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DataPath", dataPath);
        builder.UseSetting("Bootstrap:Email", "admin@example.test");
        builder.UseSetting("Bootstrap:Password", Password);
        builder.UseSetting("Logging:LogLevel:Default", "Error");
    }
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(dataPath))
        {
            Directory.Delete(dataPath, true);
        }
    }
}

[Collection("LiteDB tests")]
public sealed class SecurityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task Csrf(HttpClient client)
    {
        var result = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", result.GetProperty("token").GetString());
    }

    private static async Task<AccountResponse> Login(HttpClient client, string email = "admin@example.test")
    {
        await Csrf(client);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = AppFactory.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").First(value => value.StartsWith("__Host-soso="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        await Csrf(client);
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
    }

    private static async Task<AccountResponse> CreateUser(HttpClient admin, string email)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/accounts", new { email, name = email.Split('@')[0], password = AppFactory.Password, isAdmin = false });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
    }

    private static async Task<Board> CreateBoard(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/boards", new { name, description = "Private board" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Board>())!;
    }

    private static UpdateTicketRequest Edit(Ticket ticket, string[]? tags = null, bool archived = false) => new(ticket.Title, ticket.Description, ticket.ColumnId, ticket.Priority, ticket.AssigneeId, null, ticket.Position, [], tags ?? [], archived, ticket.Revision);

    private static PatchTicketRequest Patch(Ticket ticket, string[]? tags = null) => new(ticket.Revision, Tags: tags);

    private static JsonElement McpMessage(string body)
    {
        var json = body.TrimStart().StartsWith('{') ? body : body.Split('\n').Single(line => line.StartsWith("data:", StringComparison.Ordinal))[5..];
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static void WithMcp(Action<BoardService, McpTools, ClaimsPrincipal, HttpContextAccessor, Store> test)
    {
        var path = Path.Combine(Path.GetTempPath(), "soso-mcp-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = path }).Build();
            using var store = new Store(configuration);
            var service = new BoardService(store);
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));
            var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } };
            test(service, new McpTools(service, accessor), user, accessor, store);
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }

    [Fact]
    public void McpBatchesAreAtomicAndRespectRevisionsAndAccess()
    {
        WithMcp((service, tools, user, accessor, store) =>
        {
            var board = service.Create(new("Batch", ""), user);
            var column = board.Columns[0].Id;
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.CreateTickets(board.Id, [new("Valid", column, "Requested work", ["test"]), new("Invalid", "missing", "Requested work", ["test"])])).Status);
            Assert.Empty(tools.GetBoard(board.Id).Tickets);
            var tickets = tools.CreateTickets(board.Id, [new("First", column, "Requested work", ["test"]), new("Second", column, "Requested work", ["test"])]);
            Assert.Equal(2, tickets.Length);
            Assert.True(tickets[1].Position > tickets[0].Position);
            var updates = tickets.Select(ticket => new BatchTicketUpdateRequest(ticket.Id, Patch(ticket) with { Title = ticket.Title + " updated" })).ToArray();
            Assert.Equal(409, Assert.Throws<ApiException>(() => tools.UpdateTickets(board.Id, [updates[0], updates[1] with { Update = updates[1].Update with { Revision = -1 } }])).Status);
            var unchanged = tools.GetBoard(board.Id).Tickets;
            Assert.Equal(tickets.Select(ticket => ticket.Title), unchanged.Select(ticket => ticket.Title));
            Assert.Equal(tickets.Select(ticket => ticket.Revision), unchanged.Select(ticket => ticket.Revision));
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.UpdateTickets(board.Id, [updates[0], updates[0]])).Status);
            var changed = tools.UpdateTickets(board.Id, updates);
            Assert.All(changed, ticket => Assert.EndsWith(" updated", ticket.Title));
            Assert.Equal(tickets.Select(ticket => ticket.Revision + 1), changed.Select(ticket => ticket.Revision));
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.CreateTickets(board.Id, [])).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.CreateTickets(board.Id, Enumerable.Repeat(new McpCreateTicketRequest("Too many", column, "Requested work", ["test"]), 101).ToArray())).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.CreateTickets(board.Id, [null!])).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.UpdateTickets(board.Id, [new(changed[0].Id, Patch(changed[0]) with { Title = "Must roll back" }), new(changed[1].Id, Patch(changed[1]) with { Subtasks = [null!] })])).Status);
            Assert.Equal(changed.Select(ticket => ticket.Title), tools.GetBoard(board.Id).Tickets.Select(ticket => ticket.Title));
            accessor.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "outsider")], "test"));
            Assert.Equal(404, Assert.Throws<ApiException>(() => tools.CreateTickets(board.Id, [new("Forbidden", column, "Requested work", ["test"])])).Status);
            Assert.Equal(404, Assert.Throws<ApiException>(() => tools.UpdateTickets(board.Id, [new(changed[0].Id, Patch(changed[0]))])).Status);
        });
    }

    [Fact]
    public void McpCreationRequiresSpecificationAndWorkTags()
    {
        WithMcp((service, tools, user, accessor, store) =>
        {
            var board = service.Create(new("Specifications", ""), user);
            var column = board.Columns[0].Id;
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.CreateTicket(board.Id, column, "Empty description", " ", ["bug"])).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.CreateTicket(board.Id, column, "Missing description", null!, ["bug"])).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.CreateTicket(board.Id, column, "Empty tags", "Fix the defect", [])).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.CreateTicket(board.Id, column, "Missing tags", "Fix the defect", null!)).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.CreateTicket(board.Id, column, "Status is not a tag", "Fix the defect", ["done"])).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.CreateTicket(board.Id, column, "Long description", new string('x', 12001), ["bug"])).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.CreateTickets(board.Id, [new("Valid", column, "Fix the defect", ["bug"]), new("Invalid", column, "", ["test"])])).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.CreateTickets(board.Id, [new("Valid", column, "Fix the defect", ["bug"]), new("Invalid", column, "Fix the defect", ["custom"])])).Status);
            Assert.Empty(tools.GetBoard(board.Id).Tickets);
            var created = tools.CreateTicket(board.Id, column, "Fix a defect", "  Context, expected behavior and acceptance criteria.  ", ["bug", "test", "bug"]);
            var saved = Assert.Single(tools.GetBoard(board.Id).Tickets);
            Assert.Equal(created.Id, saved.Id);
            Assert.Equal("Context, expected behavior and acceptance criteria.", saved.Description);
            Assert.Equal(new[] { "bug", "test" }, saved.Tags);
            Assert.Empty(saved.Comments);
            var staged = tools.CreateTicket(board.Id, column, "Multi-stage work", "Complete several distinct stages", ["feature"], [new("Implement"), new("Verify")]);
            Assert.Equal(new[] { "Implement", "Verify" }, staged.Subtasks.Select(task => task.Title));
            Assert.All(staged.Subtasks, task => Assert.False(task.Done));
            var legacy = service.CreateTicket(board.Id, new("Browser client", column), user);
            Assert.Empty(legacy.Description);
            Assert.Empty(legacy.Tags);
            var assigned = service.CreateTicket(board.Id, new("Assigned to creator", column, AssigneeId: BoardService.UserId(user)), user);
            Assert.Equal(BoardService.UserId(user), assigned.AssigneeId);
            Assert.Equal(400, Assert.Throws<ApiException>(() => service.CreateTicket(board.Id, new("Invalid assignee", column, AssigneeId: "outsider"), user)).Status);
        });
    }

    [Fact]
    public void McpPartialUpdatesPreserveOmittedFieldsAndClearOnlyExplicitValues()
    {
        WithMcp((service, tools, user, accessor, store) =>
        {
            var board = service.Create(new("Patches", ""), user);
            var ticket = tools.CreateTicket(board.Id, board.Columns[1].Id, "Original title", "Original specification and acceptance criteria", ["bug", "test"]);
            var deadline = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(3));
            ticket = service.UpdateTicket(board.Id, ticket.Id, Edit(ticket, ["bug", "test"], true) with { Priority = "urgent", AssigneeId = "owner", DueDate = deadline, Position = 2048, Subtasks = [new("existing-task", "Verify behavior", true)] }, user);
            ticket = tools.AddComment(board.Id, ticket.Id, "Investigation notes");
            ticket.Images.Add("existing-image");
            store.Tickets.Update(ticket);
            ticket = Assert.Single(tools.GetBoard(board.Id).Tickets);
            var before = JsonSerializer.SerializeToElement(ticket, JsonOptions);
            var patched = tools.UpdateTicket(board.Id, ticket.Id, new(ticket.Revision, Priority: "high"));
            var after = JsonSerializer.SerializeToElement(patched, JsonOptions);
            foreach (var property in before.EnumerateObject())
            {
                var changedProperty = property.Name is "revision" or "priority" or "activity";
                if (!changedProperty)
                {
                    Assert.Equal(property.Value.GetRawText(), after.GetProperty(property.Name).GetRawText());
                }
            }
            Assert.Equal("high", patched.Priority);
            Assert.Equal(ticket.Revision + 1, patched.Revision);
            Assert.Equal(409, Assert.Throws<ApiException>(() => tools.UpdateTicket(board.Id, ticket.Id, new(ticket.Revision, Title: "Stale"))).Status);
            var nullPatch = JsonSerializer.Deserialize<PatchTicketRequest>(JsonSerializer.Serialize(new { revision = patched.Revision, assigneeId = (string?)null, dueDate = (string?)null }), JsonOptions)!;
            patched = tools.UpdateTicket(board.Id, ticket.Id, nullPatch);
            Assert.Equal("owner", patched.AssigneeId);
            Assert.Equal(deadline.UtcDateTime, patched.DueDate);
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PatchTicketRequest>("{\"priority\":\"high\"}", JsonOptions));
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.UpdateTicket(board.Id, ticket.Id, new(patched.Revision, AssigneeId: "owner", ClearAssignee: true))).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.UpdateTicket(board.Id, ticket.Id, new(patched.Revision, DueDate: deadline, ClearDueDate: true))).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.UpdateTicket(board.Id, ticket.Id, new(patched.Revision, Priority: "invalid"))).Status);
            var completed = tools.MoveTicket(board.Id, ticket.Id, board.Columns.Single(column => column.IsDone).Id, patched.Revision);
            Assert.True(board.Columns.Single(column => column.Id == completed.ColumnId).IsDone);
            Assert.Equal(ticket.Description, completed.Description);
            Assert.Equal(ticket.Tags, completed.Tags);
            Assert.Equal(ticket.AssigneeId, completed.AssigneeId);
            Assert.Equal(ticket.DueDate, completed.DueDate);
            Assert.Equal(ticket.Position, completed.Position);
            Assert.True(completed.Archived);
            Assert.Single(completed.Subtasks);
            Assert.Single(completed.Comments);
            Assert.Single(completed.Images);
            var cleared = tools.UpdateTicket(board.Id, ticket.Id, new(completed.Revision, Description: "", Position: 0, Subtasks: [], Tags: [], Archived: false, ClearAssignee: true, ClearDueDate: true));
            Assert.Empty(cleared.Description);
            Assert.Empty(cleared.Tags);
            Assert.Empty(cleared.Subtasks);
            Assert.False(cleared.Archived);
            Assert.Equal(0, cleared.Position);
            Assert.Null(cleared.AssigneeId);
            Assert.Null(cleared.DueDate);
            Assert.Single(cleared.Comments);
            Assert.Single(cleared.Images);
            var reloaded = Assert.Single(tools.GetBoard(board.Id).Tickets);
            Assert.Equal(cleared.Revision, reloaded.Revision);
            Assert.Null(reloaded.AssigneeId);
            Assert.Null(reloaded.DueDate);
            Assert.False(reloaded.Archived);
        });
    }

    [Fact]
    public void TicketActivityRecordsCreationChangesAndCommentEventsWithActorAndTime()
    {
        WithMcp((service, tools, user, accessor, store) =>
        {
            var board = service.Create(new("Activity", ""), user);
            var ticket = service.CreateTicket(board.Id, new("History ticket", board.Columns[0].Id), user);
            var created = Assert.Single(ticket.Activity);
            Assert.Equal("created", created.Action);
            Assert.Equal("owner", created.ActorName);
            Assert.NotEqual(default, created.CreatedAt);

            ticket = service.UpdateTicket(board.Id, ticket.Id, Edit(ticket) with { ColumnId = board.Columns[1].Id, Title = "Renamed" }, user);
            Assert.Contains(ticket.Activity, item => item.Action == "field_changed" && item.Field == "status" && item.OldValue == board.Columns[0].Name && item.NewValue == board.Columns[1].Name);
            Assert.Contains(ticket.Activity, item => item.Action == "field_changed" && item.Field == "title" && item.OldValue == "History ticket" && item.NewValue == "Renamed");

            ticket = service.Comment(board.Id, ticket.Id, "A note for the history", user);
            var commentEvent = Assert.Single(ticket.Activity, item => item.Action == "comment_added");
            Assert.Equal("A note for the history", commentEvent.NewValue);
            Assert.Equal("owner", commentEvent.ActorId);
            ticket = service.DeleteComment(board.Id, ticket.Id, ticket.Comments[0].Id, user);
            Assert.Contains(ticket.Activity, item => item.Action == "comment_deleted" && item.OldValue == "A note for the history");
        });
    }

    [Fact]
    public void McpTextSearchScopesAccessAndSupportsPaginationAndArchive()
    {
        WithMcp((service, tools, user, accessor, store) =>
        {
            var board = service.Create(new("Visible", ""), user);
            var tickets = tools.CreateTickets(board.Id, Enumerable.Range(0, 6).Select(index => new McpCreateTicketRequest(index == 0 ? "Needle title" : "Ticket " + index, board.Columns[0].Id, "Requested work", ["test"])).ToArray());
            Assert.Equal(tickets[0].Id, Assert.Single(tools.SearchTickets("needle").Tickets).Id);
            var byId = tools.SearchTickets(tickets[0].Id.ToUpperInvariant());
            Assert.Equal(1, byId.Total);
            Assert.Equal(tickets[0].Id, Assert.Single(byId.Tickets).Id);
            Assert.Contains(tools.SearchTickets(tickets[0].Id[..8], board.Id).Tickets, ticket => ticket.Id == tickets[0].Id);
            tools.UpdateTicket(board.Id, tickets[1].Id, Patch(tickets[1]) with { Description = "Needle description" });
            tools.UpdateTicket(board.Id, tickets[2].Id, Patch(tickets[2]) with { Subtasks = [new("task", "Needle subtask", false)] });
            tools.AddComment(board.Id, tickets[3].Id, "Needle comment");
            tools.UpdateTicket(board.Id, tickets[4].Id, Patch(tickets[4]) with { Title = "Needle archive", Archived = true });
            tools.UpdateTicket(board.Id, tickets[5].Id, Patch(tickets[5], ["research"]));
            var privateUser = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "other")], "test"));
            var hidden = service.Create(new("Hidden", ""), privateUser);
            var hiddenTicket = service.CreateTicket(hidden.Id, new("Needle secret", hidden.Columns[0].Id), privateUser);
            Assert.Empty(tools.SearchTickets(hiddenTicket.Id).Tickets);
            Assert.Empty(tools.SearchTickets(tickets[4].Id).Tickets);
            Assert.Equal(tickets[4].Id, Assert.Single(tools.SearchTickets(tickets[4].Id, includeArchived: true).Tickets).Id);
            var result = tools.SearchTickets("NEEDLE", limit: 2);
            Assert.Equal(4, result.Total);
            Assert.Equal(2, result.Tickets.Length);
            Assert.Equal(0, result.Offset);
            Assert.Equal(2, result.Limit);
            Assert.All(result.Tickets, ticket => Assert.Equal(board.Id, ticket.BoardId));
            var next = tools.SearchTickets("needle", offset: 2, limit: 2);
            Assert.Empty(result.Tickets.Select(ticket => ticket.Id).Intersect(next.Tickets.Select(ticket => ticket.Id)));
            Assert.Equal(5, tools.SearchTickets("needle", includeArchived: true).Total);
            Assert.Single(tools.SearchTickets("SEARCH", board.Id).Tickets);
            Assert.Empty(tools.SearchTickets("absent").Tickets);
            Assert.Equal(404, Assert.Throws<ApiException>(() => tools.SearchTickets("needle", hidden.Id)).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.SearchTickets(" ")).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.SearchTickets("needle", offset: -1)).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.SearchTickets("needle", limit: 101)).Status);
        });
    }

    [Fact]
    public void McpTokenBoardAssignmentsRestrictReadsSearchAndWrites()
    {
        WithMcp((service, tools, user, accessor, store) =>
        {
            var board = service.Create(new("Assigned", ""), user);
            var hidden = service.Create(new("Unassigned", ""), user);
            var assignedTicket = service.CreateTicket(board.Id, new("Needle assigned", board.Columns[0].Id), user);
            var unassignedTicket = service.CreateTicket(hidden.Id, new("Needle hidden", hidden.Columns[0].Id), user);
            var token = new AccessToken { Id = "token", UserId = "owner", ExpiresAt = DateTime.UtcNow.AddDays(1) };
            store.Tokens.Insert(token);
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "owner"), new Claim(ClaimTypes.Role, "admin"), new Claim(McpAuthentication.TokenClaim, token.Id)], "Mcp");
            accessor.HttpContext!.User = new ClaimsPrincipal(identity);
            Assert.Empty(tools.ListBoards());
            Assert.Empty(tools.SearchTickets("needle").Tickets);
            Assert.Empty(tools.SearchTickets(assignedTicket.Id).Tickets);
            Assert.Equal(404, Assert.Throws<ApiException>(() => tools.GetBoard(board.Id)).Status);
            Assert.Equal(404, Assert.Throws<ApiException>(() => tools.CreateTickets(board.Id, [new("Denied", board.Columns[0].Id, "Requested work", ["test"])])).Status);
            token.BoardIds = [board.Id];
            store.Tokens.Update(token);
            Assert.Equal(board.Id, Assert.Single(tools.ListBoards()).Id);
            Assert.Equal(board.Id, tools.GetBoard(board.Id).Board.Id);
            Assert.Single(tools.SearchTickets("needle").Tickets);
            Assert.Equal(assignedTicket.Id, Assert.Single(tools.SearchTickets(assignedTicket.Id).Tickets).Id);
            Assert.Empty(tools.SearchTickets(unassignedTicket.Id).Tickets);
            Assert.Equal(404, Assert.Throws<ApiException>(() => tools.GetBoard(hidden.Id)).Status);
            Assert.Equal(404, Assert.Throws<ApiException>(() => tools.SearchTickets("needle", hidden.Id)).Status);
            var created = tools.CreateTickets(board.Id, [new("Allowed", board.Columns[0].Id, "Requested work", ["test"])]);
            tools.UpdateTickets(board.Id, [new(created[0].Id, Patch(created[0]) with { Title = "Updated" })]);
            token.BoardIds = [];
            store.Tokens.Update(token);
            Assert.Empty(tools.ListBoards());
            Assert.Equal(404, Assert.Throws<ApiException>(() => tools.UpdateTickets(board.Id, [new(created[0].Id, Patch(created[0]))])).Status);
            Assert.Equal(2, service.List(user).Length);
            token.BoardIds = [board.Id];
            token.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            store.Tokens.Update(token);
            Assert.Empty(tools.ListBoards());
            token.ExpiresAt = DateTime.UtcNow.AddDays(1);
            token.UserId = "other";
            store.Tokens.Update(token);
            Assert.Empty(tools.ListBoards());
            store.Tokens.Delete(token.Id);
            Assert.Equal(404, Assert.Throws<ApiException>(() => tools.GetBoard(board.Id)).Status);
        });
    }

    [Fact]
    public async Task BoardIconsPersistAndRemainCompatibleWithExistingClients()
    {
        await using var factory = new AppFactory();
        using var client = factory.Browser();
        await Login(client);
        var legacy = await CreateBoard(client, "Existing client");
        Assert.Equal("columns", legacy.Icon);
        Assert.Equal("teal", legacy.Color);

        using var created = await client.PostAsJsonAsync("/api/boards", new { name = "Projects", description = "", icon = "rocket", color = "blue" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var board = (await created.Content.ReadFromJsonAsync<Board>())!;
        Assert.Equal("rocket", board.Icon);
        Assert.Equal("blue", board.Color);
        var columns = board.Columns.Select(column => new ColumnRequest(column.Id, column.Name, column.IsDone)).ToArray();
        var edit = new UpdateBoardRequest(board.Name, board.Description, [], columns, board.Revision, "house", "pink");
        using var updated = await client.PutAsJsonAsync($"/api/boards/{board.Id}", edit);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        board = (await updated.Content.ReadFromJsonAsync<Board>())!;
        Assert.Equal("house", board.Icon);
        Assert.Equal("pink", board.Color);

        using var invalid = await client.PutAsJsonAsync($"/api/boards/{board.Id}", edit with { Icon = "unknown", Revision = board.Revision });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var invalidColor = await client.PutAsJsonAsync($"/api/boards/{board.Id}", edit with { Color = "unknown", Revision = board.Revision });
        Assert.Equal(HttpStatusCode.BadRequest, invalidColor.StatusCode);
        using var stale = await client.PutAsJsonAsync($"/api/boards/{board.Id}", edit);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var oldClient = await client.PutAsJsonAsync($"/api/boards/{board.Id}", new { board.Name, board.Description, members = Array.Empty<string>(), columns, board.Revision });
        Assert.Equal(HttpStatusCode.OK, oldClient.StatusCode);
        var reloaded = (await client.GetFromJsonAsync<BoardResponse>($"/api/boards/{board.Id}"))!;
        Assert.Equal("house", reloaded.Board.Icon);
        Assert.Equal("pink", reloaded.Board.Color);
        Assert.Equal(board.Revision + 1, reloaded.Board.Revision);
        using var invalidCreate = await client.PostAsJsonAsync("/api/boards", new { name = "Invalid icon", description = "", icon = "unknown" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCreate.StatusCode);
        using var invalidColorCreate = await client.PostAsJsonAsync("/api/boards", new { name = "Invalid color", description = "", color = "unknown" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidColorCreate.StatusCode);
    }

    [Fact]
    public async Task LocalHttpDevelopmentSupportsLoginAndStillRequiresCsrf()
    {
        await using var factory = new AppFactory();
        await using var local = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("LocalHttp", "true");
        });
        using var client = local.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        using var csrfResponse = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);
        var csrfCookie = csrfResponse.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("soso-local-csrf=", StringComparison.Ordinal));
        Assert.DoesNotContain("; secure", csrfCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", csrfCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.test", password = AppFactory.Password })).StatusCode);

        var csrf = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.test", password = AppFactory.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var sessionCookie = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("soso-local=", StringComparison.Ordinal));
        Assert.DoesNotContain("; secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=strict", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    public async Task SecureCookiesRemainRequiredOutsideExplicitLocalHttp(string environment, bool localHttp)
    {
        await using var factory = new AppFactory();
        await using var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("LocalHttp", localHttp.ToString());
        });
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var csrf = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        var csrfCookie = csrf.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-soso-csrf=", StringComparison.Ordinal));
        Assert.Contains("; secure", csrfCookie, StringComparison.OrdinalIgnoreCase);
        await Login(client);

        var isProduction = environment == "Production";
        if (isProduction)
        {
            using var http = configured.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
            Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/api/auth/csrf")).StatusCode);
        }
    }

    [Fact]
    public async Task AnonymousAccessAndMissingCsrfAreRejected()
    {
        using var factory = new AppFactory();
        using var client = factory.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/boards")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/people")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/images/not-found")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/mcp", new { })).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.test", password = AppFactory.Password });
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        await Csrf(client);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/auth/register", new { })).StatusCode);
        await Login(client);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/boards", new { name = "Unsafe", description = "" })).StatusCode);
        var me = await client.GetStringAsync("/api/auth/me");
        Assert.DoesNotContain("passwordHash", me);
        Assert.DoesNotContain("securityStamp", me);
        Assert.Equal("nosniff", (await client.GetAsync("/api/auth/me")).Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("media-src 'self' blob:", (await client.GetAsync("/api/auth/me")).Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task BoardsTicketsImagesTagsAndArchiveRespectMembershipAndRevisions()
    {
        using var factory = new AppFactory();
        using var admin = factory.Browser();
        await Login(admin);
        var member = await CreateUser(admin, "member@example.test");
        await CreateUser(admin, "other@example.test");
        using var owner = factory.Browser();
        using var outsider = factory.Browser();
        await Login(owner, member.Email);
        await Login(outsider, "other@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/admin/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/admin/accounts", new { email = "new@example.test", name = "No", password = AppFactory.Password, isAdmin = true })).StatusCode);
        var board = await CreateBoard(owner, "Secret release");
        Assert.Empty((await outsider.GetFromJsonAsync<Board[]>("/api/boards"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/boards/{board.Id}")).StatusCode);
        var ticketResponse = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/tickets", new { title = "Ship Soso", columnId = board.Columns[0].Id });
        Assert.Equal(HttpStatusCode.Created, ticketResponse.StatusCode);
        var ticket = (await ticketResponse.Content.ReadFromJsonAsync<Ticket>())!;
        var path = $"/api/boards/{board.Id}/tickets/{ticket.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PutAsJsonAsync(path, Edit(ticket))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync(path, Edit(ticket, ["unknown"]))).StatusCode);
        var edit = await owner.PutAsJsonAsync(path, Edit(ticket, ["feature", "test"], true));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var updated = (await edit.Content.ReadFromJsonAsync<Ticket>())!;
        Assert.True(updated.Archived);
        Assert.Equal(2, updated.Tags.Count);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync(path, Edit(ticket))).StatusCode);
        var restore = await owner.PutAsJsonAsync(path, Edit(updated, ["feature"]));
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        var comment = await owner.PostAsJsonAsync(path + "/comments", new { text = "Ready for review" });
        Assert.Equal(HttpStatusCode.OK, comment.StatusCode);
        Assert.Single((await comment.Content.ReadFromJsonAsync<Ticket>())!.Comments);
        using var invalidForm = new MultipartFormDataContent();
        invalidForm.Add(new ByteArrayContent("<script>bad</script>"u8.ToArray()), "file", "bad.jpg");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync(path + "/images", invalidForm)).StatusCode);
        using var form = new MultipartFormDataContent();
        var imageSample = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Samples", "image_sample.png"));
        form.Add(new ByteArrayContent(imageSample), "file", "image_sample.png");
        var upload = await owner.PostAsync(path + "/images", form);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var withImage = (await upload.Content.ReadFromJsonAsync<Ticket>())!;
        var imageId = Assert.Single(withImage.Images);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/images/{imageId}")).StatusCode);
        var imageDownload = await owner.GetAsync($"/api/images/{imageId}");
        Assert.Equal("image/png", imageDownload.Content.Headers.ContentType!.MediaType);
        Assert.True((await imageDownload.Content.ReadAsByteArrayAsync()).Length > 0);
        using var videoForm = new MultipartFormDataContent();
        var videoSample = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Samples", "video_sample.mp4"));
        var mp4 = new ByteArrayContent(videoSample);
        mp4.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4");
        videoForm.Add(mp4, "file", "video_sample.mp4");
        using var thumbnail = new Image<Rgba32>(2, 2);
        using var thumbnailPng = new MemoryStream();
        await thumbnail.SaveAsPngAsync(thumbnailPng);
        videoForm.Add(new ByteArrayContent(thumbnailPng.ToArray()), "thumbnail", "thumbnail.png");
        var videoUpload = await owner.PostAsync(path + "/videos", videoForm);
        Assert.Equal(HttpStatusCode.OK, videoUpload.StatusCode);
        var withVideo = (await videoUpload.Content.ReadFromJsonAsync<Ticket>())!;
        var videoId = Assert.Single(withVideo.Videos);
        var videoDownload = await owner.GetAsync($"/api/videos/{videoId}");
        Assert.Equal("video/mp4", videoDownload.Content.Headers.ContentType!.MediaType);
        var processedVideo = await videoDownload.Content.ReadAsByteArrayAsync();
        Assert.True(processedVideo.Length > 0);
        Assert.True(processedVideo.Length < videoSample.Length, $"Expected converted video ({processedVideo.Length} bytes) to be smaller than uploaded video ({videoSample.Length} bytes).");
        Assert.True(processedVideo.Length >= 8 && processedVideo.AsSpan(4, 4).SequenceEqual("ftyp"u8));
        Assert.Equal("image/png", (await owner.GetAsync($"/api/videos/{videoId}/thumbnail")).Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/videos/{videoId}")).StatusCode);
        var shared = await owner.PutAsJsonAsync($"/api/boards/{board.Id}", new UpdateBoardRequest(board.Name, board.Description, [(await outsider.GetFromJsonAsync<AccountResponse>("/api/auth/me"))!.Id], board.Columns.Select(column => new ColumnRequest(column.Id, column.Name, column.IsDone)).ToArray(), board.Revision));
        Assert.Equal(HttpStatusCode.OK, shared.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await outsider.GetAsync($"/api/boards/{board.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.DeleteAsync($"/api/boards/{board.Id}")).StatusCode);
        Assert.True(board.Columns.Last().IsDone);
    }

    [Fact]
    public async Task AdministratorsCanAssignAndRevokeAccountBoardAccess()
    {
        using var factory = new AppFactory();
        using var admin = factory.Browser();
        await Login(admin);
        var member = await CreateUser(admin, "assigned@example.test");
        var board = await CreateBoard(admin, "Assigned board");
        using var user = factory.Browser();
        await Login(user, member.Email);

        Assert.Empty((await user.GetFromJsonAsync<Board[]>("/api/boards"))!);
        var assign = await admin.PutAsJsonAsync($"/api/admin/accounts/{member.Id}", new { disabled = false, password = (string?)null, boardIds = new[] { board.Id } });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);

        await Login(user, member.Email);
        Assert.Contains((await user.GetFromJsonAsync<Board[]>("/api/boards"))!, item => item.Id == board.Id);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"/api/boards/{board.Id}")).StatusCode);

        var revoke = await admin.PutAsJsonAsync($"/api/admin/accounts/{member.Id}", new { disabled = false, password = (string?)null, boardIds = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        await Login(user, member.Email);
        Assert.Empty((await user.GetFromJsonAsync<Board[]>("/api/boards"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/boards/{board.Id}")).StatusCode);

        var invalid = await admin.PutAsJsonAsync($"/api/admin/accounts/{member.Id}", new { disabled = false, password = (string?)null, boardIds = new[] { "unknown" } });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task RevokingTokensAndDisablingAccountsImmediatelyRemovesAccess()
    {
        using var factory = new AppFactory();
        using var admin = factory.Browser();
        var administrator = await Login(admin);
        var member = await CreateUser(admin, "member@example.test");
        using var user = factory.Browser();
        await Login(user, member.Email);
        var tokenResponse = await user.PostAsJsonAsync("/api/auth/tokens", new { name = "Integration" });
        tokenResponse.EnsureSuccessStatusCode();
        var token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        using var mcp = factory.Browser();
        mcp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        mcp.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        mcp.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        var initialize = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "security-tests", version = "1.0" } } });
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        Assert.Equal(McpTools.WorkflowInstructions, McpMessage(await initialize.Content.ReadAsStringAsync()).GetProperty("result").GetProperty("instructions").GetString());
        var tools = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } });
        Assert.Equal(HttpStatusCode.OK, tools.StatusCode);
        var body = await tools.Content.ReadAsStringAsync();
        Assert.Contains("list_boards", body);
        Assert.Contains("create_tickets", body);
        Assert.Contains("update_tickets", body);
        Assert.Contains("search_tickets", body);
        Assert.Contains("get_ticket_history", body);
        Assert.DoesNotContain("CreateAccount", body);
        var discoveredTools = McpMessage(body).GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        var createSchema = discoveredTools.Single(tool => tool.GetProperty("name").GetString() == "create_ticket").GetProperty("inputSchema");
        Assert.Contains(createSchema.GetProperty("required").EnumerateArray(), field => field.GetString() == "description");
        Assert.Contains(createSchema.GetProperty("required").EnumerateArray(), field => field.GetString() == "tags");
        var batchCreateSchema = discoveredTools.Single(tool => tool.GetProperty("name").GetString() == "create_tickets").GetProperty("inputSchema").GetProperty("properties").GetProperty("tickets").GetProperty("items");
        Assert.Contains(batchCreateSchema.GetProperty("required").EnumerateArray(), field => field.GetString() == "description");
        Assert.Contains(batchCreateSchema.GetProperty("required").EnumerateArray(), field => field.GetString() == "tags");
        Assert.Contains(createSchema.GetProperty("properties").EnumerateObject(), property => property.Name == "subtasks");
        Assert.Contains(batchCreateSchema.GetProperty("properties").EnumerateObject(), property => property.Name == "subtasks");
        var patchSchema = discoveredTools.Single(tool => tool.GetProperty("name").GetString() == "update_ticket").GetProperty("inputSchema").GetProperty("properties").GetProperty("update");
        Assert.Equal("revision", Assert.Single(patchSchema.GetProperty("required").EnumerateArray()).GetString());
        var privateBoard = await CreateBoard(admin, "Administrator private board");
        var board = await CreateBoard(user, "Member private board");
        var tokenId = AuthEndpoints.HashToken(token);
        using var initialTokens = await user.GetAsync("/api/auth/tokens");
        Assert.Empty((await initialTokens.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("boardIds").EnumerateArray());
        using var emptyCall = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 10, method = "tools/call", @params = new { name = "list_boards", arguments = new { } } });
        Assert.DoesNotContain("Member private board", await emptyCall.Content.ReadAsStringAsync());
        using var deniedRead = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 11, method = "tools/call", @params = new { name = "get_board", arguments = new { boardId = board.Id } } });
        Assert.Contains("\"isError\":true", await deniedRead.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/auth/tokens/{tokenId}/boards", new { boardIds = new[] { board.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.PutAsJsonAsync($"/api/auth/tokens/{tokenId}/boards", new { boardIds = new[] { privateBoard.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.PutAsJsonAsync($"/api/auth/tokens/{tokenId}/boards", new { boardIds = new[] { "missing" } })).StatusCode);
        using var assigned = await user.PutAsJsonAsync($"/api/auth/tokens/{tokenId}/boards", new { boardIds = new[] { board.Id, board.Id } });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        Assert.Single((await assigned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("boardIds").EnumerateArray());
        using var persistedAssignments = await user.GetAsync("/api/auth/tokens");
        var persistedToken = (await persistedAssignments.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single(item => item.GetProperty("id").GetString() == tokenId);
        Assert.Equal(board.Id, Assert.Single(persistedToken.GetProperty("boardIds").EnumerateArray()).GetString());
        var call = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "list_boards", arguments = new { } } });
        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        var callBody = await call.Content.ReadAsStringAsync();
        Assert.Contains("Member private board", callBody);
        Assert.DoesNotContain("Administrator private board", callBody);
        using var invalidInsert = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 14, method = "tools/call", @params = new { name = "create_ticket", arguments = new { boardId = board.Id, columnId = board.Columns[0].Id, title = "Missing specification" } } });
        Assert.Contains("\"isError\":true", await invalidInsert.Content.ReadAsStringAsync());
        using var insert = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 4, method = "tools/call", @params = new { name = "create_tickets", arguments = new { boardId = board.Id, tickets = new[] { new McpCreateTicketRequest("MCP batch first", board.Columns[0].Id, "Requested work", ["test"], [new("Stage one"), new("Stage two")]), new McpCreateTicketRequest("MCP batch second", board.Columns[0].Id, "Requested work", ["test"]) } } } });
        Assert.Equal(HttpStatusCode.OK, insert.StatusCode);
        Assert.DoesNotContain("\"isError\":true", await insert.Content.ReadAsStringAsync());
        var inserted = (await user.GetFromJsonAsync<BoardResponse>($"/api/boards/{board.Id}"))!.Tickets;
        Assert.Equal(2, inserted.Length);
        Assert.Equal(new[] { "Stage one", "Stage two" }, inserted.Single(ticket => ticket.Title == "MCP batch first").Subtasks.Select(task => task.Title));
        var updates = inserted.Select(ticket => new { ticketId = ticket.Id, update = new { revision = ticket.Revision, description = "Protocol search needle" } }).ToArray();
        using var update = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 5, method = "tools/call", @params = new { name = "update_tickets", arguments = new { boardId = board.Id, tickets = updates } } });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.DoesNotContain("\"isError\":true", await update.Content.ReadAsStringAsync());
        var updated = (await user.GetFromJsonAsync<BoardResponse>($"/api/boards/{board.Id}"))!.Tickets;
        Assert.All(updated, ticket => Assert.Equal("Protocol search needle", ticket.Description));
        Assert.All(updated, ticket => Assert.Equal(new[] { "test" }, ticket.Tags));
        Assert.Equal(inserted.Select(ticket => ticket.Title), updated.Select(ticket => ticket.Title));
        Assert.Equal(inserted.Select(ticket => ticket.Position), updated.Select(ticket => ticket.Position));
        using var missingRevision = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 15, method = "tools/call", @params = new { name = "update_ticket", arguments = new { boardId = board.Id, ticketId = updated[0].Id, update = new { priority = "high" } } } });
        Assert.Contains("\"isError\":true", await missingRevision.Content.ReadAsStringAsync());
        using var priorityPatch = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 16, method = "tools/call", @params = new { name = "update_ticket", arguments = new { boardId = board.Id, ticketId = updated[0].Id, update = new { revision = updated[0].Revision, priority = "high" } } } });
        Assert.DoesNotContain("\"isError\":true", await priorityPatch.Content.ReadAsStringAsync());
        var priorityUpdated = (await user.GetFromJsonAsync<BoardResponse>($"/api/boards/{board.Id}"))!.Tickets.Single(ticket => ticket.Id == updated[0].Id);
        Assert.Equal("high", priorityUpdated.Priority);
        Assert.Equal(updated[0].Description, priorityUpdated.Description);
        Assert.Equal(updated[0].Tags, priorityUpdated.Tags);
        Assert.Equal(updated[0].Revision + 1, priorityUpdated.Revision);
        using var boardRead = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 19, method = "tools/call", @params = new { name = "get_board", arguments = new { boardId = board.Id } } });
        var readResult = McpMessage(await boardRead.Content.ReadAsStringAsync()).GetProperty("result");
        var readBoard = JsonSerializer.Deserialize<BoardResponse>(readResult.GetProperty("content")[0].GetProperty("text").GetString()!, JsonOptions)!;
        var readTicket = readBoard.Tickets.Single(ticket => ticket.Id == priorityUpdated.Id);
        using var missingTicket = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 20, method = "tools/call", @params = new { name = "update_ticket", arguments = new { boardId = readBoard.Board.Id, ticketId = readTicket.Id[..8], update = new { revision = readTicket.Revision, priority = "low" } } } });
        var missingTicketResult = McpMessage(await missingTicket.Content.ReadAsStringAsync()).GetProperty("result");
        Assert.True(missingTicketResult.GetProperty("isError").GetBoolean());
        var missingTicketText = missingTicketResult.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Contains("Ticket not found", missingTicketText);
        Assert.Contains("exact board.id and ticket.id", missingTicketText);
        using var staleTicket = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 21, method = "tools/call", @params = new { name = "move_ticket", arguments = new { boardId = readBoard.Board.Id, ticketId = readTicket.Id, columnId = readBoard.Board.Columns.Single(column => column.IsDone).Id, revision = readTicket.Revision - 1 } } });
        var staleResult = McpMessage(await staleTicket.Content.ReadAsStringAsync()).GetProperty("result");
        Assert.True(staleResult.GetProperty("isError").GetBoolean());
        Assert.Contains("current ticket revision", staleResult.GetProperty("content")[0].GetProperty("text").GetString());
        using var move = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 22, method = "tools/call", @params = new { name = "move_ticket", arguments = new { boardId = readBoard.Board.Id, ticketId = readTicket.Id, columnId = readBoard.Board.Columns.Single(column => column.IsDone).Id, revision = readTicket.Revision } } });
        Assert.DoesNotContain("\"isError\":true", await move.Content.ReadAsStringAsync());
        using var comment = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 23, method = "tools/call", @params = new { name = "add_comment", arguments = new { boardId = readBoard.Board.Id, ticketId = readTicket.Id, text = "Verified via MCP" } } });
        Assert.DoesNotContain("\"isError\":true", await comment.Content.ReadAsStringAsync());
        var savedTicket = (await user.GetFromJsonAsync<BoardResponse>($"/api/boards/{board.Id}"))!.Tickets.Single(ticket => ticket.Id == readTicket.Id);
        Assert.Equal(readBoard.Board.Columns.Single(column => column.IsDone).Id, savedTicket.ColumnId);
        Assert.Equal(readTicket.Revision + 2, savedTicket.Revision);
        Assert.Equal("Verified via MCP", Assert.Single(savedTicket.Comments).Text);
        using var history = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 17, method = "tools/call", @params = new { name = "get_ticket_history", arguments = new { boardId = board.Id, ticketId = priorityUpdated.Id } } });
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var historyBody = await history.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"isError\":true", historyBody);
        Assert.Contains("created", historyBody);
        Assert.Contains("field_changed", historyBody);
        using var search = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 6, method = "tools/call", @params = new { name = "search_tickets", arguments = new { query = "NEEDLE", limit = 1 } } });
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        var searchBody = await search.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"isError\":true", searchBody);
        Assert.Contains("MCP batch first", searchBody);
        Assert.DoesNotContain("MCP batch second", searchBody);
        using var unassigned = await user.PutAsJsonAsync($"/api/auth/tokens/{tokenId}/boards", new { boardIds = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, unassigned.StatusCode);
        using var removedRead = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 12, method = "tools/call", @params = new { name = "get_board", arguments = new { boardId = board.Id } } });
        Assert.Contains("\"isError\":true", await removedRead.Content.ReadAsStringAsync());
        using var removedSearch = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 13, method = "tools/call", @params = new { name = "search_tickets", arguments = new { query = "NEEDLE" } } });
        Assert.DoesNotContain("MCP batch first", await removedSearch.Content.ReadAsStringAsync());
        using var removedHistory = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 18, method = "tools/call", @params = new { name = "get_ticket_history", arguments = new { boardId = board.Id, ticketId = priorityUpdated.Id } } });
        Assert.Contains("\"isError\":true", await removedHistory.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.PostAsJsonAsync("/mcp", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await mcp.GetAsync("/api/boards")).StatusCode);
        await user.DeleteAsync("/api/auth/tokens/" + AuthEndpoints.HashToken(token));
        Assert.Equal(HttpStatusCode.Unauthorized, (await mcp.PostAsJsonAsync("/mcp", new { })).StatusCode);
        var second = await user.PostAsJsonAsync("/api/auth/tokens", new { name = "Second" });
        var secondToken = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        mcp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secondToken);
        var disable = await admin.PutAsJsonAsync($"/api/admin/accounts/{member.Id}", new { disabled = true, password = (string?)null });
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await mcp.PostAsJsonAsync("/mcp", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/accounts/{administrator.Id}", new { disabled = true, password = (string?)null })).StatusCode);
    }

    [Fact]
    public async Task PasswordRotationInvalidatesOtherSessionsAndLoginLocksAfterFailures()
    {
        using var factory = new AppFactory();
        using var first = factory.Browser();
        using var second = factory.Browser();
        await Login(first);
        await Login(second);
        var rotate = await first.PutAsJsonAsync("/api/auth/password", new { currentPassword = AppFactory.Password, newPassword = "Rotated-test-only-password!" });
        Assert.Equal(HttpStatusCode.NoContent, rotate.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/me")).StatusCode);
        await Csrf(first);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await first.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.test", password = "wrong" })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.test", password = "Rotated-test-only-password!" })).StatusCode);
    }
}
