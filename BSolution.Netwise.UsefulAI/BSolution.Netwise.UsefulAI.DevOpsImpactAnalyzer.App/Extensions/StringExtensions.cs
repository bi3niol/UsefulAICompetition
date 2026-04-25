namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App;

public static class StringExtensions
{
    /// <summary>
    /// Dzieli tekst na chunki po granicy słowa z opcjonalnym overlappem między sąsiednimi chunkami.
    /// Overlap zapobiega utracie kontekstu na granicy chunków podczas wyszukiwania wektorowego.
    /// </summary>
    /// <param name="text">Tekst do podzielenia.</param>
    /// <param name="maxChars">Maksymalna długość chunka w znakach.</param>
    /// <param name="overlapFraction">Udział overlapa jako ułamek maxChars (0.0 = brak, 0.15 = 15%).</param>
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

            // Szukamy granicy słowa w obrębie maxChars
            var slice = text.AsSpan(pos, maxChars);
            var lastSpace = slice.LastIndexOf(' ');
            var cutAt = lastSpace > 0 ? lastSpace : maxChars;

            chunks.Add(text.Substring(pos, cutAt));

            // Cofamy się o overlap, żeby kolejny chunk zaczął się z kontekstem
            var advance = cutAt - overlapChars;
            if (advance <= 0) advance = cutAt; // zabezpieczenie przed nieskończoną pętlą
            pos += advance;

            // Pomijamy białe znaki na początku następnego chunka
            while (pos < text.Length && text[pos] == ' ') pos++;
        }

        return chunks;
    }
}
