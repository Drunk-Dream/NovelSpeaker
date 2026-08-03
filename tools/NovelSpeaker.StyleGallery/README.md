# NovelSpeaker Style Gallery

The Style Gallery is an isolated WPF development and visual-review host. It only uses Wpf.Ui resources and deterministic in-memory fixture text; it is not referenced by the production application and is not part of the production publish graph.

From the repository root, generate both themes and all registered scenes with:

```powershell
dotnet run --project tools/NovelSpeaker.StyleGallery/NovelSpeaker.StyleGallery.csproj -c Release -- --screenshot --theme all --output artifacts/visual-review/03
```

The command exits after writing `manifest.json` and one PNG per scene/theme. Use `--scene provider-controls` to render one scene while iterating. The manifest records the scene, theme, fixed size, 96 DPI and SHA-256 for every PNG.
