# NovelSpeaker Style Gallery

The Style Gallery is a WPF development and visual-review host. It consumes the production App's shared theme dictionaries and reusable component primitives together with deterministic in-memory fixture text; the production application does not reference the Gallery, and the Gallery is not part of the production publish graph.

The interactive scene selector groups concrete scenes into Theme foundations, Standard controls and Component families. Each screenshot is addressed by its stable scene/family name; the Gallery is not coupled to backlog numbering.

From the repository root, generate both themes for every registered scene with:

```powershell
dotnet run --project tools/NovelSpeaker.StyleGallery/NovelSpeaker.StyleGallery.csproj -c Release -- --screenshot --theme all --output artifacts/visual-review/gallery
```

Generate one stable resource family into its own directory with:

```powershell
dotnet run --project tools/NovelSpeaker.StyleGallery/NovelSpeaker.StyleGallery.csproj -c Release -- --screenshot --scene button-styles --theme all --output artifacts/visual-review/gallery/buttons
```

```powershell
dotnet run --project tools/NovelSpeaker.StyleGallery/NovelSpeaker.StyleGallery.csproj -c Release -- --screenshot --scene media-controls --theme all --output artifacts/visual-review/gallery/media
```

```powershell
dotnet run --project tools/NovelSpeaker.StyleGallery/NovelSpeaker.StyleGallery.csproj -c Release -- --screenshot --scene progress --theme all --output artifacts/visual-review/gallery/progress
```

The command exits after writing `manifest.json` and one PNG per selected scene/theme. `--scene` accepts a stable scene ID from `GallerySceneRegistry`; omitting it renders all registered scenes. The progress fixture keeps ProgressBar and Slider type boundaries measurable, the media fixture covers playback, volume, window actions and deterministic slider projection, and the feedback fixture covers Popup, InlineMessage, Validation and Snackbar content styles without creating host controls. The manifest records the stable artifact ID, scene, theme, fixed size, 96 DPI and SHA-256 for every PNG.
