using XPXel.Core.Documents;
using XPXel.Core.History;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for the undo system: the history cursor, the raster edit session that turns a live
/// stroke into a reversible command, and the structural layer commands.
/// </summary>
public sealed class HistoryTests
{
    private static readonly PixelColor Red = new(255, 0, 0);
    private static readonly PixelColor Blue = new(0, 0, 255);

    [Fact]
    public void MovingToAPositionUndoesOrRedoesEverythingInBetween()
    {
        DocumentHistory history = new();
        List<string> log = [];

        for (int i = 1; i <= 4; i++)
        {
            int step = i;
            history.Execute(new DelegateCommand($"Step {step}", () => log.Add($"do-{step}"), () => log.Add($"undo-{step}")));
        }

        log.Clear();

        // Clicking a row three back in the history panel is Ctrl+Z three times, and the artist
        // should not have to count them.
        Assert.True(history.MoveTo(1));
        Assert.Equal(["undo-4", "undo-3", "undo-2"], log);

        log.Clear();

        Assert.True(history.MoveTo(3));
        Assert.Equal(["do-2", "do-3"], log);

        Assert.False(history.MoveTo(3));
    }

    [Fact]
    public void LoweringTheDepthLimitDiscardsTheOldestEntriesImmediately()
    {
        DocumentHistory history = new();

        for (int i = 0; i < 10; i++)
        {
            history.Execute(new DelegateCommand("Edit", () => { }, () => { }));
        }

        // A limit that only applied to future work would look like it had done nothing to the
        // memory the artist was trying to reclaim.
        history.Capacity = 4;

        Assert.Equal(4, history.Count);
        Assert.Equal(4, history.Position);
    }

    [Fact]
    public void TrimmingNeverThrowsAwayWhatCouldStillBeRedone()
    {
        DocumentHistory history = new();

        for (int i = 0; i < 6; i++)
        {
            history.Execute(new DelegateCommand("Edit", () => { }, () => { }));
        }

        history.Undo();
        history.Undo();
        history.Undo();

        history.Capacity = 1;

        // Three entries have been undone and are still waiting to be redone. Trimming to one
        // may drop the applied entries in front of the cursor, but taking the redo stack away
        // would be watching work vanish that the artist has not finished deciding about.
        Assert.True(history.CanRedo);
        Assert.Equal(3, history.Count);
        Assert.Equal(0, history.Position);
    }

    [Fact]
    public void TheDepthLimitIsHeldWithinItsBounds()
    {
        DocumentHistory history = new();

        history.Capacity = 0;
        Assert.Equal(DocumentHistory.MinimumCapacity, history.Capacity);

        history.Capacity = int.MaxValue;
        Assert.Equal(DocumentHistory.MaximumCapacity, history.Capacity);
    }

    [Fact]
    public void TheRevisionMovesWheneverTheHistoryDoes()
    {
        DocumentHistory history = new();
        int start = history.Revision;

        history.Execute(new DelegateCommand("Edit", () => { }, () => { }));
        int afterEdit = history.Revision;
        Assert.NotEqual(start, afterEdit);

        history.Undo();
        int afterUndo = history.Revision;
        Assert.NotEqual(afterEdit, afterUndo);

        history.Redo();
        Assert.NotEqual(afterUndo, history.Revision);

        // Undoing back to where a backup was taken has to read as a change, because the
        // document on disk and the document on screen no longer match.
        Assert.NotEqual(afterEdit, history.Revision);
    }

    [Fact]
    public void UndoAndRedoWalkTheHistoryInBothDirections()
    {
        DocumentHistory history = new();
        List<string> log = [];

        history.Execute(new DelegateCommand("First", () => log.Add("do-1"), () => log.Add("undo-1")));
        history.Execute(new DelegateCommand("Second", () => log.Add("do-2"), () => log.Add("undo-2")));

        Assert.Equal("Second", history.UndoName);
        history.Undo();
        history.Undo();
        Assert.False(history.CanUndo);
        history.Redo();

        Assert.Equal(["do-1", "do-2", "undo-2", "undo-1", "do-1"], log);
    }

    [Fact]
    public void PerformingANewActionDiscardsTheRedoBranch()
    {
        DocumentHistory history = new();
        history.Execute(new DelegateCommand("First", () => { }, () => { }));
        history.Undo();

        history.Execute(new DelegateCommand("Second", () => { }, () => { }));

        Assert.False(history.CanRedo);
        Assert.Equal("Second", history.UndoName);
    }

    [Fact]
    public void SavedStateTracksTheCursorInBothDirections()
    {
        DocumentHistory history = new();
        history.Execute(new DelegateCommand("Edit", () => { }, () => { }));
        history.MarkSaved();

        Assert.True(history.IsAtSavedState);

        history.Execute(new DelegateCommand("Another", () => { }, () => { }));
        Assert.False(history.IsAtSavedState);

        // Undoing back to the saved position means the file on disk is correct again.
        history.Undo();
        Assert.True(history.IsAtSavedState);
    }

    [Fact]
    public void CapacityDiscardsTheOldestEntries()
    {
        DocumentHistory history = new(capacity: 2);
        history.Execute(new DelegateCommand("A", () => { }, () => { }));
        history.Execute(new DelegateCommand("B", () => { }, () => { }));
        history.Execute(new DelegateCommand("C", () => { }, () => { }));

        Assert.Equal(2, history.Count);
        Assert.True(history.Undo());
        Assert.True(history.Undo());
        Assert.False(history.Undo());
    }

    [Fact]
    public void RasterEditSessionRestoresExactlyWhatItChanged()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));
        RasterLayer layer = (RasterLayer)document.Root.Children[0];
        Cel cel = document.GetOrCreateCel(layer, document.Frames[0]);
        cel.Pixels.SetPixel(0, 0, Blue);

        RasterEditSession session = RasterEditSession.Begin(document, cel, "Pencil");
        session.SetPixel(0, 0, Red);
        session.SetPixel(1, 1, Red);
        IUndoableCommand command = Assert.IsType<RasterEditCommand>(session.Commit());

        Assert.Equal(Red, cel.Pixels.GetPixel(0, 0));

        command.Undo();
        Assert.Equal(Blue, cel.Pixels.GetPixel(0, 0));
        Assert.Equal(PixelColor.Transparent, cel.Pixels.GetPixel(1, 1));

        command.Redo();
        Assert.Equal(Red, cel.Pixels.GetPixel(1, 1));
    }

    [Fact]
    public void RasterEditSessionProducesNoCommandWhenNothingChanged()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(2, 2));
        Cel cel = document.GetOrCreateCel(document.Root.Children[0], document.Frames[0]);
        cel.Pixels.Fill(Red);

        RasterEditSession session = RasterEditSession.Begin(document, cel, "Pencil");
        session.SetPixel(0, 0, Red);

        Assert.False(session.HasChanges);
        Assert.Null(session.Commit());
    }

    [Fact]
    public void SnapshotAndPerPixelStrategiesAgree()
    {
        foreach (bool useSnapshot in new[] { false, true })
        {
            PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));
            Cel cel = document.GetOrCreateCel(document.Root.Children[0], document.Frames[0]);
            cel.Pixels.Fill(Blue);

            RasterEditSession session = useSnapshot
                ? RasterEditSession.BeginWithSnapshot(document, cel, "Fill")
                : RasterEditSession.Begin(document, cel, "Fill");

            session.SetPixel(1, 1, Red);
            session.SetPixel(2, 2, Red);
            IUndoableCommand? command = session.Commit();

            Assert.NotNull(command);
            command.Undo();

            Assert.Equal(Blue, cel.Pixels.GetPixel(1, 1));
            Assert.Equal(Blue, cel.Pixels.GetPixel(2, 2));
        }
    }

    [Fact]
    public void RollbackRestoresThePixelsAndKeepsTheHistoryEmpty()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));
        Cel cel = document.GetOrCreateCel(document.Root.Children[0], document.Frames[0]);

        RasterEditSession session = RasterEditSession.Begin(document, cel, "Pencil");
        session.SetPixel(1, 1, Red);
        session.Rollback();

        Assert.Equal(PixelColor.Transparent, cel.Pixels.GetPixel(1, 1));
        Assert.False(session.HasChanges);
    }

    [Fact]
    public void RemovingALayerAndUndoingRestoresItsArtwork()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));
        RasterLayer layer = (RasterLayer)document.Root.Children[0];
        document.GetOrCreateCel(layer, document.Frames[0]).Pixels.Fill(Red);

        DocumentHistory history = new();
        history.Execute(new RemoveLayerCommand("Delete Layer", document, layer));

        Assert.Empty(document.Root.Children);
        Assert.Equal(0, document.Cels.Count);

        history.Undo();

        Assert.Single(document.Root.Children);
        Assert.Equal(Red, document.Cels.Find(layer.Id, document.Frames[0].Id)!.Pixels.GetPixel(0, 0));
    }

    [Fact]
    public void DuplicatingALayerCopiesItsPixelsIndependently()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));
        RasterLayer original = (RasterLayer)document.Root.Children[0];
        document.GetOrCreateCel(original, document.Frames[0]).Pixels.Fill(Red);

        LayerNode copy = document.DuplicateLayer(original, "Copy");
        document.Cels.Find(copy.Id, document.Frames[0].Id)!.Pixels.SetPixel(0, 0, Blue);

        Assert.Equal(2, document.Root.Children.Count);
        Assert.Equal(Red, document.Cels.Find(original.Id, document.Frames[0].Id)!.Pixels.GetPixel(0, 0));
    }

    [Fact]
    public void MoveLayerCommandReordersAndRestores()
    {
        PixelDocument document = new(new ImageSize(2, 2));
        RasterLayer bottom = document.CreateRasterLayer("Bottom");
        RasterLayer top = document.CreateRasterLayer("Top");

        DocumentHistory history = new();
        history.Execute(new MoveLayerCommand("Reorder", document, top, document.Root, 0));

        Assert.Same(top, document.Root.Children[0]);

        history.Undo();
        Assert.Same(bottom, document.Root.Children[0]);
    }

    [Fact]
    public void CompositeCommandUndoesItsChildrenInReverse()
    {
        List<string> log = [];
        CompositeCommand composite = new("Merge", [
            new DelegateCommand("A", () => log.Add("do-a"), () => log.Add("undo-a")),
            new DelegateCommand("B", () => log.Add("do-b"), () => log.Add("undo-b")),
        ]);

        composite.Redo();
        composite.Undo();

        Assert.Equal(["do-a", "do-b", "undo-b", "undo-a"], log);
    }

    /// <summary>A trivial command used to observe the order the history invokes things in.</summary>
    private sealed class DelegateCommand(string name, Action redo, Action undo) : IUndoableCommand
    {
        public string Name => name;

        public void Redo() => redo();

        public void Undo() => undo();
    }
}
