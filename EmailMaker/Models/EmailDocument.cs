namespace EmailMaker.Models;

/// <summary>The whole email: global settings plus an ordered list of blocks.</summary>
public sealed class EmailDocument
{
    public string Title { get; set; } = "Untitled email";
    /// <summary>Hidden preview text shown by most inboxes next to the subject.</summary>
    public string Preheader { get; set; } = "";
    public string BodyBackground { get; set; } = "#f3f4f6";
    public string ContentBackground { get; set; } = "#ffffff";
    public int ContentWidth { get; set; } = 600;
    public int ContentBorderRadius { get; set; } = 8;
    public string FontFamily { get; set; } = "Arial, Helvetica, sans-serif";
    public List<EmailBlock> Blocks { get; set; } = [];

    /// <summary>Where a block lives: the list that contains it, its index, and its column (null = top level).</summary>
    public readonly record struct BlockLocation(List<EmailBlock> List, int Index, ColumnsBlock? Row, EmailColumn? Column)
    {
        public EmailBlock Block => List[Index];
    }

    /// <summary>Every block in the email, including those inside columns.</summary>
    public IEnumerable<EmailBlock> AllBlocks() =>
        Blocks.SelectMany(b => b is ColumnsBlock row
            ? row.Columns.SelectMany(c => c.Blocks).Prepend(b)
            : [b]);

    public BlockLocation? Find(Guid? id)
    {
        if (id is null) return null;
        for (var i = 0; i < Blocks.Count; i++)
        {
            if (Blocks[i].Id == id) return new BlockLocation(Blocks, i, null, null);
            if (Blocks[i] is not ColumnsBlock row) continue;
            foreach (var col in row.Columns)
            {
                var j = col.Blocks.FindIndex(b => b.Id == id);
                if (j >= 0) return new BlockLocation(col.Blocks, j, row, col);
            }
        }
        return null;
    }

    public (ColumnsBlock Row, EmailColumn Column)? FindColumn(Guid? columnId)
    {
        if (columnId is null) return null;
        foreach (var row in Blocks.OfType<ColumnsBlock>())
            foreach (var col in row.Columns)
                if (col.Id == columnId) return (row, col);
        return null;
    }

    /// <summary>The block list for a column, or the top level when <paramref name="columnId"/> is null.</summary>
    public List<EmailBlock>? ListFor(Guid? columnId) =>
        columnId is null ? Blocks : FindColumn(columnId)?.Column.Blocks;

    public static EmailDocument CreateSample() => new()
    {
        Title = "Welcome email",
        Preheader = "Thanks for signing up — here's what's next.",
        Blocks =
        [
            new HeadingBlock { Text = "Welcome aboard!", Align = HorizontalAlign.Center, PaddingTop = 32 },
            new TextBlock
            {
                Text = "Thanks for joining. We're **really glad** to have you.\n\nClick the button below to get started, or [read the docs](https://example.com/docs).",
                Align = HorizontalAlign.Center
            },
            new ButtonBlock { Text = "Get started", PaddingBottom = 24 },
            new ColumnsBlock
            {
                PaddingBottom = 24,
                Columns =
                [
                    new EmailColumn { WidthPercent = 50, BackgroundColor = "#f9fafb", Blocks =
                    [
                        new HeadingBlock { Text = "Fast setup", Level = 3, FontSize = 18, PaddingLeft = 16, PaddingRight = 16, PaddingTop = 16, PaddingBottom = 4 },
                        new TextBlock { Text = "Be up and running in under five minutes.", FontSize = 14, PaddingLeft = 16, PaddingRight = 16, PaddingTop = 0, PaddingBottom = 16 }
                    ]},
                    new EmailColumn { WidthPercent = 50, BackgroundColor = "#f9fafb", Blocks =
                    [
                        new HeadingBlock { Text = "Friendly support", Level = 3, FontSize = 18, PaddingLeft = 16, PaddingRight = 16, PaddingTop = 16, PaddingBottom = 4 },
                        new TextBlock { Text = "Real people, ready to help whenever you need it.", FontSize = 14, PaddingLeft = 16, PaddingRight = 16, PaddingTop = 0, PaddingBottom = 16 }
                    ]}
                ]
            },
            new DividerBlock(),
            new TextBlock
            {
                Text = "You're receiving this email because you signed up.\n[Unsubscribe](https://example.com/unsubscribe)",
                FontSize = 12, Color = "#9ca3af", LinkColor = "#9ca3af", Align = HorizontalAlign.Center,
                PaddingBottom = 24
            }
        ]
    };
}
