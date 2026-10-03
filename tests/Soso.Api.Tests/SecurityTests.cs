using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
        using var pixel = new Image<Rgba32>(2, 2);
        using var png = new MemoryStream();
        await pixel.SaveAsPngAsync(png);
        form.Add(new ByteArrayContent(png.ToArray()), "file", "pixel.png");
        var upload = await owner.PostAsync(path + "/images", form);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var withImage = (await upload.Content.ReadFromJsonAsync<Ticket>())!;
        var imageId = Assert.Single(withImage.Images);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/images/{imageId}")).StatusCode);
        Assert.Equal("image/png", (await owner.GetAsync($"/api/images/{imageId}")).Content.Headers.ContentType!.MediaType);
        var shared = await owner.PutAsJsonAsync($"/api/boards/{board.Id}", new UpdateBoardRequest(board.Name, board.Description, [(await outsider.GetFromJsonAsync<AccountResponse>("/api/auth/me"))!.Id], board.Columns.Select(column => new ColumnRequest(column.Id, column.Name, column.IsDone)).ToArray(), board.Revision));
        Assert.Equal(HttpStatusCode.OK, shared.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await outsider.GetAsync($"/api/boards/{board.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.DeleteAsync($"/api/boards/{board.Id}")).StatusCode);
        Assert.True(board.Columns.Last().IsDone);
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
        var tools = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } });
        Assert.Equal(HttpStatusCode.OK, tools.StatusCode);
        var body = await tools.Content.ReadAsStringAsync();
        Assert.Contains("list_boards", body);
        Assert.DoesNotContain("CreateAccount", body);
        await CreateBoard(admin, "Administrator private board");
        await CreateBoard(user, "Member private board");
        var call = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "list_boards", arguments = new { } } });
        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        var callBody = await call.Content.ReadAsStringAsync();
        Assert.Contains("Member private board", callBody);
        Assert.DoesNotContain("Administrator private board", callBody);
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