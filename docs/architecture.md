# XP-xel architecture

This document explains how XP-xel is put together and, more importantly, *why*. It is
written for someone — human or AI — arriving at the codebase with no prior context.

The guiding constraint for the whole project is that it must be able to grow. Animation,
selections, more file formats, more tools and game-engine export are all expected. The
architecture is arranged so that each of those is an addition rather than a rewrite, and
most of the decisions below only make sense in that light.

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

v0.1 creates exactly one frame and shows no timeline, so from the outside it looks like an
ordinary layered editor. The reason for the extra axis is that the obvious alternative —
each layer owns one image — collapses "which layer" and "which moment" into a single
dimension. Adding animation on top of that model later would mean simultaneously
restructuring the document, the compositor, every tool, the undo system and the file format.
Carrying the second axis now costs one dictionary lookup and buys the entire animation
feature as additive work.

Frames and layers carry **`Guid` identities** rather than being addressed by position, and
cels are keyed by `(layerId, frameId)`. If they were positional, inserting or reordering a
frame would require rewriting every cel key and every undo record that mentions one. With
stable identities, reordering is a list operation and nothing else in the document notices.

Two smaller decisions follow the same reasoning:

- **The layers form a tree, not a list.** Only `RasterLayer` can be created in v0.1, but
  `GroupLayer` exists and the compositor handles it, so layer folders are a UI feature away.
- **Ordering is bottom-first.** `GroupLayer.Children[0]` is the *bottom* layer, which lets
  the compositor walk forwards. Layer panels conventionally show the top layer first, so the
  view model reverses it — in exactly one place.

### 2.2 Straight alpha, not premultiplied

`PixelColor` stores straight (non-premultiplied) RGBA, and so does every buffer in the
model. Renderers prefer premultiplied alpha because compositing is cheaper, but
premultiplying is lossy: a pixel at 20% alpha retains only about 51 of 256 levels per
channel, so a save, a reload or an undo could silently alter the artist's colour. In a
medium where a single pixel is a deliberate decision, that is unacceptable.

The conversion to premultiplied BGRA happens exactly once, in `DocumentRenderTarget`, when
the composited image is uploaded to the on-screen bitmap. **Nothing in `Core` or `Formats`
should ever premultiply.**

### 2.3 Change notification and dirty rectangles

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

---

## 4. Tools

`ITool` receives pointer input already converted to image coordinates, together with a
`ToolContext` giving it the document, the colours, the settings and the means to record an
undoable edit. Tools therefore reference no UI type at all and are unit-tested by
constructing a context over a throwaway document.

Adding a tool is: implement `ITool`, register it in `AppServices`. The toolbox is driven by
`ToolRegistry`, which is populated from the container, so nothing else changes.

**Selections are already in the contract** even though no tool creates one. `ToolContext`
exposes a nullable `SelectionMask` and every write goes through the session that honours it.
Adding a marquee tool later therefore changes no existing tool — which is precisely the kind
of retro-fit that would otherwise touch every file in the folder.

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

### 5.2 The `.xpxel` project format

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

### 6.2 View models and dialogs

MVVM with **CommunityToolkit.Mvvm**, chosen over ReactiveUI because its
`[ObservableProperty]` and `[RelayCommand]` are compile-time source generators with no
runtime reflection — which is what keeps the WebAssembly build trimmable and leaves
NativeAOT available for the desktop build.

Dialogs are **overlays inside the shell**, not child windows, because the browser has no
child windows. One implementation therefore behaves identically everywhere. A dialog exposes
its result as a `Task`, so calling code reads as a straight line rather than as callbacks.

### 6.3 Theming

Every colour in the application comes from semantic tokens in `Themes/Tokens.axaml`, defined
once per theme variant. No view names a literal colour. `IThemeService` only switches the
variant; setting it to `Default` makes Avalonia resolve the theme from the operating system
and update live when the OS setting changes. Adding a high-contrast or custom theme is a
resource dictionary, not a code change.

### 6.4 Composition

`AppServices` is the composition root and is the extension point for most new features. A
new tool, format or panel is a class plus one line there. Registration order is deliberately
the UI order — the toolbox and the file dialogs list things in the order they are registered
— so those decisions stay visible rather than depending on reflection or alphabetical
accident.

Each platform head passes a callback that runs last, so it can replace any default simply by
registering its own implementation. The desktop head does exactly that for `ISettingsStore`,
swapping the in-memory default for a JSON file; the browser head swaps in local storage.

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
