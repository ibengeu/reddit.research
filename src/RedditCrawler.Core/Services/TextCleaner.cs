using System.Text.RegularExpressions;

namespace RedditCrawler.Services;

public static partial class TextCleaner
{
    public static string Clean(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var result = text;
        result = MarkdownLinkRegex().Replace(result, "$1");   // [text](url) -> text
        result = BoldItalicRegex().Replace(result, "$1");       // **bold** / *italic* -> text
        result = StrikethroughRegex().Replace(result, "$1");    // ~~text~~ -> text
        result = InlineCodeRegex().Replace(result, "$1");       // `code` -> code
        result = BlockQuoteRegex().Replace(result, "");         // > quote prefix
        result = HeadingRegex().Replace(result, "");            // # heading prefix
        result = HrRegex().Replace(result, " ");                // --- horizontal rules
        result = HtmlEntityRegex().Replace(result, match => DecodeEntity(match.Value));
        result = MultipleNewlinesRegex().Replace(result, "\n"); // collapse blank lines
        result = MultipleSpacesRegex().Replace(result, " ");    // collapse spaces

        return result.Trim();
    }

    /// <summary>
    /// Rough token estimate: ~4 chars per token for English text.
    /// </summary>
    public static int EstimateTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);

    public static List<string> ChunkText(string text, int maxTokens, int overlapTokens = 0)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var totalTokens = EstimateTokens(text);
        if (totalTokens <= maxTokens) return [text];

        var sentences = SentenceSplitRegex().Split(text)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

        var chunks = new List<string>();
        var current = "";

        for (var i = 0; i < sentences.Length; i++)
        {
            var sentence = sentences[i];
            var candidate = string.IsNullOrEmpty(current) ? sentence : current + " " + sentence;

            if (EstimateTokens(candidate) > maxTokens && !string.IsNullOrEmpty(current))
            {
                chunks.Add(current.Trim());

                // Build overlap tail: walk back through sentences already in `current`
                // collecting up to overlapTokens worth of text to seed the next chunk.
                // Sentences are collected in reverse then reversed back to preserve original order.
                var overlap = "";
                if (overlapTokens > 0)
                {
                    var currentSentences = SentenceSplitRegex().Split(current)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToArray();

                    var overlapParts = new List<string>();
                    for (var j = currentSentences.Length - 1; j >= 0; j--)
                    {
                        var tentative = new List<string>(overlapParts) { currentSentences[j] };
                        tentative.Reverse();
                        var joined = string.Join(" ", tentative);
                        if (EstimateTokens(joined) <= overlapTokens)
                            overlapParts.Add(currentSentences[j]);
                        else
                            break;
                    }
                    overlapParts.Reverse();
                    overlap = string.Join(" ", overlapParts);
                }

                current = string.IsNullOrEmpty(overlap) ? sentence : overlap + " " + sentence;
            }
            else
            {
                current = candidate;
            }
        }

        if (!string.IsNullOrWhiteSpace(current))
            chunks.Add(current.Trim());

        return chunks;
    }

    private static string DecodeEntity(string entity) => entity switch
    {
        "&amp;" => "&",
        "&lt;" => "<",
        "&gt;" => ">",
        "&quot;" => "\"",
        "&#39;" => "'",
        "&#x27;" => "'",
        "&nbsp;" or "&#160;" => " ",
        "&#8217;" => "\u2019",
        "&#8216;" => "\u2018",
        "&#8220;" => "\u201C",
        "&#8221;" => "\u201D",
        "&#8230;" => "\u2026",
        "&#8211;" => "\u2013",
        "&#8212;" => "\u2014",
        _ => entity
    };

    [GeneratedRegex(@"\[([^\]]+)\]\([^\)]+\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"\*{1,3}([^*]+)\*{1,3}")]
    private static partial Regex BoldItalicRegex();

    [GeneratedRegex(@"~~([^~]+)~~")]
    private static partial Regex StrikethroughRegex();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"^>\s?", RegexOptions.Multiline)]
    private static partial Regex BlockQuoteRegex();

    [GeneratedRegex(@"^#{1,6}\s+", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^-{3,}$", RegexOptions.Multiline)]
    private static partial Regex HrRegex();

    [GeneratedRegex(@"&(?:amp|lt|gt|quot|nbsp|#39|#x27|#160|#821[1267]|#8220|#8221|#8230);")]
    private static partial Regex HtmlEntityRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleNewlinesRegex();

    [GeneratedRegex(@" {2,}")]
    private static partial Regex MultipleSpacesRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceSplitRegex();
}
