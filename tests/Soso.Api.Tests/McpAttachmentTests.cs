using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ImageMagick;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using Soso.Api;
using Xunit;

namespace Soso.Api.Tests;

[Collection("LiteDB tests")]
public sealed class McpAttachmentTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static void WithMcp(Action<BoardService, McpTools, ClaimsPrincipal, HttpContextAccessor, Store> test)
    {
        var path = Path.Combine(Path.GetTempPath(), "soso-attach-tests-" + Guid.NewGuid().ToString("N"));
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

    private static Ticket SeedTicket(McpTools tools, BoardService service, ClaimsPrincipal user)
    {
        var board = service.Create(new("Attachments", ""), user);
        return tools.CreateTicket(board.Id, board.Columns[0].Id, "Attachment ticket", "Specification", ["test"]);
    }

    private static byte[] SamplePng(IMagickColor<byte> color, uint width = 4, uint height = 4)
    {
        using var image = new MagickImage(color, width, height);
        return image.ToByteArray(MagickFormat.Png);
    }

    private static JsonElement McpMessage(string body)
    {
        var json = body.TrimStart().StartsWith('{') ? body : body.Split('\n').Single(line => line.StartsWith("data:", StringComparison.Ordinal))[5..];
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    [Fact]
    public async Task ImageRoundtrip_ListGetRemove()
    {
        string imageId = "";
        byte[] stored = [];
        WithMcp((service, tools, user, _, _) =>
        {
            var ticket = SeedTicket(tools, service, user);
            var uploaded = tools.UploadTicketImage(ticket.BoardId, ticket.Id, Convert.ToBase64String(SamplePng(MagickColors.Red))).GetAwaiter().GetResult();
            imageId = Assert.Single(uploaded.Images);
            stored = service.TicketImage(ticket.BoardId, ticket.Id, imageId, user).Content;

            var listed = tools.ListTicketAttachments(ticket.BoardId, ticket.Id);
            var info = Assert.Single(listed);
            Assert.Equal(imageId, info.Id);
            Assert.Equal("image", info.Kind);
            Assert.Equal("image/png", info.ContentType);
            Assert.True(info.SizeBytes > 0);
            Assert.Null(info.ThumbnailSizeBytes);

            var result = tools.GetTicketImage(ticket.BoardId, ticket.Id, imageId);
            Assert.True(result.IsError != true);
            Assert.Equal(2, result.Content.Count);
            var text = Assert.IsType<TextContentBlock>(result.Content[0]);
            Assert.Contains(imageId, text.Text);
            var image = Assert.IsType<ImageContentBlock>(result.Content[1]);
            Assert.Equal("image/png", image.MimeType);
            Assert.Equal(stored, image.DecodedData.ToArray());

            var removed = tools.RemoveTicketAttachment(ticket.BoardId, ticket.Id, imageId);
            Assert.Empty(removed.Images);
            Assert.Empty(tools.ListTicketAttachments(ticket.BoardId, ticket.Id));
            Assert.Equal(404, Assert.Throws<ApiException>(() => tools.RemoveTicketAttachment(ticket.BoardId, ticket.Id, imageId)).Status);
            Assert.Equal(404, Assert.Throws<ApiException>(() => tools.GetTicketImage(ticket.BoardId, ticket.Id, imageId)).Status);
        });
        await Task.CompletedTask;
    }

    [Fact]
    public void UploadRejectsInvalidPayloads()
    {
        WithMcp((service, tools, user, _, _) =>
        {
            var ticket = SeedTicket(tools, service, user);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.UploadTicketImage(ticket.BoardId, ticket.Id, "not-base64!!!").GetAwaiter().GetResult()).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.UploadTicketImage(ticket.BoardId, ticket.Id, "").GetAwaiter().GetResult()).Status);
            var notAnImage = Convert.ToBase64String("hello, this is not an image"u8.ToArray());
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.UploadTicketImage(ticket.BoardId, ticket.Id, notAnImage).GetAwaiter().GetResult()).Status);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.UploadTicketVideo(ticket.BoardId, ticket.Id, "bogus", "video/mp4", Convert.ToBase64String(SamplePng(MagickColors.Blue))).GetAwaiter().GetResult()).Status);
        });
    }

    [Fact]
    public void SixAttachmentCapIsEnforcedViaMcp()
    {
        WithMcp((service, tools, user, _, _) =>
        {
            var ticket = SeedTicket(tools, service, user);
            for (var index = 0; index < 6; index++)
            {
                tools.UploadTicketImage(ticket.BoardId, ticket.Id, Convert.ToBase64String(SamplePng(MagickColors.Green))).GetAwaiter().GetResult();
            }
            Assert.Equal(6, tools.ListTicketAttachments(ticket.BoardId, ticket.Id).Length);
            Assert.Equal(400, Assert.Throws<ApiException>(() => tools.UploadTicketImage(ticket.BoardId, ticket.Id, Convert.ToBase64String(SamplePng(MagickColors.Green))).GetAwaiter().GetResult()).Status);
        });
    }

    [Fact]
    public async Task VideoUploadThumbnailAndSingleUseLinkFlow()
    {
        var videoSample = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Samples", "video_sample.mp4"));
        WithMcp((service, tools, user, _, store) =>
        {
            var ticket = SeedTicket(tools, service, user);
            var uploaded = tools.UploadTicketVideo(
                ticket.BoardId, ticket.Id,
                Convert.ToBase64String(videoSample), "video/mp4",
                Convert.ToBase64String(SamplePng(MagickColors.Transparent, 2, 2))).GetAwaiter().GetResult();
            var videoId = Assert.Single(uploaded.Videos);

            var listed = tools.ListTicketAttachments(ticket.BoardId, ticket.Id);
            var info = Assert.Single(listed);
            Assert.Equal("video", info.Kind);
            Assert.Equal("video/mp4", info.ContentType);
            Assert.True(info.SizeBytes > 0);
            Assert.True(info.ThumbnailSizeBytes > 0);

            var thumbnail = tools.GetTicketVideoThumbnail(ticket.BoardId, ticket.Id, videoId);
            Assert.True(thumbnail.IsError != true);
            Assert.Contains("video/mp4", Assert.IsType<TextContentBlock>(thumbnail.Content[0]).Text);
            var thumbBlock = Assert.IsType<ImageContentBlock>(thumbnail.Content[1]);
            Assert.Equal("image/png", thumbBlock.MimeType);
            Assert.NotEmpty(thumbBlock.DecodedData.ToArray());

            var (secret, _) = service.CreateVideoLink(ticket.BoardId, ticket.Id, videoId, user);
            var (content, contentType) = service.RedeemVideoLink(secret);
            Assert.Equal("video/mp4", contentType);
            Assert.Equal(service.TicketVideo(ticket.BoardId, ticket.Id, videoId, user).Content, content);
            Assert.Equal(404, Assert.Throws<ApiException>(() => service.RedeemVideoLink(secret)).Status);
            Assert.Equal(404, Assert.Throws<ApiException>(() => service.RedeemVideoLink("unknown-token")).Status);

            store.VideoLinks.Insert(new VideoLink
            {
                Id = AuthEndpoints.HashToken("expired-secret"),
                VideoId = videoId,
                TicketId = ticket.Id,
                BoardId = ticket.BoardId,
                OwnerId = "owner",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            });
            Assert.Equal(404, Assert.Throws<ApiException>(() => service.RedeemVideoLink("expired-secret")).Status);
            Assert.Equal(404, Assert.Throws<ApiException>(() => service.CreateVideoLink(ticket.BoardId, ticket.Id, "missing-video", user)).Status);

            var removed = tools.RemoveTicketAttachment(ticket.BoardId, ticket.Id, videoId);
            Assert.Empty(removed.Videos);
            Assert.Equal(404, Assert.Throws<ApiException>(() => service.CreateVideoLink(ticket.BoardId, ticket.Id, videoId, user)).Status);
        });
    }

    [Fact]
    public void RemovingVideoInvalidatesItsLinks()
    {
        WithMcp((service, _, user, _, store) =>
        {
            var board = service.Create(new("Attachments", ""), user);
            var ticket = service.CreateTicket(board.Id, new("Attachment ticket", board.Columns[0].Id, "Specification", ["test"]), user);
            var asset = new ImageAsset { OwnerId = "owner", BoardId = board.Id, Content = [0x00], ContentType = "video/mp4", Thumbnail = SamplePng(MagickColors.Black, 2, 2) };
            service.AddImage(board.Id, ticket.Id, asset, user, true);
            var (secret, _) = service.CreateVideoLink(board.Id, ticket.Id, asset.Id, user);
            service.RemoveImage(board.Id, ticket.Id, asset.Id, user);
            Assert.Equal(404, Assert.Throws<ApiException>(() => service.RedeemVideoLink(secret)).Status);
        });
    }

    [Fact]
    public async Task UnassignedMcpTokenCannotUseAttachmentTools()
    {
        await using var factory = new AppFactory();
        using var admin = factory.Browser();
        await Login(admin);
        var board = await CreateBoard(admin, "Private attachments");
        var ticketResponse = await admin.PostAsJsonAsync($"/api/boards/{board.Id}/tickets", new { title = "Attachments", columnId = board.Columns[0].Id });
        var ticket = (await ticketResponse.Content.ReadFromJsonAsync<Ticket>())!;
        var path = $"/api/boards/{board.Id}/tickets/{ticket.Id}";
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(SamplePng(MagickColors.Red)), "file", "red.png");
        var upload = await admin.PostAsync(path + "/images", form);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        using var user = factory.Browser();
        await Login(user, "member@example.test", admin);
        var tokenResponse = await user.PostAsJsonAsync("/api/auth/tokens", new { name = "NoBoards" });
        var token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        using var mcp = factory.Browser();
        mcp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        mcp.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        mcp.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        using var denied = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "list_ticket_attachments", arguments = new { boardId = board.Id, ticketId = ticket.Id } } });
        Assert.Contains("\"isError\":true", await denied.Content.ReadAsStringAsync());

        using var anonymous = factory.Browser();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/video-links/unknown-token")).StatusCode);
    }

    [Fact]
    public async Task VideoLinkCurlRoundtripOverHttp()
    {
        await using var factory = new AppFactory();
        using var admin = factory.Browser();
        await Login(admin);
        var board = await CreateBoard(admin, "Video links");
        var ticketResponse = await admin.PostAsJsonAsync($"/api/boards/{board.Id}/tickets", new { title = "Video", columnId = board.Columns[0].Id });
        var ticket = (await ticketResponse.Content.ReadFromJsonAsync<Ticket>())!;
        var path = $"/api/boards/{board.Id}/tickets/{ticket.Id}";
        using var videoForm = new MultipartFormDataContent();
        var videoSample = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Samples", "video_sample.mp4"));
        var mp4 = new ByteArrayContent(videoSample);
        mp4.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4");
        videoForm.Add(mp4, "file", "video_sample.mp4");
        videoForm.Add(new ByteArrayContent(SamplePng(MagickColors.Transparent, 2, 2)), "thumbnail", "thumbnail.png");
        var videoUpload = await admin.PostAsync(path + "/videos", videoForm);
        Assert.Equal(HttpStatusCode.OK, videoUpload.StatusCode);
        var videoId = Assert.Single((await videoUpload.Content.ReadFromJsonAsync<Ticket>())!.Videos);

        var tokenResponse = await admin.PostAsJsonAsync("/api/auth/tokens", new { name = "Links" });
        var token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var tokenId = AuthEndpoints.HashToken(token);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/auth/tokens/{tokenId}/boards", new { boardIds = new[] { board.Id } })).StatusCode);
        using var mcp = factory.Browser();
        mcp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        mcp.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        mcp.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        using var mint = await mcp.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "create_ticket_video_download_link", arguments = new { boardId = board.Id, ticketId = ticket.Id, videoId } } });
        var mintBody = await mint.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"isError\":true", mintBody);
        var payload = JsonSerializer.Deserialize<VideoDownloadLink>(McpMessage(mintBody).GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!, JsonOptions)!;
        var url = payload.Url;
        Assert.StartsWith("http", url);
        Assert.Contains("/api/video-links/", url);
        Assert.Contains(url, payload.CurlExample);

        using var anonymous = factory.Browser();
        var download = await anonymous.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("video/mp4", download.Content.Headers.ContentType!.MediaType);
        var stored = await admin.GetFromJsonAsync<BoardResponse>($"/api/boards/{board.Id}");
        Assert.Equal(videoId, Assert.Single(stored!.Tickets.Single(item => item.Id == ticket.Id).Videos));
        Assert.True((await download.Content.ReadAsByteArrayAsync()).Length > 0);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(url)).StatusCode);
    }

    private static async Task Login(HttpClient client, string email = "admin@example.test", HttpClient? admin = null)
    {
        if (admin is not null && email != "admin@example.test")
        {
            var created = await admin.PostAsJsonAsync("/api/admin/accounts", new { email, name = email.Split('@')[0], password = AppFactory.Password, isAdmin = false });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = AppFactory.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }

    private static async Task<Board> CreateBoard(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/boards", new { name, description = "Private board" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Board>())!;
    }
}
