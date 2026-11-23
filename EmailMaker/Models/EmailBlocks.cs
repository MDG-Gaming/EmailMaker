using System.Text.Json.Serialization;

namespace EmailMaker.Models;

public enum HorizontalAlign { Left, Center, Right }

/// <summary>Base class for every block that can be placed in an email.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(HeadingBlock), "heading")]
[JsonDerivedType(typeof(TextBlock), "text")]
[JsonDerivedType(typeof(ImageBlock), "image")]
[JsonDerivedType(typeof(ButtonBlock), "button")]
[JsonDerivedType(typeof(DividerBlock), "divider")]
[JsonDerivedType(typeof(SpacerBlock), "spacer")]
[JsonDerivedType(typeof(ImageTextBlock), "imageText")]
[JsonDerivedType(typeof(HtmlBlock), "html")]
[JsonDerivedType(typeof(ColumnsBlock), "columns")]
public abstract class EmailBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Background behind this block's row. Empty = transparent (uses content background).</summary>
    public string BackgroundColor { get; set; } = "";
    public int PaddingTop { get; set; } = 12;
    public int PaddingBottom { get; set; } = 12;
    public int PaddingLeft { get; set; } = 24;
    public int PaddingRight { get; set; } = 24;

    [JsonIgnore] public abstract string DisplayName { get; }
    [JsonIgnore] public abstract string Icon { get; }

    /// <summary>Creates a deep copy with a fresh Id.</summary>
    public virtual EmailBlock Clone()
    {
        var copy = (EmailBlock)MemberwiseClone();
        copy.Id = Guid.NewGuid();
        return copy;
    }

    /// <summary>Removes the default side padding so the block fits snugly inside a column.</summary>
    public void FitForColumn()
    {
        PaddingLeft = 0;
        PaddingRight = 0;
        PaddingTop = Math.Min(PaddingTop, 8);
        PaddingBottom = Math.Min(PaddingBottom, 8);
    }
}

public sealed class HeadingBlock : EmailBlock
{
    public string Text { get; set; } = "Your heading here";
    public int Level { get; set; } = 1;
    public int FontSize { get; set; } = 28;
    public string Color { get; set; } = "#111827";
    public bool Bold { get; set; } = true;
    public HorizontalAlign Align { get; set; } = HorizontalAlign.Left;

    [JsonIgnore] public override string DisplayName => "Heading";
    [JsonIgnore] public override string Icon => "H";
}

public sealed class TextBlock : EmailBlock
{
    /// <summary>
    /// Plain text. Supports a tiny markup subset: **bold**, *italic*, [label](https://url).
    /// Blank lines start a new paragraph, single newlines become line breaks.
    /// </summary>
    public string Text { get; set; } =
        "Write your message here. You can use **bold**, *italic* and [links](https://example.com).";
    public int FontSize { get; set; } = 16;
    public double LineHeight { get; set; } = 1.6;
    public string Color { get; set; } = "#374151";
    public string LinkColor { get; set; } = "#2563eb";
    public HorizontalAlign Align { get; set; } = HorizontalAlign.Left;

    [JsonIgnore] public override string DisplayName => "Text";
    [JsonIgnore] public override string Icon => "¶";
}

public sealed class ImageBlock : EmailBlock
{
    /// <summary>File name of an uploaded image (see <see cref="Services.ImageStore"/>).</summary>
    public string? ImageId { get; set; }
    /// <summary>Used when no uploaded image is set.</summary>
    public string ExternalUrl { get; set; } = "";
    public string AltText { get; set; } = "";
    public string LinkUrl { get; set; } = "";
    /// <summary>Width as a percentage of the available content width.</summary>
    public int WidthPercent { get; set; } = 100;
    public int BorderRadius { get; set; }
    public HorizontalAlign Align { get; set; } = HorizontalAlign.Center;

    [JsonIgnore] public override string DisplayName => "Image";
    [JsonIgnore] public override string Icon => "🖼";
}

public sealed class ButtonBlock : EmailBlock
{
    public string Text { get; set; } = "Click me";
    public string Url { get; set; } = "https://example.com";
    public string BackgroundColorButton { get; set; } = "#2563eb";
    public string TextColor { get; set; } = "#ffffff";
    public int FontSize { get; set; } = 16;
    public int BorderRadius { get; set; } = 6;
    public int PaddingVertical { get; set; } = 12;
    public int PaddingHorizontal { get; set; } = 28;
    public bool FullWidth { get; set; }
    public HorizontalAlign Align { get; set; } = HorizontalAlign.Center;

    [JsonIgnore] public override string DisplayName => "Button";
    [JsonIgnore] public override string Icon => "▭";
}

public sealed class DividerBlock : EmailBlock
{
    public string Color { get; set; } = "#e5e7eb";
    public int Thickness { get; set; } = 1;
    public int WidthPercent { get; set; } = 100;

    [JsonIgnore] public override string DisplayName => "Divider";
    [JsonIgnore] public override string Icon => "—";
}

public sealed class SpacerBlock : EmailBlock
{
    public int Height { get; set; } = 32;

    public SpacerBlock() { PaddingTop = 0; PaddingBottom = 0; }

    [JsonIgnore] public override string DisplayName => "Spacer";
    [JsonIgnore] public override string Icon => "↕";
}

/// <summary>Two-column row: image on one side, heading + text on the other. Stacks on mobile.</summary>
public sealed class ImageTextBlock : EmailBlock
{
    public string? ImageId { get; set; }
    public string ExternalUrl { get; set; } = "";
    public string AltText { get; set; } = "";
    public bool ImageOnRight { get; set; }
    public string Title { get; set; } = "Feature title";
    public string Text { get; set; } = "Describe the feature or product in a sentence or two.";
    public string TitleColor { get; set; } = "#111827";
    public string Color { get; set; } = "#374151";
    public string LinkColor { get; set; } = "#2563eb";
    public int FontSize { get; set; } = 15;

    [JsonIgnore] public override string DisplayName => "Image + text";
    [JsonIgnore] public override string Icon => "◧";
}

/// <summary>Raw HTML, inserted as-is. For advanced users.</summary>
public sealed class HtmlBlock : EmailBlock
{
    public string Html { get; set; } = "<p style=\"margin:0\">Custom <strong>HTML</strong></p>";

    [JsonIgnore] public override string DisplayName => "Custom HTML";
    [JsonIgnore] public override string Icon => "</>";
}
