using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using Soso.Api;
using Xunit;

namespace Soso.Api.Tests;

[Collection("LiteDB tests")]
public sealed class ImageProcessingTests
{
    private static async Task<(HttpClient Client, string Path)> PrepareTicket(AppFactory factory)
    {
        var client = factory.Browser();
        await RefreshCsrf(client);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.test", password = AppFactory.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await RefreshCsrf(client);
        var boardResponse = await client.PostAsJsonAsync("/api/boards", new { name = "Image pipeline", description = "" });
        Assert.Equal(HttpStatusCode.Created, boardResponse.StatusCode);
        var board = (await boardResponse.Content.ReadFromJsonAsync<Board>())!;
        var ticketResponse = await client.PostAsJsonAsync($"/api/boards/{board.Id}/tickets", new { title = "Attachments", columnId = board.Columns[0].Id });
        Assert.Equal(HttpStatusCode.Created, ticketResponse.StatusCode);
        var ticket = (await ticketResponse.Content.ReadFromJsonAsync<Ticket>())!;
        return (client, $"/api/boards/{board.Id}/tickets/{ticket.Id}/images");
    }

    private static async Task RefreshCsrf(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient client, string path, byte[] content, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(content);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(part, "file", fileName);
        return await client.PostAsync(path, form);
    }

    [Fact]
    public async Task AnimatedUploadsAreRejected()
    {
        using var factory = new AppFactory();
        var (client, path) = await PrepareTicket(factory);
        using var first = new MagickImage(MagickColors.Red, 4, 4);
        using var second = new MagickImage(MagickColors.Blue, 4, 4);
        using var animated = new MagickImageCollection { first, second };
        var bytes = animated.ToByteArray(MagickFormat.Gif);
        var response = await Upload(client, path, bytes, "loop.gif");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ImagesOverSixteenMegapixelsAreRejected()
    {
        using var factory = new AppFactory();
        var (client, path) = await PrepareTicket(factory);
        using var large = new MagickImage(MagickColors.White, 4100, 4000);
        var bytes = large.ToByteArray(MagickFormat.Png);
        var response = await Upload(client, path, bytes, "huge.png");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FormatsOutsideTheAllowListAreRejected()
    {
        using var factory = new AppFactory();
        var (client, path) = await PrepareTicket(factory);
        using var pixmap = new MagickImage(MagickColors.Black, 4, 4);
        var bytes = pixmap.ToByteArray(MagickFormat.Pgm);
        var response = await Upload(client, path, bytes, "not-allowed.pgm");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EveryAllowedFormatIsNormalisedToPng()
    {
        using var factory = new AppFactory();
        var (client, path) = await PrepareTicket(factory);
        foreach (var format in new[] { MagickFormat.Png, MagickFormat.Jpeg, MagickFormat.Gif, MagickFormat.Bmp, MagickFormat.Tiff })
        {
            using var image = new MagickImage(MagickColors.Teal, 6, 4);
            var bytes = image.ToByteArray(format);
            var response = await Upload(client, path, bytes, "sample");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var saved = (await response.Content.ReadFromJsonAsync<Ticket>())!;
            var storedId = saved.Images[^1];
            var download = await client.GetAsync($"/api/images/{storedId}");
            Assert.Equal("image/png", download.Content.Headers.ContentType!.MediaType);
            var stored = await download.Content.ReadAsByteArrayAsync();
            using var decoded = new MagickImage(stored);
            Assert.Equal(MagickFormat.Png, decoded.Format);
        }
    }

    [Fact]
    public async Task OversizedImagesAreShrunkToOneThousandSixHundredPixelsOnTheLongSide()
    {
        using var factory = new AppFactory();
        var (client, path) = await PrepareTicket(factory);
        using var source = new MagickImage(MagickColors.White, 3200, 1800);
        var bytes = source.ToByteArray(MagickFormat.Png);
        var response = await Upload(client, path, bytes, "wide.png");
        response.EnsureSuccessStatusCode();
        var saved = (await response.Content.ReadFromJsonAsync<Ticket>())!;
        var download = await client.GetAsync($"/api/images/{saved.Images.Single()}");
        var stored = await download.Content.ReadAsByteArrayAsync();
        using var decoded = new MagickImage(stored);
        Assert.Equal(1600, (int)decoded.Width);
        Assert.Equal(900, (int)decoded.Height);
    }

    [Fact]
    public async Task ExifProfileIsStrippedFromStoredImages()
    {
        using var factory = new AppFactory();
        var (client, path) = await PrepareTicket(factory);
        using var source = new MagickImage(MagickColors.White, 8, 8);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Make, "Sosô Test");
        source.SetProfile(exif);
        var bytes = source.ToByteArray(MagickFormat.Jpeg);
        var response = await Upload(client, path, bytes, "profile.jpg");
        response.EnsureSuccessStatusCode();
        var saved = (await response.Content.ReadFromJsonAsync<Ticket>())!;
        var download = await client.GetAsync($"/api/images/{saved.Images.Single()}");
        var stored = await download.Content.ReadAsByteArrayAsync();
        using var decoded = new MagickImage(stored);
        Assert.Null(decoded.GetExifProfile()?.GetValue(ExifTag.Make)?.Value);
    }
}
