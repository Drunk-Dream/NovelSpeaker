namespace NovelSpeaker.Application.Desktop.MediaControls;

public interface IMediaControlFailureReporter
{
    void ReportCommandFailure(MediaControlCommand command, Exception exception);

    void ReportMetadataFailure(Exception exception);
}
