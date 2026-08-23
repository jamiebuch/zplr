# Code and WYSIWYG editor

Open `/editor` in the local web app to edit ZPL source and the rendered label in one synchronized workspace. Source remains authoritative: every visual action produces an undoable source edit, and source edits immediately rerender the designer.

## Visual layout

- Click a layer, Shift-click additional layers, or drag a marquee to select several fields.
- Drag or use the arrow keys to move the selection. Arrow keys move by the configured snap amount; Shift uses a larger step.
- Use **Arrange** to align or distribute selected layers. Object edges, centers, the label, and manual guides can act as snap targets.
- Drag from a ruler to add a guide. Double-click a guide, or use its context action, to remove it.
- Locking prevents source-changing visual operations. Hiding affects only the designer and is stored as editor metadata in the ZPL.
- Resizeable fields expose resize cursors at their edges and corners instead of persistent handles. Double-click text to edit its field data directly on the label.

Layers and Properties share the right panel. Deselecting a field returns to Layers. Selecting a rendered field or source command scrolls and temporarily selects the corresponding source span.

## Variable data

Expand **Data** in the left sidebar to choose a dataset or preview record. The section starts collapsed. Use **Create or import data** or **Edit data** to open the focused data editor for datasets, columns, and records. Columns bind to numbered `^FN` fields; prompts such as `^FN1"Customer"` become column labels where possible.

The sidebar record navigator switches the live preview without changing the ZPL. Editing bound text on the label updates the active record. The active dataset can also be removed there after confirmation. CSV export stays in the Data section, and **Export PNGs** appears once the active dataset has records and at least one column is linked to a `^FN` placeholder. PNG export produces a ZIP for up to 500 records.

## Images and fonts

Open **Assets** to manage printer resources.

- PNG, JPEG, WebP, GIF, BMP, or SVG images can be resized and converted with threshold, Bayer, or Floyd–Steinberg dithering. Insert them as compressed `~DG` + `^XG` resources or inline `^GFA` data.
- TrueType/OpenType imports are validated before being encoded as `~DY`; `^CW` registers a one-character font identifier. Select a text field to apply that font while retaining its orientation and dimensions.
- Rename updates definitions and references atomically. Deleting removes the definition but intentionally leaves uses visible through missing-resource diagnostics.

Original imported files are stored in IndexedDB and included in workspace archives so they can be recovered or edited later.

## Files and portability

`Cmd/Ctrl+S` downloads the active ZPL file. `Cmd/Ctrl+Shift+S` downloads a `.zip` workspace containing all open labels, datasets, guides, resource metadata, and original assets. Open or drop that archive to restore the workspace.

Workspace import rejects unsupported manifests and enforces limits of 100 labels, 64 MB expanded data, 32 MB per entry, and 8 MB per ZPL source.

## Sharing

The **Share** button (or `Cmd/Ctrl+Shift+L`) copies a self-contained link of the form `https://zplr.de/editor#s=<token>` to the clipboard. The token compresses the active label's ZPL source and, when present, its bound variable data — nothing is uploaded. Opening the link loads the label into a fresh editor tab on the recipient's device.

Shared links embed source only. Imported images and fonts live in the sender's browser storage and are not part of the link, so a shared label that references them renders with missing-resource diagnostics until those assets are re-imported. Labels whose compressed token exceeds the URL size limit cannot be shared as a link; save them as a workspace archive instead.

## Rotation

Text, barcode, and QR fields can be rotated in 90° steps with **Rotate clockwise** / **Rotate counterclockwise** in the **Arrange** menu or the `R` / `Shift+R` shortcuts. Rotation edits the field's ZPL orientation parameter, cycling its documented values in the order N → R → I → B, so the source stays authoritative and the change is undoable like every other visual edit. The Properties panel exposes the same orientation as a select.

Fields without an orientation parameter — boxes, circles, ellipses, and lines — cannot be rotated; the rotate actions stay disabled for them. Locked fields are skipped when a selection mixes rotatable and non-rotatable layers.

## Shortcuts

| Action | Shortcut |
| --- | --- |
| Show editor help | `?` |
| New / open / save label | `Cmd/Ctrl+N`, `Cmd/Ctrl+O`, `Cmd/Ctrl+S` |
| Save workspace | `Cmd/Ctrl+Shift+S` |
| Copy share link | `Cmd/Ctrl+Shift+L` |
| Select all visual layers | `Cmd/Ctrl+A` while the designer is focused |
| Copy / paste / duplicate layers | `Cmd/Ctrl+C`, `Cmd/Ctrl+V`, `Cmd/Ctrl+D` |
| Delete selected layers | `Backspace` or `Delete` |
| Move selected layers | Arrow keys |
| Rotate selected layers 90° | `R` clockwise, `Shift+R` counterclockwise |
| Undo / redo visual changes | `Cmd/Ctrl+Z`, `Cmd/Ctrl+Shift+Z` |
| Render now | `Cmd/Ctrl+Enter` |
| Command palette | `Cmd/Ctrl+P` |
| Format source | `Cmd/Ctrl+Shift+F` |
| Deselect / close dialogs | `Escape` |
