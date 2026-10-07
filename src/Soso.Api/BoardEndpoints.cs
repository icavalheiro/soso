using System.Security.Claims;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats;

namespace Soso.Api;

public static class BoardEndpoints
{
    private static readonly SemaphoreSlim ImageGate = new(2);
    private static readonly SemaphoreSlim VideoGate = new(2);
    public static void MapBoards(this WebApplication app)
    {
        var boards = app.MapGroup("/api/boards");
        boards.MapGet("/", (ClaimsPrincipal user, BoardService service) => TypedResults.Ok(service.List(user)));
        boards.MapPost("/", (CreateBoardRequest request, ClaimsPrincipal user, BoardService service) =>
        {
            var board = service.Create(request, user);
            return TypedResults.Created($"/api/boards/{board.Id}", board);
        });
        boards.MapGet("/{id}", (string id, ClaimsPrincipal user, BoardService service) => TypedResults.Ok(service.Get(id, user)));
        boards.MapPut("/{id}", (string id, UpdateBoardRequest request, ClaimsPrincipal user, BoardService service) => TypedResults.Ok(service.Update(id, request, user)));
        boards.MapDelete("/{id}", (string id, ClaimsPrincipal user, BoardService service, Store store) =>
        {
            lock (store.Gate)
            {
                service.RequireBoard(id, user, true);
                store.Tickets.DeleteMany(ticket => ticket.BoardId == id);
                store.Images.DeleteMany(image => image.BoardId == id);
                store.Boards.Delete(id);
                return TypedResults.NoContent();
            }
        });
        boards.MapPost("/{id}/tickets", (string id, CreateTicketRequest request, ClaimsPrincipal user, BoardService service) =>
        {
            var ticket = service.CreateTicket(id, request, user);
            return TypedResults.Created($"/api/boards/{id}/tickets/{ticket.Id}", ticket);
        });
        boards.MapPut("/{id}/tickets/{ticketId}", (string id, string ticketId, UpdateTicketRequest request, ClaimsPrincipal user, BoardService service) => TypedResults.Ok(service.UpdateTicket(id, ticketId, request, user)));
        boards.MapDelete("/{id}/tickets/{ticketId}", (string id, string ticketId, ClaimsPrincipal user, BoardService service, Store store) =>
        {
            lock (store.Gate)
            {
                var ticket = service.RequireTicket(id, ticketId, user);
                foreach (var image in ticket.Images.Concat(ticket.Videos))
                {
                    store.Images.Delete(image);
                }
                store.Tickets.Delete(ticketId);
                return TypedResults.NoContent();
            }
        });
        boards.MapPost("/{id}/tickets/{ticketId}/comments", (string id, string ticketId, CommentRequest request, ClaimsPrincipal user, BoardService service) => TypedResults.Ok(service.Comment(id, ticketId, request.Text, user)));
        boards.MapDelete("/{id}/tickets/{ticketId}/comments/{commentId}", (string id, string ticketId, string commentId, ClaimsPrincipal user, BoardService service) => TypedResults.Ok(service.DeleteComment(id, ticketId, commentId, user)));
        boards.MapPost("/{id}/tickets/{ticketId}/images", async (string id, string ticketId, HttpContext context, BoardService service) =>
        {
            service.RequireTicket(id, ticketId, context.User);
            var content = await ReadImage(context);
            var image = new ImageAsset { OwnerId = BoardService.UserId(context.User), BoardId = id, Content = content };
            return TypedResults.Ok(service.AddImage(id, ticketId, image, context.User));
        });
        boards.MapPost("/{id}/tickets/{ticketId}/videos", async (string id, string ticketId, HttpContext context, BoardService service) =>
        {
            service.RequireTicket(id, ticketId, context.User);
            var (content, contentType, thumbnail) = await ReadVideo(context);
            var video = new ImageAsset { OwnerId = BoardService.UserId(context.User), BoardId = id, Content = content, ContentType = contentType, Thumbnail = thumbnail };
            return TypedResults.Ok(service.AddImage(id, ticketId, video, context.User, true));
        });
        boards.MapDelete("/{id}/tickets/{ticketId}/images/{imageId}", (string id, string ticketId, string imageId, ClaimsPrincipal user, BoardService service) => TypedResults.Ok(service.RemoveImage(id, ticketId, imageId, user)));
        app.MapPost("/api/auth/avatar", async (HttpContext context, Store store) =>
        {
            var content = await ReadImage(context);
            lock (store.Gate)
            {
                var account = store.Accounts.FindById(BoardService.UserId(context.User));
                if (account.AvatarId is not null)
                {
                    store.Images.Delete(account.AvatarId);
                }
                var image = new ImageAsset { OwnerId = account.Id, Content = content };
                store.Images.Insert(image);
                account.AvatarId = image.Id;
                store.Accounts.Update(account);
                return TypedResults.Ok(AccountResponse.From(account));
            }
        });
        app.MapGet("/api/images/{id}", (string id, ClaimsPrincipal user, Store store, BoardService service, HttpContext context) =>
        {
            var image = store.Images.FindById(id) ?? throw new ApiException(404, "Image not found.");
            if (image.BoardId is not null)
            {
                service.RequireBoard(image.BoardId, user);
            }
            context.Response.Headers.CacheControl = "private, no-store";
            return TypedResults.File(image.Content, image.ContentType);
        });
        app.MapGet("/api/videos/{id}", (string id, ClaimsPrincipal user, Store store, BoardService service, HttpContext context) =>
        {
            var video = store.Images.FindById(id) ?? throw new ApiException(404, "Video not found.");
            if (video.BoardId is not null)
            {
                service.RequireBoard(video.BoardId, user);
            }
            context.Response.Headers.CacheControl = "private, no-store";
            return TypedResults.File(video.Content, video.ContentType);
        });
        app.MapGet("/api/videos/{id}/thumbnail", (string id, ClaimsPrincipal user, Store store, BoardService service, HttpContext context) =>
        {
            var video = store.Images.FindById(id) ?? throw new ApiException(404, "Video not found.");
            if (video.BoardId is not null)
            {
                service.RequireBoard(video.BoardId, user);
            }
            context.Response.Headers.CacheControl = "private, no-store";
            return TypedResults.File(video.Thumbnail, "image/png");
        });
    }

    private static async Task<byte[]> ReadImage(HttpContext context)
    {
        var acquired = await ImageGate.WaitAsync(TimeSpan.FromSeconds(5), context.RequestAborted);
        if (!acquired)
        {
            throw new ApiException(429, "Image processing is busy. Try again shortly.");
        }
        try
        {
            return await DecodeImage(context);
        }
        finally
        {
            ImageGate.Release();
        }
    }

    private static async Task<byte[]> DecodeImage(HttpContext context)
    {
        var isMultipart = context.Request.HasFormContentType;
        if (!isMultipart)
        {
            throw new ApiException(400, "Upload a multipart image file.");
        }
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var file = form.Files.GetFile("file");
        return await DecodeImageFile(file, context.RequestAborted);
    }

    private static async Task<(byte[] Content, string ContentType, byte[] Thumbnail)> ReadVideo(HttpContext context)
    {
        var acquired = await VideoGate.WaitAsync(TimeSpan.FromSeconds(5), context.RequestAborted);
        if (!acquired)
        {
            throw new ApiException(429, "Video processing is busy. Try again shortly.");
        }
        try
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var file = form.Files.GetFile("file");
            if (file is not { Length: > 0 and <= 50 * 1024 * 1024 })
            {
                throw new ApiException(400, "Choose a video under 50 MB.");
            }
            var contentType = file.ContentType.ToLowerInvariant();
            if (contentType is not ("video/mp4" or "video/webm" or "video/ogg"))
            {
                throw new ApiException(400, "Use an MP4, WebM or Ogg video.");
            }
            var thumbnail = await DecodeImageFile(form.Files.GetFile("thumbnail"), context.RequestAborted);
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, context.RequestAborted);
            var content = stream.ToArray();
            if (!HasVideoSignature(content, contentType))
            {
                throw new ApiException(400, "The video content does not match its file type.");
            }
            return (content, contentType, thumbnail);
        }
        finally
        {
            VideoGate.Release();
        }
    }

    private static bool HasVideoSignature(byte[] content, string contentType) => contentType switch
    {
        "video/mp4" => content.Length >= 8 && content.AsSpan(4, 4).SequenceEqual("ftyp"u8),
        "video/webm" => content.AsSpan().StartsWith(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }),
        "video/ogg" => content.AsSpan().StartsWith("OggS"u8),
        _ => false
    };

    private static async Task<byte[]> DecodeImageFile(IFormFile? file, CancellationToken cancellationToken)
    {
        var validSize = file is { Length: > 0 and <= 5 * 1024 * 1024 };
        if (!validSize)
        {
            throw new ApiException(400, "Choose an image under 5 MB.");
        }
        try
        {
            await using var stream = file!.OpenReadStream();
            var information = await Image.IdentifyAsync(stream, cancellationToken);
            var pixels = (long)information.Width * information.Height;
            if (pixels > 16_000_000 || information.FrameMetadataCollection.Count > 1)
            {
                throw new ApiException(400, "Use a non-animated image up to 16 megapixels.");
            }
            stream.Position = 0;
            using var image = await Image.LoadAsync(new DecoderOptions { SkipMetadata = true }, stream, cancellationToken);
            image.Mutate(operation => operation.Resize(new ResizeOptions { Size = new Size(1600, 1600), Mode = ResizeMode.Max }));
            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.XmpProfile = null;
            using var output = new MemoryStream();
            await image.SaveAsPngAsync(output, cancellationToken);
            var bytes = output.ToArray();
            if (bytes.Length > 5 * 1024 * 1024)
            {
                throw new ApiException(400, "Processed image is too large.");
            }
            return bytes;
        }
        catch (UnknownImageFormatException)
        {
            throw new ApiException(400, "Unsupported image format.");
        }
        catch (InvalidImageContentException)
        {
            throw new ApiException(400, "Invalid image file.");
        }
    }
}
