using XPXel.Core.Primitives;

namespace XPXel.Formats.Animation;

/// <summary>Where one frame sits on a sprite sheet.</summary>
/// <param name="ClipIndex">Which animation the frame belongs to.</param>
/// <param name="FrameIndex">The frame's position within that animation.</param>
/// <param name="Bounds">The cell's rectangle on the sheet, in sheet pixels.</param>
public sealed record SheetCell(int ClipIndex, int FrameIndex, ImageRect Bounds);

/// <summary>
/// The arrangement of every frame on a sprite sheet, worked out before a single pixel is
/// written.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every cell is the same size.</b> That is the one property a sprite sheet must have to
/// be useful, because it is what lets an engine slice the image by cell size rather than
/// being told each rectangle — Unity's "Grid By Cell Size", Godot's <c>hframes</c> and
/// <c>vframes</c>, and every hand-rolled importer all assume it. Packing frames tightly
/// against their own bounding boxes would save texture space and make the sheet unusable
/// without its metadata.
/// </para>
/// <para>
/// Separated from the exporter so the arithmetic can be tested on its own. A sheet that is
/// one cell too small is a mistake that only shows up as a clipped last frame.
/// </para>
/// </remarks>
public sealed class SpriteSheetPlan
{
    private SpriteSheetPlan(ImageSize sheetSize, ImageSize cellSize, int columns, int rows, IReadOnlyList<SheetCell> cells)
    {
        SheetSize = sheetSize;
        CellSize = cellSize;
        Columns = columns;
        Rows = rows;
        Cells = cells;
    }

    /// <summary>The dimensions of the finished sheet.</summary>
    public ImageSize SheetSize { get; }

    /// <summary>The dimensions of one cell, excluding padding.</summary>
    public ImageSize CellSize { get; }

    /// <summary>How many cells fit across the sheet.</summary>
    public int Columns { get; }

    /// <summary>How many rows of cells the sheet has.</summary>
    public int Rows { get; }

    /// <summary>Every occupied cell, in the order the frames were given.</summary>
    public IReadOnlyList<SheetCell> Cells { get; }

    /// <summary>Works out where each frame of each clip belongs.</summary>
    /// <param name="clips">The animations being written, already rendered.</param>
    /// <param name="layout">How to arrange them.</param>
    /// <param name="padding">Transparent pixels between and around cells.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ImageFormatException">The clips have no frames, or disagree on frame size.</exception>
    public static SpriteSheetPlan Create(
        IReadOnlyList<AnimationClip> clips,
        SpriteSheetLayout layout,
        int padding)
    {
        ArgumentNullException.ThrowIfNull(clips);

        int total = clips.Sum(c => c.Count);
        if (total == 0)
        {
            throw new ImageFormatException("There are no frames to lay out on a sprite sheet.");
        }

        ImageSize cell = clips[0].FrameSize;
        if (clips.Any(c => c.Count > 0 && c.FrameSize != cell))
        {
            // Cannot happen from one document, whose frames are all canvas-sized, but it is
            // the invariant the whole grid depends on and is worth stating rather than
            // producing a silently misaligned sheet.
            throw new ImageFormatException("Every frame on a sprite sheet must be the same size.");
        }

        return layout switch
        {
            SpriteSheetLayout.RowPerAnimation => LayOutRows(clips, cell, padding),
            SpriteSheetLayout.HorizontalStrip => LayOutFlat(clips, cell, padding, total, 1),
            SpriteSheetLayout.VerticalStrip => LayOutFlat(clips, cell, padding, 1, total),
            _ => LayOutGrid(clips, cell, padding, total),
        };
    }

    /// <summary>One row per animation, each row padded out to the longest.</summary>
    private static SpriteSheetPlan LayOutRows(IReadOnlyList<AnimationClip> clips, ImageSize cell, int padding)
    {
        int columns = clips.Max(c => c.Count);
        int rows = clips.Count;

        List<SheetCell> cells = [];

        for (int row = 0; row < clips.Count; row++)
        {
            for (int column = 0; column < clips[row].Count; column++)
            {
                cells.Add(new SheetCell(row, column, CellBounds(column, row, cell, padding)));
            }
        }

        return new SpriteSheetPlan(MeasureSheet(columns, rows, cell, padding), cell, columns, rows, cells);
    }

    /// <summary>A single strip, in either direction.</summary>
    private static SpriteSheetPlan LayOutFlat(
        IReadOnlyList<AnimationClip> clips,
        ImageSize cell,
        int padding,
        int columns,
        int rows)
    {
        List<SheetCell> cells = [];
        int position = 0;

        for (int clip = 0; clip < clips.Count; clip++)
        {
            for (int frame = 0; frame < clips[clip].Count; frame++)
            {
                int column = columns == 1 ? 0 : position;
                int row = columns == 1 ? position : 0;

                cells.Add(new SheetCell(clip, frame, CellBounds(column, row, cell, padding)));
                position++;
            }
        }

        return new SpriteSheetPlan(MeasureSheet(columns, rows, cell, padding), cell, columns, rows, cells);
    }

    /// <summary>The squarest grid that holds every frame, filled left to right, top to bottom.</summary>
    private static SpriteSheetPlan LayOutGrid(
        IReadOnlyList<AnimationClip> clips,
        ImageSize cell,
        int padding,
        int total)
    {
        // Squarest by cell count rather than by pixels. A sheet of tall frames comes out
        // taller than it is wide either way, and counting cells keeps the arithmetic
        // obvious — which matters because an artist has to be able to find frame 12 by eye.
        int columns = (int)Math.Ceiling(Math.Sqrt(total));
        int rows = (int)Math.Ceiling(total / (double)columns);

        List<SheetCell> cells = [];
        int position = 0;

        for (int clip = 0; clip < clips.Count; clip++)
        {
            for (int frame = 0; frame < clips[clip].Count; frame++)
            {
                cells.Add(new SheetCell(clip, frame, CellBounds(position % columns, position / columns, cell, padding)));
                position++;
            }
        }

        return new SpriteSheetPlan(MeasureSheet(columns, rows, cell, padding), cell, columns, rows, cells);
    }

    /// <summary>
    /// The rectangle of one cell, with padding applied as a border around every cell.
    /// </summary>
    /// <remarks>
    /// Padding surrounds each cell rather than only separating them, so that the outermost
    /// frames get the same margin as the inner ones. A sheet where only the interior gaps are
    /// padded still bleeds at its edges, which defeats the point.
    /// </remarks>
    private static ImageRect CellBounds(int column, int row, ImageSize cell, int padding)
    {
        int stepX = cell.Width + (padding * 2);
        int stepY = cell.Height + (padding * 2);

        return new ImageRect(
            (column * stepX) + padding,
            (row * stepY) + padding,
            cell.Width,
            cell.Height);
    }

    private static ImageSize MeasureSheet(int columns, int rows, ImageSize cell, int padding) =>
        new(
            Math.Max(1, columns * (cell.Width + (padding * 2))),
            Math.Max(1, rows * (cell.Height + (padding * 2))));
}
