using System.Text.Json.Serialization;

namespace EmailMaker.Models;

public enum VerticalAlign { Top, Middle, Bottom }

/// <summary>One column inside a <see cref="ColumnsBlock"/>. Holds its own list of blocks.</summary>
public sealed class EmailColumn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Share of the row's width. Columns are scaled relative to each other, so they don't have to sum to 100.</summary>
    public int WidthPercent { get; set; } = 50;
    public string BackgroundColor { get; set; } = "";
    public List<EmailBlock> Blocks { get; set; } = [];
}

/// <summary>
/// A row of 2–4 columns, each containing regular blocks. Columns can't be nested.
/// On phones the columns stack vertically (optional).
/// </summary>
public sealed class ColumnsBlock : EmailBlock
{
    public const int MinColumns = 2;
    public const int MaxColumns = 4;

    public static readonly (string Name, int[] Widths)[] Presets =
    [
        ("50 / 50", [50, 50]),
        ("33 / 67", [33, 67]),
        ("67 / 33", [67, 33]),
        ("25 / 75", [25, 75]),
        ("75 / 25", [75, 25]),
        ("33 / 33 / 33", [33, 34, 33]),
        ("25 / 50 / 25", [25, 50, 25]),
        ("25 × 4", [25, 25, 25, 25]),
    ];

    public List<EmailColumn> Columns { get; set; } = [new() { WidthPercent = 50 }, new() { WidthPercent = 50 }];
    /// <summary>Horizontal space between columns, in px.</summary>
    public int Gap { get; set; } = 16;
    public bool StackOnMobile { get; set; } = true;
    public VerticalAlign VerticalAlign { get; set; } = VerticalAlign.Top;

    [JsonIgnore] public override string DisplayName => "Columns";
    [JsonIgnore] public override string Icon => "▥";

    public static ColumnsBlock Create(params int[] widths)
    {
        var block = new ColumnsBlock();
        block.ApplyLayout(widths);
        return block;
    }

    /// <summary>
    /// Changes the number/width of columns. Content of removed columns is moved into the
    /// last remaining column so nothing is lost.
    /// </summary>
    public void ApplyLayout(int[] widths)
    {
        var count = Math.Clamp(widths.Length, MinColumns, MaxColumns);
        while (Columns.Count < count) Columns.Add(new EmailColumn());
        while (Columns.Count > count)
        {
            var removed = Columns[^1];
            Columns.RemoveAt(Columns.Count - 1);
            Columns[^1].Blocks.AddRange(removed.Blocks);
        }
        for (var i = 0; i < count; i++)
            Columns[i].WidthPercent = i < widths.Length ? Math.Max(1, widths[i]) : 100 / count;
    }

    public bool MatchesPreset(int[] widths) =>
        widths.Length == Columns.Count && Columns.Select(c => c.WidthPercent).SequenceEqual(widths);

    /// <summary>Deep copy: new ids for the row, its columns and every child block.</summary>
    public override EmailBlock Clone()
    {
        var copy = (ColumnsBlock)base.Clone();
        copy.Columns = Columns.Select(c => new EmailColumn
        {
            WidthPercent = c.WidthPercent,
            BackgroundColor = c.BackgroundColor,
            Blocks = c.Blocks.Select(b => b.Clone()).ToList()
        }).ToList();
        return copy;
    }
}
