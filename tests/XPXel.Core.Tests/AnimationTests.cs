using XPXel.Core.Animation;
using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.History;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for the timeline: frames, named animations, playback order and onion skinning.
/// </summary>
public sealed class AnimationTests
{
    private static readonly PixelColor Red = new(255, 0, 0);
    private static readonly PixelColor Blue = new(0, 0, 255);

    private static PixelDocument CreateDocument(int frameCount)
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));

        while (document.Frames.Count < frameCount)
        {
            document.CreateFrame();
        }

        return document;
    }

    // ---------------------------------------------------------------------------------
    // Frames
    // ---------------------------------------------------------------------------------

    [Fact]
    public void DuplicatingAFrameCopiesEveryLayersArtwork()
    {
        PixelDocument document = CreateDocument(1);
        LayerNode layer = document.Root.Children[0];
        document.GetOrCreateCel(layer, document.Frames[0]).Pixels.SetPixel(1, 1, Red);

        Frame copy = document.DuplicateFrame(document.Frames[0]);

        Assert.Equal(2, document.Frames.Count);
        Assert.Equal(1, document.IndexOfFrame(copy));
        Assert.Equal(Red, document.Cels.Find(layer.Id, copy.Id)!.Pixels.GetPixel(1, 1));

        // A copy, not a share: painting on one frame must not change the other.
        document.Cels.Find(layer.Id, copy.Id)!.Pixels.SetPixel(2, 2, Blue);
        Assert.True(document.Cels.Find(layer.Id, document.Frames[0].Id)!.Pixels.GetPixel(2, 2).IsTransparent);
    }

    [Fact]
    public void MovingAFrameTakesItsArtworkWithIt()
    {
        PixelDocument document = CreateDocument(3);
        LayerNode layer = document.Root.Children[0];
        Frame third = document.Frames[2];
        document.GetOrCreateCel(layer, third).Pixels.SetPixel(0, 0, Red);

        document.MoveFrame(2, 0);

        // Cels are keyed by the frame's identity, so nothing had to be rewritten.
        Assert.Same(third, document.Frames[0]);
        Assert.Equal(Red, document.Cels.Find(layer.Id, third.Id)!.Pixels.GetPixel(0, 0));
    }

    [Fact]
    public void TheLastFrameCannotBeRemoved()
    {
        PixelDocument document = CreateDocument(1);

        Assert.Null(document.RemoveFrame(document.Frames[0]));
        Assert.Single(document.Frames);
    }

    [Fact]
    public void RemovingAFrameIsUndoneWithItsArtworkIntact()
    {
        PixelDocument document = CreateDocument(2);
        DocumentWorkspace workspace = new(document);
        LayerNode layer = document.Root.Children[0];

        Frame second = document.Frames[1];
        document.GetOrCreateCel(layer, second).Pixels.SetPixel(3, 3, Blue);

        workspace.History.Execute(new RemoveFrameCommand("Delete Frame", document, second));
        Assert.Single(document.Frames);

        workspace.History.Undo();

        Assert.Equal(2, document.Frames.Count);
        Assert.Same(second, document.Frames[1]);
        Assert.Equal(Blue, document.Cels.Find(layer.Id, second.Id)!.Pixels.GetPixel(3, 3));
    }

    [Fact]
    public void RetimingSeveralFramesIsOneUndoStep()
    {
        PixelDocument document = CreateDocument(3);
        DocumentWorkspace workspace = new(document);

        SetFrameDurationsCommand command = new(
            "Set Frame Rate",
            document.Frames.Select(f => (f, f.DurationMilliseconds, 83)));

        workspace.History.Execute(command);
        Assert.All(document.Frames, f => Assert.Equal(83, f.DurationMilliseconds));

        workspace.History.Undo();
        Assert.All(document.Frames, f => Assert.Equal(100, f.DurationMilliseconds));
        Assert.False(workspace.History.CanUndo);
    }

    // ---------------------------------------------------------------------------------
    // Tags
    // ---------------------------------------------------------------------------------

    [Fact]
    public void InsertingAFrameInsideATagExtendsIt()
    {
        PixelDocument document = CreateDocument(6);
        AnimationTag walk = document.CreateTag("walk", 2, 4);

        document.CreateFrame(3);

        Assert.Equal(2, walk.From);
        Assert.Equal(5, walk.To);
    }

    [Fact]
    public void InsertingAFrameBeforeATagShiftsItAlong()
    {
        PixelDocument document = CreateDocument(6);
        AnimationTag walk = document.CreateTag("walk", 2, 4);

        document.CreateFrame(0);

        Assert.Equal(3, walk.From);
        Assert.Equal(5, walk.To);
    }

    [Fact]
    public void InsertingAFrameAfterATagLeavesItAlone()
    {
        PixelDocument document = CreateDocument(6);
        AnimationTag walk = document.CreateTag("walk", 2, 4);

        document.CreateFrame(5);

        Assert.Equal(2, walk.From);
        Assert.Equal(4, walk.To);
    }

    [Fact]
    public void RemovingAFrameShrinksTheTagThatHeldIt()
    {
        PixelDocument document = CreateDocument(6);
        AnimationTag walk = document.CreateTag("walk", 2, 4);

        document.RemoveFrame(document.Frames[3]);

        Assert.Equal(2, walk.From);
        Assert.Equal(3, walk.To);
    }

    [Fact]
    public void ATagLeftCoveringNothingIsDropped()
    {
        PixelDocument document = CreateDocument(3);
        document.CreateTag("hit", 1, 1);

        document.RemoveFrame(document.Frames[1]);

        // Keeping it would leave an animation that silently exports as an empty sheet.
        Assert.Empty(document.Tags);
    }

    [Fact]
    public void MovingAFrameDoesNotDragTagBoundariesWithIt()
    {
        PixelDocument document = CreateDocument(6);
        AnimationTag walk = document.CreateTag("walk", 3, 5);

        document.MoveFrame(0, 4);

        // A tag names a stretch of the timeline, so moving a drawing into it makes that
        // drawing part of the animation rather than moving the animation.
        Assert.Equal(3, walk.From);
        Assert.Equal(5, walk.To);
    }

    // ---------------------------------------------------------------------------------
    // Playback order
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AForwardSequenceIsTheTagsFramesInOrder()
    {
        PixelDocument document = CreateDocument(5);
        AnimationTag tag = document.CreateTag("walk", 1, 3);

        AnimationSequence sequence = AnimationSequence.Build(document, tag);

        Assert.Equal([1, 2, 3], Enumerable.Range(0, sequence.Count).Select(sequence.SourceIndexAt));
    }

    [Fact]
    public void AReverseSequenceRunsBackwards()
    {
        PixelDocument document = CreateDocument(5);
        AnimationTag tag = document.CreateTag("walk", 1, 3);
        tag.Direction = LoopDirection.Reverse;

        AnimationSequence sequence = AnimationSequence.Build(document, tag);

        Assert.Equal([3, 2, 1], Enumerable.Range(0, sequence.Count).Select(sequence.SourceIndexAt));
    }

    [Fact]
    public void APingPongSequencePlaysTheEndFramesOncePerCycle()
    {
        PixelDocument document = CreateDocument(5);
        AnimationTag tag = document.CreateTag("swing", 0, 3);
        tag.Direction = LoopDirection.PingPong;

        AnimationSequence sequence = AnimationSequence.Build(document, tag);

        // 0 1 2 3 2 1, then round again — repeating 0 and 3 would make the turn stutter.
        Assert.Equal([0, 1, 2, 3, 2, 1], Enumerable.Range(0, sequence.Count).Select(sequence.SourceIndexAt));
    }

    [Fact]
    public void ASequenceWithoutLoopExpansionKeepsTheAuthoredOrder()
    {
        PixelDocument document = CreateDocument(4);
        AnimationTag tag = document.CreateTag("swing", 0, 3);
        tag.Direction = LoopDirection.PingPong;

        AnimationSequence sequence = AnimationSequence.Build(document, tag, expandLoop: false);

        // A sprite sheet stores each drawing once and lets the engine handle the direction.
        Assert.Equal([0, 1, 2, 3], Enumerable.Range(0, sequence.Count).Select(sequence.SourceIndexAt));
    }

    [Fact]
    public void AnUntaggedDocumentPlaysItsWholeTimeline()
    {
        PixelDocument document = CreateDocument(3);

        AnimationSequence sequence = AnimationSequence.Build(document, tag: null);

        Assert.Equal(3, sequence.Count);
        Assert.Equal(300, sequence.TotalDurationMilliseconds);
    }

    [Fact]
    public void TheStepAtATimeFollowsEachFramesOwnDuration()
    {
        PixelDocument document = CreateDocument(3);
        document.Frames[0].DurationMilliseconds = 100;
        document.Frames[1].DurationMilliseconds = 50;
        document.Frames[2].DurationMilliseconds = 250;

        AnimationSequence sequence = AnimationSequence.Build(document, tag: null);

        Assert.Equal(0, sequence.StepAtTime(0));
        Assert.Equal(0, sequence.StepAtTime(99));
        Assert.Equal(1, sequence.StepAtTime(100));
        Assert.Equal(2, sequence.StepAtTime(150));
        Assert.Equal(2, sequence.StepAtTime(399));
    }

    [Fact]
    public void TimePastTheEndWrapsRoundRatherThanClamping()
    {
        PixelDocument document = CreateDocument(2);

        AnimationSequence sequence = AnimationSequence.Build(document, tag: null);

        // A caller can accumulate elapsed time forever and never think about looping.
        Assert.Equal(0, sequence.StepAtTime(200));
        Assert.Equal(1, sequence.StepAtTime(350));
        Assert.Equal(1, sequence.StepAtTime(-50));
    }

    // ---------------------------------------------------------------------------------
    // Onion skinning
    // ---------------------------------------------------------------------------------

    [Fact]
    public void OnionSkinningOffCompositesOnlyTheCurrentFrame()
    {
        PixelDocument document = CreateDocument(2);
        LayerNode layer = document.Root.Children[0];
        document.GetOrCreateCel(layer, document.Frames[0]).Pixels.SetPixel(0, 0, Red);
        document.GetOrCreateCel(layer, document.Frames[1]).Pixels.SetPixel(1, 1, Blue);

        PixelBuffer target = new(document.Size);
        new OnionSkinCompositor().Compose(document, 1, target, document.Bounds, settings: null);

        Assert.Equal(Blue, target.GetPixel(1, 1));
        Assert.True(target.GetPixel(0, 0).IsTransparent);
    }

    [Fact]
    public void OnionSkinningGhostsTheNeighbouringFramesBeneathTheCurrentOne()
    {
        PixelDocument document = CreateDocument(3);
        LayerNode layer = document.Root.Children[0];
        document.GetOrCreateCel(layer, document.Frames[0]).Pixels.SetPixel(0, 0, Red);
        document.GetOrCreateCel(layer, document.Frames[1]).Pixels.SetPixel(1, 1, Blue);
        document.GetOrCreateCel(layer, document.Frames[2]).Pixels.SetPixel(2, 2, Red);

        OnionSkinSettings settings = new() { IsEnabled = true, FramesBefore = 1, FramesAfter = 1 };

        PixelBuffer target = new(document.Size);
        new OnionSkinCompositor().Compose(document, 1, target, document.Bounds, settings);

        // The current frame is untouched, and both neighbours show through faintly.
        Assert.Equal(Blue, target.GetPixel(1, 1));

        PixelColor before = target.GetPixel(0, 0);
        PixelColor after = target.GetPixel(2, 2);

        Assert.False(before.IsTransparent);
        Assert.False(after.IsTransparent);
        Assert.True(before.A < 255);

        // Tinted differently, so it is obvious which way the motion runs: the earlier frame
        // reads red and the later one green.
        Assert.True(before.R > before.G);
        Assert.True(after.G > after.R);
    }

    [Fact]
    public void OnionSkinningStaysInsideTheAnimationBeingWorkedOn()
    {
        PixelDocument document = CreateDocument(4);
        LayerNode layer = document.Root.Children[0];
        document.GetOrCreateCel(layer, document.Frames[0]).Pixels.SetPixel(0, 0, Red);
        document.GetOrCreateCel(layer, document.Frames[1]).Pixels.SetPixel(1, 1, Blue);

        AnimationTag attack = document.CreateTag("attack", 1, 3);
        OnionSkinSettings settings = new() { IsEnabled = true, FramesBefore = 1, FramesAfter = 0 };

        PixelBuffer target = new(document.Size);
        new OnionSkinCompositor().Compose(document, 1, target, document.Bounds, settings, attack);

        // Frame 0 belongs to a different animation, so it must not bleed into this one.
        Assert.True(target.GetPixel(0, 0).IsTransparent);
        Assert.Equal(Blue, target.GetPixel(1, 1));
    }

    [Fact]
    public void GhostsFadeWithDistance()
    {
        OnionSkinSettings settings = new() { Opacity = 60 };

        Assert.Equal(0.6, settings.GetOpacityAtDistance(1), 3);
        Assert.Equal(0.3, settings.GetOpacityAtDistance(2), 3);
    }

    // ---------------------------------------------------------------------------------
    // Workspace
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheWorkspaceLandsOnAValidFrameAfterTheActiveOneIsDeleted()
    {
        PixelDocument document = CreateDocument(3);
        DocumentWorkspace workspace = new(document);

        workspace.GoToFrame(2);
        workspace.EnsureValidFrame();

        document.RemoveFrame(document.Frames[2]);
        workspace.EnsureValidFrame();

        // Left pointing at a frame the document has never heard of, every cel lookup would
        // return nothing and the canvas would silently go blank.
        Assert.Contains(workspace.ActiveFrame, document.Frames);
        Assert.Equal(1, workspace.ActiveFrameIndex);
    }

    [Fact]
    public void OnionSkinningSurvivesAnAnimationRangeThatRunsPastTheLastFrame()
    {
        PixelDocument document = CreateDocument(3);

        // A tag's range is positional and independently editable, so it can name frames the
        // timeline does not have — by being typed in, or by a frame being deleted from under
        // it. The compositor used to index the frame list with the tag's numbers and threw.
        AnimationTag stale = document.CreateTag("walk", 0, 2);
        stale.To = 9;

        OnionSkinSettings settings = new() { IsEnabled = true, FramesBefore = 1, FramesAfter = 3 };

        PixelBuffer target = new(document.Size);
        new OnionSkinCompositor().Compose(document, 2, target, document.Bounds, settings, stale);
    }

    [Fact]
    public void OnionSkinningSurvivesAnAnimationRangeThatStartsBeforeTheFirstFrame()
    {
        PixelDocument document = CreateDocument(3);

        AnimationTag tag = document.CreateTag("walk", 0, 2);
        tag.To = 2;

        OnionSkinSettings settings = new() { IsEnabled = true, FramesBefore = 3, FramesAfter = 1 };

        PixelBuffer target = new(document.Size);
        new OnionSkinCompositor().Compose(document, 0, target, document.Bounds, settings, tag);
    }

    [Fact]
    public void OnionSkinningIsOnByDefaultButDoesNothingToAStillImage()
    {
        // Defaulting it on is only safe because a single-frame document costs nothing: the
        // compositor delegates straight through rather than ghosting anything.
        Assert.True(new OnionSkinSettings().IsEnabled);

        PixelDocument document = CreateDocument(1);
        LayerNode layer = document.Root.Children[0];
        document.GetOrCreateCel(layer, document.Frames[0]).Pixels.SetPixel(1, 1, Blue);

        PixelBuffer target = new(document.Size);
        new OnionSkinCompositor().Compose(document, 0, target, document.Bounds, new OnionSkinSettings());

        Assert.Equal(Blue, target.GetPixel(1, 1));
    }

    [Fact]
    public void TheWorkspaceForgetsATagThatHasBeenDeleted()
    {
        PixelDocument document = CreateDocument(3);
        DocumentWorkspace workspace = new(document);

        AnimationTag tag = document.CreateTag("idle", 0, 2);
        workspace.ActiveTag = tag;

        document.RemoveTag(tag);
        workspace.EnsureValidFrame();

        Assert.Null(workspace.ActiveTag);
    }
}
