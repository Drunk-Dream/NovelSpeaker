# NovelSpeaker Style Gallery

The Style Gallery is an isolated WPF development and visual-review host. It only uses Wpf.Ui resources, the shared palette/token dictionaries and deterministic in-memory fixture text; it is not referenced by the production application and is not part of the production publish graph.

From the repository root, generate both themes and the task 8 media-control scene with:

```powershell
dotnet run --project tools/NovelSpeaker.StyleGallery/NovelSpeaker.StyleGallery.csproj -c Release -- --screenshot --task 08 --scene media-controls --theme all --output artifacts/visual-review/08
```

The command exits after writing `manifest.json` and one PNG per selected scene/theme. The task must be selected explicitly (`03` through `08`); omitting `--task` preserves the existing task `03` default and output directory. Use `--scene media-controls` to render only the task 8 media fixture. The slider tooltip displays a deterministic `x / y` projection, and changing its value updates only the Gallery fixture. The manifest records the selected task, scene, theme, fixed size, 96 DPI and SHA-256 for every PNG.
