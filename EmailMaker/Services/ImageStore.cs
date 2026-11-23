namespace EmailMaker.Services;

/// <summary>
/// Stores uploaded images on disk (App_Data/uploads) and serves them at /uploads/{id}.
/// The id is a random file name, so it's safe to put in a URL and can't escape the folder.
/// </summary>
public sealed class ImageStore
{
    public const long MaxFileSize = 10 * 1024 * 1024; // 10 MB
    public const string RequestPath = "/uploads";

    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
    };

    private static readonly Dictionary<string, string> ContentTypesByExtension =
        AllowedTypes.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    public string RootPath { get; }

    public ImageStore(IWebHostEnvironment env)
    {
        RootPath = Path.Combine(env.ContentRootPath, "App_Data", "uploads");
        Directory.CreateDirectory(RootPath);
    }

    public static string AcceptAttribute => string.Join(",", AllowedTypes.Keys);

    public static bool IsAllowed(string contentType) => AllowedTypes.ContainsKey(contentType);

    /// <summary>Saves the stream and returns the new image id (its file name).</summary>
    public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct = default)
    {
        if (!AllowedTypes.TryGetValue(contentType, out var ext))
            throw new InvalidOperationException($"Unsupported image type '{contentType}'. Use PNG, JPEG, GIF or WebP.");

        // Buffer so we can verify the file really is the image type it claims to be.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxFileSize)
            throw new InvalidOperationException("Image is larger than 10 MB.");
        if (!HasValidSignature(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), ext))
            throw new InvalidOperationException("The file doesn't look like a valid image.");

        var id = $"{Guid.NewGuid():N}{ext}";
        buffer.Position = 0;
        await using var file = File.Create(Path.Combine(RootPath, id));
        await buffer.CopyToAsync(file, ct);
        return id;
    }

    /// <summary>Resolves an id to a file path, rejecting anything that isn't a plain stored file name.</summary>
    public bool TryGet(string? id, out string path, out string contentType)
    {
        path = ""; contentType = "";
        if (string.IsNullOrWhiteSpace(id) || id != Path.GetFileName(id)) return false;
        if (!ContentTypesByExtension.TryGetValue(Path.GetExtension(id), out var type)) return false;

        var full = Path.Combine(RootPath, id);
        if (!File.Exists(full)) return false;
        path = full; contentType = type;
        return true;
    }

    public async Task<(byte[] Bytes, string ContentType)?> ReadAsync(string? id, CancellationToken ct = default)
    {
        if (!TryGet(id, out var path, out var type)) return null;
        return (await File.ReadAllBytesAsync(path, ct), type);
    }

    private static bool HasValidSignature(ReadOnlySpan<byte> data, string ext) => ext switch
    {
        ".png" => data.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        ".jpg" => data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]),
        ".gif" => data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8),
        ".webp" => data.Length > 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8),
        _ => false
    };
}
