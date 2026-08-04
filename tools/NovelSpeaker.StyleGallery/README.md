# NovelSpeaker Style Gallery

The Style Gallery is an isolated WPF development and visual-review host. It only uses Wpf.Ui resources, the shared palette/token dictionaries and deterministic in-memory fixture text; it is not referenced by the production application and is not part of the production publish graph.

From the repository root, generate both themes and all registered scenes for the explicit review task with:

```powershell
dotnet run --project tools/NovelSpeaker.StyleGallery/NovelSpeaker.StyleGallery.csproj -c Release -- --screenshot --task 06 --theme all --output artifacts/visual-review/06
```

The command exits after writing `manifest.json` and one PNG per scene/theme. The task must be selected explicitly (`03`, `04`, `05` or `06`); omitting `--task` preserves the existing task `03` default and output directory. Use `--scene token-components` to render the new PageHeader, SectionSurface and StatusView samples while iterating. The manifest records the selected task, scene, theme, fixed size, 96 DPI and SHA-256 for every PNG.
