namespace NovelSpeaker.Application.Books;

public enum DirectBookImportStatus
{
    Imported,
    RequiresEncodingSelection,
    RequiresMetadataConfirmation,
    Failed
}
