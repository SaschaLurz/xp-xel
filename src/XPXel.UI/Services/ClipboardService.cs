using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.Primitives;
using XPXel.Core.Text;
using XPXel.UI.Rendering;

namespace XPXel.UI.Services;

/// <summary>What a clipboard command did, for the status bar to report.</summary>
/// <param name="Succeeded">Whether anything happened.</param>
/// <param name="Message">What to tell the artist.</param>
public readonly record struct ClipboardResult(bool Succeeded, string Message);

/// <summary>
/// Copy, cut and paste, across the system clipboard as well as within the editor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two things go on the clipboard for every copy.</b> An ordinary bitmap, so the artwork can
/// be pasted into a chat window or another editor, and XP-xel's own exact clip beside it under a
/// private format. Pasting prefers the private one when it is there, which is what keeps a copy
/// and paste inside the editor lossless: the alpha channel, the shape of a non-rectangular
/// selection and the position it was cut from all survive, and none of the three survives a
/// round trip through a platform bitmap.
/// </para>
/// <para>
/// <b>Pasting takes whatever it can get</b>, in the order of how much it knows about it: an
/// XP-xel clip, then any image, then text, and finally the last thing copied in this session if
/// the platform has no clipboard at all — which is the case in the browser build, where a
/// document copied and pasted within one tab still works.
/// </para>
/// <para>
/// <b>Text becomes a text layer, not pixels.</b> Pasted words stay words: the layer can be
/// re-opened and rewritten, which is what the text tool would have given, and rasterising them
/// on arrival would throw that away for no gain. Text pasted while a text layer is being typed
/// into goes into the words at the caret instead, which is what Ctrl+V means there.
/// </para>
/// <para>
/// <b>Every clipboard call is wrapped.</b> The system clipboard is shared mutable state owned by
/// another process: it can be locked, empty, or holding something whose format lies about
/// itself, and none of that is worth an exception reaching the artist mid-drawing. A failure
/// here degrades to the in-session clip.
/// </para>
/// </remarks>
public sealed class ClipboardService
{
    /// <summary>
    /// The private format XP-xel's exact pixels travel under.
    /// </summary>
    /// <remarks>
    /// An application format rather than a platform one, so the operating system never sees the
    /// identifier directly — but two copies of XP-xel, or XP-xel and a future version of itself,
    /// still recognise each other's clips.
    /// </remarks>
    private static readonly DataFormat<byte[]> ClipFormat =
        DataFormat.CreateBytesApplicationFormat("xpxel-clip");

    private readonly ITextRasterizer _rasterizer;
    private readonly ToolSettings _settings;
    private readonly ColorSlots _colors;

    /// <summary>
    /// The last thing copied in this session, kept as a fallback.
    /// </summary>
    /// <remarks>
    /// Used only when the system clipboard cannot be read — there is no clipboard, or the read
    /// failed. When it can be read, what it holds wins, because something else may have copied
    /// since and the artist means the newer thing.
    /// </remarks>
    private PixelClip? _sessionClip;

    /// <summary>Initialises the service.</summary>
    /// <param name="rasterizer">Draws the glyphs of a text layer pasted from outside.</param>
    /// <param name="settings">The tool options, for the typography pasted text adopts.</param>
    /// <param name="colors">The drawing colours, for the colour pasted text adopts.</param>
    public ClipboardService(ITextRasterizer rasterizer, ToolSettings settings, ColorSlots colors)
    {
        ArgumentNullException.ThrowIfNull(rasterizer);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(colors);

        _rasterizer = rasterizer;
        _settings = settings;
        _colors = colors;
    }

    /// <summary>Copies the marked pixels, and optionally clears them.</summary>
    /// <param name="workspace">The document to copy from.</param>
    /// <param name="cut">Whether to erase what was copied.</param>
    /// <returns>What happened, for the status bar.</returns>
    public async Task<ClipboardResult> CopyAsync(DocumentWorkspace workspace, bool cut)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (PixelClip.FromSelection(workspace) is not { } clip)
        {
            return new ClipboardResult(false, "Mark something first — there is nothing to copy");
        }

        _sessionClip = clip;
        await PublishAsync(clip).ConfigureAwait(true);

        string what = $"{clip.Size.Width} × {clip.Size.Height}";

        if (!cut)
        {
            return new ClipboardResult(true, $"Copied {what}");
        }

        // The pixels are already safely on the clipboard, so the erase cannot lose them even if
        // it is the thing that fails.
        return workspace.EraseSelection("Cut")
            ? new ClipboardResult(true, $"Cut {what}")
            : new ClipboardResult(false, "Copied, but nothing could be cut — the layer is locked, hidden, or not a drawing layer");
    }

    /// <summary>Pastes whatever the clipboard is carrying.</summary>
    /// <param name="workspace">The document to paste into.</param>
    /// <returns>What happened, for the status bar.</returns>
    public async Task<ClipboardResult> PasteAsync(DocumentWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        (PixelClip? clip, string? text) = await ReadAsync().ConfigureAwait(true);

        clip ??= _sessionClip;

        if (clip is not null)
        {
            return workspace.BeginPaste(clip)
                ? new ClipboardResult(true, "Pasted — drag it into place, then press Enter. Escape puts it back")
                : new ClipboardResult(false, "Nothing to paste onto — the layer is locked, hidden, or not a drawing layer");
        }

        if (!string.IsNullOrEmpty(text))
        {
            return PasteText(workspace, text);
        }

        return new ClipboardResult(false, "There is nothing on the clipboard to paste");
    }

    /// <summary>Writes a clip to the system clipboard, as both a bitmap and an exact clip.</summary>
    private static async Task PublishAsync(PixelClip clip)
    {
        if (ResolveClipboard() is not { } clipboard)
        {
            return;
        }

        try
        {
            WriteableBitmap bitmap = ClipBitmap.ToBitmap(clip);

            DataTransferItem item = new();
            item.SetBitmap(bitmap);
            item.Set(ClipFormat, clip.ToBytes());

            DataTransfer transfer = new();
            transfer.Add(item);

            // The clipboard takes ownership of the transfer, and of the bitmap inside it, so
            // neither is disposed here.
            await clipboard.SetDataAsync(transfer).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Deliberately broad: another process can be holding the clipboard open, and a copy
            // that failed to reach other programs still worked inside this one.
        }
    }

    /// <summary>Reads whatever the system clipboard is carrying that XP-xel can use.</summary>
    private static async Task<(PixelClip? Clip, string? Text)> ReadAsync()
    {
        if (ResolveClipboard() is not { } clipboard)
        {
            return (null, null);
        }

        try
        {
            using IAsyncDataTransfer? data = await clipboard.TryGetDataAsync().ConfigureAwait(true);

            if (data is null)
            {
                return (null, null);
            }

            // XP-xel's own clip first: it is the only form that keeps the alpha, the shape and
            // the position, and it is only ever there because this program put it there.
            if (await data.TryGetValueAsync(ClipFormat).ConfigureAwait(true) is { } bytes
                && PixelClip.TryFromBytes(bytes, out PixelClip? exact))
            {
                return (exact, null);
            }

            if (await data.TryGetBitmapAsync().ConfigureAwait(true) is { } bitmap)
            {
                using (bitmap)
                {
                    return (ClipBitmap.FromBitmap(bitmap), null);
                }
            }

            return (null, await data.TryGetTextAsync().ConfigureAwait(true));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Same reasoning as the write: a clipboard that will not be read is a reason to fall
            // back, not a reason to interrupt.
            return (null, null);
        }
    }

    /// <summary>Puts pasted words onto the canvas as editable text.</summary>
    private ClipboardResult PasteText(DocumentWorkspace workspace, string text)
    {
        // Already typing: the words go in at the caret, which is what Ctrl+V means inside text
        // anywhere else on the machine.
        if (workspace.ActiveTextEdit is { } editing)
        {
            editing.InsertText(text);
            return new ClipboardResult(true, "Pasted text");
        }

        TextStyle style = _settings.TextDefaults.Clone();
        style.Color = _colors.Primary;

        TextLayer layer = workspace.Document.CreateTextLayer(BuildTextLayerName(workspace.Document), style);

        // Just inside the top-left corner, because text runs right and down from its origin and
        // anywhere further in would push a long line straight off the canvas.
        layer.Origin = new ImagePoint(1, 1);

        TextEditSession session = new(workspace, layer, _rasterizer, isNewLayer: true);

        workspace.ActiveLayer = layer;
        workspace.BeginTextEdit(session);
        session.InsertText(text);

        // Left open on purpose, exactly as clicking with the text tool leaves it open: the words
        // are almost never in the right place yet, and the artist is one click away from moving
        // on, which is what puts the whole thing into the history as one step.
        return new ClipboardResult(true, "Pasted as a text layer — keep typing, or click away to place it");
    }

    /// <summary>Names a pasted text layer the way the text tool names the ones it makes.</summary>
    private static string BuildTextLayerName(PixelDocument document)
    {
        int count = 1;

        foreach (LayerNode node in document.EnumerateLayers())
        {
            if (node is TextLayer)
            {
                count++;
            }
        }

        return $"Text {count}";
    }

    /// <summary>Finds the clipboard, which hangs off whichever window the shell is in.</summary>
    private static IClipboard? ResolveClipboard() => Application.Current?.ApplicationLifetime switch
    {
        IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow?.Clipboard,
        ISingleViewApplicationLifetime { MainView: { } view } => TopLevel.GetTopLevel(view)?.Clipboard,
        _ => null,
    };
}
