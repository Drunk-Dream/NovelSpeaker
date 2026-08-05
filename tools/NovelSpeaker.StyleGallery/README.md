# NovelSpeaker Style Gallery

The Style Gallery is an isolated WPF development and visual-review host. It only uses Wpf.Ui resources, the shared palette/token dictionaries and deterministic in-memory fixture text; it is not referenced by the production application and is not part of the production publish graph.

From the repository root, generate both themes and a task 7, 8, 11, 12 or 13 scene with:

```powershell
dotnet run --project tools/NovelSpeaker.StyleGallery/NovelSpeaker.StyleGallery.csproj -c Release -- --screenshot --task 07 --scene button-styles --theme all --output artifacts/visual-review/07
```

```powershell
dotnet run --project tools/NovelSpeaker.StyleGallery/NovelSpeaker.StyleGallery.csproj -c Release -- --screenshot --task 08 --scene media-controls --theme all --output artifacts/visual-review/08
```

```powershell
dotnet run --project tools/NovelSpeaker.StyleGallery/NovelSpeaker.StyleGallery.csproj -c Release -- --screenshot --task 12 --scene list-components --theme all --output artifacts/visual-review/12
```

```powershell
dotnet run --project tools/NovelSpeaker.StyleGallery/NovelSpeaker.StyleGallery.csproj -c Release -- --screenshot --task 13 --scene navigation-feedback --theme all --output artifacts/visual-review/13
```

The command exits after writing `manifest.json` and one PNG per selected scene/theme. The task must be selected explicitly (`03` through `08`, `11`, `12` or `13`); omitting `--task` preserves the existing task `03` default and output directory. Use `--scene button-styles` for the task 7 button fixture, `--scene media-controls` for the task 8 media fixture, `--scene input-controls` for task 11, `--scene list-components` for task 12, or `--scene navigation-feedback` for task 13. The list fixture includes independent selected/current-playback/hover/focus/disabled states, long-title trimming with tooltips, accessible names, a virtualized selection host and Gallery-only EmptyState fixtures. The navigation-feedback fixture includes explicit Provider-based navigation entries, grouped neutral/danger menus, distinct ProgressBar/Slider controls, raised flyout/dialog and menu surfaces, a non-blocking Snackbar, Escape/default/cancel semantics and Loading/Error/NoResult states. The manifest records the selected task, scene, theme, fixed size, 96 DPI and SHA-256 for every PNG.
