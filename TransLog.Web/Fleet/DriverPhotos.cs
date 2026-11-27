using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Html;
using TransLog.Web.Data;
using TransLog.Web.Security;
using TransLog.Web.Storage;

namespace TransLog.Web.Fleet;

/// <summary>
/// Driver profile photos (Part 4c). The browser crops the photo square (600 px, plus a 160 px copy) from the phone camera or
/// gallery (page-driver-photo.js); the server checks it is a JPEG and stores both files under
/// <c>drivers/{companyId}/{driverId}/{guid}.jpg</c> and <c>{guid}-t.jpg</c>. A new photo replaces the old files.
/// Shown by /files/driver/{id} (see Captures/PhotoFiles.cs) to the driver and every office user of the company.
/// </summary>
public class DriverPhotos(AppDbContext db, IFileStore files, AuditLogger audit)
{
    public const int MaxPhotoBytes = 1024 * 1024;
    public const int MaxThumbBytes = 256 * 1024;
    public const int MaxSide = 1200;

    /// <summary>Address of the photo; the "v" changes with every new photo, so phones may keep each one.</summary>
    public static string? Url(Driver driver, bool thumb = true) =>
        driver.PhotoPath is { } path ? Url(driver.Id, path, thumb) : null;

    public static string Url(int driverId, string photoPath, bool thumb = true) =>
        string.Create(CultureInfo.InvariantCulture, $"/files/driver/{driverId}{(thumb ? "/thumb" : "")}?v={Path.GetFileNameWithoutExtension(photoPath)}");

    /// <summary>
    /// A round picture for lists and pages: the photo, or the driver's initials when there is none.
    /// <paramref name="css"/> sets the size (tl-avatar-sm, tl-avatar-lg, tl-avatar-xl).
    /// </summary>
    public static IHtmlContent Avatar(int driverId, string? photoPath, string name, string css = "tl-avatar-sm")
    {
        var cls = WebUtility.HtmlEncode("tl-avatar " + css);
        if (photoPath is not null)
            return new HtmlString($"<img class=\"{cls}\" src=\"{WebUtility.HtmlEncode(Url(driverId, photoPath, thumb: !css.Contains("xl")))}\" alt=\"Photo of {WebUtility.HtmlEncode(name)}\" loading=\"lazy\" />");
        return new HtmlString($"<span class=\"{cls}\" aria-hidden=\"true\">{WebUtility.HtmlEncode(Initials(name))}</span>");
    }

    /// <summary>"Sipho van Wyk" → "SW"; "Sipho" → "S".</summary>
    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
        };
    }

    /// <summary>"drivers/1/2/abc.jpg" → "drivers/1/2/abc-t.jpg".</summary>
    public static string ThumbOf(string photoPath) => photoPath[..^".jpg".Length] + "-t.jpg";

    /// <summary>Reads the files <c>photo</c> and <c>thumb</c> (optional) from the upload form.</summary>
    public static async Task<(byte[]? Photo, byte[]? Thumb, string? Error)> ReadAsync(HttpRequest request)
    {
        if (!request.HasFormContentType)
            return (null, null, "No photo was sent.");
        var form = await request.ReadFormAsync();
        var photo = form.Files["photo"];
        if (photo is null || photo.Length == 0)
            return (null, null, "No photo was sent.");
        if (photo.Length > MaxPhotoBytes)
            return (null, null, "The photo is too big.");
        var thumb = form.Files["thumb"];
        if (thumb is { Length: > MaxThumbBytes })
            return (null, null, "The preview is too big.");
        return (await BytesAsync(photo), thumb is { Length: > 0 } ? await BytesAsync(thumb) : null, null);
    }

    /// <summary>Stores a new photo for a tracked driver, writes the activity log, saves, then removes the old files. Returns an error or null.</summary>
    public async Task<string?> SaveAsync(Driver driver, byte[] photo, byte[]? thumb, bool byDriver)
    {
        if (JpegInfo.Read(photo) is not { } size)
            return "That file is not a JPEG photo.";
        var (width, height) = size;
        if (width > MaxSide || height > MaxSide)
            return "The photo is too big.";
        if (thumb is not null && JpegInfo.Read(thumb) is null)
            thumb = null;

        var path = string.Create(CultureInfo.InvariantCulture, $"drivers/{driver.CompanyId}/{driver.Id}/{Guid.NewGuid():N}.jpg");
        await files.SaveAsync(path, photo);
        await files.SaveAsync(ThumbOf(path), thumb ?? photo);

        var old = driver.PhotoPath;
        driver.PhotoPath = path;
        audit.Add(old is null ? "DriverPhotoAdded" : "DriverPhotoChanged",
            $"{driver.FullName}{(byDriver ? " (by the driver)" : "")}", driver.CompanyId);
        await db.SaveChangesAsync();

        DeleteFiles(old);
        return null;
    }

    /// <summary>Removes the photo of a tracked driver (Owner or Admin).</summary>
    public async Task RemoveAsync(Driver driver)
    {
        var old = driver.PhotoPath;
        if (old is null)
            return;
        driver.PhotoPath = null;
        audit.Add("DriverPhotoRemoved", driver.FullName, driver.CompanyId);
        await db.SaveChangesAsync();
        DeleteFiles(old);
    }

    private void DeleteFiles(string? path)
    {
        if (path is null)
            return;
        files.Delete(path);
        files.Delete(ThumbOf(path));
    }

    private static async Task<byte[]> BytesAsync(IFormFile file)
    {
        using var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms);
        return ms.ToArray();
    }
}

/// <summary>What the photo box partial (Pages/Shared/_DriverPhotoForm.cshtml) needs (Part 4c).</summary>
/// <param name="Camera">"user" (front camera, the driver himself) or "environment" (back camera, office).</param>
public sealed record DriverPhotoBox(int DriverId, string? PhotoPath, string Name, string PostUrl, string DoneUrl, string Camera);
