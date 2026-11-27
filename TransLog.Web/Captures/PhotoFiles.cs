using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Security;
using TransLog.Web.Storage;

namespace TransLog.Web.Captures;

/// <summary>
/// Hands out stored photos after checking who is asking (Part 4a). Photos are outside wwwroot, so this is the only way to them.
/// <list type="bullet">
/// <item>A driver sees only captures they sent or that are theirs.</item>
/// <item>Office users (Owner, Admin, Capturer, Viewer) see every capture of their own company (the tenant filter).</item>
/// <item>Anyone else, or a photo of another company, gets "not found".</item>
/// </list>
/// </summary>
public static class PhotoFiles
{
    public static void MapPhotoFiles(this IEndpointRouteBuilder app)
    {
        app.MapGet("/files/capture/{id:int}", (int id, HttpContext ctx, AppDbContext db, IFileStore files) =>
            SendAsync(id, thumb: false, ctx, db, files)).RequireAuthorization();
        app.MapGet("/files/capture/{id:int}/thumb", (int id, HttpContext ctx, AppDbContext db, IFileStore files) =>
            SendAsync(id, thumb: true, ctx, db, files)).RequireAuthorization();
    }

    private static async Task<IResult> SendAsync(int imageId, bool thumb, HttpContext ctx, AppDbContext db, IFileStore files)
    {
        var user = ctx.User;
        if (user.GetCompanyId() is null)
            return Results.NotFound();

        var images = db.CaptureImages.AsNoTracking().Where(i => i.Id == imageId);
        if (user.IsInRole(Roles.Driver))
        {
            var userId = user.GetUserId();
            images = images.Where(i => i.Capture!.CapturedByUserId == userId || i.Capture.Driver!.UserId == userId);
        }
        else if (!Roles.Company.Any(user.IsInRole))
        {
            return Results.NotFound();
        }

        var image = await images.Select(i => new { i.FilePath, i.ThumbPath }).FirstOrDefaultAsync();
        if (image is null)
            return Results.NotFound();

        var stream = files.OpenRead(thumb && image.ThumbPath is not null ? image.ThumbPath : image.FilePath);
        if (stream is null)
            return Results.NotFound();

        // A stored photo never changes (a turn makes a new file), so the phone may keep it; "private" keeps it out of shared caches.
        ctx.Response.Headers.CacheControl = "private, max-age=604800, immutable";
        ctx.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(stream, "image/jpeg");
    }
}
