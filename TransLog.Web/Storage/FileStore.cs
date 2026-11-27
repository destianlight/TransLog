namespace TransLog.Web.Storage;

/// <summary>
/// Where uploaded files (slip photos, later driver photos) are kept. Only relative paths go in the database.
/// Behind an interface so the files can move (another disk, cloud storage) without touching the pages.
/// </summary>
public interface IFileStore
{
    /// <summary>Writes the bytes to the relative path, making folders as needed. Replaces an existing file.</summary>
    Task SaveAsync(string path, byte[] bytes, CancellationToken ct = default);

    /// <summary>Opens the file for reading, or null when it does not exist.</summary>
    Stream? OpenRead(string path);

    /// <summary>Deletes the file if it exists.</summary>
    void Delete(string path);

    bool Exists(string path);
}

/// <summary>
/// Files on the server's own disk. The root is the setting <c>Storage:Root</c>, or <c>App_Data/files</c> next to the app.
/// It is outside wwwroot, so files are never served directly: pages hand them out after checking who is asking
/// (Captures/PhotoFiles.cs). IIS also refuses to serve anything under App_Data.
/// Back up this folder together with the database.
/// </summary>
public class LocalFileStore : IFileStore
{
    private readonly string _root;

    public LocalFileStore(IConfiguration config, IWebHostEnvironment env)
    {
        var root = config["Storage:Root"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(root) ? Path.Combine(env.ContentRootPath, "App_Data", "files") : root);
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public async Task SaveAsync(string path, byte[] bytes, CancellationToken ct = default)
    {
        var full = FullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        // Write to a temporary name first, so a half-written file never sits under the real name.
        var temp = full + ".part";
        await File.WriteAllBytesAsync(temp, bytes, ct);
        File.Move(temp, full, overwrite: true);
    }

    public Stream? OpenRead(string path)
    {
        var full = FullPath(path);
        return File.Exists(full) ? new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true) : null;
    }

    public void Delete(string path)
    {
        var full = FullPath(path);
        if (File.Exists(full))
            File.Delete(full);
    }

    public bool Exists(string path) => File.Exists(FullPath(path));

    /// <summary>Turns a stored relative path ("captures/1/2026/11/x.jpg") into a full path, refusing anything that leaves the root.</summary>
    private string FullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(".."))
            throw new ArgumentException("Bad file path.", nameof(path));
        var full = Path.GetFullPath(Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Bad file path.", nameof(path));
        return full;
    }
}
