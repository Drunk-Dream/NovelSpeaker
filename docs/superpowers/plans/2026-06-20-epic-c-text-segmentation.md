# Epic C Text Segmentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first runtime text-segmentation slice for NovelSpeaker: convert a persisted `Chapter` into ordered `SpeechSegment` items, support configurable long-paragraph splitting, and expose the settings through the desktop settings page.

**Architecture:** Keep segmentation dynamic and runtime-only. `ITextSegmenter` turns `Chapter.Content` into `SpeechSegment[]` using `TextSegmentationOptions`, while a minimal settings store provides global options without coupling view models or the segmenter to raw JSON. The WPF settings page stays thin and only edits the two Epic C options confirmed in the approved design.

**Tech Stack:** C#, .NET 10, WPF, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, System.Text.Json, xUnit

---

## Scope Check

This plan covers one subsystem: runtime text segmentation plus the minimal settings infrastructure required to drive it. It does not include no-chapter fallback, playback coordination, reading-progress persistence, or any HTTP TTS work.

## File Structure

### New files

- `src/NovelSpeaker.Application/Books/ITextSegmenter.cs`
- `src/NovelSpeaker.Application/Books/ITextSegmentationOptionsProvider.cs`
- `src/NovelSpeaker.Domain/Books/SpeechSegment.cs`
- `src/NovelSpeaker.Domain/Books/TextSegmentationOptions.cs`
- `src/NovelSpeaker.Application/Settings/IAppSettingsStore.cs`
- `src/NovelSpeaker.Domain/Settings/AppSettings.cs`
- `src/NovelSpeaker.Infrastructure/Books/Parsing/TextSegmenter.cs`
- `src/NovelSpeaker.Infrastructure/Settings/JsonAppSettingsStore.cs`
- `tests/NovelSpeaker.UnitTests/Books/TextSegmenterTests.cs`
- `tests/NovelSpeaker.UnitTests/Settings/JsonAppSettingsStoreTests.cs`
- `tests/NovelSpeaker.UnitTests/ViewModels/SettingsViewModelTests.cs`

### Modified files

- `src/NovelSpeaker.Application/Abstractions/IAppDataDirectoryProvider.cs`
- `src/NovelSpeaker.Infrastructure/FileSystem/LocalAppDataDirectoryProvider.cs`
- `src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- `src/NovelSpeaker.App/ServiceCollectionExtensions.cs`
- `src/NovelSpeaker.App/ViewModels/SettingsViewModel.cs`
- `src/NovelSpeaker.App/Views/SettingsView.xaml`
- `src/NovelSpeaker.App/Views/SettingsView.xaml.cs`
- `tests/NovelSpeaker.UnitTests/DependencyInjection/ServiceCollectionExtensionsTests.cs`

### Responsibilities

- `SpeechSegment.cs`
  Defines the runtime segment model with chapter-content offsets.
- `TextSegmentationOptions.cs`
  Defines the two Epic C segmentation options and their defaults.
- `ITextSegmenter.cs`
  Defines the runtime segmentation boundary.
- `ITextSegmentationOptionsProvider.cs`
  Allows application consumers to read current segmentation options without knowing how they are stored.
- `AppSettings.cs`
  Defines the JSON-backed desktop settings document for this stage.
- `IAppSettingsStore.cs`
  Defines load/save behavior for non-sensitive settings.
- `TextSegmenter.cs`
  Implements newline-based paragraph splitting, sentence-boundary splitting on `。！？`, and hard-cut fallback.
- `JsonAppSettingsStore.cs`
  Reads and writes `settings.json`, supplies defaults, and clamps invalid threshold values.
- `SettingsViewModel.cs`
  Loads, edits, and saves the two segmentation settings.
- `SettingsView.xaml`
  Exposes the new settings controls in the existing Settings page shell.
- `SettingsView.xaml.cs`
  Triggers initial settings loading when the view is shown.

---

### Task 1: Add Epic C domain types and service interfaces

**Files:**
- Create: `src/NovelSpeaker.Domain/Books/SpeechSegment.cs`
- Create: `src/NovelSpeaker.Domain/Books/TextSegmentationOptions.cs`
- Create: `src/NovelSpeaker.Application/Books/ITextSegmenter.cs`
- Create: `src/NovelSpeaker.Application/Books/ITextSegmentationOptionsProvider.cs`
- Test: `tests/NovelSpeaker.UnitTests/Books/TextSegmenterTests.cs`

- [ ] **Step 1: Write the failing contract test**

Create `tests/NovelSpeaker.UnitTests/Books/TextSegmenterTests.cs` with:

```csharp
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class TextSegmenterTests
{
    [Fact]
    public void Segment_returns_single_segment_for_short_single_line_paragraph()
    {
        var chapter = new Chapter(
            "chapter-1",
            "book-1",
            0,
            "第一章",
            "这一段很短，不需要拆分。",
            0,
            "这一段很短，不需要拆分。".Length);

        var options = new TextSegmentationOptions(
            EnableLongParagraphSplitting: true,
            LongParagraphThreshold: 300);

        ITextSegmenter segmenter = new Infrastructure.Books.Parsing.TextSegmenter();

        var segments = segmenter.Segment(chapter, options);

        Assert.Single(segments);
        Assert.Equal(0, segments[0].SegmentIndex);
        Assert.Equal(0, segments[0].StartOffset);
        Assert.Equal(chapter.Content.Length, segments[0].Length);
        Assert.Equal(chapter.Content, segments[0].DisplayText);
        Assert.Equal(chapter.Content, segments[0].SpeechText);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter Segment_returns_single_segment_for_short_single_line_paragraph
```

Expected: FAIL because `ITextSegmenter`, `TextSegmentationOptions`, `SpeechSegment`, and `TextSegmenter` do not exist yet.

- [ ] **Step 3: Add the core Epic C types**

Create `src/NovelSpeaker.Domain/Books/SpeechSegment.cs`:

```csharp
namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Represents one runtime speech unit mapped to a range in <see cref="Chapter.Content"/>.
/// </summary>
public sealed record SpeechSegment(
    int SegmentIndex,
    int StartOffset,
    int Length,
    string DisplayText,
    string SpeechText);
```

Create `src/NovelSpeaker.Domain/Books/TextSegmentationOptions.cs`:

```csharp
namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Global runtime options that control how chapter text is split into speech segments.
/// </summary>
public sealed record TextSegmentationOptions(
    bool EnableLongParagraphSplitting,
    int LongParagraphThreshold)
{
    public const int DefaultLongParagraphThreshold = 300;
    public const int MinimumLongParagraphThreshold = 50;

    public static TextSegmentationOptions Default { get; } =
        new(true, DefaultLongParagraphThreshold);

    public TextSegmentationOptions Normalize()
    {
        var threshold = LongParagraphThreshold < MinimumLongParagraphThreshold
            ? MinimumLongParagraphThreshold
            : LongParagraphThreshold;

        return this with { LongParagraphThreshold = threshold };
    }
}
```

Create `src/NovelSpeaker.Application/Books/ITextSegmenter.cs`:

```csharp
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>
/// Converts one persisted chapter into ordered runtime speech segments.
/// </summary>
public interface ITextSegmenter
{
    IReadOnlyList<SpeechSegment> Segment(
        Chapter chapter,
        TextSegmentationOptions options);
}
```

Create `src/NovelSpeaker.Application/Books/ITextSegmentationOptionsProvider.cs`:

```csharp
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>
/// Provides the current global text-segmentation options without exposing storage details.
/// </summary>
public interface ITextSegmentationOptionsProvider
{
    TextSegmentationOptions GetCurrent();
}
```

Create a minimal `src/NovelSpeaker.Infrastructure/Books/Parsing/TextSegmenter.cs`:

```csharp
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Books.Parsing;

/// <summary>
/// Temporary minimal implementation used to establish the runtime segmentation contract.
/// </summary>
public sealed class TextSegmenter : ITextSegmenter
{
    public IReadOnlyList<SpeechSegment> Segment(Chapter chapter, TextSegmentationOptions options)
    {
        if (string.IsNullOrWhiteSpace(chapter.Content))
        {
            return [];
        }

        return
        [
            new SpeechSegment(
                0,
                0,
                chapter.Content.Length,
                chapter.Content,
                chapter.Content)
        ];
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter Segment_returns_single_segment_for_short_single_line_paragraph
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/NovelSpeaker.UnitTests/Books/TextSegmenterTests.cs src/NovelSpeaker.Domain/Books/SpeechSegment.cs src/NovelSpeaker.Domain/Books/TextSegmentationOptions.cs src/NovelSpeaker.Application/Books/ITextSegmenter.cs src/NovelSpeaker.Application/Books/ITextSegmentationOptionsProvider.cs src/NovelSpeaker.Infrastructure/Books/Parsing/TextSegmenter.cs
git commit -m "feat: add text segmentation contracts"
```

---

### Task 2: Implement newline-based paragraph segmentation and sentence fallback

**Files:**
- Modify: `src/NovelSpeaker.Infrastructure/Books/Parsing/TextSegmenter.cs`
- Test: `tests/NovelSpeaker.UnitTests/Books/TextSegmenterTests.cs`

- [ ] **Step 1: Write the failing segmentation behavior tests**

Append these tests to `tests/NovelSpeaker.UnitTests/Books/TextSegmenterTests.cs`:

```csharp
[Fact]
public void Segment_splits_each_non_blank_line_into_a_natural_paragraph_segment()
{
    var chapter = new Chapter(
        "chapter-1",
        "book-1",
        0,
        "第一章",
        "第一段。\n第二段。\n\n第三段。",
        0,
        "第一段。\n第二段。\n\n第三段。".Length);

    var options = TextSegmentationOptions.Default;
    ITextSegmenter segmenter = new Infrastructure.Books.Parsing.TextSegmenter();

    var segments = segmenter.Segment(chapter, options);

    Assert.Equal(3, segments.Count);
    Assert.Equal("第一段。", segments[0].DisplayText);
    Assert.Equal(0, segments[0].StartOffset);
    Assert.Equal("第二段。", segments[1].DisplayText);
    Assert.Equal(5, segments[1].StartOffset);
    Assert.Equal("第三段。", segments[2].DisplayText);
    Assert.Equal(11, segments[2].StartOffset);
}

[Fact]
public void Segment_keeps_long_paragraph_unchanged_when_splitting_is_disabled()
{
    var text = string.Concat(Enumerable.Repeat("这是一句很长的话。", 40));
    var chapter = new Chapter("chapter-2", "book-1", 0, "第一章", text, 0, text.Length);
    var options = new TextSegmentationOptions(false, 50);
    ITextSegmenter segmenter = new Infrastructure.Books.Parsing.TextSegmenter();

    var segments = segmenter.Segment(chapter, options);

    Assert.Single(segments);
    Assert.Equal(text, segments[0].DisplayText);
}

[Fact]
public void Segment_splits_long_paragraph_on_sentence_boundaries_before_hard_cutting()
{
    var text = string.Concat(
        Enumerable.Repeat("这是第一句。", 12),
        Enumerable.Repeat("这是第二句！", 12),
        Enumerable.Repeat("这是第三句？", 12));
    var chapter = new Chapter("chapter-3", "book-1", 0, "第一章", text, 0, text.Length);
    var options = new TextSegmentationOptions(true, 60);
    ITextSegmenter segmenter = new Infrastructure.Books.Parsing.TextSegmenter();

    var segments = segmenter.Segment(chapter, options);

    Assert.True(segments.Count > 1);
    Assert.All(
        segments,
        segment => Assert.Contains(segment.DisplayText[^1], "。！？"));
    Assert.Equal(0, segments[0].StartOffset);
    Assert.Equal(text.Length, segments.Sum(segment => segment.Length));
}

[Fact]
public void Segment_hard_cuts_a_long_line_without_supported_sentence_punctuation()
{
    var text = new string('长', 140);
    var chapter = new Chapter("chapter-4", "book-1", 0, "第一章", text, 0, text.Length);
    var options = new TextSegmentationOptions(true, 50);
    ITextSegmenter segmenter = new Infrastructure.Books.Parsing.TextSegmenter();

    var segments = segmenter.Segment(chapter, options);

    Assert.Equal(3, segments.Count);
    Assert.Equal(50, segments[0].Length);
    Assert.Equal(50, segments[1].Length);
    Assert.Equal(40, segments[2].Length);
    Assert.Equal(100, segments[2].StartOffset);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter FullyQualifiedName~NovelSpeaker.UnitTests.Books.TextSegmenterTests
```

Expected: FAIL because the temporary implementation always returns one segment.

- [ ] **Step 3: Replace the temporary implementation with the real algorithm**

Replace `src/NovelSpeaker.Infrastructure/Books/Parsing/TextSegmenter.cs` with:

```csharp
using System.Text;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Books.Parsing;

/// <summary>
/// Splits chapter content into newline-based natural paragraphs and optionally subdivides long paragraphs.
/// </summary>
public sealed class TextSegmenter : ITextSegmenter
{
    private static readonly SearchValues<char> SentenceTerminators = SearchValues.Create("。！？");

    public IReadOnlyList<SpeechSegment> Segment(Chapter chapter, TextSegmentationOptions options)
    {
        if (string.IsNullOrWhiteSpace(chapter.Content))
        {
            return [];
        }

        var normalizedOptions = options.Normalize();
        var segments = new List<SpeechSegment>();
        var content = chapter.Content;
        var segmentIndex = 0;
        var lineStart = 0;

        while (lineStart < content.Length)
        {
            var lineEnd = content.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = content.Length;
            }

            var lineLength = lineEnd - lineStart;
            if (lineLength > 0)
            {
                var lineText = content.Substring(lineStart, lineLength);
                if (!string.IsNullOrWhiteSpace(lineText))
                {
                    foreach (var segment in SplitParagraph(lineText, lineStart, normalizedOptions, segmentIndex))
                    {
                        segments.Add(segment);
                        segmentIndex++;
                    }
                }
            }

            lineStart = lineEnd == content.Length ? content.Length : lineEnd + 1;
        }

        return segments;
    }

    private static IReadOnlyList<SpeechSegment> SplitParagraph(
        string paragraphText,
        int paragraphStartOffset,
        TextSegmentationOptions options,
        int startingSegmentIndex)
    {
        if (!options.EnableLongParagraphSplitting || paragraphText.Length <= options.LongParagraphThreshold)
        {
            return
            [
                new SpeechSegment(
                    startingSegmentIndex,
                    paragraphStartOffset,
                    paragraphText.Length,
                    paragraphText,
                    paragraphText)
            ];
        }

        var sentenceRanges = SplitIntoSentenceRanges(paragraphText);
        if (sentenceRanges.Count == 1 && sentenceRanges[0].Length == paragraphText.Length)
        {
            return HardCut(paragraphText, paragraphStartOffset, options.LongParagraphThreshold, startingSegmentIndex);
        }

        var segments = new List<SpeechSegment>();
        var currentStart = sentenceRanges[0].Start;
        var currentLength = 0;
        var nextSegmentIndex = startingSegmentIndex;

        foreach (var (start, length) in sentenceRanges)
        {
            if (length > options.LongParagraphThreshold)
            {
                if (currentLength > 0)
                {
                    segments.Add(CreateSegment(paragraphText, paragraphStartOffset, currentStart, currentLength, nextSegmentIndex++));
                    currentLength = 0;
                }

                foreach (var segment in HardCut(
                    paragraphText.Substring(start, length),
                    paragraphStartOffset + start,
                    options.LongParagraphThreshold,
                    nextSegmentIndex))
                {
                    segments.Add(segment);
                    nextSegmentIndex++;
                }

                currentStart = start + length;
                continue;
            }

            if (currentLength == 0)
            {
                currentStart = start;
                currentLength = length;
                continue;
            }

            if (currentLength + length > options.LongParagraphThreshold)
            {
                segments.Add(CreateSegment(paragraphText, paragraphStartOffset, currentStart, currentLength, nextSegmentIndex++));
                currentStart = start;
                currentLength = length;
                continue;
            }

            currentLength += length;
        }

        if (currentLength > 0)
        {
            segments.Add(CreateSegment(paragraphText, paragraphStartOffset, currentStart, currentLength, nextSegmentIndex));
        }

        return segments;
    }

    private static List<(int Start, int Length)> SplitIntoSentenceRanges(string paragraphText)
    {
        var ranges = new List<(int Start, int Length)>();
        var sentenceStart = 0;

        for (var index = 0; index < paragraphText.Length; index++)
        {
            if (!SentenceTerminators.Contains(paragraphText[index]))
            {
                continue;
            }

            ranges.Add((sentenceStart, index - sentenceStart + 1));
            sentenceStart = index + 1;
        }

        if (sentenceStart < paragraphText.Length)
        {
            ranges.Add((sentenceStart, paragraphText.Length - sentenceStart));
        }

        if (ranges.Count == 0)
        {
            ranges.Add((0, paragraphText.Length));
        }

        return ranges;
    }

    private static IReadOnlyList<SpeechSegment> HardCut(
        string text,
        int startOffset,
        int threshold,
        int startingSegmentIndex)
    {
        var segments = new List<SpeechSegment>();
        var nextSegmentIndex = startingSegmentIndex;
        var offset = 0;

        while (offset < text.Length)
        {
            var length = Math.Min(threshold, text.Length - offset);
            segments.Add(new SpeechSegment(
                nextSegmentIndex++,
                startOffset + offset,
                length,
                text.Substring(offset, length),
                text.Substring(offset, length)));
            offset += length;
        }

        return segments;
    }

    private static SpeechSegment CreateSegment(
        string paragraphText,
        int paragraphStartOffset,
        int localStart,
        int localLength,
        int segmentIndex)
    {
        var text = paragraphText.Substring(localStart, localLength);
        return new SpeechSegment(
            segmentIndex,
            paragraphStartOffset + localStart,
            localLength,
            text,
            text);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter FullyQualifiedName~NovelSpeaker.UnitTests.Books.TextSegmenterTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/NovelSpeaker.UnitTests/Books/TextSegmenterTests.cs src/NovelSpeaker.Infrastructure/Books/Parsing/TextSegmenter.cs
git commit -m "feat: implement runtime text segmentation"
```

---

### Task 3: Add a minimal JSON-backed settings store for Epic C options

**Files:**
- Create: `src/NovelSpeaker.Domain/Settings/AppSettings.cs`
- Create: `src/NovelSpeaker.Application/Settings/IAppSettingsStore.cs`
- Create: `src/NovelSpeaker.Infrastructure/Settings/JsonAppSettingsStore.cs`
- Modify: `src/NovelSpeaker.Application/Abstractions/IAppDataDirectoryProvider.cs`
- Modify: `src/NovelSpeaker.Infrastructure/FileSystem/LocalAppDataDirectoryProvider.cs`
- Modify: `src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Test: `tests/NovelSpeaker.UnitTests/Settings/JsonAppSettingsStoreTests.cs`
- Test: `tests/NovelSpeaker.UnitTests/DependencyInjection/ServiceCollectionExtensionsTests.cs`

- [ ] **Step 1: Write the failing settings-store tests**

Create `tests/NovelSpeaker.UnitTests/Settings/JsonAppSettingsStoreTests.cs`:

```csharp
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Settings;
using Xunit;

namespace NovelSpeaker.UnitTests.Settings;

public sealed class JsonAppSettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_returns_defaults_when_settings_file_does_not_exist()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var store = new JsonAppSettingsStore(directories);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.True(settings.EnableLongParagraphSplitting);
        Assert.Equal(300, settings.LongParagraphThreshold);
    }

    [Fact]
    public async Task SaveAsync_persists_updated_segmentation_settings()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var store = new JsonAppSettingsStore(directories);

        var settings = (await store.LoadAsync(CancellationToken.None)) with
        {
            EnableLongParagraphSplitting = false,
            LongParagraphThreshold = 42
        };

        await store.SaveAsync(settings, CancellationToken.None);
        var reloaded = await store.LoadAsync(CancellationToken.None);

        Assert.False(reloaded.EnableLongParagraphSplitting);
        Assert.Equal(50, reloaded.LongParagraphThreshold);
    }
}
```

Update `tests/NovelSpeaker.UnitTests/DependencyInjection/ServiceCollectionExtensionsTests.cs` by adding:

```csharp
Assert.IsAssignableFrom<Application.Settings.IAppSettingsStore>(
    provider.GetRequiredService<Application.Settings.IAppSettingsStore>());
Assert.IsAssignableFrom<ITextSegmentationOptionsProvider>(
    provider.GetRequiredService<ITextSegmentationOptionsProvider>());
Assert.IsAssignableFrom<ITextSegmenter>(provider.GetRequiredService<ITextSegmenter>());
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter FullyQualifiedName~NovelSpeaker.UnitTests.Settings.JsonAppSettingsStoreTests
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter AddNovelSpeakerInfrastructure_registers_core_services
```

Expected: FAIL because the settings store, settings path, and new registrations do not exist yet.

- [ ] **Step 3: Add the settings model, settings store, and registrations**

Create `src/NovelSpeaker.Domain/Settings/AppSettings.cs`:

```csharp
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Domain.Settings;

/// <summary>
/// Stores non-sensitive desktop settings for the current user.
/// </summary>
public sealed record AppSettings(
    bool EnableLongParagraphSplitting,
    int LongParagraphThreshold)
{
    public static AppSettings Default { get; } =
        new(
            TextSegmentationOptions.Default.EnableLongParagraphSplitting,
            TextSegmentationOptions.Default.LongParagraphThreshold);

    public TextSegmentationOptions ToTextSegmentationOptions()
    {
        return new TextSegmentationOptions(
            EnableLongParagraphSplitting,
            LongParagraphThreshold).Normalize();
    }
}
```

Create `src/NovelSpeaker.Application/Settings/IAppSettingsStore.cs`:

```csharp
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.Settings;

/// <summary>
/// Persists and loads non-sensitive desktop settings.
/// </summary>
public interface IAppSettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
```

Modify `src/NovelSpeaker.Application/Abstractions/IAppDataDirectoryProvider.cs`:

```csharp
string SettingsPath { get; }
```

Modify `src/NovelSpeaker.Infrastructure/FileSystem/LocalAppDataDirectoryProvider.cs` constructor and properties:

```csharp
SettingsPath = Path.Combine(rootDirectoryPath, "settings.json");
```

```csharp
public string SettingsPath { get; }
```

Create `src/NovelSpeaker.Infrastructure/Settings/JsonAppSettingsStore.cs`:

```csharp
using System.Text.Json;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.Settings;

/// <summary>
/// Reads and writes the desktop settings JSON file.
/// </summary>
public sealed class JsonAppSettingsStore :
    IAppSettingsStore,
    ITextSegmentationOptionsProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly IAppDataDirectoryProvider _directories;

    public JsonAppSettingsStore(IAppDataDirectoryProvider directories)
    {
        _directories = directories;
    }

    public TextSegmentationOptions GetCurrent()
    {
        var settings = LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        return settings.ToTextSegmentationOptions();
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_directories.SettingsPath))
        {
            return AppSettings.Default;
        }

        await using var stream = File.OpenRead(_directories.SettingsPath);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
            stream,
            SerializerOptions,
            cancellationToken);

        return settings is null
            ? AppSettings.Default
            : settings with
            {
                LongParagraphThreshold = settings.ToTextSegmentationOptions().LongParagraphThreshold
            };
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _directories.EnsureCreatedAsync(cancellationToken);

        var normalized = settings with
        {
            LongParagraphThreshold = settings.ToTextSegmentationOptions().LongParagraphThreshold
        };

        await using var stream = File.Create(_directories.SettingsPath);
        await JsonSerializer.SerializeAsync(stream, normalized, SerializerOptions, cancellationToken);
    }
}
```

Modify `src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` registrations:

```csharp
services.AddSingleton<ITextSegmenter, TextSegmenter>();
services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
services.AddSingleton<ITextSegmentationOptionsProvider>(serviceProvider =>
    (JsonAppSettingsStore)serviceProvider.GetRequiredService<IAppSettingsStore>());
```

Add the namespace imports:

```csharp
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Infrastructure.Settings;
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter FullyQualifiedName~NovelSpeaker.UnitTests.Settings.JsonAppSettingsStoreTests
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter AddNovelSpeakerInfrastructure_registers_core_services
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/NovelSpeaker.UnitTests/Settings/JsonAppSettingsStoreTests.cs tests/NovelSpeaker.UnitTests/DependencyInjection/ServiceCollectionExtensionsTests.cs src/NovelSpeaker.Domain/Settings/AppSettings.cs src/NovelSpeaker.Application/Settings/IAppSettingsStore.cs src/NovelSpeaker.Application/Abstractions/IAppDataDirectoryProvider.cs src/NovelSpeaker.Infrastructure/FileSystem/LocalAppDataDirectoryProvider.cs src/NovelSpeaker.Infrastructure/Settings/JsonAppSettingsStore.cs src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs
git commit -m "feat: add app settings store for text segmentation"
```

---

### Task 4: Connect Epic C settings to the Settings page

**Files:**
- Modify: `src/NovelSpeaker.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/NovelSpeaker.App/Views/SettingsView.xaml`
- Modify: `src/NovelSpeaker.App/Views/SettingsView.xaml.cs`
- Test: `tests/NovelSpeaker.UnitTests/ViewModels/SettingsViewModelTests.cs`

- [ ] **Step 1: Write the failing SettingsViewModel tests**

Create `tests/NovelSpeaker.UnitTests/ViewModels/SettingsViewModelTests.cs`:

```csharp
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task LoadAsync_populates_segmentation_settings_from_store()
    {
        var store = new FakeAppSettingsStore(new AppSettings(false, 120));
        var viewModel = new SettingsViewModel(store);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.EnableLongParagraphSplitting);
        Assert.Equal(120, viewModel.LongParagraphThreshold);
    }

    [Fact]
    public async Task SaveAsync_persists_updated_segmentation_settings()
    {
        var store = new FakeAppSettingsStore(AppSettings.Default);
        var viewModel = new SettingsViewModel(store)
        {
            EnableLongParagraphSplitting = false,
            LongParagraphThreshold = 25
        };

        await viewModel.SaveAsync(CancellationToken.None);

        Assert.NotNull(store.LastSavedSettings);
        Assert.False(store.LastSavedSettings!.EnableLongParagraphSplitting);
        Assert.Equal(25, store.LastSavedSettings.LongParagraphThreshold);
    }

    private sealed class FakeAppSettingsStore : IAppSettingsStore
    {
        private readonly AppSettings _loadedSettings;

        public FakeAppSettingsStore(AppSettings loadedSettings)
        {
            _loadedSettings = loadedSettings;
        }

        public AppSettings? LastSavedSettings { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_loadedSettings);
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            LastSavedSettings = settings;
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter FullyQualifiedName~NovelSpeaker.UnitTests.ViewModels.SettingsViewModelTests
```

Expected: FAIL because `SettingsViewModel` does not expose load/save behavior or segmentation properties.

- [ ] **Step 3: Update the Settings page view model and XAML**

Replace `src/NovelSpeaker.App/ViewModels/SettingsViewModel.cs` with:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsStore _settingsStore;

    public SettingsViewModel(IAppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    [ObservableProperty]
    private bool enableLongParagraphSplitting;

    [ObservableProperty]
    private int longParagraphThreshold;

    [ObservableProperty]
    private string statusMessage = "在这里配置导入与文本分段偏好。";

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        EnableLongParagraphSplitting = settings.EnableLongParagraphSplitting;
        LongParagraphThreshold = settings.LongParagraphThreshold;
    }

    [RelayCommand]
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        var settings = new AppSettings(
            EnableLongParagraphSplitting,
            LongParagraphThreshold);

        await _settingsStore.SaveAsync(settings, cancellationToken);
        var normalized = await _settingsStore.LoadAsync(cancellationToken);
        EnableLongParagraphSplitting = normalized.EnableLongParagraphSplitting;
        LongParagraphThreshold = normalized.LongParagraphThreshold;
        StatusMessage = "文本分段设置已保存。";
    }
}
```

Replace `src/NovelSpeaker.App/Views/SettingsView.xaml` with:

```xml
<UserControl x:Class="NovelSpeaker.App.Views.SettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Margin="24">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0"
                   FontSize="28"
                   FontWeight="SemiBold"
                   Text="设置" />

        <StackPanel Grid.Row="1"
                    Margin="0,20,0,0">
            <TextBlock FontSize="18"
                       FontWeight="SemiBold"
                       Text="导入与文本" />
            <CheckBox Margin="0,12,0,0"
                      Content="启用超长段落拆分"
                      IsChecked="{Binding EnableLongParagraphSplitting}" />

            <StackPanel Margin="0,12,0,0"
                        Orientation="Horizontal">
                <TextBlock VerticalAlignment="Center"
                           Text="拆分阈值" />
                <TextBox Width="120"
                         Margin="12,0,0,0"
                         Text="{Binding LongParagraphThreshold, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
        </StackPanel>

        <Button Grid.Row="2"
                Width="120"
                Margin="0,20,0,0"
                HorizontalAlignment="Left"
                Command="{Binding SaveCommand}"
                Content="保存设置" />

        <TextBlock Grid.Row="3"
                   Margin="0,16,0,0"
                   Foreground="#5C6470"
                   Text="{Binding StatusMessage}" />
    </Grid>
</UserControl>
```

Replace `src/NovelSpeaker.App/Views/SettingsView.xaml.cs` with:

```csharp
using System.Windows.Controls;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            await viewModel.LoadAsync(CancellationToken.None);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter FullyQualifiedName~NovelSpeaker.UnitTests.ViewModels.SettingsViewModelTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/NovelSpeaker.UnitTests/ViewModels/SettingsViewModelTests.cs src/NovelSpeaker.App/ViewModels/SettingsViewModel.cs src/NovelSpeaker.App/Views/SettingsView.xaml src/NovelSpeaker.App/Views/SettingsView.xaml.cs
git commit -m "feat: expose text segmentation settings in desktop UI"
```

---

### Task 5: Run full verification and update handoff notes

**Files:**
- Modify: `docs/11_TASK_BACKLOG.md`

- [ ] **Step 1: Update the backlog checkboxes**

Modify `docs/11_TASK_BACKLOG.md` to mark these Epic C items complete once Tasks 1-4 are fully merged and verified:

```markdown
- [x] 定义 `ITextSegmenter`。
- [x] 实现自然段切分。
- [x] 实现句号、问号、感叹号切分。
- [x] 实现超长段落强制切分。
- [x] 分离 DisplayText 和 SpeechText。
- [x] 保留字符范围。
- [x] 添加中文小说边缘测试。
```

Leave `实现无章节回退` unchecked.

- [ ] **Step 2: Run targeted verification**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter FullyQualifiedName~NovelSpeaker.UnitTests.Books.TextSegmenterTests
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter FullyQualifiedName~NovelSpeaker.UnitTests.Settings.JsonAppSettingsStoreTests
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter FullyQualifiedName~NovelSpeaker.UnitTests.ViewModels.SettingsViewModelTests
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter AddNovelSpeakerInfrastructure_registers_core_services
```

Expected: PASS for all commands.

- [ ] **Step 3: Run repo-level verification**

Run:

```bash
dotnet format --verify-no-changes
dotnet build -c Release
dotnet test -c Release
```

Expected: PASS. If any command is not configured in the repo, record that explicitly in the delivery notes instead of claiming success.

- [ ] **Step 4: Commit**

```bash
git add docs/11_TASK_BACKLOG.md
git commit -m "docs: update Epic C backlog progress"
```

---

## Self-Review

### Spec coverage

- Runtime `SpeechSegment` model: Task 1.
- `ITextSegmenter` boundary: Task 1.
- Newline-based paragraph splitting: Task 2.
- `。！？` splitting and hard-cut fallback: Task 2.
- Global on/off switch and threshold: Tasks 3-4.
- Default values and threshold clamp: Task 3.
- Keep offsets based on `Chapter.Content`: Tasks 1-2.
- `DisplayText` and `SpeechText` split with equal first-version values: Tasks 1-2.
- Settings page exposure: Task 4.
- Chinese edge-case tests: Task 2.
- Leave no-chapter fallback out of scope: Task 5 backlog note.

No approved spec requirement is left without a task.

### Placeholder scan

- No `TODO`, `TBD`, or “implement later” placeholders remain.
- Every code-changing step contains concrete code or exact text edits.
- Every verification step contains an exact command and expected result.

### Type consistency

- `TextSegmentationOptions`, `SpeechSegment`, `ITextSegmenter`, and `ITextSegmentationOptionsProvider` use the same names in all tasks.
- `IAppSettingsStore` and `AppSettings` are introduced before they are consumed by `SettingsViewModel`.
- The plan keeps `Chapter.Content` as the only offset base throughout.
