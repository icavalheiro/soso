using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Soso.Api;
using Xunit;

namespace Soso.Api.Tests;

[Collection("LiteDB tests")]
public sealed class McpTicketFieldsTests
{
    private static void WithMcp(Action<BoardService, McpTools, ClaimsPrincipal, HttpContextAccessor, Store> test)
    {
        var path = Path.Combine(Path.GetTempPath(), "soso-fields-tests-" + Guid.NewGuid().ToString("N"));
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

    private static Ticket SeedRichTicket(BoardService service, McpTools tools, ClaimsPrincipal user, Board board)
    {
        var created = tools.CreateTickets(board.Id, [new McpCreateTicketRequest("Needle fields", board.Columns[0].Id, "A long description for projection", ["test"])])[0];
        var patched = tools.UpdateTicket(board.Id, created.Id, new PatchTicketRequest(created.Revision, Subtasks: [new SubtaskRequest("task", "Needle subtask", false)]));
        return tools.AddComment(board.Id, patched.Id, "Needle comment");
    }

    [Fact]
    public void SearchTicketsTitleOnlyProjectsTicketsAndKeepsIdentity()
    {
        WithMcp((service, tools, user, _, _) =>
        {
            var board = service.Create(new("Fields", ""), user);
            var full = SeedRichTicket(service, tools, user, board);

            var result = tools.SearchTickets("needle", fields: ["title"]);

            var projected = Assert.Single(result.Tickets);
            Assert.Equal(full.Id, projected.Id);
            Assert.Equal(board.Id, projected.BoardId);
            Assert.Equal(full.Revision, projected.Revision);
            Assert.Equal(full.Title, projected.Title);
            Assert.Equal("", projected.Description);
            Assert.Equal("", projected.ColumnId);
            Assert.Empty(projected.Tags);
            Assert.Empty(projected.Subtasks);
            Assert.Empty(projected.Comments);
            Assert.Empty(projected.Activity);
            Assert.Empty(projected.Images);
            Assert.Empty(projected.Videos);
            Assert.Equal(1, result.Total);
        });
    }

    [Fact]
    public void GetBoardProjectsMultipleFieldsAndPreservesPaginationMetadata()
    {
        WithMcp((service, tools, user, _, _) =>
        {
            var board = service.Create(new("Fields", ""), user);
            var full = SeedRichTicket(service, tools, user, board);

            var response = tools.GetBoard(board.Id, fields: ["title", "description", "tags"]);

            var projected = Assert.Single(response.Tickets);
            Assert.Equal(full.Id, projected.Id);
            Assert.Equal(board.Id, projected.BoardId);
            Assert.Equal(full.Revision, projected.Revision);
            Assert.Equal(full.Title, projected.Title);
            Assert.Equal(full.Description, projected.Description);
            Assert.Equal(full.Tags, projected.Tags);
            Assert.Empty(projected.Subtasks);
            Assert.Empty(projected.Comments);
            Assert.Empty(projected.Activity);
            Assert.Equal(board.Id, response.Board.Id);
            var unprojected = tools.GetBoard(board.Id);
            Assert.Equal(unprojected.Board.Id, response.Board.Id);
            Assert.Equal(unprojected.Members.Length, response.Members.Length);
        });
    }

    [Fact]
    public void FieldSelectionIsCaseInsensitiveAndIgnoresDuplicatesAndWhitespace()
    {
        WithMcp((service, tools, user, _, _) =>
        {
            var board = service.Create(new("Fields", ""), user);
            SeedRichTicket(service, tools, user, board);

            var result = tools.SearchTickets("needle", fields: ["  TITLE ", "title", "Description"]);
            var projected = Assert.Single(result.Tickets);
            Assert.NotEqual("", projected.Title);
            Assert.NotEqual("", projected.Description);
            Assert.Empty(projected.Comments);
        });
    }

    [Fact]
    public void OmittedFieldsReturnsFullTickets()
    {
        WithMcp((service, tools, user, _, _) =>
        {
            var board = service.Create(new("Fields", ""), user);
            var full = SeedRichTicket(service, tools, user, board);

            var viaSearch = Assert.Single(tools.SearchTickets("needle").Tickets);
            Assert.Equal(full.Description, viaSearch.Description);
            Assert.NotEmpty(viaSearch.Comments);
            Assert.NotEmpty(viaSearch.Activity);
            Assert.NotEmpty(viaSearch.Subtasks);

            var viaBoard = Assert.Single(tools.GetBoard(board.Id).Tickets);
            Assert.Equal(full.Description, viaBoard.Description);
            Assert.NotEmpty(viaBoard.Comments);
        });
    }

    [Fact]
    public void SearchKeepsTotalAndPaginationBeforeProjection()
    {
        WithMcp((service, tools, user, _, _) =>
        {
            var board = service.Create(new("Fields", ""), user);
            tools.CreateTickets(board.Id, Enumerable.Range(0, 3).Select(index => new McpCreateTicketRequest("Needle " + index, board.Columns[0].Id, "Requested work", ["test"])).ToArray());

            var page = tools.SearchTickets("needle", offset: 1, limit: 1, fields: ["title"]);
            Assert.Equal(3, page.Total);
            Assert.Equal(1, page.Offset);
            Assert.Equal(1, page.Limit);
            var single = Assert.Single(page.Tickets);
            Assert.NotEqual("", single.Title);
            Assert.Equal("", single.Description);
            Assert.NotEqual("", single.Id);
        });
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("title,description")]
    [InlineData(" ")]
    public void UnknownFieldsAreRejectedWith400(string field)
    {
        WithMcp((service, tools, user, _, _) =>
        {
            var board = service.Create(new("Fields", ""), user);
            SeedRichTicket(service, tools, user, board);

            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.SearchTickets("needle", fields: [field])).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.GetBoard(board.Id, fields: [field])).Status);
        });
    }

    [Fact]
    public void EmptyFieldsArrayIsRejectedWith400()
    {
        WithMcp((service, tools, user, _, _) =>
        {
            var board = service.Create(new("Fields", ""), user);
            SeedRichTicket(service, tools, user, board);

            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.SearchTickets("needle", fields: [])).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.GetBoard(board.Id, fields: [])).Status);
        });
    }
}
