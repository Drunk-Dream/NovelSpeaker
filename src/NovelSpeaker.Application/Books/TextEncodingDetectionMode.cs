namespace NovelSpeaker.Application.Books;

public enum TextEncodingDetectionMode
{
    ManualOverride,
    BomUtf8,
    BomUtf16Le,
    BomUtf16Be,
    StrictUtf8,
    Gb18030Fallback
}
