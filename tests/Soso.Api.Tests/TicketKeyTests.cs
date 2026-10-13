using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Soso.Api;
using Xunit;

namespace Soso.Api.Tests;

[Collection("LiteDB tests")]
public sealed class TicketKeyTests
{
    private static readonly ClaimsPrincipal Owner = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));

    private static void WithService(Action<BoardService, Store> test)
    {
        var path = Path.Combine(Path.GetTempPath(), "soso-key-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = path }).Build();
            using var store = new Store(configuration);
            test(new BoardService(store), store);
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }

    private static UpdateBoardRequest UpdateFor(Board board, string? code = null) =>
        new(board.Name, board.Description, [], board.Columns.Select(column => new ColumnRequest(column.Id, column.Name, column.IsDone)).ToArray(), board.Revision, Code: code);

    [Theory]
    [InlineData("Sosô", "SOS")]
    [InlineData("Site Homologo", "SIT")]
    [InlineData("OtxIdle", "OTX")]
    public void NormalizeCodeDerivesExpectedPrefixes(string name, string expected)
    {
        Assert.Equal(expected, BoardService.NormalizeCode(null, name));
    }

    [Theory]
    [InlineData("sos", "SOS")]
    [InlineData("otx", "OTX")]
    [InlineData("  sih  ", "SIH")]
    public void NormalizeCodeUppercasesUserCodes(string input, string expected)
    {
        Assert.Equal(expected, BoardService.NormalizeCode(input, "Ignored"));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("A1")]
    [InlineData("TOOLONGCODES")]
    public void NormalizeCodeRejectsInvalidCodes(string input)
    {
        Assert.Throws<ApiException>(() => BoardService.NormalizeCode(input, "Board"));
    }

    [Theory]
    [InlineData("SOS", 1, "SOS-00001")]
    [InlineData("SOS", 42, "SOS-00042")]
    [InlineData("SIH", 100000, "SIH-100000")]
    public void TicketKeyPadsToFiveDigitsAndExpands(string code, int number, string expected)
    {
        Assert.Equal(expected, BoardService.TicketKey(code, number));
    }

    [Fact]
    public void BoardsGetUniqueCodes()
    {
        WithService((service, _) =>
        {
            var first = service.Create(new("Sosô", ""), Owner);
            var second = service.Create(new("Sosô", ""), Owner);
            Assert.Equal("SOS", first.Code);
            Assert.NotEqual(first.Code, second.Code);
            Assert.StartsWith("SOS", second.Code, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void DuplicateCustomCodesAreRejected()
    {
        WithService((service, _) =>
        {
            service.Create(new("First", "", Code: "SOS"), Owner);
            var duplicate = Assert.Throws<ApiException>(() => service.Create(new("Second", "", Code: "sos"), Owner));
            Assert.Equal(400, duplicate.Status);
        });
    }

    [Fact]
    public void BoardUpdateChangesCode()
    {
        WithService((service, _) =>
        {
            var board = service.Create(new("Original", "", Code: "AAA"), Owner);
            var updated = service.Update(board.Id, UpdateFor(board, "BBB"), Owner);
            Assert.Equal("BBB", updated.Code);
            var duplicate = service.Create(new("Other", "", Code: "CCC"), Owner);
            var conflict = Assert.Throws<ApiException>(() => service.Update(duplicate.Id, UpdateFor(duplicate, "bbb"), Owner));
            Assert.Equal(400, conflict.Status);
        });
    }

    [Fact]
    public void TicketsGetSequentialNumbersThatSurviveDeletion()
    {
        WithService((service, _) =>
        {
            var board = service.Create(new("Numbers", "", Code: "NUM"), Owner);
            var column = board.Columns[0].Id;
            var first = service.CreateTicket(board.Id, new("One", column), Owner);
            var second = service.CreateTicket(board.Id, new("Two", column), Owner);
            Assert.Equal(1, first.Number);
            Assert.Equal(2, second.Number);
            service.DeleteTicket(board.Id, second.Id, Owner);
            var third = service.CreateTicket(board.Id, new("Three", column), Owner);
            Assert.Equal(3, third.Number);
            Assert.Equal("NUM-00001", BoardService.TicketKey(board, first));
            Assert.Equal("NUM-00003", BoardService.TicketKey(service.RequireBoard(board.Id, Owner), third));
        });
    }

    [Fact]
    public void EnsureTicketKeysBackfillsLegacyData()
    {
        WithService((service, store) =>
        {
            var board = service.Create(new("Legacy", "", Code: "LEG"), Owner);
            var column = board.Columns[0].Id;
            var ticket = service.CreateTicket(board.Id, new("Old", column), Owner);
            board.Code = "";
            board.NextTicketNumber = 0;
            ticket.Number = 0;
            store.Boards.Update(board);
            store.Tickets.Update(ticket);

            service.EnsureTicketKeys();

            var fixedBoard = store.Boards.FindById(board.Id)!;
            var fixedTicket = store.Tickets.FindById(ticket.Id)!;
            Assert.Equal("LEG", fixedBoard.Code);
            Assert.Equal(1, fixedTicket.Number);
            Assert.Equal(2, fixedBoard.NextTicketNumber);
        });
    }

    [Fact]
    public void SearchFindsTicketsByHumanKey()
    {
        WithService((service, _) =>
        {
            var board = service.Create(new("Searchable", "", Code: "SCH"), Owner);
            var ticket = service.CreateTicket(board.Id, new("Find me", board.Columns[0].Id), Owner);
            var key = BoardService.TicketKey(board, ticket);
            var result = service.SearchTickets(key.ToLowerInvariant(), Owner, board.Id);
            Assert.Contains(result.Tickets, item => item.Id == ticket.Id);
        });
    }

    [Fact]
    public async Task BoardEndpointsReturnCodesAndTicketNumbers()
    {
        await using var factory = new AppFactory();
        using var client = await LoginAdmin(factory);
        var created = await client.PostAsJsonAsync("/api/boards", new { name = "Key Board", description = "", code = "KEY" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var board = (await created.Content.ReadFromJsonAsync<Board>())!;
        Assert.Equal("KEY", board.Code);
        Assert.Equal(1, board.NextTicketNumber);

        var ticketResponse = await client.PostAsJsonAsync($"/api/boards/{board.Id}/tickets", new { title = "First", columnId = board.Columns[0].Id });
        Assert.Equal(HttpStatusCode.Created, ticketResponse.StatusCode);
        var ticket = (await ticketResponse.Content.ReadFromJsonAsync<Ticket>())!;
        Assert.Equal(1, ticket.Number);

        var fetched = (await client.GetFromJsonAsync<BoardResponse>($"/api/boards/{board.Id}"))!;
        Assert.Equal("KEY", fetched.Board.Code);
        Assert.Equal(2, fetched.Board.NextTicketNumber);
        Assert.Equal(1, fetched.Tickets[0].Number);
    }

    private static async Task<HttpClient> LoginAdmin(AppFactory factory)
    {
        var client = factory.Browser();
        await SetCsrf(client);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.test", password = AppFactory.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await SetCsrf(client);
        return client;
    }

    private static async Task SetCsrf(HttpClient client)
    {
        var result = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", result.GetProperty("token").GetString());
    }
}
