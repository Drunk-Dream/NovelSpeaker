using System.Buffers;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Books.Parsing;

/// <summary>
/// Splits chapter text into newline-based natural paragraphs and optionally subdivides long paragraphs.
/// </summary>
public sealed class TextSegmenter : ITextSegmenter
{
    private static readonly SearchValues<char> SentenceTerminators = SearchValues.Create("。！？");

    public IReadOnlyList<SpeechSegment> Segment(string chapterText, TextSegmentationOptions options)
    {
        if (string.IsNullOrWhiteSpace(chapterText))
        {
            return [];
        }

        var normalizedOptions = options.Normalize();
        var segments = new List<SpeechSegment>();
        var content = chapterText;
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
            var segmentText = text.Substring(offset, length);
            segments.Add(new SpeechSegment(
                nextSegmentIndex++,
                startOffset + offset,
                length,
                segmentText,
                segmentText));
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
