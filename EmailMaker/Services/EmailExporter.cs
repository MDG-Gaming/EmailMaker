using System.IO.Compression;
using System.Text;
using System.Text.Json;
using EmailMaker.Models;

namespace EmailMaker.Services;

/// <summary>Produces downloadable files: HTML, a ZIP with images, and the editable JSON design.</summary>
public sealed class EmailExporter(EmailHtmlRenderer renderer, ImageStore images)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public async Task<byte[]> ExportHtmlAsync(EmailDocument doc, ImageMode mode, string? baseUrl)
    {
        var result = await renderer.RenderDocumentAsync(doc, mode, baseUrl);
        return Encoding.UTF8.GetBytes(result.Html);
    }

    /// <summary>index.html + images/ folder, ready to upload to an ESP or web server.</summary>
    public async Task<byte[]> ExportZipAsync(EmailDocument doc)
    {
        var result = await renderer.RenderDocumentAsync(doc, ImageMode.Zip);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("index.html", CompressionLevel.Optimal);
            await using (var s = entry.Open())
                await s.WriteAsync(Encoding.UTF8.GetBytes(result.Html));

            foreach (var id in result.ImageIds)
            {
                if (!images.TryGet(id, out var path, out _)) continue;
                zip.CreateEntryFromFile(path, $"images/{id}", CompressionLevel.NoCompression);
            }
        }
        return ms.ToArray();
    }

    public static byte[] SerializeDesign(EmailDocument doc) =>
        JsonSerializer.SerializeToUtf8Bytes(doc, JsonOptions);

    public static EmailDocument DeserializeDesign(Stream json) =>
        JsonSerializer.Deserialize<EmailDocument>(json, JsonOptions)
        ?? throw new InvalidOperationException("The design file is empty.");

    public static string SafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string((title ?? "").Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : char.ToLowerInvariant(c)).ToArray()).Trim('-');
        return string.IsNullOrEmpty(name) ? "email" : name;
    }
}
