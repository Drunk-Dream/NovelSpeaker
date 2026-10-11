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
    public void Session_replacement_preparation_does_not_publish_target_or_cancel_or_release_current_session()
    {
        using var runtime = Open();
        var oldState = runtime.Current;
        var oldToken = runtime.SessionToken;
        var protection = new TrackingDisposable();
        Assert.True(runtime.TryProtectAudio(oldState.Identity!, protection));

        var preparation = runtime.PrepareSessionReplacement(Target() with { Position = new(4, 1) });

        Assert.True(preparation.IsAccepted);
        Assert.Same(oldState, runtime.Current);
        Assert.False(oldToken.IsCancellationRequested);
        Assert.Equal(0, protection.DisposeCount);
    }

    [Fact]
    public void Invalid_position_is_rejected_without_changing_current_state()
    {
        foreach (var (chapter, segment) in new[]
                 {
                     (-1, 0),
                     (0, 0),
                     (4, -1),
                     (4, 3),
                     (4, 2), // Display-only segment is not a playable target.
                     (8, 0), // Unloaded chapter.
                     (9, 0), // Loaded empty chapter.
                     (10, 0) // Failed chapter.
                 })
        {
            using var runtime = Open();
            var before = runtime.Current;

            var result = runtime.PrepareSessionReplacement(Target() with { Position = new(chapter, segment) });

            Assert.Equal(PlaybackTransitionRejection.InvalidTarget, result.Rejection);
            Assert.Null(result.Replacement);
            Assert.Same(before, runtime.Current);
        }
    }

    [Fact]
    public void Cancelled_commit_preserves_position_lifetime_and_audio_protection()
    {
        using var runtime = Open();
        var before = runtime.Current;
        var oldToken = runtime.SessionToken;
        var protection = new TrackingDisposable();
        runtime.TryProtectAudio(before.Identity!, protection);
        var replacement = runtime.PrepareSessionReplacement(Target() with { Position = new(4, 1) }).Replacement!;

        var result = runtime.CommitSessionReplacement(replacement, new CancellationToken(true));

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

        var result = runtime.CommitSessionReplacement(runtime.PrepareSessionReplacement(target).Replacement!, CancellationToken.None);

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
        var retirement = Assert.Single(result.Effects.OfType<PlaybackRetireSessionEffect>());
        retirement.Lifetime.Dispose();
        retirement.Lifetime.Dispose();
        Assert.True(oldToken.IsCancellationRequested);
        Assert.Equal(1, protection.DisposeCount);
        Assert.False(runtime.SessionToken.IsCancellationRequested);
    }

    [Fact]
    public void Superseded_or_replayed_session_replacement_cannot_commit()
    {
        using var runtime = Open();
        var older = runtime.PrepareSessionReplacement(Target()).Replacement!;
        var newer = runtime.PrepareSessionReplacement(Target() with { Position = new(4, 1) }).Replacement!;
        Retire(runtime.CommitSessionReplacement(newer, CancellationToken.None));
        var before = runtime.Current;

        Assert.Equal(PlaybackTransitionRejection.StaleSessionReplacement, runtime.CommitSessionReplacement(older, CancellationToken.None).Rejection);
        Assert.Equal(PlaybackTransitionRejection.StaleSessionReplacement, runtime.CommitSessionReplacement(newer, CancellationToken.None).Rejection);
        Assert.Same(before, runtime.Current);
    }

    [Fact]
    public void Session_replacement_from_another_runtime_is_rejected()
    {
        using var runtime = Open();
        using var other = Open();
        var before = runtime.Current;

        var result = runtime.CommitSessionReplacement(other.PrepareSessionReplacement(Target()).Replacement!, CancellationToken.None);

        Assert.Equal(PlaybackTransitionRejection.StaleSessionReplacement, result.Rejection);
        Assert.Same(before, runtime.Current);
    }

    [Fact]
    public void Audio_progress_invalidates_session_replacement_instead_of_overwriting_newer_resume_position()
    {
        using var runtime = Open();
        var prepared = runtime.PrepareSessionReplacement(Target()).Replacement!;
        runtime.AcceptAudio(Audio(runtime.Current, PlaybackState.Playing, new(true, 321, 1000, false)));
        var before = runtime.Current;

        var result = runtime.CommitSessionReplacement(prepared, CancellationToken.None);

        Assert.Equal(PlaybackTransitionRejection.StaleSessionReplacement, result.Rejection);
        Assert.Same(before, runtime.Current);
        Assert.Equal(321, runtime.Current.PositionForSave);
    }

    [Fact]
    public void Preparing_commit_emits_play_intent_only_with_resolved_provider_and_playable_position()
    {
        using var runtime = new PlaybackRuntime();
        var target = Target() with { State = PlaybackState.Preparing };
        Assert.False(runtime.PrepareSessionReplacement(target with { Provider = null }).IsAccepted);
        Assert.False(runtime.PrepareSessionReplacement(target with { Position = null }).IsAccepted);
        Assert.False(runtime.PrepareSessionReplacement(target with { ResumePositionMilliseconds = -1 }).IsAccepted);
        Assert.False(runtime.PrepareSessionReplacement(target with { ConsecutiveSegmentFailureCount = -1 }).IsAccepted);

        var result = runtime.CommitSessionReplacement(runtime.PrepareSessionReplacement(target).Replacement!, CancellationToken.None);

        Assert.True(result.IsAccepted);
        var preparation = Assert.Single(result.Effects.OfType<PlaybackPrepareTargetAudioEffect>());
        Assert.Same(runtime.Current, preparation.State);
        Assert.Equal(runtime.Current.Preparation, preparation.Preparation);
        Assert.False(runtime.Current.Audio.HasLoadedAudio);
    }

    [Fact]
    public void Same_session_target_commit_advances_logical_position_before_audio_is_ready()
    {
        using var runtime = Open();
        var before = runtime.Current;
        var beforeIdentity = before.Identity!;

        var result = runtime.CommitTarget(before.Book!, new(4, 1), PlaybackIntent.Play,
            CancellationToken.None, "已跳转到目标段落，等待播放。");

        Assert.True(result.IsAccepted);
        Assert.Equal(beforeIdentity, runtime.Current.Identity);
        Assert.Equal(new PlaybackPosition(4, 1), runtime.Current.Position);
        Assert.Equal(before.TargetRevision + 1, runtime.Current.TargetRevision);
        Assert.Equal(PlaybackIntent.Play, runtime.Current.Intent);
        Assert.NotNull(runtime.Current.Preparation);
        Assert.False(runtime.Current.Audio.HasLoadedAudio);
        Assert.Equal(0, runtime.Current.PositionForSave);
        var preparation = Assert.Single(result.Effects.OfType<PlaybackPrepareTargetAudioEffect>());
        Assert.Equal(runtime.Current.Preparation, preparation.Preparation);
        Assert.Equal(new PlaybackProgressUpdate("book-1", 4, 1, 6, 0, before.Book!.SourceContext),
            result.Effects.OfType<PlaybackCheckpointEffect>().Last().Progress);

        var second = runtime.CommitTarget(runtime.Current.Book!, new(4, 0), PlaybackIntent.Pause, CancellationToken.None);
        Assert.True(second.IsAccepted);
        Assert.Equal(beforeIdentity, runtime.Current.Identity);
        Assert.Equal(new PlaybackPosition(4, 0), runtime.Current.Position);
        Assert.Equal(before.TargetRevision + 2, runtime.Current.TargetRevision);
        Assert.Null(runtime.Current.Preparation);
    }

    [Fact]
    public void Same_session_can_commit_a_target_after_content_refresh_removed_the_previous_target()
    {
        using var runtime = Open();
        var book = runtime.Current.Book!;
        var session = runtime.Current.Identity!;
        Assert.True(runtime.UpdateContent(book, null).IsAccepted);
        Assert.Null(runtime.Current.Target);

        var transition = runtime.CommitTarget(book, new(4, 1), PlaybackIntent.Play, CancellationToken.None);

        Assert.True(transition.IsAccepted);
        Assert.Equal(session, runtime.Current.Identity);
        Assert.Equal(new PlaybackPosition(4, 1), runtime.Current.Position);
        Assert.Single(transition.Effects.OfType<PlaybackPrepareTargetAudioEffect>());
    }

    [Fact]
    public void Target_revision_rejects_audio_facts_from_the_previous_target_in_the_same_session()
    {
        using var runtime = Open();
        var previous = runtime.Current;
        Assert.True(runtime.BeginPlayback(resetFailureWindow: false).IsAccepted);
        var previousPreparation = runtime.Current.Preparation!.Identity;
        previous = runtime.Current;
        Assert.True(runtime.AcceptAudio(Audio(previous, PlaybackState.Playing, new(true, 321, 1000, false))).IsAccepted);

        var transition = runtime.CommitTarget(runtime.Current.Book!, new(4, 1), PlaybackIntent.Play, CancellationToken.None);
        Assert.True(transition.IsAccepted);
        var committed = runtime.Current;
        var stale = runtime.AcceptAudio(new(previous.Identity!, previous.Position!.Value, PlaybackState.Playing,
            new(true, 900, 1000, false), TargetIdentity: previous.Target!.Identity));

        Assert.Equal(PlaybackTransitionRejection.StaleSession, stale.Rejection);
        Assert.Equal(previous.Identity, committed.Identity);
        Assert.NotEqual(previous.Target!.Identity, committed.Target!.Identity);
        Assert.False(runtime.IsCurrentPreparation(previousPreparation));
        Assert.Equal(0, committed.PositionForSave);
        Assert.False(committed.Audio.HasLoadedAudio);
    }

    [Fact]
    public void Preparation_identity_binds_session_target_and_current_synthesis_profile()
    {
        using var runtime = Open();
        var started = runtime.BeginPlayback(resetFailureWindow: false);
        Assert.True(started.IsAccepted);
        var original = runtime.Current.Preparation!;
        Assert.True(runtime.IsCurrentPreparation(original.Identity));

        runtime.ChangeSpeechConfiguration(runtime.Current.Identity, runtime.Current.Provider,
            runtime.Current.SpeakSpeed + 1);

        Assert.False(runtime.IsCurrentPreparation(original.Identity));
        var sameTargetRetry = runtime.BeginPlayback(resetFailureWindow: false);
        Assert.True(sameTargetRetry.IsAccepted);
        var current = runtime.Current.Preparation!;
        Assert.Equal(original.Identity.Session, current.Identity.Session);
        Assert.Equal(original.Identity.Target, current.Identity.Target);
        Assert.NotEqual(original.Identity.Synthesis, current.Identity.Synthesis);
        Assert.True(runtime.IsCurrentPreparation(current.Identity));
    }

    [Fact]
    public void Restarted_preparation_attempt_on_the_same_target_rejects_the_older_result()
    {
        using var runtime = Open();
        var previous = runtime.Current;
        var previousPreparation = previous.Preparation!.Identity;

        Assert.True(runtime.BeginPlayback(resetFailureWindow: false).IsAccepted);
        var current = runtime.Current;
        Assert.Equal(previous.Target!.Identity, current.Target!.Identity);
        Assert.NotEqual(previousPreparation.AttemptId, current.Preparation!.Identity.AttemptId);

        var stale = runtime.AcceptAudio(Audio(previous, PlaybackState.Playing, new(true, 500, 1000, false)));

        Assert.Equal(PlaybackTransitionRejection.StaleSession, stale.Rejection);
        Assert.Same(current, runtime.Current);
    }

    [Fact]
    public void Checkpoint_capture_rejects_audio_position_from_a_previous_target_revision()
    {
        using var runtime = Open();
        var previous = runtime.Current;
        Assert.True(runtime.AcceptAudio(Audio(previous, PlaybackState.Playing, new(true, 321, 1000, false))).IsAccepted);
        previous = runtime.Current;
        var previousPreparation = previous.Audio.PreparationIdentity!;

        Assert.True(runtime.CommitTarget(runtime.Current.Book!, new(4, 1), PlaybackIntent.Play, CancellationToken.None).IsAccepted);
        var committed = runtime.Current;
        runtime.CaptureCheckpointPosition(previous.Identity!, previous.Target!.Identity,
            previousPreparation.AttemptId, 900);

        Assert.Equal(new PlaybackPosition(4, 1), runtime.Current.Position);
        Assert.Equal(0, runtime.Current.PositionForSave);
        Assert.Same(committed, runtime.Current);
    }

    [Fact]
    public void Invalid_audio_facts_do_not_change_state()
    {
        foreach (var (state, loaded, position, duration) in new[]
                 {
                     (PlaybackState.Playing, false, 0L, 1000L),
                     (PlaybackState.Stopped, true, 0L, 1000L),
                     (PlaybackState.Playing, true, -1L, 1000L),
                     (PlaybackState.Playing, true, 0L, -1L),
                     (PlaybackState.Idle, false, 0L, 0L)
                 })
        {
            using var runtime = Open();
            var before = runtime.Current;

            var result = runtime.AcceptAudio(Audio(before, state, new(loaded, position, duration, false)));

            Assert.Equal(PlaybackTransitionRejection.InvalidTarget, result.Rejection);
            Assert.Same(before, runtime.Current);
        }
    }

    [Fact]
    public void Terminal_audio_keeps_resume_checkpoint_when_device_position_resets()
    {
        using var runtime = Open();
        runtime.AcceptAudio(Audio(runtime.Current, PlaybackState.Playing, new(true, 321, 1000, false)));

        var result = runtime.AcceptAudio(Audio(runtime.Current, PlaybackState.Stopped, PlaybackAudioFacts.Empty));

        Assert.True(result.IsAccepted);
        Assert.Equal(321, runtime.Current.PositionForSave);
        Assert.Equal(PlaybackIntent.Play, runtime.Current.Intent);
        Assert.Equal(0, PlaybackSnapshotProjector.Project(runtime.Current).PositionMilliseconds);
        Assert.False(runtime.Current.Audio.HasLoadedAudio);
    }

    [Fact]
    public void Audio_result_requires_current_session_book_source_catalog_and_position()
    {
        foreach (var mismatch in new[] { "session", "book", "source", "catalog", "position", "target-revision" })
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
            var targetIdentity = mismatch == "target-revision"
                ? before.Target!.Identity with { Revision = before.Target.Identity.Revision + 1 }
                : before.Target!.Identity;
            var result = runtime.AcceptAudio(new(identity,
                mismatch == "position" ? new(4, 1) : before.Position!.Value,
                PlaybackState.Playing, new(true, 321, 1000, true), TargetIdentity: targetIdentity,
                PreparationIdentity: before.Preparation!.Identity));

            Assert.Equal(PlaybackTransitionRejection.StaleSession, result.Rejection);
            Assert.Empty(result.Effects);
            Assert.Same(before, runtime.Current);
        }
    }

    [Fact]
    public void Old_audio_and_protection_are_rejected_after_replacement()
    {
        using var runtime = Open();
        var old = runtime.Current;
        Retire(runtime.CommitSessionReplacement(runtime.PrepareSessionReplacement(Target()).Replacement!, CancellationToken.None));
        var before = runtime.Current;
        var protection = new TrackingDisposable();

        foreach (var state in new[] { PlaybackState.Playing, PlaybackState.Stopped, PlaybackState.Faulted })
        {
            var result = runtime.AcceptAudio(Audio(old, state,
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
        Assert.True(runtime.AcceptAudio(Audio(runtime.Current, PlaybackState.Playing, new(true, 321, 1000, true))).IsAccepted);
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
        runtime.AcceptAudio(Audio(runtime.Current, PlaybackState.Playing, new(true, 321, 1000, false)));
        var before = runtime.Current;

        var result = runtime.ChangeSpeechConfiguration(before.Identity!, null, int.MaxValue);

        Assert.True(result.IsAccepted);
        Assert.Null(runtime.Current.Provider);
        Assert.Equal(AppSettings.NormalizeSpeakSpeed(int.MaxValue), runtime.Current.SpeakSpeed);
        Assert.Same(before.Audio, runtime.Current.Audio);
        Assert.Equal(before.Position, runtime.Current.Position);
        Assert.Equal(PlaybackState.Playing, runtime.Current.State);
    }

    [Fact]
    public void Only_successful_playing_result_clears_failure_window()
    {
        using var runtime = new PlaybackRuntime();
        runtime.CommitSessionReplacement(runtime.PrepareSessionReplacement(Target() with
        {
            ConsecutiveSegmentFailureCount = 2,
            State = PlaybackState.Preparing
        }).Replacement!, CancellationToken.None);
        runtime.AcceptAudio(Audio(runtime.Current, PlaybackState.Paused, new(true, 0, 1000, false)));
        Assert.Equal(2, runtime.Current.ConsecutiveSegmentFailureCount);
        runtime.AcceptAudio(Audio(runtime.Current, PlaybackState.Playing, new(true, 0, 1000, false)));
        Assert.Equal(0, runtime.Current.ConsecutiveSegmentFailureCount);
    }

    [Fact]
    public void Empty_or_unloaded_book_can_be_stopped_without_fabricated_position_or_checkpoint()
    {
        using var runtime = new PlaybackRuntime();
        var target = Target() with { Book = new("book-1", "测试小说", []), Position = null, State = PlaybackState.Stopped };

        var result = runtime.CommitSessionReplacement(runtime.PrepareSessionReplacement(target).Replacement!, CancellationToken.None);

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
        Assert.Throws<ObjectDisposedException>(() => runtime.PrepareSessionReplacement(Target()));
    }

    private static PlaybackRuntime Open()
    {
        var runtime = new PlaybackRuntime();
        Assert.True(runtime.CommitSessionReplacement(runtime.PrepareSessionReplacement(Target() with { State = PlaybackState.Preparing }).Replacement!, CancellationToken.None).IsAccepted);
        return runtime;
    }

    private static PlaybackAudioResult Audio(
        PlaybackRuntimeState state,
        PlaybackState playbackState,
        PlaybackAudioFacts facts,
        string? message = null) =>
        new(state.Identity!, state.Position!.Value, playbackState, facts, message, state.Target!.Identity,
            state.Preparation?.Identity ?? state.Audio.PreparationIdentity);

    [Fact]
    public void Cancellation_of_a_captured_old_identity_cannot_cancel_the_replacement()
    {
        using var runtime = Open();
        var captured = runtime.Current.Identity!;
        var replacement = runtime.CommitSessionReplacement(runtime.PrepareSessionReplacement(Target() with
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
