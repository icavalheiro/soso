using System.Security.Claims;

namespace Soso.Api;

public static class BoardEndpoints
{
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
        boards.MapDelete("/{id}", (string id, ClaimsPrincipal user, BoardService service) =>
        {
            service.DeleteBoard(id, user);
            return TypedResults.NoContent();
        });
        boards.MapPost("/{id}/tickets", (string id, CreateTicketRequest request, ClaimsPrincipal user, BoardService service) =>
        {
            var ticket = service.CreateTicket(id, request, user);
            return TypedResults.Created($"/api/boards/{id}/tickets/{ticket.Id}", ticket);
        });
        boards.MapPut("/{id}/tickets/{ticketId}", (string id, string ticketId, UpdateTicketRequest request, ClaimsPrincipal user, BoardService service) => TypedResults.Ok(service.UpdateTicket(id, ticketId, request, user)));
        boards.MapPost("/{id}/columns/{columnId}/archive", (string id, string columnId, ClaimsPrincipal user, BoardService service) => TypedResults.Ok(service.ArchiveColumnTickets(id, columnId, user)));
        boards.MapDelete("/{id}/tickets/{ticketId}", (string id, string ticketId, ClaimsPrincipal user, BoardService service) =>
        {
            service.DeleteTicket(id, ticketId, user);
            return TypedResults.NoContent();
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
        app.MapGet("/api/video-links/{token}", (string token, BoardService service, HttpContext context) =>
        {
            var (content, contentType) = service.RedeemVideoLink(token);
            context.Response.Headers.CacheControl = "private, no-store";
            return TypedResults.File(content, contentType, "video.mp4");
        }).AllowAnonymous();
    }

    private static async Task<byte[]> ReadImage(HttpContext context)
    {
        return await AttachmentProcessor.WithImageGate(async () =>
        {
            if (!context.Request.HasFormContentType)
            {
                throw new ApiException(400, "Upload a multipart image file.");
            }
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            return DecodeImageFormFile(form.Files.GetFile("file"), context.RequestAborted);
        }, context.RequestAborted);
    }

    private static byte[] DecodeImageFormFile(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is not { Length: > 0 and <= AttachmentProcessor.MaxImageBytes })
        {
            throw new ApiException(400, "Choose an image under 5 MB.");
        }
        using var content = new MemoryStream();
        using var stream = file.OpenReadStream();
        stream.CopyTo(content);
        return AttachmentProcessor.DecodeImageBytes(content.ToArray(), cancellationToken);
    }

    private static async Task<(byte[] Content, string ContentType, byte[] Thumbnail)> ReadVideo(HttpContext context)
    {
        return await AttachmentProcessor.WithVideoGate(async () =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var file = form.Files.GetFile("file");
            if (file is not { Length: > 0 and <= AttachmentProcessor.MaxVideoBytes })
            {
                throw new ApiException(400, "Choose a video under 50 MB.");
            }
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, context.RequestAborted);
            var thumbnail = DecodeImageFormFile(form.Files.GetFile("thumbnail"), context.RequestAborted);
            return await AttachmentProcessor.ValidateVideoUploadAsync(stream.ToArray(), file.ContentType, thumbnail, context.RequestAborted);
        }, context.RequestAborted);
    }
}
