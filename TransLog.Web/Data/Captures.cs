using System.ComponentModel.DataAnnotations;

namespace TransLog.Web.Data;

/// <summary>Where a capture is in its life. Stored as a number; never reorder.</summary>
public enum CaptureStatus
{
    Uploading = 0,

    /// <summary>Received and waiting for the AI to read it (Part 5).</summary>
    Processing = 1,

    NeedsReview = 2,
    Approved = 3,
    Rejected = 4,
    Failed = 5,
}

/// <summary>How a photo got into TransLog. Stored as a number; never reorder.</summary>
public enum ImageSource
{
    /// <summary>Taken with the TransLog camera page or the phone's own camera.</summary>
    Camera = 0,

    /// <summary>Picked from the phone's gallery (could be an older photo).</summary>
    Gallery = 1,

    /// <summary>Added by an office user (Part 4b).</summary>
    Office = 2,
}

/// <summary>Who turned the photo. Stored as a number; never reorder.</summary>
public enum RotatedBy
{
    Driver = 0,
    Ai = 1,
    Reviewer = 2,

    /// <summary>Turned by the office user before uploading (Part 4b).</summary>
    Office = 3,
}

/// <summary>
/// One scan: a slip, ticket, odometer or receipt. One generic table for every type (Part 4a holds only the capture side;
/// the type, AI and review columns come in Parts 5 and 6). Never deleted.
/// </summary>
public class Capture
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>
    /// Made on the phone for each photo. A retry or a double tap sends the same key, so the same photo is never stored twice.
    /// Unique per company.
    /// </summary>
    public Guid ClientKey { get; set; }

    /// <summary>The driver's linked vehicle when the photo was sent; empty when they had none (the reviewer sets it).</summary>
    public int? VehicleId { get; set; }

    public Vehicle? Vehicle { get; set; }

    public int? DriverId { get; set; }

    public Driver? Driver { get; set; }

    /// <summary>The login that sent it (the driver, or an office user: Part 4b office capture).</summary>
    public int CapturedByUserId { get; set; }

    /// <summary>UTC. When the photo was taken, by the phone's clock (a photo can wait on the phone without signal).</summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>UTC. When the server received it.</summary>
    public DateTime ReceivedAt { get; set; }

    public CaptureStatus Status { get; set; } = CaptureStatus.Processing;

    public List<CaptureImage> Images { get; set; } = [];
}

/// <summary>
/// A photo of a capture. The files live on disk (see Storage/FileStore.cs); only their paths are stored here.
/// One photo per capture for now; SortOrder allows more pages later.
/// </summary>
public class CaptureImage
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int CaptureId { get; set; }

    public Capture? Capture { get; set; }

    /// <summary>Relative path under the file store, for example "captures/12/2026/11/abc.jpg".</summary>
    [MaxLength(200)]
    public string FilePath { get; set; } = "";

    /// <summary>Small copy (about 320 px) made by the phone, for lists.</summary>
    [MaxLength(200)]
    public string? ThumbPath { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public int Bytes { get; set; }

    /// <summary>SHA-256 of the stored file, 64 hex characters (finds the same photo sent twice).</summary>
    [MaxLength(64)]
    public string Sha256 { get; set; } = "";

    public int SortOrder { get; set; }

    /// <summary>0, 90, 180 or 270 degrees clockwise, already applied to the stored file.</summary>
    public int Rotation { get; set; }

    public RotatedBy? RotatedBy { get; set; }

    public ImageSource Source { get; set; }

    /// <summary>Location fix from the phone when the photo was taken; empty when the driver said no or there was no fix.</summary>
    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    /// <summary>Metres.</summary>
    public int? Accuracy { get; set; }

    /// <summary>UTC. When the phone got the fix.</summary>
    public DateTime? LocationTime { get; set; }
}
