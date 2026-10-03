using System.Security.Claims;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats;

namespace Soso.Api;

public static class BoardEndpoints
{
    private static readonly SemaphoreSlim ImageGate = new(2);
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
                foreach (var image in ticket.Images)
                {
                    store.Images.Delete(image);
                }
                store.Tickets.Delete(ticketId);
                return TypedResults.NoContent();
            }
        });
        boards.MapPost("/{id}/tickets/{ticketId}/comments", (string id, string ticketId, CommentRequest request, ClaimsPrincipal user, BoardService service) => TypedResults.Ok(service.Comment(id, ticketId, request.Text, user)));
        boards.MapDelete("/{id}/tickets/{ticketId}/comments/{commentId}", (string id, string ticketId, string commentId, ClaimsPrincipal user, BoardService service, Store store) =>
        {
            lock (store.Gate)
            {
                var ticket = service.RequireTicket(id, ticketId, user);
                var comment = ticket.Comments.FirstOrDefault(comment => comment.Id == commentId) ?? throw new ApiException(404, "Comment not found.");
                var canDelete = comment.AuthorId == BoardService.UserId(user) || user.IsInRole("admin");
                if (!canDelete)
                {
                    throw new ApiException(403, "You can only delete your own comments.");
                }
                ticket.Comments.Remove(comment);
                ticket.Revision++;
                store.Tickets.Update(ticket);
                return TypedResults.Ok(ticket);
            }
        });
        boards.MapPost("/{id}/tickets/{ticketId}/images", async (string id, string ticketId, HttpContext context, BoardService service, Store store) =>
        {
            service.RequireTicket(id, ticketId, context.User);
            var content = await ReadImage(context);
            lock (store.Gate)
            {
                var ticket = service.RequireTicket(id, ticketId, context.User);
                if (ticket.Images.Count >= 6)
                {
                    throw new ApiException(400, "A ticket supports up to six images.");
                }
                var image = new ImageAsset { OwnerId = BoardService.UserId(context.User), BoardId = id, Content = content };
                store.Images.Insert(image);
                ticket.Images.Add(image.Id);
                ticket.Revision++;
                store.Tickets.Update(ticket);
                return TypedResults.Ok(ticket);
            }
        });
        boards.MapDelete("/{id}/tickets/{ticketId}/images/{imageId}", (string id, string ticketId, string imageId, ClaimsPrincipal user, BoardService service, Store store) =>
        {
            lock (store.Gate)
            {
                var ticket = service.RequireTicket(id, ticketId, user);
                if (!ticket.Images.Remove(imageId))
                {
                    throw new ApiException(404, "Image not found.");
                }
                store.Images.Delete(imageId);
                ticket.Revision++;
                store.Tickets.Update(ticket);
                return TypedResults.Ok(ticket);
            }
        });
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
            return TypedResults.File(image.Content, "image/png");
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
        var validSize = file is { Length: > 0 and <= 5 * 1024 * 1024 };
        if (!validSize)
        {
            throw new ApiException(400, "Choose an image under 5 MB.");
        }
        try
        {
            await using var stream = file!.OpenReadStream();
            var information = await Image.IdentifyAsync(stream, context.RequestAborted);
            var pixels = (long)information.Width * information.Height;
            if (pixels > 16_000_000 || information.FrameMetadataCollection.Count > 1)
            {
                throw new ApiException(400, "Use a non-animated image up to 16 megapixels.");
            }
            stream.Position = 0;
            using var image = await Image.LoadAsync(new DecoderOptions { SkipMetadata = true }, stream, context.RequestAborted);
            image.Mutate(operation => operation.Resize(new ResizeOptions { Size = new Size(1600, 1600), Mode = ResizeMode.Max }));
            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.XmpProfile = null;
            using var output = new MemoryStream();
            await image.SaveAsPngAsync(output, context.RequestAborted);
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