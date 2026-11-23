using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EmailMaker.Models;

namespace EmailMaker.Services;

public enum ImageMode
{
    /// <summary>Relative /uploads/... URLs. Used inside the designer.</summary>
    Preview,
    /// <summary>Images embedded as base64 data URIs. Self-contained, but Gmail and Outlook.com block these.</summary>
    Embedded,
    /// <summary>Absolute URLs pointing at where this app (or your CDN) hosts the uploads. Best for real sending.</summary>
    Hosted,
    /// <summary>Relative images/... paths, packaged in a ZIP next to index.html.</summary>
    Zip
}

/// <summary>
/// Turns an <see cref="EmailDocument"/> into email-client-friendly HTML:
/// table layout, inline styles, Outlook conditionals, and a mobile media query.
/// </summary>
public sealed partial class EmailHtmlRenderer(ImageStore images)
{
    public sealed record RenderResult(string Html, IReadOnlyCollection<string> ImageIds);

    public async Task<RenderResult> RenderDocumentAsync(EmailDocument doc, ImageMode mode, string? baseUrl = null)
    {
        var usedIds = doc.AllBlocks().Select(GetImageId).OfType<string>().Distinct().ToList();
        var resolve = await CreateResolverAsync(usedIds, mode, baseUrl);

        var bg = Color(doc.BodyBackground, "#f3f4f6");
        var contentBg = Color(doc.ContentBackground, "#ffffff");
        var width = Math.Clamp(doc.ContentWidth, 320, 900);
        var font = FontFamily(doc.FontFamily);

        var sb = new StringBuilder();
        sb.Append($$"""
            <!DOCTYPE html>
            <html lang="en" xmlns="http://www.w3.org/1999/xhtml" xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="x-apple-disable-message-reformatting">
            <meta name="format-detection" content="telephone=no,address=no,email=no,date=no,url=no">
            <title>{{Enc(doc.Title)}}</title>
            <!--[if mso]><noscript><xml><o:OfficeDocumentSettings><o:PixelsPerInch>96</o:PixelsPerInch></o:OfficeDocumentSettings></xml></noscript><![endif]-->
            <style>
              body { margin:0; padding:0; width:100% !important; -webkit-text-size-adjust:100%; -ms-text-size-adjust:100%; }
              table { border-collapse:collapse; mso-table-lspace:0pt; mso-table-rspace:0pt; }
              img { border:0; outline:none; text-decoration:none; -ms-interpolation-mode:bicubic; }
              @media screen and (max-width: {{width + 20}}px) {
                .em-container { width:100% !important; border-radius:0 !important; }
                .em-stack { display:block !important; width:100% !important; max-width:100% !important; padding-left:0 !important; padding-right:0 !important; }
                .em-stack + .em-stack { padding-top:16px !important; }
                .em-fluid { max-width:100% !important; height:auto !important; }
                .em-col { display:block !important; width:100% !important; max-width:100% !important; }
                .em-col-gap { display:block !important; width:100% !important; height:16px !important; line-height:16px !important; }
              }
            </style>
            </head>
            <body style="margin:0; padding:0; background-color:{{bg}};">
            """);
        sb.Append('\n');

        if (!string.IsNullOrWhiteSpace(doc.Preheader))
        {
            // Trailing zero-width filler stops clients pulling body text into the preview line.
            var filler = string.Concat(Enumerable.Repeat("&#8199;&#65279;&#847; ", 60));
            sb.Append($"""<div style="display:none; max-height:0; overflow:hidden; mso-hide:all; font-size:1px; line-height:1px; color:{bg}; opacity:0;">{Enc(doc.Preheader)}{filler}</div>""").Append('\n');
        }

        sb.Append($$"""
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" bgcolor="{{bg}}" style="background-color:{{bg}};">
            <tr><td align="center" style="padding:24px 0;">
            <!--[if mso]><table role="presentation" width="{{width}}" align="center" cellspacing="0" cellpadding="0" border="0"><tr><td><![endif]-->
            <table role="presentation" class="em-container" width="{{width}}" cellspacing="0" cellpadding="0" border="0" bgcolor="{{contentBg}}" style="width:100%; max-width:{{width}}px; background-color:{{contentBg}}; border-radius:{{Math.Max(0, doc.ContentBorderRadius)}}px; overflow:hidden;">
            <tr><td style="font-family:{{font}};">
            """);
        sb.Append('\n');

        foreach (var block in doc.Blocks)
            sb.Append(RenderBlock(block, doc, ContentWidth(doc), resolve, mode)).Append('\n');

        sb.Append("""
            </td></tr>
            </table>
            <!--[if mso]></td></tr></table><![endif]-->
            </td></tr>
            </table>
            </body>
            </html>
            """);

        return new RenderResult(sb.ToString(), usedIds);
    }

    /// <summary>Renders a single block for the designer canvas.</summary>
    /// <param name="availableWidth">Width of the container the block sits in; defaults to the email's content width.</param>
    public string RenderBlockPreview(EmailBlock block, EmailDocument doc, int? availableWidth = null) =>
        RenderBlock(block, doc, availableWidth ?? ContentWidth(doc), id => $"{ImageStore.RequestPath}/{id}", ImageMode.Preview);

    /// <summary>Pixel width of each column's content area (used to size images inside columns).</summary>
    public static int[] ColumnWidths(ColumnsBlock row, int availableWidth)
    {
        var inner = Math.Max(1, availableWidth - row.PaddingLeft - row.PaddingRight);
        var gap = Math.Clamp(row.Gap, 0, 80);
        var space = Math.Max(row.Columns.Count, inner - gap * (row.Columns.Count - 1));
        var total = Math.Max(1, row.Columns.Sum(c => Math.Max(1, c.WidthPercent)));
        return row.Columns.Select(c => Math.Max(1, space * Math.Max(1, c.WidthPercent) / total)).ToArray();
    }

    public static int ContentWidth(EmailDocument doc) => Math.Clamp(doc.ContentWidth, 320, 900);

    private async Task<Func<string, string>> CreateResolverAsync(IEnumerable<string> ids, ImageMode mode, string? baseUrl)
    {
        switch (mode)
        {
            case ImageMode.Embedded:
                var dataUris = new Dictionary<string, string>();
                foreach (var id in ids)
                {
                    var img = await images.ReadAsync(id);
                    if (img is { } i) dataUris[id] = $"data:{i.ContentType};base64,{Convert.ToBase64String(i.Bytes)}";
                }
                return id => dataUris.GetValueOrDefault(id, "");
            case ImageMode.Hosted:
                var root = (baseUrl ?? "").TrimEnd('/');
                return id => $"{root}{ImageStore.RequestPath}/{id}";
            case ImageMode.Zip:
                return id => $"images/{id}";
            default:
                return id => $"{ImageStore.RequestPath}/{id}";
        }
    }

    private static string? GetImageId(EmailBlock b) => b switch
    {
        ImageBlock { ImageId: { Length: > 0 } id } => id,
        ImageTextBlock { ImageId: { Length: > 0 } id } => id,
        _ => null
    };

    private static string ResolveSrc(string? imageId, string externalUrl, Func<string, string> resolve) =>
        !string.IsNullOrEmpty(imageId) ? resolve(imageId) : SafeUrl(externalUrl, allowEmpty: true);

    // ---------------------------------------------------------------- blocks

    private static string RenderBlock(EmailBlock block, EmailDocument doc, int availableWidth, Func<string, string> resolve, ImageMode mode)
    {
        var font = FontFamily(doc.FontFamily);
        var innerWidth = Math.Max(1, availableWidth - block.PaddingLeft - block.PaddingRight);

        string content;
        string align = "left";
        switch (block)
        {
            case HeadingBlock h:
                align = Align(h.Align);
                var level = Math.Clamp(h.Level, 1, 3);
                content = $"""<h{level} style="margin:0; font-family:{font}; font-size:{Px(h.FontSize)}; line-height:1.25; color:{Color(h.Color, "#111827")}; font-weight:{(h.Bold ? "bold" : "normal")}; text-align:{align};">{Enc(h.Text).ReplaceLineEndings("<br>")}</h{level}>""";
                break;

            case TextBlock t:
                align = Align(t.Align);
                content = RenderParagraphs(t.Text, font, t.FontSize, t.LineHeight, Color(t.Color, "#374151"), Color(t.LinkColor, "#2563eb"), align);
                break;

            case ImageBlock img:
            {
                align = Align(img.Align);
                var src = ResolveSrc(img.ImageId, img.ExternalUrl, resolve);
                if (string.IsNullOrEmpty(src))
                {
                    if (mode != ImageMode.Preview) return "";
                    content = Placeholder("Upload an image or enter an image URL");
                    break;
                }
                var px = Math.Max(1, innerWidth * Math.Clamp(img.WidthPercent, 5, 100) / 100);
                var margin = img.Align switch { HorizontalAlign.Left => "0 auto 0 0", HorizontalAlign.Right => "0 0 0 auto", _ => "0 auto" };
                var tag = $"""<img src="{Enc(src)}" alt="{Enc(img.AltText)}" width="{px}" class="em-fluid" style="display:block; width:100%; max-width:{px}px; height:auto; margin:{margin}; border:0; border-radius:{Math.Max(0, img.BorderRadius)}px;">""";
                content = string.IsNullOrWhiteSpace(img.LinkUrl)
                    ? tag
                    : $"""<a href="{Enc(SafeUrl(img.LinkUrl))}" target="_blank" style="text-decoration:none;">{tag}</a>""";
                break;
            }

            case ButtonBlock btn:
            {
                align = Align(btn.Align);
                var bg = Color(btn.BackgroundColorButton, "#2563eb");
                var fg = Color(btn.TextColor, "#ffffff");
                var radius = Math.Max(0, btn.BorderRadius);
                var widthAttr = btn.FullWidth ? " width=\"100%\"" : "";
                var display = btn.FullWidth ? "block" : "inline-block";
                content = $"""
                    <table role="presentation" cellspacing="0" cellpadding="0" border="0" align="{align}"{widthAttr} style="margin:{(btn.Align == HorizontalAlign.Center ? "0 auto" : "0")};">
                    <tr><td align="center" bgcolor="{bg}" style="border-radius:{radius}px; background-color:{bg};">
                    <a href="{Enc(SafeUrl(btn.Url))}" target="_blank" style="display:{display}; padding:{Px(btn.PaddingVertical)} {Px(btn.PaddingHorizontal)}; font-family:{font}; font-size:{Px(btn.FontSize)}; line-height:1.2; font-weight:bold; color:{fg}; text-decoration:none; text-align:center; border-radius:{radius}px; mso-padding-alt:0;">
                    <!--[if mso]><i style="mso-font-width:150%; mso-text-raise:{btn.PaddingVertical * 2}px;">&#8202;</i><![endif]-->{Enc(btn.Text)}<!--[if mso]><i style="mso-font-width:150%;">&#8202;</i><![endif]-->
                    </a></td></tr></table>
                    """;
                break;
            }

            case DividerBlock d:
                align = "center";
                content = $"""<table role="presentation" width="{Math.Clamp(d.WidthPercent, 5, 100)}%" align="center" cellspacing="0" cellpadding="0" border="0"><tr><td style="border-top:{Math.Clamp(d.Thickness, 1, 20)}px solid {Color(d.Color, "#e5e7eb")}; font-size:0; line-height:0; height:0;">&nbsp;</td></tr></table>""";
                break;

            case SpacerBlock s:
                var h2 = Math.Clamp(s.Height, 1, 400);
                content = $"""<div style="height:{h2}px; line-height:{h2}px; font-size:1px;">&nbsp;</div>""";
                break;

            case ImageTextBlock it:
            {
                var src = ResolveSrc(it.ImageId, it.ExternalUrl, resolve);
                var colWidth = (innerWidth - 16) / 2;
                var imageHtml = string.IsNullOrEmpty(src)
                    ? (mode == ImageMode.Preview ? Placeholder("Image") : "")
                    : $"""<img src="{Enc(src)}" alt="{Enc(it.AltText)}" width="{colWidth}" class="em-fluid" style="display:block; width:100%; max-width:{colWidth}px; height:auto; border:0;">""";
                var textHtml =
                    $"""<h3 style="margin:0 0 8px 0; font-family:{font}; font-size:{Px(it.FontSize + 5)}; line-height:1.3; color:{Color(it.TitleColor, "#111827")};">{Enc(it.Title)}</h3>""" +
                    RenderParagraphs(it.Text, font, it.FontSize, 1.6, Color(it.Color, "#374151"), Color(it.LinkColor, "#2563eb"), "left");

                // dir="rtl" swaps visual order while keeping the image first when columns stack on mobile.
                var dir = it.ImageOnRight ? " dir=\"rtl\"" : "";
                var (padImg, padText) = it.ImageOnRight ? ("padding-left:8px;", "padding-right:8px;") : ("padding-right:8px;", "padding-left:8px;");
                content = $"""
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0"{dir}>
                    <tr>
                    <td class="em-stack" width="50%" valign="top" dir="ltr" style="width:50%; {padImg}">{imageHtml}</td>
                    <td class="em-stack" width="50%" valign="middle" dir="ltr" style="width:50%; {padText} text-align:left;">{textHtml}</td>
                    </tr></table>
                    """;
                break;
            }

            case ColumnsBlock row:
                content = RenderColumns(row, doc, availableWidth, resolve, mode);
                if (content.Length == 0) return "";
                break;

            case HtmlBlock html:
                content = html.Html;
                break;

            default:
                return "";
        }

        var rowBg = string.IsNullOrWhiteSpace(block.BackgroundColor) ? "" : Color(block.BackgroundColor, "");
        var bgAttr = rowBg.Length > 0 ? $" bgcolor=\"{rowBg}\"" : "";
        var bgStyle = rowBg.Length > 0 ? $" background-color:{rowBg};" : "";
        return $"""<table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0"><tr><td align="{align}"{bgAttr} style="padding:{Px(block.PaddingTop)} {Px(block.PaddingRight)} {Px(block.PaddingBottom)} {Px(block.PaddingLeft)};{bgStyle} text-align:{align};">{content}</td></tr></table>""";
    }

    private static string RenderColumns(ColumnsBlock row, EmailDocument doc, int availableWidth, Func<string, string> resolve, ImageMode mode)
    {
        // An empty row is only useful in the designer.
        if (mode != ImageMode.Preview && row.Columns.All(c => c.Blocks.Count == 0)) return "";

        var widths = ColumnWidths(row, availableWidth);
        var inner = Math.Max(1, availableWidth - row.PaddingLeft - row.PaddingRight);
        var gap = Math.Clamp(row.Gap, 0, 80);
        var valign = row.VerticalAlign switch { VerticalAlign.Middle => "middle", VerticalAlign.Bottom => "bottom", _ => "top" };
        var colClass = row.StackOnMobile ? " class=\"em-col\"" : "";
        var gapClass = row.StackOnMobile ? " class=\"em-col-gap\"" : "";

        // Percent widths keep the row fluid on screens narrower than the template;
        // the pixel width attributes are what desktop Outlook uses.
        static string Pct(int px, int of) => (px * 100.0 / of).ToString("0.##", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\"><tr>");
        for (var i = 0; i < row.Columns.Count; i++)
        {
            var col = row.Columns[i];
            if (i > 0 && gap > 0)
                sb.Append($"<td{gapClass} width=\"{gap}\" style=\"width:{Pct(gap, inner)}%; font-size:0; line-height:0;\">&nbsp;</td>");

            var bg = string.IsNullOrWhiteSpace(col.BackgroundColor) ? "" : Color(col.BackgroundColor, "");
            var bgAttr = bg.Length > 0 ? $" bgcolor=\"{bg}\"" : "";
            var bgStyle = bg.Length > 0 ? $" background-color:{bg};" : "";

            sb.Append($"<td{colClass} width=\"{widths[i]}\" valign=\"{valign}\"{bgAttr} style=\"width:{Pct(widths[i], inner)}%; vertical-align:{valign};{bgStyle}\">");
            foreach (var child in col.Blocks)
            {
                if (child is ColumnsBlock) continue; // columns can't be nested
                sb.Append(RenderBlock(child, doc, widths[i], resolve, mode));
            }
            if (col.Blocks.Count == 0) sb.Append("&nbsp;");
            sb.Append("</td>");
        }
        sb.Append("</tr></table>");
        return sb.ToString();
    }

    private static string Placeholder(string label) =>
        $"""<div style="background:#e5e7eb; color:#6b7280; border:2px dashed #9ca3af; border-radius:6px; padding:40px 12px; text-align:center; font:14px Arial, sans-serif;">{Enc(label)}</div>""";

    // ---------------------------------------------------------------- text

    private static string RenderParagraphs(string text, string font, int fontSize, double lineHeight, string color, string linkColor, string align)
    {
        var paragraphs = Regex.Split((text ?? "").ReplaceLineEndings("\n").Trim(), @"\n\s*\n");
        var sb = new StringBuilder();
        for (var i = 0; i < paragraphs.Length; i++)
        {
            var margin = i == paragraphs.Length - 1 ? 0 : fontSize;
            sb.Append($"""<p style="margin:0 0 {margin}px 0; font-family:{font}; font-size:{Px(fontSize)}; line-height:{Math.Clamp(lineHeight, 1, 3).ToString("0.##", CultureInfo.InvariantCulture)}; color:{color}; text-align:{align};">""")
              .Append(InlineMarkup(paragraphs[i], linkColor))
              .Append("</p>");
        }
        return sb.ToString();
    }

    /// <summary>Encodes text, then applies **bold**, *italic* and [label](url).</summary>
    private static string InlineMarkup(string text, string linkColor)
    {
        var s = Enc(text);
        s = LinkRegex().Replace(s, m =>
        {
            var url = WebUtility.HtmlDecode(m.Groups[2].Value);
            return $"""<a href="{Enc(SafeUrl(url))}" target="_blank" style="color:{linkColor}; text-decoration:underline;">{m.Groups[1].Value}</a>""";
        });
        s = BoldRegex().Replace(s, "<strong>$1</strong>");
        s = ItalicRegex().Replace(s, "<em>$1</em>");
        return s.Replace("\n", "<br>");
    }

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")] private static partial Regex LinkRegex();
    [GeneratedRegex(@"\*\*(.+?)\*\*")] private static partial Regex BoldRegex();
    [GeneratedRegex(@"(?<![*\w])\*(?!\s)(.+?)(?<!\s)\*(?![*\w])")] private static partial Regex ItalicRegex();

    // ---------------------------------------------------------------- sanitising helpers

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string Px(int v) => $"{Math.Clamp(v, 0, 400)}px";

    private static string Align(HorizontalAlign a) => a switch
    {
        HorizontalAlign.Center => "center",
        HorizontalAlign.Right => "right",
        _ => "left"
    };

    [GeneratedRegex(@"^(#[0-9a-fA-F]{3,8}|[a-zA-Z]{3,20}|rgba?\(\s*[\d.\s,%]+\))$")] private static partial Regex ColorRegex();
    private static string Color(string? value, string fallback) =>
        value is not null && ColorRegex().IsMatch(value.Trim()) ? value.Trim() : fallback;

    [GeneratedRegex(@"^[\w\s,'""-]+$")] private static partial Regex FontRegex();
    private static string FontFamily(string? value) =>
        value is not null && FontRegex().IsMatch(value) ? value.Replace("\"", "'") : "Arial, Helvetica, sans-serif";

    /// <summary>
    /// A link that starts with a placeholder: {verificationLink}, {{unsubscribe_url}}, {baseUrl}/verify?t={token}.
    /// The rest may not contain whitespace, quotes or angle brackets.
    /// </summary>
    [GeneratedRegex(@"^\{\{?\s*[A-Za-z_][\w.\-]*\s*\}\}?[^\s""'<>]*$")] private static partial Regex PlaceholderUrlRegex();

    /// <summary>Allows http(s), mailto, tel, anchors, data: images and placeholders like {verificationLink} or {{unsubscribe_url}}.</summary>
    private static string SafeUrl(string? url, bool allowEmpty = false)
    {
        url = url?.Trim() ?? "";
        if (url.Length == 0) return allowEmpty ? "" : "#";
        if (url.StartsWith('#') || url.StartsWith('/') || PlaceholderUrlRegex().IsMatch(url)) return url;
        if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && !url.Contains("svg", StringComparison.OrdinalIgnoreCase)) return url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" or "mailto" or "tel")
            return url;
        return allowEmpty ? "" : "#";
    }
}
