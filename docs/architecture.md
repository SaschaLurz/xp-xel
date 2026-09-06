# XP-xel architecture

This document explains how XP-xel is put together and, more importantly, *why*. It is
written for someone — human or AI — arriving at the codebase with no prior context.

The guiding constraint for the whole project is that it must be able to grow. The
architecture is arranged so that each new capability is an addition rather than a rewrite, and
most of the decisions below only make sense in that light. Selections, text, animation and
game-engine export have all since been added on those terms; more file formats and a tilemap
mode are the ones still expected.

---

## 1. Project structure and the dependency rule

```
XPXel.Core  ←  XPXel.Formats  ←  XPXel.UI  ←  XPXel.Desktop
                                          ←  XPXel.Browser
```

Dependencies point one way only, and two rules are load-bearing:

**`XPXel.Core` references nothing but the BCL.** No UI framework, no imaging library, no
other project. Everything about what a pixel art document *is* — buffers, layers, frames,
compositing, tools, undo — lives there and can be exercised headlessly. This is what makes
the test suite fast and complete, and it means a future command-line exporter or build-time
asset pipeline can reuse the entire model untouched.

**Platform code lives only in the heads.** `XPXel.UI` is a class library containing the
whole user interface. `XPXel.Desktop` and `XPXel.Browser` are thin projects that start
Avalonia and register the service implementations unique to their platform. There are no
`#if` directives in `XPXel.UI`; anything platform-specific is expressed as an interface
there and implemented in a head. Shipping on a new platform should be a small new project,
not a port.

---

## 2. The document model

### 2.1 Two axes, meeting at cels

This is the single most consequential decision in the codebase.

A document has a **layer tree** (structure) *and* a **frame list** (time). Pixel data does
not live on either: it lives in a **`Cel`**, which is the intersection of one layer and one
frame.

```
                frame 0     frame 1     frame 2
   layer "fx"   [ cel ]     [ cel ]     [  —  ]
   layer "body" [ cel ]     [ cel ]     [ cel ]
   layer "bg"   [ cel ]     [  —  ]     [ cel ]
```

The reason for the second axis is that the obvious alternative — each layer owns one image —
collapses "which layer" and "which moment" into a single dimension. Adding animation on top of
that model would have meant simultaneously restructuring the document, the compositor, every
tool, the undo system and the file format.

**That bet has now been collected.** Animation was added as a timeline panel, a set of frame
commands and four exporters; the compositor, the tools, the undo system and the cel store were
not touched. The one place that needed care was frame *identity*: cels are keyed by
`(layerId, frameId)`, so reordering the timeline moves the artwork with it for free — see
`PixelDocument.MoveFrame`, which is a list operation and nothing else.

Frames and layers carry **`Guid` identities** rather than being addressed by position, and
cels are keyed by `(layerId, frameId)`. If they were positional, inserting or reordering a
frame would require rewriting every cel key and every undo record that mentions one. With
stable identities, reordering is a list operation and nothing else in the document notices.

`LayerNode.TagColor` is worth a note here because it is the one property on a layer that the
renderer must never read. It is an organisational label — a way to see at a glance which
layers are the character and which are the background — and it deliberately lives on the
model rather than in a UI-side lookup so that it survives a save and reload.

Two smaller decisions follow the same reasoning:

- **The layers form a tree, not a list.** `GroupLayer` has been in the model and the
  compositor since v0.1; the layers panel now creates them, and that bet collected in the
  same way the frame axis did — folders needed `PixelDocument.CreateGroupLayer`, one command
  and some drag-and-drop, and touched neither the compositor nor the file format.
- **Ordering is bottom-first.** `GroupLayer.Children[0]` is the *bottom* layer, which lets
  the compositor walk forwards. Layer panels conventionally show the top layer first, so the
  view model reverses it — in exactly one place.

### 2.2 Named animations are positional, and deliberately so

An `AnimationTag` is a name over a run of frames — "idle" is frames 0 to 7 — with a loop
direction. It is what makes one `.xpxel` file one character rather than one motion, which
matters because a walk and an idle built on differently named layers cannot share a colour
change.

**Tags store indices, and that contradicts the rule two paragraphs up.** Everything else in
the model avoids positions in favour of identity, precisely so that reordering does not
disturb it. A tag is the opposite case: it means "this contiguous stretch of the timeline", so
`PixelDocument` maintains the ranges as frames come and go. Inserting a frame inside a tag
extends it, which is what adding an in-between to a walk cycle should do; inserting before it
shifts it along; and a tag left covering nothing is dropped rather than left to export as an
empty sheet.

Moving a frame deliberately does *not* drag tag boundaries with it. Dragging a drawing out of
"walk" and into "idle" makes it part of the idle animation — which is what every editor with
both does, and what an artist means by the gesture.

**Playback order is expanded once, in `AnimationSequence`.** A ping-pong tag over frames 1–4
plays 1, 2, 3, 4, 3, 2. Interpreting the direction inside a playback loop would mean the same
edge cases written again in the GIF encoder, the sheet exporter and the preview panel — four
chances to disagree about whether the end frames repeat. Expanding it into a plain list means
every consumer just walks the list. The sheet exporter is the one that asks for it
*unexpanded*, because a sheet stores each drawing once and records the direction for the
engine to act on.

### 2.3 Text layers keep their pixels in a cel

`TextLayer` is the second leaf layer type, and the shape of it is worth understanding because
it is the pattern any future generated layer should follow.

A text layer owns its **words and typography** — that is document data, it goes in the
manifest, and it is what makes the text re-editable a year later. It does *not* own its
glyphs. Those go into an ordinary `Cel`, exactly like a raster layer's artwork.

That one decision is why text cost so little. The compositor, the exporters, the layer
thumbnails and the `.xpxel` archive already knew how to handle a cel, so none of them needed
changing; the compositor gained one type in a pattern match and nothing else. It also means an
older build that has never heard of text layers still opens the file, still shows the text and
still exports it correctly — it simply cannot change the words. That is exactly the
forward-compatibility behaviour the format's rules are written to produce, and it is why the
text layer shipped without a schema version bump.

**The cel is a cache, and the words are the truth.** Every path that changes the text, the
style or the position has to re-render through `TextLayerRenderer`, which clears the cel and
redraws. Because rendering is a pure function of those three values, undo needs no pixels at
all: `SetTextCommand` stores the before and after of the words, the style and the origin, and
regenerates. A pixel-storing version would be larger and would eventually let a layer's words
and its glyphs drift apart in the history.

Drawing tools deliberately cannot edit a text layer — `DocumentWorkspace.ActiveRasterLayer`
does not match it, so `BeginRasterEdit` returns nothing. A stroke laid on a text layer would
be thrown away by the next re-render, and silently losing work is worse than refusing it. The
shell says so in the status bar rather than letting the tool appear broken.

**Glyphs need a font engine, which `XPXel.Core` may not have.** `ITextRasterizer` is the
seam: Core declares it, `AvaloniaTextRasterizer` implements it on Avalonia's text stack, and
`NullTextRasterizer` lets the whole model — typing, undo, save, reload — be unit-tested with
no display present.

### 2.4 Straight alpha, not premultiplied

`PixelColor` stores straight (non-premultiplied) RGBA, and so does every buffer in the
model. Renderers prefer premultiplied alpha because compositing is cheaper, but
premultiplying is lossy: a pixel at 20% alpha retains only about 51 of 256 levels per
channel, so a save, a reload or an undo could silently alter the artist's colour. In a
medium where a single pixel is a deliberate decision, that is unacceptable.

The conversion to premultiplied BGRA happens exactly once, in `DocumentRenderTarget`, when
the composited image is uploaded to the on-screen bitmap. **Nothing in `Core` or `Formats`
should ever premultiply.**

### 2.5 Change notification and dirty rectangles

`PixelDocument` raises a single `Changed` event carrying a `DocumentChangeKind` and a dirty
`ImageRect`. It also forwards property changes from every layer it contains, so the UI needs
one subscription to know when to redraw.

Pixel edits deliberately do **not** use `INotifyPropertyChanged`. A bucket fill can touch
hundreds of thousands of pixels, and one callback each would freeze the application. Instead
every operation reports the rectangle it touched, and that rectangle flows through the whole
pipeline: the compositor recomposites only that region, and the canvas re-uploads only that
region to the GPU. This is what keeps drawing responsive on a large canvas, so **preserve
the dirty rectangle when adding operations** rather than falling back to "invalidate
everything".

### 2.6 Resizing the canvas is one operation in both directions

`PixelDocument.Resize(newSize, offset)` covers cropping *and* enlarging. There is no separate
crop: a smaller size with a negative offset takes pixels away and a larger size with a
positive one pads, and the arithmetic is identical. `CanvasAnchor` and
`PixelDocument.GetAnchorOffset` exist only to turn "keep the bottom edge where it is" into
that offset, so the nine-way anchor in the dialog costs the model nothing.

Two details are load-bearing:

- **Cels are replaced, not resized in place.** `Cel` promises that its `PixelBuffer`
  reference never changes for the cel's lifetime, which is what lets a live stroke hold onto
  a buffer safely. A resize therefore allocates a new buffer, copies the surviving region
  into it, and puts a *new* `Cel` in the store under the same key.
- **A text layer's `Origin` moves with the offset.** The glyphs already live in a cel and
  move with everything else, but the origin is what the next re-render draws from. Leaving it
  behind would make the text jump long after the resize, at a moment impossible to connect to
  its cause.

`ResizeCanvasCommand` stores the *previous cels* rather than inverting the offset on undo,
because a crop destroys pixels and no offset can bring them back. The command hands them to
`PixelDocument.RestoreCanvas`, which is the only other method allowed to assign `Size`.

The change is reported as `DocumentChangeKind.Size` rather than `Structure`, because
everything cached at the old dimensions — the render target's bitmap above all — has to be
rebuilt rather than merely repainted. A `SelectionMask` is allocated for one canvas size and
cannot survive the change either, so the shell clears the selection after a resize.

---

## 3. Editing and undo

`DocumentWorkspace` is one open document plus the state the editor tracks about it: undo
history, active layer, active frame, selection, file path. `PixelDocument` deliberately does
not know which layer is "active" — that is a property of an editing session, not of the
artwork. One workspace equals one document tab, so multi-document support is a collection of
these rather than a restructure.

`PixelDocument`'s methods mutate directly and do not touch the undo stack. Undoable
operations are **commands** that call those methods in both directions. Application code
should go through commands; only the commands and the deserializer call the document
directly.

**`RasterEditSession` is where a live stroke becomes a reversible command.** Drawing happens
as the artist moves the pointer, but the undo entry can only be created at the end, and it
needs to know what the pixels looked like *before*. A session captures exactly that, using
one of two strategies:

- *Per-pixel recording* for freehand strokes, which touch a small, unpredictable set.
- *A whole-cel snapshot* for operations that may cover the canvas, such as a bucket fill,
  where per-pixel bookkeeping would cost more than the copy.

Either way the resulting command stores only the rectangle that actually changed, so a
pencil dab costs a few dozen bytes rather than two copies of the canvas.

Selection is enforced inside the session rather than in each tool, so no tool can forget it.
`SelectionTransform` — the free transform behind Ctrl+T — is the sole exception and asks for a
session without one, because moving the selected pixels is by definition an edit that writes
outside the selection. It is a deliberate, documented exception rather than a hole: the type
says so, and a test asserts it.

**A move is a preview that gets committed.** The pixels are copied out of the layer once, and
every drag resets the session to the state before the transform and redraws from that copy. Two
things fall out of that. Dragging around and back again leaves the document byte-for-byte as it
was — the layer is never read a second time, so nothing smears. And however far the artist
dragged, the artist gets one "Move Selection" in the Edit menu. The marquee is moved with the
pixels as a `SetSelectionCommand` inside the same composite, so an undo does not leave it
stranded over the hole the artwork used to fill.

**`DocumentHistory.Revision` counts movements, not states.** It exists for autosave, which has
to distinguish "nothing has happened since the last backup" from "something happened and was
undone" — the document looks identical either way, and only the first means there is nothing
worth writing.

### 3.1 Backups

`AutosaveService` copies every open document that has changed to `IAutosaveStore` every three
minutes. Three decisions in it are worth knowing:

- **A backup is not a save.** It never touches the artist's file, never marks the document
  saved, and never appears in the undo history. The copies exist to be found after a crash.
- **Serialising is on the UI thread; writing is not.** The document is not thread-safe and the
  artist may be mid-stroke, so it is turned into bytes on the thread that owns it and only the
  finished buffer is handed to the store. This is why the clock is a `DispatcherTimer`.
- **The store is an interface for the same reason `ISettingsStore` is.** The desktop head writes
  plain `.xpxel` files into a folder under local application data; the browser head has nowhere
  to put several megabytes and registers nothing, so `NullAutosaveStore` answers honestly and
  the File menu entry is disabled rather than lying about a safety net that is not there.

Recovery is deliberately not a feature. The backups are complete project files with readable
names in a folder the File menu opens, so recovering is File → Open. Anything cleverer would be
something else to learn at the worst possible moment.

---

## 4. Tools

`ITool` receives pointer input already converted to image coordinates, together with a
`ToolContext` giving it the document, the colours, the settings and the means to record an
undoable edit. Tools therefore reference no UI type at all and are unit-tested by
constructing a context over a throwaway document.

Adding a tool is: implement `ITool`, register it in `AppServices`. The toolbox is driven by
`ToolRegistry`, which is populated from the container, so nothing else changes.

**A tool is never told it has been deselected**, which is a deliberate simplification and the
reason two pieces of cleanup live in the canvas control instead. When the active tool changes
mid-stroke the canvas hands the stroke over: the outgoing tool is given the pointer release it
would otherwise never see, and the incoming tool the matching press at the same point. That is
what makes pressing E halfway through a pencil stroke carry on erasing from where the pencil got
to — and, more importantly, what stops the pencil's `RasterEditSession` being abandoned with the
pre-stroke pixels still in it, which used to resurrect erased artwork on the next press.

**Shift locks a stroke to one axis**, in `StrokeToolBase` so the pencil, brush and eraser all get
it. The axis is chosen by the first movement *after* shift goes down rather than by shift itself,
because that is the moment the artist has said which way they meant; the anchor is where the
stroke had reached, not where it began. Releasing shift re-arms the choice, which is how a corner
is turned without lifting the button.

**Selections were already in the contract** before any tool created one. `ToolContext`
exposes a nullable `SelectionMask` and every write goes through the session that honours it,
so `RectangleSelectTool` was added by filling that mask in — and not one existing tool
changed. That is the retro-fit the original design was arranged to avoid having to make, and
it is worth remembering the next time something looks like premature generality.

**Three families of tool now share three base classes.** `StrokeToolBase` paints along the
pointer's path; `ShapeToolBase` is dragged out between two corners; everything else implements
`ITool` directly. Two things about them are load-bearing:

- **Brush dynamics are opt-in.** Softness fades a single stamp's edge; stabilisation smooths
  the path the stamps are placed along. They are separate settings because they act on
  different things, and only tools returning `true` from `SupportsBrushDynamics` honour
  either. The pencil returns `false` on purpose: it exists to put one exact colour in one
  exact pixel at the position the artist pointed at, and both settings would break that.
- **A soft stroke remembers its coverage.** Consecutive stamps overlap almost completely, so
  compositing each onto the result of the last would darken the stroke wherever the pointer
  slowed down until a soft brush painted a hard line. Every pixel instead remembers the
  highest coverage the stroke has reached and composites against its *pre-stroke* value,
  which is what `RasterEditSession.GetOriginalPixel` is for.

**A shape tool previews itself by drawing the real thing and undoing it.** One
`RasterEditSession` is opened on button-down and kept open for the whole drag, and
`ResetToOriginal` restores the pixels before each redraw. The artist therefore sees exactly
what they will get — correct blend mode, correct opacity, correctly clipped by the selection —
with no second rendering path to keep in agreement with the first, and the drag produces one
undo entry however far the pointer travelled.

**Typing is not a pointer gesture,** so it does not go through `ITool` at all. `TextTool`
decides *which* text is being edited; a `TextEditSession` on the workspace decides what
happens to it, and the canvas routes keystrokes there. A tool is a singleton shared by every
open document and must hold no per-document state, which is the reason the session lives on
the workspace rather than on the tool.

> A trap worth remembering: on Windows, marking a `KeyDown` as handled stops the operating
> system from producing the character message that becomes `TextInput`. The canvas therefore
> must **not** swallow letter keys to keep them away from the tool shortcuts — doing so makes
> it impossible to type a letter at all. The shell resolves that conflict instead, by asking
> the canvas whether text is being edited before it claims any bare key.

---

## 5. File formats

Nothing outside `XPXel.Formats` names a concrete codec. `ImageFormatRegistry` resolves an
importer or exporter by file extension, or by sniffing the leading bytes — because file
extensions are frequently wrong, and opening a mislabelled file correctly is better than
mangling it.

Each format declares a `FormatDescriptor` with capability flags (`Layers`, `Animation`,
`IndexedColor`, `Alpha`, `Metadata`). The UI reads those flags rather than knowing about
individual formats, which is how the export path can warn that PNG will flatten a layer
stack without containing the word "PNG" anywhere.

**Adding a format is one class plus one registration line.** No menu, dialog, or open/save
code changes.

### 5.1 The PNG codec is hand-written, deliberately

XP-xel implements PNG in pure managed code rather than binding an imaging library. Three
reasons:

1. A native library means per-platform binaries, which is exactly what breaks a WebAssembly
   build.
2. It would need version-matching against whatever the renderer already bundles.
3. PNG's decoder is small and completely specified, .NET supplies the zlib layer, and owning
   it keeps indexed colour and custom chunks reachable — both are on the roadmap, and APNG
   (animation) is a chunk-level extension of the same file.

The decoder handles all five colour types, bit depths 1 to 16, and both forms of
transparency. Adam7 interlacing is explicitly rejected with a clear message rather than
decoded incorrectly.

### 5.2 Animation export is a different shape from image export

`IImageExporter` writes one document to one stream. That is the wrong shape for animation:
a sprite sheet is an image *plus* its metadata, and a frame sequence is one file per frame.
`IAnimationExporter` therefore returns a list of named byte arrays — `ExportArtifact` — and
the shell decides where they go. Keeping the exporters free of file handles and folders is
what lets the same code run in the browser, where neither exists in the usual form.

**The sprite sheet is first in the registry, and that is a decision rather than an accident.**
It is the format 2D game animation actually ships in: an engine loads one texture and draws a
different rectangle of it each frame, which costs one texture bind instead of one per frame.
The first entry in a list is the one a person in a hurry takes, so it had better be the right
one.

Three properties of the sheet are load-bearing:

- **Every cell is the same size.** That is what lets an engine slice the image by cell size
  rather than being told each rectangle — Unity's "Grid By Cell Size", Godot's `hframes` and
  `vframes`, and every hand-rolled importer assume it. Packing frames against their own
  bounding boxes would save texture space and make the sheet useless without its metadata.
- **The JSON matches Aseprite's array format.** That format is the closest thing this corner
  of game development has to an interchange standard, and matching it means an XP-xel sheet
  drops into an existing importer with no work at all. The consequence is that those property
  names are not ours to tidy: renaming `frameTags` would be an improvement in isolation and
  would break every one of those importers.
- **A ping-pong animation is written once, forwards, with its direction in the metadata.**
  Baking the return leg into the sheet would store the same drawing twice.

**GIF and APNG are hand-written for the same reason the PNG codec is.** A native imaging
library means per-platform binaries, which is exactly what breaks the WebAssembly build. They
are the honest answer to "make a video of it": a real video file needs a native encoder, and
there is no managed one. GIF costs 256 colours and hard-edged transparency; APNG costs
nothing at all and is a handful of extra chunks on the encoder that already exists.

> A trap worth knowing if either encoder is touched. GIF's LZW grows its code width when the
> *next* code passes the current limit, while a decoder — which builds its dictionary one
> entry behind — must grow when the next code *reaches* it. The two rules look like an
> off-by-one and are not. Getting it wrong produces a file that opens as a garbled mess rather
> than an error, which is why `TestGifDecoder` exists in the test project: it decodes the
> encoder's own output back to pixels and compares them.

### 5.3 The `.xpxel` project format

A `.xpxel` file is a zip archive:

```
manifest.json    canvas size, layer tree, frames, cel index, palette, metadata
cels/0.png       one PNG per cel
thumbnail.png    flattened preview
```

Chosen over a custom binary format because every future feature — animation, tags, export
hints — is a new JSON property or a new archive entry rather than a change to a byte layout,
and because a file that will not open can be unzipped and read by anyone, which is worth a
great deal in an open source project.

The manifest carries a `schemaVersion` and there is a **migration pipeline** in place from
version 1, before any migration exists. That is deliberate: the alternative is discovering
at version 2 that a year of users' files cannot be opened.

Two rules for anyone editing the manifest types:

- Adding an optional property with a sensible default is always safe.
- Renaming, removing or repurposing a property requires bumping the version and adding a
  migration.

Enumerations are stored **by name**, and an unrecognised name falls back to a default rather
than failing the load, so a file written by a newer build still opens in an older one.

> One trap already hit and fixed: JSON serialization must **not** omit properties equal to
> their CLR default. A hidden layer stores `isVisible: false`, which is `bool`'s default, and
> omitting it means the property reloads as `true`.

---

## 6. The user interface

### 6.1 The canvas

`PixelCanvasControl` is the only place in XP-xel where screen coordinates exist. It converts
them to image coordinates through `CanvasViewTransform` before handing anything to a tool,
which is what keeps every tool free of zoom, scroll and device-pixel concerns.

Bitmap interpolation is forced to `None`, and the Windows manifest declares per-monitor DPI
awareness. Both exist for the same reason: a blurry pixel editor is a broken pixel editor.

`DocumentRenderTarget` bridges the model's representation and the renderer's, holding a
straight-alpha `PixelBuffer` that the compositor writes into and a premultiplied BGRA
`WriteableBitmap` that the GPU draws. It keeps them in step through the dirty rectangle.

> A subtle bug worth remembering: `Visual.Bounds` is only assigned *after* `ArrangeOverride`
> returns, so reading it while arranging yields a zero size. The control tracks the arranged
> size itself.

The canvas keeps re-fitting the document whenever its viewport changes size, and stops the
moment the artist zooms or pans. Fitting only once was enough before docking; a docked canvas
is arranged several times at growing sizes while the layout settles, and fitting to the first
of those left the artwork parked in a corner. Continuing to re-fit *after* a deliberate zoom
would be worse than either, so `_hasUserAdjustedView` draws the line.

**Everything drawn on top of the artwork lives in one place.** The pixel under the pointer,
the brush footprint, the selection edge and the text caret are all painted by the canvas after
the document, in document coordinates scaled by the same transform as the artwork. Two
consequences are worth knowing:

- **The brush cursor is not a cursor.** A brush outline has to be the size the brush will
  actually paint, which changes with both the brush size and the zoom; baking that into a
  bitmap cursor would mean rebuilding it on every zoom step and would break down past the
  roughly 128-pixel limit the platforms impose. Drawing it here is exact at any magnification
  and costs nothing. The pointer stays a crosshair underneath it.
- **Every overlay is drawn twice**, a dark line beside a bright one. A single-colour outline
  is invisible against artwork of that colour, and a pixel editor's canvas is by definition
  covered in every colour the artist has used.

A tool that needs an overlay it cannot draw into the artwork — a marquee, which must never
become pixels even briefly — describes it through `ToolContext.Overlay` in terms a tool can
express without knowing what a screen is, and the canvas decides how to render it. Shape
tools do not use this: they preview by drawing the real shape and undoing it, which is right
for something that *will* end up as pixels.

Holding space turns the left button into a grab handle. The key is watched on the window
rather than on the canvas, because it has to work regardless of which panel was clicked last
— an artist who has just picked a layer is exactly the one who wants to move the view. It
steps aside while a text box has focus, where a space is a space.

**The right button opens the active tool's settings and does not draw.** It used to paint
with the secondary colour; a settings panel where the pointer already is turned out to be
worth more than a second drawing button, and the secondary colour is one `X` away. The canvas
leaves a right press entirely alone — unhandled and without capturing the pointer — so that
the ordinary `ContextFlyout` machinery opens the panel on release, the way every context menu
on the platform behaves. `ToolContext.ColorFor` still maps the right button to the secondary
colour: the model of "which button means which colour" is unchanged, the canvas simply no
longer produces that button.

**The rulers are a separate control, outside the canvas.** `CanvasRulerControl` sits in the
grid cells above and to the left of `PixelCanvasControl` and binds to the canvas's own
`ViewTransform` and `DocumentSize`, both published as direct properties for that purpose.
Drawing them inside the canvas would have meant either scrolling them away with the artwork
or excluding a strip of the viewport from the transform; binding them to the transform
instead means the ruler does no arithmetic about where the document is and cannot drift out
of step with it.

**Rulers and the status bar count from 1; everything else counts from 0.** The document,
every tool and every file format index pixels from zero and must keep doing so. The
conversion happens in exactly two places — `CanvasRulerControl` and
`MainViewModel.HoveredPixelText` — which are the only two places a coordinate is shown to a
person.

### 6.2 The shell is a docking layout

The panels and the document tabs are not laid out by the shell. `MainView` contributes only
a menu, a tool options bar and a status bar; everything between them is a `DockControl` whose
contents the artist arranges by dragging.

**`XpDockFactory` describes the default arrangement and nothing more.** After the first run
the layout belongs to the artist and is restored from settings.

**The panels are a table, not a sequence of statements.** `XpDockFactory.Definitions` names
each panel once — its id, its title, its view model, the dock it lives in and the side it
belongs on — and the default layout, the View menu's checklist, the re-attachment of a
restored layout and the "switch this panel back on" path all read from it. Adding a panel is
one entry there plus a data template.

That table is also what fixed a genuine trap in the earlier design. The View menu used to
offer a plain "Tools" item that brought the panel forward, and choosing it when the panel had
been closed rebuilt the *entire default layout* — so asking for one panel switched every panel
back on and threw away the artist's arrangement. The menu now shows a checkmark per panel,
which states what is currently true and changes only the thing it names. Because a panel can
also be closed with the × on its tab or by shutting the window it was torn off into, the
checkmarks are refreshed from the layout on every add, close and remove rather than tracked by
the menu itself.

Switching a panel off removes it from the tree but does not destroy it — its view model holds
the artist's brush, colours and selection — so the factory keeps the wrapper and puts the same
object back. Closing the last panel in a dock collapses that dock away, so a panel coming back
may find its home gone; it then falls back to a dock on the side it belongs to, and only then
to any dock at all.

Three further things are worth knowing before changing anything:

- **Ids are the persistence key.** Every dockable carries a stable `Id`, because that is what
  a saved layout stores. Renaming one orphans that panel in layouts saved by older builds.
  The loader tolerates it — the panel simply returns to its default place — but it is a
  silent change in behaviour for existing users.
- **A saved layout stores structure, not view models.** A `PanelTool` comes back with its
  `Content` empty and `XpDockFactory.RestorePanelContent` reattaches the live panels by id.
  The content property is `[JsonIgnore]`d, and it has to be: a panel view model reaches the
  document, its layers and their parent references, which is a cycle the serialiser throws
  on. Because saving the layout is deliberately best-effort, that throw is swallowed, and the
  only symptom is that the arrangement is silently never remembered.
- **Documents are never persisted.** Artwork lives in `.xpxel` files, not in a settings file,
  so any document found in a restored layout is an empty shell and is discarded on load.
- **`DockLayoutStore.LayoutVersion` is the escape hatch.** Bump it whenever the default
  arrangement changes shape enough that restoring an old one would produce something
  confusing, and every saved layout is silently discarded. It went to `2` when the brush and
  text panels arrived: a version 1 layout has neither, and its left dock is too narrow to show
  even the toolbox's own title.

The layout is captured when the main window is *closing*, not when the application exits.
Torn-off panels are real windows that get closed before exit, and by exit time a floating
panel has already been folded back into the docked tree.

**One panel, many documents.** Each open image is an `EditorDocument` holding one
`DocumentWorkspace`; the layers and colour panels are single instances re-pointed at whichever
tab is active. This is why the original `Attach` design mattered: multi-document support
turned out to be a collection of workspaces rather than a restructuring of the editor, exactly
as intended.

Anything genuinely shared across documents — the current tool, the brush, the two colours,
whether the pixel grid is on — lives outside the document, because an artist expects to pick
a brush once and keep it while switching tabs. Anything about how one image is being *viewed*
— its zoom, the pixel under the pointer — lives on the document.

> A trap worth remembering: a floating panel is an ordinary top-level window, and Avalonia
> shuts down when the last one closes. Without
> `CloseFloatingWindowsOnMainWindowClose`, closing the editor while a panel is floating leaves
> the process running with only that panel on screen.

### 6.3 Animation in the user interface

**The timeline is the document model drawn on screen.** Frames across, layers down, one cell
per cel — which is why it is that shape rather than a row of frames, and why it was worth
carrying two axes in the model from the beginning.

**Playing moves the document's own active frame.** There is no second animation running
somewhere: the canvas, the timeline and the preview all show the active frame, so advancing it
animates every one of them and they cannot disagree. It also means the artist can stop on any
frame and immediately draw on it. `AnimationPlaybackService` is a single application-wide
object for the same reason — two panels with transport controls would otherwise mean two
clocks drifting apart.

Its elapsed time comes from a `Stopwatch` rather than from counting timer ticks. A dispatcher
timer fires late whenever the UI thread is busy, which in a paint program is whenever the
artist is drawing; counting ticks would slow the animation down exactly when it is being
watched. Measuring real time keeps playback at the authored speed and drops frames under load,
which is the right trade.

**Onion skinning composites into the same buffer as the artwork.** `OnionSkinCompositor` wraps
`LayerCompositor` and is a drop-in replacement from the canvas's point of view; with the
feature off it delegates straight through, so the ordinary drawing path pays nothing for it
existing. Ghosts go underneath, oldest first, and the current frame last — the artist is
drawing on that one and it must never be dimmed by context. The colour picker samples this
same buffer and therefore never picks a ghost, because `SampleComposite` reads it after the
current frame has been laid over the top.

### 6.4 Tool presentation

A tool in `XPXel.Core` must not know what an icon or a mouse cursor is — that is the whole
reason the tools are unit-testable without a display. `ToolPresentations` in the UI is
therefore a lookup from a tool's stable id to how it is presented: its icon geometry, which
cursor it uses, where that cursor's working point is, whether the canvas should outline the
brush footprint under it, and which single key selects it.

The same table records **which settings each tool actually uses**, as a `ToolOptions` flags
value. That is what lets the right-click panel on the canvas ask the bucket about tolerance
and the pencil about brush size, without the view acquiring a list of tool ids of its own: a
tool declares what it uses beside its icon, and the panel follows.

Adding a tool still needs no existing file to change *in order to work* — an unregistered tool
appears in the toolbox with a placeholder icon and a crosshair, draws perfectly well, and
reports no settings. Adding an entry there is what gives it its own look.

The icons are stroked vector geometry on a 24×24 grid, kept to one visual weight so the set
reads as a set; the same paths are reused to draw the bitmap cursors, which is what makes a
tool's cursor recognisably the same picture as its button.

**Bare-key shortcuts are handled in the shell's code-behind, not as `KeyBinding`s.** A
`KeyBinding` fires wherever the key is pressed, so binding `B` to the brush would switch tools
in the middle of renaming a layer to "Body". Every bare key is instead gated on a check that
nothing is being typed into — which has to ask about both an ordinary focused text box *and* a
text layer being typed on the canvas, since the second has no focused text box at all. The
handler is attached to the top level rather than to `MainView`, because immediately after
launch nothing inside the shell has focus and an overridden `OnKeyDown` would never run.

### 6.5 Colour

`ColorHarmony` and `HsvColor` live in Core rather than the UI, because suggesting the colours
that go with a colour is arithmetic with no view concepts in it and is worth testing without a
display. Two decisions in there are worth knowing:

- **The shading ramp shifts hue, and that is the whole point.** Darkening by value alone gives
  the muddy, plastic shading that marks out beginner pixel art. The ramp rotates shadows
  towards blue and highlights towards yellow, gains saturation going down and loses it going
  up — what light actually does, and what makes a generated ramp usable rather than a starting
  point the artist has to fix. The rotation always takes the shorter arc and never overshoots
  its target, so a blue's shadow stays blue instead of sliding into cyan.
- **Hue is remembered, not re-read.** A grey has no hue: every hue produces the same grey, and
  `HsvColor.FromColor` reports zero for one. The colour editor therefore keeps the artist's
  hue as its own state, which is what lets saturation be pulled to zero and back without the
  colour snapping to red.

**The palette's first ten entries are a hotbar.** They are numbered on screen and answer to
the number keys. Nothing in the model distinguishes a hotbar slot from any other palette
entry — the numbers are simply what the first ten positions are worth — so the hotbar needs no
storage of its own and is saved with the document like the rest of the palette. The digits are
handled beside the bare-letter tool shortcuts in the shell's code-behind, under the same "is
anything being typed into" guard and for the same reason.

The right-click editor is one `Flyout` in the colour panel's resources, shown against whichever
swatch was clicked. Because it is shared it cannot bind its way to the editor view model — it
is shown against a swatch, whose data context is that swatch — so the code-behind assigns it
when the editor is pointed at something.

### 6.6 View models and dialogs

MVVM with **CommunityToolkit.Mvvm**, chosen over ReactiveUI because its
`[ObservableProperty]` and `[RelayCommand]` are compile-time source generators with no
runtime reflection — which is what keeps the WebAssembly build trimmable and leaves
NativeAOT available for the desktop build.

Dialogs are **overlays inside the shell**, not child windows, because the browser has no
child windows. One implementation therefore behaves identically everywhere. A dialog exposes
its result as a `Task`, so calling code reads as a straight line rather than as callbacks.

### 6.7 Theming

Every colour in the application comes from semantic tokens in `Themes/Tokens.axaml`, defined
once per theme variant. No view names a literal colour. `IThemeService` only switches the
variant; setting it to `Default` makes Avalonia resolve the theme from the operating system
and update live when the OS setting changes. Adding a high-contrast or custom theme is a
resource dictionary, not a code change.

### 6.8 Composition

`AppServices` is the composition root and is the extension point for most new features. A
new tool, format or panel is a class plus one line there. Registration order is deliberately
the UI order — the toolbox and the file dialogs list things in the order they are registered
— so those decisions stay visible rather than depending on reflection or alphabetical
accident.

Each platform head passes a callback that runs last, so it can replace any default simply by
registering its own implementation. The desktop head does exactly that for `ISettingsStore`,
swapping the in-memory default for a JSON file, and for `IAutosaveStore`, swapping the store that
keeps nothing for one that writes project files to disk; the browser head swaps in local storage
and leaves the autosave default in place.

---

## 7. Conventions

- **Warnings are errors**, and `.editorconfig` enforces the official .NET conventions.
  Fix the code rather than suppressing the rule, unless the suppression carries a
  justification.
- **`XPXel.Core` and `XPXel.Formats` require XML documentation on every public member.**
  They are the API every other project codes against.
- **Comments explain why.** The codebase is written on the assumption that its next reader
  has no context, so every non-obvious decision states its reasoning. Please keep that up;
  it is the single thing that makes the project approachable.
- **Central package management**: versions live in `Directory.Packages.props`, never inline
  in a `.csproj`. All Avalonia packages must move in lockstep.
