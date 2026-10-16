using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Soso.Api;
using Xunit;

namespace Soso.Api.Tests;

[Collection("LiteDB tests")]
public sealed class ColumnArchiveTests
{
    private static void WithService(Action<BoardService, McpTools, ClaimsPrincipal, Store> test)
    {
        var path = Path.Combine(Path.GetTempPath(), "soso-column-archive-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = path }).Build();
            using var store = new Store(configuration);
            var service = new BoardService(store);
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));
            var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } };
            test(service, new McpTools(service, accessor), user, store);
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
    public void ArchiveColumnArchivesOnlyActiveTicketsOfThatColumnAtomically()
    {
        WithService((service, _, user, store) =>
        {
            var board = service.Create(new("Column archive", ""), user);
            var target = board.Columns[0].Id;
            var other = board.Columns[1].Id;
            var first = service.CreateTicket(board.Id, new("First", target, "Work", ["feature"]), user);
            var second = service.CreateTicket(board.Id, new("Second", target, "Work", ["feature"]), user);
            var elsewhere = service.CreateTicket(board.Id, new("Elsewhere", other, "Work", ["feature"]), user);
            var alreadyArchived = service.CreateTicket(board.Id, new("Archived", target, "Work", ["feature"]), user);
            service.UpdateTicket(board.Id, alreadyArchived.Id, new("Archived", "Work", target, "normal", null, null, alreadyArchived.Position, [], ["feature"], true, alreadyArchived.Revision), user);

            var archived = service.ArchiveColumnTickets(board.Id, target, user);

            Assert.Equal(new[] { first.Id, second.Id }.OrderBy(id => id, StringComparer.Ordinal), archived.Select(ticket => ticket.Id).OrderBy(id => id, StringComparer.Ordinal));
            Assert.All(archived, ticket => Assert.True(ticket.Archived));
            Assert.Equal(first.Revision + 1, archived.Single(ticket => ticket.Id == first.Id).Revision);
            Assert.Equal(target, store.Tickets.FindById(first.Id)!.ColumnId);
            Assert.False(store.Tickets.FindById(elsewhere.Id)!.Archived);
            Assert.Equal(3, store.Tickets.Find(ticket => ticket.BoardId == board.Id && ticket.Archived).Count());
            var history = service.GetTicketHistory(board.Id, first.Id, user);
            Assert.Contains(history, entry => entry.Action == "field_changed" && entry.Field == "archived" && entry.NewValue == "True");
        });
    }

    [Fact]
    public void ArchiveColumnIsIdempotentForEmptyOrFullyArchivedColumns()
    {
        WithService((service, _, user, _) =>
        {
            var board = service.Create(new("Idempotent", ""), user);
            var column = board.Columns[0].Id;
            Assert.Empty(service.ArchiveColumnTickets(board.Id, column, user));
            service.CreateTicket(board.Id, new("Only", column, "Work", ["feature"]), user);
            Assert.Single(service.ArchiveColumnTickets(board.Id, column, user));
            Assert.Empty(service.ArchiveColumnTickets(board.Id, column, user));
        });
    }

    [Fact]
    public void ArchiveColumnRejectsUnknownColumnAndNonMembers()
    {
        WithService((service, _, user, _) =>
        {
            var board = service.Create(new("Denied", ""), user);
            service.CreateTicket(board.Id, new("Stay", board.Columns[0].Id, "Work", ["feature"]), user);
            Assert.Throws<ApiException>(() => service.ArchiveColumnTickets(board.Id, "missing-column", user));
            var stranger = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "stranger")], "test"));
            Assert.Throws<ApiException>(() => service.ArchiveColumnTickets(board.Id, board.Columns[0].Id, stranger));
            Assert.All(service.Get(board.Id, user).Tickets, ticket => Assert.False(ticket.Archived));
        });
    }

    [Fact]
    public void McpArchiveColumnTicketsUsesTheSameServicePath()
    {
        WithService((service, tools, user, _) =>
        {
            var board = service.Create(new("MCP archive", ""), user);
            var column = board.Columns[0].Id;
            service.CreateTicket(board.Id, new("Mcp ticket", column, "Work", ["feature"]), user);

            var archived = tools.ArchiveColumnTickets(board.Id, column);

            Assert.Single(archived);
            Assert.True(archived[0].Archived);
            Assert.Empty(tools.ArchiveColumnTickets(board.Id, column));
        });
    }

    [Fact]
    public async Task ArchiveColumnEndpointReturnsArchivedTicketsAndRejectsUnknownColumns()
    {
        await using var factory = new AppFactory();
        using var client = factory.Browser();
        await LoginAdmin(client);
        var board = (await (await client.PostAsJsonAsync("/api/boards", new { name = "Endpoint archive", description = "" })).Content.ReadFromJsonAsync<Board>())!;
        var column = board.Columns[0].Id;
        var created = await client.PostAsJsonAsync($"/api/boards/{board.Id}/tickets", new CreateTicketRequest("Endpoint ticket", column, "Work", ["feature"]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var response = await client.PostAsync($"/api/boards/{board.Id}/columns/{column}/archive", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var archived = (await response.Content.ReadFromJsonAsync<Ticket[]>())!;
        Assert.Single(archived);
        Assert.True(archived[0].Archived);

        var again = await client.PostAsync($"/api/boards/{board.Id}/columns/{column}/archive", null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Empty((await again.Content.ReadFromJsonAsync<Ticket[]>())!);

        var unknown = await client.PostAsync($"/api/boards/{board.Id}/columns/missing/archive", null);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    private static async Task LoginAdmin(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.test", password = AppFactory.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var refreshed = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", refreshed.GetProperty("token").GetString());
    }
}
