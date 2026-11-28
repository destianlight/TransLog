using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Storage;

namespace TransLog.Web.Captures;

/// <summary>One photo as it arrives from the phone (Part 4a).</summary>
public sealed record CaptureUpload
{
    public const int MaxPhotoBytes = 8 * 1024 * 1024;
    public const int MaxThumbBytes = 512 * 1024;
    public const int MaxSide = 6000;

    public Guid ClientKey { get; init; }

    public byte[] Photo { get; init; } = [];

    public byte[]? Thumb { get; init; }

    public DateTime? CapturedAtUtc { get; init; }

    public ImageSource Source { get; init; }

    public int Rotation { get; init; }

    public decimal? Latitude { get; init; }

    public decimal? Longitude { get; init; }

    public int? Accuracy { get; init; }

    public DateTime? LocationTimeUtc { get; init; }

    /// <summary>
    /// Reads the upload form sent by page-capture-queue.js: files <c>photo</c> and <c>thumb</c>; fields <c>clientKey</c>,
    /// <c>capturedAt</c> and <c>locAt</c> (milliseconds since 1970, UTC), <c>source</c> (camera or gallery), <c>rotation</c>,
    /// <c>lat</c>, <c>lng</c>, <c>acc</c>. Numbers use "." (invariant culture). Returns an error message for the driver when it is not usable.
    /// </summary>
    public static async Task<(CaptureUpload? Upload, string? Error)> ReadAsync(HttpRequest request, bool allowOffice = false)
    {
        if (!request.HasFormContentType)
            return (null, "No photo was sent.");
        var form = await request.ReadFormAsync();

        if (!Guid.TryParse(form["clientKey"], out var key) || key == Guid.Empty)
            return (null, "The photo has no key. Take it again.");

        var photoFile = form.Files["photo"];
        if (photoFile is null || photoFile.Length == 0)
            return (null, "No photo was sent.");
        if (photoFile.Length > MaxPhotoBytes)
            return (null, "The photo is too big.");
        var photo = await ReadAllAsync(photoFile);

        byte[]? thumb = null;
        var thumbFile = form.Files["thumb"];
        if (thumbFile is { Length: > 0 })
        {
            if (thumbFile.Length > MaxThumbBytes)
                return (null, "The preview is too big.");
            thumb = await ReadAllAsync(thumbFile);
        }

        var source = ((string?)form["source"])?.ToLowerInvariant() switch
        {
            "gallery" => ImageSource.Gallery,
            "office" when allowOffice => ImageSource.Office,
            _ => ImageSource.Camera,
        };

        var rotation = Int(form["rotation"]) ?? 0;
        if (rotation is not (0 or 90 or 180 or 270))
            rotation = 0;

        decimal? lat = Dec(form["lat"]), lng = Dec(form["lng"]);
        if (lat is null || lng is null || lat < -90m || lat > 90m || lng < -180m || lng > 180m)
            lat = lng = null;
        var acc = lat is null ? null : Int(form["acc"]);
        if (acc is < 0)
            acc = null;
        if (acc is > 100_000)
            acc = 100_000;

        return (new CaptureUpload
        {
            ClientKey = key,
            Photo = photo,
            Thumb = thumb,
            CapturedAtUtc = Millis(form["capturedAt"]),
            Source = source,
            Rotation = rotation,
            Latitude = lat is null ? null : Math.Round(lat.Value, 6),
            Longitude = lng is null ? null : Math.Round(lng.Value, 6),
            Accuracy = acc,
            LocationTimeUtc = lat is null ? null : Millis(form["locAt"]),
        }, null);
    }

    private static async Task<byte[]> ReadAllAsync(IFormFile file)
    {
        using var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static int? Int(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static decimal? Dec(string? s) =>
        decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static DateTime? Millis(string? s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) && ms > 0 && ms < 32_503_680_000_000
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
            : null;
}

public sealed record CaptureSaveResult(bool Ok, int? CaptureId = null, bool Duplicate = false, string? Error = null)
{
    public static CaptureSaveResult Fail(string error) => new(false, Error: error);
}

/// <summary>
/// Saves uploaded photos: checks the file, writes it and its preview to the file store, and adds the Capture with its image.
/// The same ClientKey twice returns the first capture, so retries from the phone are safe.
/// </summary>
public class CaptureService(AppDbContext db, IFileStore files, ILogger<CaptureService> log, TransLog.Web.Ai.AiQueue aiQueue, CaptureLive live, IConfiguration config)
{
    /// <summary>A driver sends a photo from the camera page. The vehicle is the driver's linked vehicle at that moment (or none).</summary>
    public async Task<CaptureSaveResult> SaveForDriverAsync(int userId, CaptureUpload upload)
    {
        // The tenant filter keeps this inside the driver's own company.
        var driver = await db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.UserId == userId);
        if (driver is null)
            return CaptureSaveResult.Fail("Your login is not linked to a driver. Ask your company to check your driver details.");
        if (!driver.IsActive)
            return CaptureSaveResult.Fail("Your driver profile is switched off. Ask your company.");

        var vehicleId = await db.DriverVehicleLinks
            .Where(l => l.DriverId == driver.Id && l.UnlinkedAt == null)
            .Select(l => (int?)l.VehicleId)
            .FirstOrDefaultAsync();

        return await SaveAsync(driver.CompanyId, userId, driver.Id, vehicleId, upload);
    }

    /// <summary>
    /// An office user (Owner, Admin, Capturer) adds a photo for a driver and vehicle of their company (Part 4b); both optional.
    /// Always source Office, no location, and the photo date is the moment it arrives (a file's date on a PC is often only
    /// when it was copied). The date printed on the slip is read by the AI in Part 5.
    /// </summary>
    public async Task<CaptureSaveResult> SaveForOfficeAsync(int companyId, int userId, int? driverId, int? vehicleId, CaptureUpload upload)
    {
        // The tenant filter hides other companies' drivers and vehicles, so their ids are simply "not found".
        if (driverId is int d && !await db.Drivers.AnyAsync(x => x.Id == d))
            return CaptureSaveResult.Fail("That driver was not found. Reload the page.");
        if (vehicleId is int v && !await db.Vehicles.AnyAsync(x => x.Id == v))
            return CaptureSaveResult.Fail("That vehicle was not found. Reload the page.");

        var office = upload with
        {
            Source = ImageSource.Office,
            CapturedAtUtc = null,
            Latitude = null,
            Longitude = null,
            Accuracy = null,
            LocationTimeUtc = null,
        };
        return await SaveAsync(companyId, userId, driverId, vehicleId, office);
    }

    /// <summary>Checks and stores one photo as a new capture (drivers and office capture).</summary>
    public async Task<CaptureSaveResult> SaveAsync(int companyId, int userId, int? driverId, int? vehicleId, CaptureUpload upload)
    {
        var existing = await FindAsync(upload.ClientKey);
        if (existing is int already)
            return new CaptureSaveResult(true, already, Duplicate: true);

        var size = JpegInfo.Read(upload.Photo);
        if (size is null)
            return CaptureSaveResult.Fail("That file is not a JPEG photo.");
        var (width, height) = size.Value;
        if (width > CaptureUpload.MaxSide || height > CaptureUpload.MaxSide)
            return CaptureSaveResult.Fail("The photo is too big.");
        var thumb = upload.Thumb is { } t && JpegInfo.Read(t) is not null ? t : null;

        var now = DateTime.UtcNow;
        // The phone's clock decides when the photo was taken, unless it is clearly wrong.
        var capturedAt = upload.CapturedAtUtc is { } c && c <= now.AddMinutes(10) && c >= now.AddDays(-60) ? c : now;

        var folder = string.Create(CultureInfo.InvariantCulture, $"captures/{companyId}/{now:yyyy}/{now:MM}");
        var name = Guid.NewGuid().ToString("N");
        var photoPath = $"{folder}/{name}.jpg";
        var thumbPath = thumb is null ? null : $"{folder}/{name}-t.jpg";

        await files.SaveAsync(photoPath, upload.Photo);
        if (thumbPath is not null)
            await files.SaveAsync(thumbPath, thumb!);

        var capture = new Capture
        {
            CompanyId = companyId,
            ClientKey = upload.ClientKey,
            DriverId = driverId,
            VehicleId = vehicleId,
            CapturedByUserId = userId,
            CapturedAt = capturedAt,
            ReceivedAt = now,
            Status = CaptureStatus.Processing,
            // Temporary (5b testing): wait before reading so the live list can be seen. Setting Ai:Worker:ReadDelaySeconds.
            AiNextTryAt = ReadDelay() is { } wait ? now + wait : null,
            Images =
            [
                new CaptureImage
                {
                    CompanyId = companyId,
                    FilePath = photoPath,
                    ThumbPath = thumbPath,
                    Width = width,
                    Height = height,
                    Bytes = upload.Photo.Length,
                    Sha256 = Convert.ToHexStringLower(SHA256.HashData(upload.Photo)),
                    SortOrder = 0,
                    Rotation = upload.Rotation,
                    RotatedBy = upload.Rotation == 0 ? null : upload.Source == ImageSource.Office ? RotatedBy.Office : RotatedBy.Driver,
                    Source = upload.Source,
                    Latitude = upload.Latitude,
                    Longitude = upload.Longitude,
                    Accuracy = upload.Accuracy,
                    LocationTime = upload.LocationTimeUtc,
                },
            ],
        };
        db.Captures.Add(capture);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Two copies of the same photo arrived at once: keep the first, remove this one's files.
            files.Delete(photoPath);
            if (thumbPath is not null)
                files.Delete(thumbPath);
            db.ChangeTracker.Clear();
            if (await FindAsync(upload.ClientKey) is int first)
                return new CaptureSaveResult(true, first, Duplicate: true);
            log.LogError(ex, "Saving capture {Key} failed", upload.ClientKey);
            throw;
        }

        if (ReadDelay() is { } delay)
            _ = Task.Delay(delay + TimeSpan.FromSeconds(1)).ContinueWith(_ => aiQueue.Wake(), TaskScheduler.Default);
        else
            aiQueue.Wake(); // Part 5a2b2: start reading at once
        // Part 5b: open lists show "1 new slip".
        var driverUserId = driverId is int d
            ? await db.Drivers.IgnoreQueryFilters().Where(x => x.Id == d && x.CompanyId == companyId).Select(x => x.UserId).FirstOrDefaultAsync()
            : null;
        await live.AddedAsync(companyId, capture.Id, userId, driverUserId);
        return new CaptureSaveResult(true, capture.Id);
    }

    /// <summary>Seconds to wait after an upload before the AI reads it (default 0 = at once).</summary>
    private TimeSpan? ReadDelay()
    {
        var seconds = Math.Clamp(config.GetValue("Ai:Worker:ReadDelaySeconds", 0), 0, 600);
        return seconds == 0 ? null : TimeSpan.FromSeconds(seconds);
    }

    private Task<int?> FindAsync(Guid clientKey) =>
        db.Captures.Where(c => c.ClientKey == clientKey).Select(c => (int?)c.Id).FirstOrDefaultAsync();
}
