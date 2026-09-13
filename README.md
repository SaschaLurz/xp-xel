# XP-xel

A free and open source pixel art creator and editor studio, built for game artists.

XP-xel is a layer-based pixel editor in the spirit of Photoshop, shaped around what sprite
work actually needs: crisp nearest-neighbour zoom, exact colour values, per-layer blending,
and export that never softens an edge. It runs on Windows, Linux, macOS and in the browser
from a single codebase.

> **Status: v0.1, early.** The editor is genuinely usable — draw, stack layers, undo, save,
> export — but it is an early foundation rather than a finished product. See
> [Roadmap](#roadmap).

---

## What works today

- **A workspace you arrange yourself.** Every panel is a tab that can be dragged to another
  edge, tabbed together with another panel, pinned, switched off, or torn off into its own
  floating window. Images open as document tabs, several at a time. The arrangement is
  remembered between sessions; **View → Panels** ticks the ones on screen and switches any
  of them on or off individually, and **Default layout** in the options bar puts everything
  back without closing a single document.
- **Layouts you name and keep.** Arrange the panels for the job in hand and save the
  arrangement under a name; **View → Layouts** lists it beside the two that ship with the
  editor, and **Manage Layouts** renames, reorders and removes the ones you saved. **Simple**
  is the beginner's arrangement — the colour panel, the tools and the canvas, and nothing
  else — and every panel it leaves out is still one tick away in **View → Panels**.
- **Layers and folders**, with per-layer opacity, visibility, locking, fourteen W3C-standard
  blend modes, a live preview of each layer's own contents, colour labels, renaming, and
  drag-and-drop restacking. Group layers into folders — hiding or fading a folder hides or
  fades everything inside it. The whole panel is direct-manipulable: drop a layer on **+** to
  duplicate it, on the **folder** button to wrap it in a new folder, on **✕** to delete
  *that* layer, or onto a folder row to move it inside.
- **A background, in one click.** The layers panel's **background** button adds an opaque
  white layer at the bottom that is the *same drawing on every frame* — including frames added
  afterwards — so it never has to be maintained per frame. Any layer can be switched to that
  behaviour from its right-click menu (**Same on Every Frame**), and a marker beside its name
  says so. When you want copies that can then diverge instead, **Copy to All Frames** puts this
  frame's drawing on every other one as independent copies.
- **Copy a whole layer.** **Copy Layer** and **Paste Layer** in the layers panel take the layer
  itself — its name, its opacity, its blend mode and every frame's drawing — and paste it into
  this document or another one. Separate from `Ctrl` `C`, which is still about the pixels in
  front of you; with nothing marked it now copies everything on the layer rather than refusing.
- **Right-click any cell in the timeline** to cut, copy, paste or clear that one layer's
  drawing on that one frame, spread it to every frame, or make the layer the same throughout.
- **Several layers at once, and it means one thing.** `Ctrl`-click or `Shift`-click to mark a
  run of layers, and *everything* takes the lot — drawing included. A stroke, a fill, a shape or
  the eraser lands on every marked layer in one undo step, and a badge appears beside the
  pointer counting them, so a line arriving on four layers is never a surprise. Every
  structural operation takes the lot too: drag them onto the **folder** button to wrap them all in
  one folder, or onto a folder row to move them all inside, in one undo step and keeping the
  order they were stacked in. Deleting, duplicating, visibility, opacity and blend mode apply
  to every marked layer, and so do copy, cut and `Ctrl` `T` — copying several layers flattens
  what you can see across them into one clip.
- **Tools**: pen-xel, brush, eraser, bucket fill (with tolerance, contiguous or global),
  colour picker, line, rectangle, ellipse, text, and three ways of marking pixels — each with
  its own icon, its own cursor and a single-key shortcut shown beside the icon. Tools that do
  the same job differently **share a toolbox square**: the pen-xel with the brush, the three
  shapes together, the three marking tools together. Right-click a square with a folded corner
  to pick another; every tool keeps its own key, so grouping costs nothing at the keyboard.
  Right-click the *canvas* for a panel of just the settings the active tool actually uses, with
  the size slider at the top of it, because that is the one you opened the menu to change.
- **Every tool keeps its own size.** Set the eraser to twenty and the pen-xel is still one
  pixel when you go back to it.
- **A brush that is not a pen-xel.** The pen-xel writes one exact colour into one exact pixel,
  always — it only ever draws pixels, which is how it got the name. The brush adds a shape, a
  size, a soft edge and stroke stabilisation, with a live preview of the tip in the Brush panel
  and its footprint outlined on the canvas as you move.
- **Editable text, as a text field.** Click with the text tool and type on the canvas, inside
  a dashed frame that says you are typing rather than drawing. Drag across the glyphs to select
  characters, hold `Shift` with the arrow keys to extend the range, `Ctrl` `A` for all of it —
  then type over it, delete it, or copy and cut it as words. Grab the frame to slide the whole
  block, or move it with the move tool; double-click any text with any tool in hand and the
  caret drops into it. The words stay words: a text layer can be re-opened and rewritten at any
  time, and the Text panel carries font, size, bold, italic, underline, strikethrough,
  alignment, line spacing, colour and an anti-aliasing switch that is off by default because
  sprite text wants hard pixels.
- **Animation.** A timeline of frames across the bottom, with a cell per layer per frame, so
  a background can hold still while only the legs move. Duplicate a frame to draw the next
  one, drag frames to reorder, set a hold time per frame or a frame rate for all of them, and
  play it back in place. **Onion skinning** ghosts the neighbouring frames underneath — red
  behind, green ahead — which is how a walk cycle actually gets lined up. It is on by
  default, so the frame before shows through as soon as there is one; a single-frame image is
  unaffected.
- **Named animations in one file.** Cut "idle", "walk" and "attack" out of one timeline as
  tagged frame ranges, each with its own loop direction including ping-pong. One `.xpxel` is
  one character, and export gives you one clip per motion. Every document starts with one
  animation already in it, the way it starts with one layer, and an animation runs **every
  frame** — including the ones you add afterwards — until you untick **All frames** and pick a
  range yourself. Frames are numbered from 1 everywhere, in the timeline and in the range
  boxes alike.
- **Export that a game engine reads.** A **sprite sheet** on a uniform grid with a `.json`
  sidecar — Unity slices it with Grid By Cell Size, and the JSON matches the Aseprite format
  that Godot, Phaser and most importers already understand. Also numbered **PNG frames**, an
  animated **GIF** for showing people, and an **APNG** for the same without losing a colour.
- **Selections** — drag a rectangular marquee and every tool is clipped to it, with Select
  All, Deselect and Invert in the Select menu. `Esc` clears a selection, so does clicking the
  empty space beside the picture, and `Delete` or `Backspace` erases what is marked.
- **Free transform.** `Ctrl` `T` puts a box with eight handles around what is marked — or
  around everything on the marked layers when nothing is marked. Drag a handle to scale — the
  proportions are held by default, and `Shift` lets go of them — drag one past its opposite edge
  to flip, and press `R` to turn a quarter turn (`H` and `V` flip outright). The handles are
  exactly the eight squares you can see, and there is a ninth in the middle for moving the
  artwork itself, which always wins over the edges however small the sprite is. Scaling is
  nearest-neighbour, so a doubled sprite is made of four-pixel blocks in the palette it started
  with, and rotation is offered in quarter turns only because no other angle survives a pixel
  grid. `Enter` applies the whole episode as one undo step and clears the marks; `Escape` puts
  everything back. **Transform Selection**, on the Select menu and the canvas menu, does the
  same to the marching ants alone and leaves the artwork exactly where it is — for when the
  shape you marked is right but the size is not.
- **A transform never costs you a drawing.** Apply one on top of artwork that is already there
  and the result lands on a new layer above it, so nothing underneath is written over — and a
  paste does the same, arriving on a layer of its own. Both are on by default and both are
  switchable, from the gear button in the layers panel or from Settings ▸ Layers. Artwork
  pushed over the canvas edge by a transform is kept as well, hidden rather than cut, exactly
  as it is when a layer is dragged half off the side.
- **Copy, cut and paste, in and out of the editor.** `Ctrl` `C` and `Ctrl` `X` take what is
  marked — the exact pixels, alpha and all, and the shape of a selection that is not a
  rectangle. What lands on the system clipboard is an ordinary image as well, so it pastes
  into a chat window or another editor. `Ctrl` `V` takes an XP-xel clip, an image copied from
  anywhere else, or **text**, which arrives as an editable text layer rather than as pixels.
  Pasted artwork lands **floating, inside a transform box**: drag it, pull a handle to resize
  it, nudge it with the arrow keys, place it with `Enter`, or press `Esc` and it was never
  there. Something cut comes back where it was cut from, which is what makes "cut, change
  layer, paste" mean what it looks like.
- **Undo and redo** for every edit, with a stroke, a shape or a whole episode of typing each
  counting as one step.
- **Canvas**: nearest-neighbour zoom to 64×, hold **Space** to grab and drag the image, a
  transparency checkerboard that can be switched off when it is more noise than information,
  a second checkerboard that can be laid **over** the artwork as a texture — masked to the
  pixels you actually painted, and switched on and off separately — a pixel grid that appears
  as you zoom in, a highlight on the pixel under the pointer, and
  **rulers** down the top and left edges numbered from 1, with the pointer's own coordinate in
  the status bar.
- **Canvas size, both ways.** **Image → Canvas Size** crops a 32×32 down to 16×32 or grows it
  to 64×64, with a nine-way anchor deciding which part of the artwork keeps its place. **Crop to
  Selection** does the same from a marquee, and the **crop tool** (`C`) puts a frame with handles
  around the picture that drags in to trim or out to enlarge. The pointer opens into a hand over
  the frame and closes while you drag it, and the ground outside the frame stays ruled and
  visible under a dimming wash, so what you would be adding or trimming is in front of you.
- **Move what you mean.** With something marked, the move tool moves *that* rather than the
  layer under it, and so does a drag begun on the marching ants themselves — the pointer turns
  into the four-way arrow as it crosses the edge, whichever tool is in hand, so a mark can be
  made and moved without a trip back to the toolbox. With nothing marked, `V` moves the whole
  layer as it always has.
- **Nothing is ever destroyed by moving it.** Move a layer, drag marked pixels, or apply a
  transform, and whatever leaves the canvas is kept — hidden rather than cut, exactly as a shape
  dragged half off a slide stays on the slide. Drag it back, grow the canvas, or open the crop
  tool, which shows you what is out there. **The only things that delete pixels are the eraser
  and the commands that say so**: `Delete`, `Backspace`, Cut, and the canvas menu's *Delete
  marked pixels*.
- **Selections that are not boxes.** The pixel tool (`K`) marks pixels one at a time, however far
  apart, and the wand (`W`) marks a whole area of one colour. `Shift` adds and `Ctrl` takes away
  on every selection tool, the marching ants trace the actual edge rather than a bounding box, and
  `Ctrl`-clicking a layer's preview marks everything that layer paints.
- **Colour**: a spectrum picker — a saturation-and-value square beside a hue strip, the
  layout every image editor uses — sitting directly under the palette and wired into it. The
  palette is at the top of the panel, its first ten swatches a **hotbar** carrying the numbers
  1–9 and 0 so the colour you want is one keystroke away, and the swatch you are working on is
  ringed in blue inside white. The editor below edits *that* swatch: move a slider and the
  swatch and the brush follow it as it moves, with no "save to palette" step to remember. Hue,
  saturation, value and alpha, a hexadecimal field for an exact value, a shading ramp that
  shifts shadows towards blue and highlights towards yellow the way light actually behaves, and
  the colour's complement and analogous hues.
- **The drawing colour follows the pointer** as a small round dot attached to the cursor
  itself, so you can see what a stroke will lay down without looking away from the canvas.
  **View → Show Colour by the Cursor** turns it off.
- **Text is drawn with a font built for pixel art.** The default font ships inside XP-xel and
  is made of pixels rather than outlines, so at every size it offers it is exactly crisp and
  exactly legible — no smoothing to clean up, and the same on every machine. The system fonts
  are still there for larger type, held at ten pixels and above, which is where they stop
  holding together without anti-aliasing.
- **The History panel** lists what you have done and takes you back to any of it in one click.
  How many actions to keep is yours to set, in the panel's footer — each one holds the pixels
  it changed, so the depth is a trade between memory and regret.
- **Your work is backed up every three minutes.** The last three copies of every open document
  are kept as ordinary project files in a folder you can open from the File menu, so a crash
  costs minutes rather than an afternoon. A backup never touches your own file: recovering is
  opening the copy.
- **Files**: `.xpxel` projects that preserve everything, plus PNG import and export with
  integer upscaling.
- **Light and dark themes**, following the operating system by default.

## Getting started

XP-xel needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
git clone https://github.com/SaschaLurz/xp-xel.git
cd xp-xel

dotnet build                                  # build everything
dotnet test                                   # run the test suite
dotnet run --project src/XPXel.Desktop        # launch the editor
```

To produce a standalone build for a particular operating system, publish the desktop head
with the matching runtime identifier — `win-x64`, `linux-x64`, `osx-arm64`, and so on:

```bash
dotnet publish src/XPXel.Desktop -c Release -r win-x64
```

To build the browser version, publish the WebAssembly head and serve the resulting folder
as static files:

```bash
dotnet publish src/XPXel.Browser -c Release -o out/browser
```

## Using the editor

| Action | Input |
| --- | --- |
| Pen-xel / brush / eraser | `Q` / `B` / `E` |
| Reach another tool in the same square | Right-click the square — the folded corner marks one |
| Bucket fill / colour picker | `G` / `I` |
| Line / rectangle / ellipse | `L` / `R` / `O` |
| Text / rectangular marquee | `T` / `M` |
| Mark individual pixels / magic wand | `K` / `W` |
| Add to a selection / take away from it | Hold `Shift` / hold `Ctrl`, on any selection tool |
| Mark everything on a layer | `Ctrl`-click its preview in the layers panel |
| Move what you have marked | Drag its edge, or press inside it and drag |
| Move the artwork inside a transform box | Drag the handle in its middle, or anywhere else inside it |
| Scale, flip or turn what is marked | `Ctrl` `T`, then drag the handles; `Enter` applies, `Escape` cancels |
| Transform the marked layers instead | `Ctrl` `T` with nothing marked on the canvas |
| Let go of the proportions while scaling | Hold `Shift` on a corner handle — they are held otherwise |
| Turn a quarter turn / flip, mid-transform | `R` (`Shift` `R` the other way) / `H` and `V` |
| Reshape the marks without moving the artwork | Select → Transform Selection, or the canvas menu |
| Copy / cut / paste what you have marked | `Ctrl` `C` / `Ctrl` `X` / `Ctrl` `V` |
| Resize something pasted before placing it | Drag a handle of the box it arrives in |
| Place or abandon something pasted | `Enter` / `Esc` |
| Erase what you have marked | `Delete`, `Backspace`, or the canvas menu |
| Clear the selection | `Enter`, `Esc`, a click beside the picture, or `Ctrl` `D` |
| Move a whole layer | `V`, then drag, with nothing marked |
| Draw on several layers at once | Mark them in the layers panel; a badge by the pointer counts them |
| Crop | `C`, drag the frame's handles, then `Enter` — the pointer shows which way each edge moves |
| Draw | Left mouse button |
| Open the active tool's settings | Right-click the canvas |
| Pan the canvas | Hold `Space` and drag, or middle mouse button drag |
| Zoom | Mouse wheel, or `Ctrl` `+` / `Ctrl` `-` |
| Fit to window / actual size | `Ctrl` `0` / `Ctrl` `1` |
| Undo / redo | `Ctrl` `Z` / `Ctrl` `Y` |
| Swap the primary and secondary colours | `X` |
| Pick a palette colour | `1`–`9` and `0` for the first ten swatches |
| Edit a colour on the spectrum | Click its swatch; the editor below the palette follows it |
| Add the current colour to the palette, or remove one | `+` / `−` beside the palette heading |
| Save the panel arrangement, or return to one | View → Layouts |
| New / open / save / export | `Ctrl` `N` / `O` / `S` / `E` |
| Export the animation | `Ctrl` `Shift` `E` |
| Close the current document | `Ctrl` `W` |
| Open the folder holding the autosaved backups | File → Open Backups Folder |
| Constrain a shape to a square, circle or 45° line | Hold `Shift` while dragging |
| Draw a perfectly straight run | Hold `Shift` while drawing; let go and press it again to turn a corner |
| Change tool without letting go | Press the new tool's key mid-stroke |
| Move the selected pixels | Select → Move Selected Pixels, then drag or use the arrow keys |
| Change any setting there is | The **Settings** button at the right-hand end of the menu bar |
| Change what happens to a layer when artwork lands on it | The gear button in the layers panel, or Settings ▸ Layers |
| Show or hide the pixel grid | The **Grid** box on the options bar, or the View menu |
| Lay the checkerboard over the artwork | The **On art** box on the options bar, or the View menu |
| Change how far one zoom step travels | The **Zoom speed** slider on the options bar |
| Go back several steps at once | Click that point in the History panel |
| Select all / deselect / invert the selection | `Ctrl` `A` / `Ctrl` `D` / `Ctrl` `Shift` `I` |
| Crop or enlarge the canvas | `Ctrl` `Alt` `C` |
| Finish a piece of text | Click elsewhere on the canvas, or pick another tool |
| Abandon a piece of text | `Escape` |
| Edit text you drew earlier | Double-click it, whatever tool is in hand |
| Select characters | Drag across them, or `Shift` with the arrow keys, or `Ctrl` `A` |
| Move a block of text | Drag its frame, or use the move tool |
| Sample the active layer only | Hold `Alt` with the colour picker |
| Rename a layer or folder | Double-click its name, or right-click it |
| Restack a layer | Drag it up or down the list |
| Duplicate a layer | Drag it onto the **+** button |
| Mark several layers | `Ctrl`-click to add one, `Shift`-click for a run |
| Put a layer in a new folder | Drag it onto the **folder** button |
| Put several layers in one folder | Mark them, then drag any of them onto the **folder** button |
| Move a layer into an existing folder | Drag it onto the folder's row |
| Delete the layer you are dragging | Drag it onto the **✕** button |
| Label a layer or folder with a colour | Right-click it |
| Put a white background behind everything | The **background** button in the layers panel |
| Make any layer the same on every frame | Right-click it → **Same on Every Frame** |
| Spread this frame's drawing to all frames | Right-click the layer or the timeline cell → **Copy to All Frames** |
| Copy a whole layer, artwork and all | Right-click it → **Copy Layer**, then **Paste Layer** |
| Do something to one drawing on one frame | Right-click its cell in the timeline |
| Merge layers into one | Mark them, right-click, **Merge Layers** — every frame, exactly as they look |
| Delete one layer or every marked one | Right-click it and choose **Delete**, or use the bin button |

Panels are moved by dragging their title bar. Drop one on an edge of another panel to dock
it there, on the middle to tab it together, or anywhere outside the window to float it.

## Project layout

```
src/
  XPXel.Core       The document model - buffers, layers, frames, cels, tools, undo.
                   Depends on nothing but the BCL, so it is fully unit-testable.
  XPXel.Formats    File formats: the PNG codec and the .xpxel project archive.
  XPXel.UI         The Avalonia application - views, view models, the canvas control.
  XPXel.Desktop    Windows / Linux / macOS entry point.
  XPXel.Browser    WebAssembly entry point.
tests/             Unit tests for Core and Formats.
docs/              Architecture notes.
```

Read [`docs/architecture.md`](docs/architecture.md) before making a substantial change. It
explains the decisions that are load-bearing — in particular why documents are modelled as
layers × frames from the start, even though the animation UI does not exist yet.

## Contributing

Contributions are welcome. A few things worth knowing:

- The build treats warnings as errors and enforces the official .NET coding conventions
  through `.editorconfig`. If the build complains about style, fix the code rather than
  suppressing the rule.
- Comments should explain *why*, not restate *what*. Every non-obvious decision in this
  codebase has a comment giving its reasoning, and that is deliberate — it is what makes the
  project approachable to a new contributor.
- Adding a tool or a file format should not require editing existing files. If it does,
  that is a design problem worth raising.
- Please add tests for anything in `XPXel.Core` or `XPXel.Formats`; both run headless.

## Roadmap

Roughly in the order these are likely to be tackled:

- **More selection tools** — lasso and magic wand, and moving a selection's contents. The
  rectangular marquee proved the plumbing; these are new mask producers, nothing more.
- **More tools** — gradient, dithering, transform.
- **More formats** — GIF, BMP, JPEG, and Aseprite import; indexed PNG output.
- **Image resampling** — scaling the artwork itself, with nearest-neighbour and the pixel-art
  upscalers. Canvas Size deliberately only changes how much room there is; resizing what is
  drawn is a separate, destructive operation with its own choices to make.
- **Dragging a text block to reposition it**, rather than deleting it and clicking again.
- **Copying cels between frames and layers** by dragging them around the timeline.
- **Importing a sprite sheet** back into frames, so existing artwork can be brought in.
- **Restoring floating panels as floating** — the arrangement is remembered, but a panel
  that was torn off into its own window comes back docked in the next session.

## Built with

[Avalonia](https://avaloniaui.net/) on .NET 10, with [Dock](https://github.com/wieslawsoltes/Dock)
for the movable panels. Avalonia is the only mature .NET UI framework covering desktop,
mobile and the browser from one codebase, and it renders through Skia — which is exactly the
control a pixel editor needs over how its pixels reach the screen.

## Licence

Not yet chosen.
