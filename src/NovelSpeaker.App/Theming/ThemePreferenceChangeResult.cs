using System;

namespace NovelSpeaker.App.Theming;

public sealed record ThemePreferenceChangeResult(
    bool IsSuccess,
    bool IsStale,
    string EffectiveTheme,
    Exception? Exception = null);
