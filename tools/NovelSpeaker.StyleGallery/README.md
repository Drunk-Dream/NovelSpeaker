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
dotnet run --project tools/NovelSpeaker.StyleGallery/NovelSpeaker.StyleGallery.csproj -c Release -- --screenshot --scene navigation-feedback --theme all --output artifacts/visual-review/gallery/navigation
```

The command exits after writing `manifest.json` and one PNG per selected scene/theme. `--scene` accepts a stable scene ID from `GallerySceneRegistry`; omitting it renders all registered scenes. The list fixture includes independent selected/current-playback/hover/focus/disabled states, long-title trimming with tooltips, accessible names, a virtualized selection host and shared EmptyState fixtures. The navigation-feedback fixture includes explicit Provider-based navigation entries, grouped neutral/danger menus, distinct ProgressBar/Slider controls, raised flyout/dialog and menu surfaces, a non-blocking Snackbar, Escape/default/cancel semantics and Loading/Error/NoResult states. The manifest records the stable artifact ID, scene, theme, fixed size, 96 DPI and SHA-256 for every PNG.
