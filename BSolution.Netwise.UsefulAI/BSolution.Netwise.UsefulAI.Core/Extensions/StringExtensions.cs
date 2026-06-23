namespace BSolution.Netwise.UsefulAI.Core.Extensions;

public static class StringExtensions
{
    /// <summary>
    /// Splits text into chunks on word boundaries with optional overlap between adjacent chunks.
    /// Overlap prevents context loss at chunk boundaries during vector search.
    /// </summary>
    /// <param name="text">Text to split.</param>
    /// <param name="maxChars">Maximum chunk length in characters.</param>
    /// <param name="overlapFraction">Overlap share as a fraction of maxChars (0.0 = none, 0.15 = 15%).</param>
    public static List<string> SplitIntoChunks(this string text, int maxChars, double overlapFraction = 0)
    {
        if (text.Length <= maxChars) return [text];

        var overlapChars = (int)(maxChars * overlapFraction);
        var chunks = new List<string>();
        var pos = 0;

        while (pos < text.Length)
        {
            if (text.Length - pos <= maxChars)
            {
                chunks.Add(text[pos..]);
                break;
            }

            // Search for a word boundary within maxChars
            var slice = text.AsSpan(pos, maxChars);
            var lastSpace = slice.LastIndexOf(' ');
            var cutAt = lastSpace > 0 ? lastSpace : maxChars;

            chunks.Add(text.Substring(pos, cutAt));

            // Step back by overlap so the next chunk starts with context
            var advance = cutAt - overlapChars;
            if (advance <= 0) advance = cutAt; // guard against infinite loop
            pos += advance;

            // Skip leading whitespace at the start of the next chunk
            while (pos < text.Length && text[pos] == ' ') pos++;
        }

        return chunks;
    }
}
