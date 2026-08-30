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
  edge, tabbed together with another panel, pinned, closed, or torn off into its own
  floating window. Images open as document tabs, several at a time. The arrangement is
  remembered between sessions, and **View → Panels → Reset Layout** puts it back.
- **Layers**, with per-layer opacity, visibility, locking, fourteen W3C-standard blend
  modes, a live preview of each layer's own contents, colour labels, and drag-and-drop
  restacking. Dropping a layer on the **+** button duplicates it.
- **Tools**: pencil, eraser, bucket fill (with tolerance, contiguous or global), and a
  colour picker that samples the flattened image.
- **Undo and redo** for every edit, with a stroke counting as one step.
- **Canvas**: nearest-neighbour zoom to 64×, hold **Space** to grab and drag the image, a
  transparency checkerboard, and a pixel grid that appears as you zoom in.
- **Colour**: RGBA channel editor, hexadecimal input, and a per-document palette.
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
| Draw with the primary / secondary colour | Left / right mouse button |
| Pan the canvas | Hold `Space` and drag, or middle mouse button drag |
| Zoom | Mouse wheel, or `Ctrl` `+` / `Ctrl` `-` |
| Actual size / fit to window | `Ctrl` `0` / `Ctrl` `1` |
| Undo / redo | `Ctrl` `Z` / `Ctrl` `Y` |
| Swap colours | `X` |
| New / open / save / export | `Ctrl` `N` / `O` / `S` / `E` |
| Close the current document | `Ctrl` `W` |
| Sample the active layer only | Hold `Alt` with the colour picker |
| Rename a layer | Double-click its name |
| Restack a layer | Drag it up or down the list |
| Duplicate a layer | Drag it onto the **+** button |
| Label a layer with a colour | Right-click it |

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

- **Selections** — rectangular marquee, lasso, magic wand. The tool contract already carries
  a selection mask, so the tools themselves need no changes.
- **Animation** — a timeline over the frames the document model already has, with onion
  skinning and animated PNG or sprite-sheet export.
- **More tools** — line, rectangle and ellipse, gradient, dithering, transform.
- **More formats** — GIF, BMP, JPEG, and Aseprite import; indexed PNG output.
- **Game-engine export** — sprite sheets with metadata that imports cleanly into Unity,
  Godot and Unreal.
- **Layer folders** — the document model and compositor already support them; they need a
  UI.
- **Restoring floating panels as floating** — the arrangement is remembered, but a panel
  that was torn off into its own window comes back docked in the next session.

## Built with

[Avalonia](https://avaloniaui.net/) on .NET 10, with [Dock](https://github.com/wieslawsoltes/Dock)
for the movable panels. Avalonia is the only mature .NET UI framework covering desktop,
mobile and the browser from one codebase, and it renders through Skia — which is exactly the
control a pixel editor needs over how its pixels reach the screen.

## Licence

Not yet chosen.
