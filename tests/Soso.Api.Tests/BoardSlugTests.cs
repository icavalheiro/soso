using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Soso.Api;
using Xunit;

namespace Soso.Api.Tests;

[Collection("LiteDB tests")]
public sealed class BoardSlugTests
{
    private static readonly ClaimsPrincipal Owner = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));

    private static void WithService(Action<BoardService, Store> test)
    {
        var path = Path.Combine(Path.GetTempPath(), "soso-slug-tests-" + Guid.NewGuid().ToString("N"));
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

    [Theory]
    [InlineData("OtxIdle", "otxidle")]
    [InlineData("Sosô Projeto — é 2026!", "soso-projeto-e-2026")]
    [InlineData("  A   B  ", "a-b")]
    [InlineData("???", "board")]
    public void SlugifyProducesUrlSafeLowercaseSlugs(string name, string expected)
    {
        Assert.Equal(expected, BoardService.Slugify(name));
    }

    [Fact]
    public void SlugifyLimitsLengthAndNeverEndsWithASeparator()
    {
        var slug = BoardService.Slugify(new string('A', 200) + " tail");
        Assert.True(slug.Length <= 60);
        Assert.False(slug.EndsWith('-'));
        Assert.StartsWith("aaa", slug);
    }

    [Fact]
    public void BoardsGetUniqueSlugsDerivedFromTheirName()
    {
        WithService((service, _) =>
        {
            var first = service.Create(new("OtxIdle", ""), Owner);
            var second = service.Create(new("OtxIdle", ""), Owner);
            Assert.Equal("otxidle", first.Slug);
            Assert.Equal("otxidle-2", second.Slug);
        });
    }

    [Fact]
    public void RenamingABoardKeepsItsSlugStable()
    {
        WithService((service, _) =>
        {
            var board = service.Create(new("Original Name", ""), Owner);
            Assert.Equal("original-name", board.Slug);
            var request = new UpdateBoardRequest("A Different Name", board.Description, [], board.Columns.Select(column => new ColumnRequest(column.Id, column.Name, column.IsDone)).ToArray(), board.Revision);
            var updated = service.Update(board.Id, request, Owner);
            Assert.Equal("original-name", updated.Slug);
            Assert.Equal("A Different Name", updated.Name);
        });
    }

    [Fact]
    public void EnsureSlugsBackfillsLegacyBoardsWithUniqueSlugs()
    {
        WithService((service, store) =>
        {
            var first = service.Create(new("Legacy Board", ""), Owner);
            var second = service.Create(new("Legacy Board", ""), Owner);
            first.Slug = "";
            second.Slug = "";
            store.Boards.Update(first);
            store.Boards.Update(second);

            service.EnsureSlugs();

            var slugs = store.Boards.FindAll().Select(board => board.Slug).ToArray();
            Assert.Equal(2, slugs.Length);
            Assert.Equal(slugs.Length, slugs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Contains("legacy-board", slugs);
            Assert.Contains("legacy-board-2", slugs);

            service.EnsureSlugs();
            Assert.Equal(slugs, store.Boards.FindAll().Select(board => board.Slug).ToArray());
        });
    }

    [Fact]
    public async Task BoardEndpointsReturnSlugsToClients()
    {
        await using var factory = new AppFactory();
        using var client = await LoginAdmin(factory);
        var created = await client.PostAsJsonAsync("/api/boards", new { name = "Deep Link Board", description = "" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var board = (await created.Content.ReadFromJsonAsync<Board>())!;
        Assert.Equal("deep-link-board", board.Slug);

        var list = (await client.GetFromJsonAsync<Board[]>("/api/boards"))!;
        Assert.Contains(list, item => item.Id == board.Id && item.Slug == "deep-link-board");

        var fetched = (await client.GetFromJsonAsync<BoardResponse>($"/api/boards/{board.Id}"))!;
        Assert.Equal("deep-link-board", fetched.Board.Slug);
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