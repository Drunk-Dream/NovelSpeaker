using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.TestKit.Speech;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class PlaybackRuntimeTests
{
    [Fact]
    public void Idle_projection_preserves_initial_snapshot_semantics()
    {
        Assert.Equal(PlaybackSnapshot.Idle, PlaybackSnapshotProjector.Project(PlaybackRuntimeState.Idle));
    }

    [Fact]
    public void Preparation_does_not_publish_target_or_cancel_or_release_current_session()
    {
        using var runtime = Open();
        var oldState = runtime.Current;
        var oldToken = runtime.SessionToken;
        var protection = new TrackingDisposable();
        Assert.True(runtime.TryProtectAudio(oldState.Identity!, protection));

        var preparation = runtime.PrepareReplacement(Target() with { Position = new(4, 1) });

        Assert.True(preparation.IsAccepted);
        Assert.Same(oldState, runtime.Current);
        Assert.False(oldToken.IsCancellationRequested);
        Assert.Equal(0, protection.DisposeCount);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(4, -1)]
    [InlineData(4, 3)]
    [InlineData(4, 2)] // Display-only segment is not a playable target.
    [InlineData(8, 0)] // Unloaded chapter.
    [InlineData(9, 0)] // Loaded empty chapter.
    [InlineData(10, 0)] // Failed chapter.
    public void Invalid_position_is_rejected_without_changing_current_state(int chapter, int segment)
    {
        using var runtime = Open();
        var before = runtime.Current;

        var result = runtime.PrepareReplacement(Target() with { Position = new(chapter, segment) });

        Assert.Equal(PlaybackTransitionRejection.InvalidTarget, result.Rejection);
        Assert.Null(result.Replacement);
        Assert.Same(before, runtime.Current);
    }

    [Fact]
    public void Cancelled_commit_preserves_position_lifetime_and_audio_protection()
    {
        using var runtime = Open();
        var before = runtime.Current;
        var oldToken = runtime.SessionToken;
        var protection = new TrackingDisposable();
        runtime.TryProtectAudio(before.Identity!, protection);
        var replacement = runtime.PrepareReplacement(Target() with { Position = new(4, 1) }).Replacement!;

        var result = runtime.CommitReplacement(replacement, new CancellationToken(true));

        Assert.Equal(PlaybackTransitionRejection.Cancelled, result.Rejection);
        Assert.Empty(result.Effects);
        Assert.Same(before, runtime.Current);
        Assert.False(oldToken.IsCancellationRequested);
        Assert.Equal(0, protection.DisposeCount);
    }

    [Fact]
    public void Commit_changes_complete_session_then_hands_old_resources_to_retirement_effect()
    {
        using var runtime = Open();
        var before = runtime.Current;
        var oldToken = runtime.SessionToken;
        var protection = new TrackingDisposable();
        runtime.TryProtectAudio(before.Identity!, protection);
        var target = Target() with { Position = new(4, 1), ResumePositionMilliseconds = 321 };

        var result = runtime.CommitReplacement(runtime.PrepareReplacement(target).Replacement!, CancellationToken.None);

        Assert.True(result.IsAccepted);
        Assert.Equal(new PlaybackPosition(4, 1), runtime.Current.Position);
        Assert.NotEqual(before.Identity, runtime.Current.Identity);
        Assert.Equal(target.Book.SourceContext, runtime.Current.Identity!.SourceContext);
        Assert.Equal(321, runtime.Current.PositionForSave);
        Assert.Same(runtime.Current, result.State);
        Assert.False(oldToken.IsCancellationRequested);
        Assert.Equal(0, protection.DisposeCount);
        var checkpoints = result.Effects.OfType<PlaybackCheckpointEffect>().ToArray();
        Assert.Equal(2, checkpoints.Length);
        Assert.Equal(0, checkpoints[0].Progress.SegmentIndex);
        var checkpoint = checkpoints[1].Progress;
        Assert.Equal(new PlaybackProgressUpdate("book-1", 4, 1, 6, 321, target.Book.SourceContext), checkpoint);
        Assert.Same(result.State, Assert.Single(result.Effects.OfType<PlaybackRefreshPrefetchEffect>()).Session);

        var retirement = Assert.Single(result.Effects.OfType<PlaybackRetireSessionEffect>());
        retirement.Lifetime.Dispose();
        retirement.Lifetime.Dispose();
        Assert.True(oldToken.IsCancellationRequested);
        Assert.Equal(1, protection.DisposeCount);
        Assert.False(runtime.SessionToken.IsCancellationRequested);
    }

    [Fact]
    public void Superseded_or_replayed_preparation_cannot_commit()
    {
        using var runtime = Open();
        var older = runtime.PrepareReplacement(Target()).Replacement!;
        var newer = runtime.PrepareReplacement(Target() with { Position = new(4, 1) }).Replacement!;
        Retire(runtime.CommitReplacement(newer, CancellationToken.None));
        var before = runtime.Current;

        Assert.Equal(PlaybackTransitionRejection.StalePreparation, runtime.CommitReplacement(older, CancellationToken.None).Rejection);
        Assert.Equal(PlaybackTransitionRejection.StalePreparation, runtime.CommitReplacement(newer, CancellationToken.None).Rejection);
        Assert.Same(before, runtime.Current);
    }

    [Fact]
    public void Preparation_from_another_runtime_is_rejected()
    {
        using var runtime = Open();
        using var other = Open();
        var before = runtime.Current;

        var result = runtime.CommitReplacement(other.PrepareReplacement(Target()).Replacement!, CancellationToken.None);

        Assert.Equal(PlaybackTransitionRejection.StalePreparation, result.Rejection);
        Assert.Same(before, runtime.Current);
    }

    [Fact]
    public void Audio_progress_invalidates_preparation_instead_of_overwriting_newer_resume_position()
    {
        using var runtime = Open();
        var prepared = runtime.PrepareReplacement(Target()).Replacement!;
        runtime.AcceptAudio(new(runtime.Current.Identity!, runtime.Current.Position!.Value,
            PlaybackState.Playing, new(true, 321, 1000, false)));
        var before = runtime.Current;

        var result = runtime.CommitReplacement(prepared, CancellationToken.None);

        Assert.Equal(PlaybackTransitionRejection.StalePreparation, result.Rejection);
        Assert.Same(before, runtime.Current);
        Assert.Equal(321, runtime.Current.PositionForSave);
    }

    [Fact]
    public void Preparing_commit_emits_play_intent_only_with_resolved_provider_and_playable_position()
    {
        using var runtime = new PlaybackRuntime();
        var target = Target() with { State = PlaybackState.Preparing };
        Assert.False(runtime.PrepareReplacement(target with { Provider = null }).IsAccepted);
        Assert.False(runtime.PrepareReplacement(target with { Position = null }).IsAccepted);
        Assert.False(runtime.PrepareReplacement(target with { ResumePositionMilliseconds = -1 }).IsAccepted);
        Assert.False(runtime.PrepareReplacement(target with { ConsecutiveSegmentFailureCount = -1 }).IsAccepted);

        var result = runtime.CommitReplacement(runtime.PrepareReplacement(target).Replacement!, CancellationToken.None);

        Assert.True(result.IsAccepted);
        Assert.Same(runtime.Current, Assert.Single(result.Effects.OfType<PlaybackPlaySegmentEffect>()).Session);
        Assert.False(runtime.Current.Audio.HasLoadedAudio);
    }

    [Theory]
    [InlineData(PlaybackState.Playing, false, 0, 1000)]
    [InlineData(PlaybackState.Stopped, true, 0, 1000)]
    [InlineData(PlaybackState.Playing, true, -1, 1000)]
    [InlineData(PlaybackState.Playing, true, 0, -1)]
    [InlineData(PlaybackState.Idle, false, 0, 0)]
    public void Invalid_audio_facts_do_not_change_state(PlaybackState state, bool loaded, long position, long duration)
    {
        using var runtime = Open();
        var before = runtime.Current;

        var result = runtime.AcceptAudio(new(before.Identity!, before.Position!.Value,
            state, new(loaded, position, duration, false)));

        Assert.Equal(PlaybackTransitionRejection.InvalidTarget, result.Rejection);
        Assert.Same(before, runtime.Current);
    }

    [Fact]
    public void Terminal_audio_keeps_resume_checkpoint_when_device_position_resets()
    {
        using var runtime = Open();
        runtime.AcceptAudio(new(runtime.Current.Identity!, runtime.Current.Position!.Value,
            PlaybackState.Playing, new(true, 321, 1000, false)));

        var result = runtime.AcceptAudio(new(runtime.Current.Identity!, runtime.Current.Position!.Value,
            PlaybackState.Stopped, PlaybackAudioFacts.Empty));

        Assert.True(result.IsAccepted);
        Assert.Equal(321, runtime.Current.PositionForSave);
        Assert.Equal(0, PlaybackSnapshotProjector.Project(runtime.Current).PositionMilliseconds);
        Assert.False(runtime.Current.Audio.HasLoadedAudio);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("book")]
    [InlineData("source")]
    [InlineData("catalog")]
    [InlineData("position")]
    public void Audio_result_requires_current_session_book_source_catalog_and_position(string mismatch)
    {
        using var runtime = Open();
        var before = runtime.Current;
        var identity = before.Identity!;
        identity = mismatch switch
        {
            "session" => identity with { SessionId = Guid.NewGuid() },
            "book" => identity with { BookId = "other-book" },
            "source" => identity with { SourceContext = new("other-source", "catalog-1") },
            "catalog" => identity with { SourceContext = new("source-1", "other-catalog") },
            _ => identity
        };
        var result = runtime.AcceptAudio(new(identity,
            mismatch == "position" ? new(4, 1) : before.Position!.Value,
            PlaybackState.Playing, new(true, 321, 1000, true)));

        Assert.Equal(PlaybackTransitionRejection.StaleSession, result.Rejection);
        Assert.Empty(result.Effects);
        Assert.Same(before, runtime.Current);
    }

    [Fact]
    public void Old_audio_and_protection_are_rejected_after_replacement()
    {
        using var runtime = Open();
        var old = runtime.Current;
        Retire(runtime.CommitReplacement(runtime.PrepareReplacement(Target()).Replacement!, CancellationToken.None));
        var before = runtime.Current;
        var protection = new TrackingDisposable();

        foreach (var state in new[] { PlaybackState.Playing, PlaybackState.Stopped, PlaybackState.Faulted })
        {
            var result = runtime.AcceptAudio(new(old.Identity!, old.Position!.Value, state,
                new(state == PlaybackState.Playing, 123, 1000, false)));
            Assert.Equal(PlaybackTransitionRejection.StaleSession, result.Rejection);
        }
        Assert.False(runtime.TryProtectAudio(old.Identity!, protection));
        Assert.Equal(0, protection.DisposeCount); // Rejected ownership stays with caller.
        protection.Dispose();
        Assert.Same(before, runtime.Current);
    }

    [Fact]
    public void Projection_and_checkpoint_use_same_committed_audio_and_do_not_mutate_runtime()
    {
        using var runtime = Open();
        Assert.True(runtime.AcceptAudio(new(runtime.Current.Identity!, runtime.Current.Position!.Value,
            PlaybackState.Playing, new(true, 321, 1000, true))).IsAccepted);
        var before = runtime.Current;

        var first = PlaybackSnapshotProjector.Project(before, 0.25);
        var second = PlaybackSnapshotProjector.Project(before, 0.25);
        var editedSnapshot = first with { BookId = "other-book", ChapterIndex = 99, PositionMilliseconds = 999 };
        var checkpoint = Assert.Single(runtime.Checkpoint(before.Identity!).Effects.OfType<PlaybackCheckpointEffect>()).Progress;

        Assert.Equal(first, second);
        Assert.Same(before, runtime.Current);
        Assert.Equal("other-book", editedSnapshot.BookId);
        Assert.Equal("book-1", first.BookId);
        Assert.Equal(before.Identity!.SourceContext, first.SourceContext);
        Assert.Equal(4, first.ChapterIndex);
        Assert.Equal("测试章节", first.ChapterTitle);
        Assert.Equal(before.Provider!.ProviderId, first.ProviderId);
        Assert.Equal(321, first.PositionMilliseconds);
        Assert.Equal(1000, first.DurationMilliseconds);
        Assert.True(first.HasLoadedAudio);
        Assert.True(first.IsUsingCache);
        Assert.Equal(0.25, first.Volume);
        Assert.Equal(3, first.SegmentCount);
        Assert.Equal(321, checkpoint.AudioPositionMilliseconds);
        Assert.Equal(3, checkpoint.CharacterOffset);
    }

    [Fact]
    public void Provider_and_speed_change_preserve_current_audio_and_refresh_next_segment_prefetch()
    {
        using var runtime = Open();
        runtime.AcceptAudio(new(runtime.Current.Identity!, runtime.Current.Position!.Value,
            PlaybackState.Playing, new(true, 321, 1000, false)));
        var before = runtime.Current;

        var result = runtime.ChangeSpeechConfiguration(before.Identity!, null, int.MaxValue);

        Assert.True(result.IsAccepted);
        Assert.Null(runtime.Current.Provider);
        Assert.Equal(AppSettings.NormalizeSpeakSpeed(int.MaxValue), runtime.Current.SpeakSpeed);
        Assert.Same(before.Audio, runtime.Current.Audio);
        Assert.Equal(before.Position, runtime.Current.Position);
        Assert.Equal(PlaybackState.Playing, runtime.Current.State);
        Assert.Single(result.Effects.OfType<PlaybackRefreshPrefetchEffect>());
    }

    [Fact]
    public void Only_successful_playing_result_clears_failure_window()
    {
        using var runtime = new PlaybackRuntime();
        runtime.CommitReplacement(runtime.PrepareReplacement(Target() with { ConsecutiveSegmentFailureCount = 2 }).Replacement!, CancellationToken.None);
        runtime.AcceptAudio(new(runtime.Current.Identity!, runtime.Current.Position!.Value,
            PlaybackState.Paused, new(true, 0, 1000, false)));
        Assert.Equal(2, runtime.Current.ConsecutiveSegmentFailureCount);
        runtime.AcceptAudio(new(runtime.Current.Identity!, runtime.Current.Position!.Value,
            PlaybackState.Playing, new(true, 0, 1000, false)));
        Assert.Equal(0, runtime.Current.ConsecutiveSegmentFailureCount);
    }

    [Fact]
    public void Empty_or_unloaded_book_can_be_stopped_without_fabricated_position_or_checkpoint()
    {
        using var runtime = new PlaybackRuntime();
        var target = Target() with { Book = new("book-1", "测试小说", []), Position = null, State = PlaybackState.Stopped };

        var result = runtime.CommitReplacement(runtime.PrepareReplacement(target).Replacement!, CancellationToken.None);

        Assert.True(result.IsAccepted);
        Assert.Null(runtime.Current.Position);
        Assert.Empty(result.Effects.OfType<PlaybackCheckpointEffect>());
        var snapshot = PlaybackSnapshotProjector.Project(runtime.Current);
        Assert.Equal(0, snapshot.SegmentCount);
        Assert.Null(snapshot.ChapterTitle);
    }

    [Fact]
    public void Clear_rejects_late_results_and_retirement_releases_resources()
    {
        using var runtime = Open();
        var before = runtime.Current;
        var token = runtime.SessionToken;
        var protection = new TrackingDisposable();
        runtime.TryProtectAudio(before.Identity!, protection);

        var result = runtime.Clear(CancellationToken.None);

        Assert.True(result.IsAccepted);
        Assert.Null(runtime.Current.Identity);
        Assert.Null(runtime.Current.Book);
        Assert.False(runtime.IsCurrent(before.Identity!));
        Assert.Equal(PlaybackState.Idle, PlaybackSnapshotProjector.Project(runtime.Current).State);
        Retire(result);
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(1, protection.DisposeCount);
    }

    [Fact]
    public void Disposal_cancels_session_and_releases_audio_protection_once()
    {
        var runtime = Open();
        var token = runtime.SessionToken;
        var protection = new TrackingDisposable();
        runtime.TryProtectAudio(runtime.Current.Identity!, protection);

        runtime.Dispose();
        runtime.Dispose();

        Assert.True(token.IsCancellationRequested);
        Assert.Equal(1, protection.DisposeCount);
        Assert.False(runtime.IsCurrent(new(Guid.NewGuid(), "book-1", null)));
        Assert.Throws<ObjectDisposedException>(() => runtime.PrepareReplacement(Target()));
    }

    private static PlaybackRuntime Open()
    {
        var runtime = new PlaybackRuntime();
        Assert.True(runtime.CommitReplacement(runtime.PrepareReplacement(Target()).Replacement!, CancellationToken.None).IsAccepted);
        return runtime;
    }

    [Fact]
    public void Cancellation_of_a_captured_old_identity_cannot_cancel_the_replacement()
    {
        using var runtime = Open();
        var captured = runtime.Current.Identity!;
        var replacement = runtime.CommitReplacement(runtime.PrepareReplacement(Target() with
        { Book = Target().Book with { BookId = "book-2" } }).Replacement!, CancellationToken.None);
        var token = runtime.SessionToken;

        runtime.CancelSession(captured);
        runtime.CancelWork("book-1", null);

        Assert.False(token.IsCancellationRequested);
        Assert.True(runtime.IsCurrent(runtime.Current.Identity!));
        Retire(replacement);
    }

    private static PlaybackSessionTarget Target() => new(
        new("book-1", "测试小说",
        [
            PlaybackChapterContent.FromLoaded(4, "测试章节",
            [new SpeechSegment(0, 3, 3, "甲", "甲"), new SpeechSegment(1, 6, 6, "乙", "乙"), new SpeechSegment(2, 9, 3, "仅展示", "")]),
            PlaybackChapterContent.Unloaded(8, "未加载"),
            PlaybackChapterContent.FromLoaded(9, "空章节", []),
            PlaybackChapterContent.Failed(10, "加载失败")
        ], "测试作者", new("source-1", "catalog-1")),
        new(4, 0), TestSpeechProviders.Resolve(TestSpeechProviders.Item(1, "测试服务")), 10, PlaybackState.Paused);

    private static void Retire(PlaybackTransition transition)
    {
        foreach (var effect in transition.Effects.OfType<PlaybackRetireSessionEffect>()) effect.Lifetime.Dispose();
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
}
