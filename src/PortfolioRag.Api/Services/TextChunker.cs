using System.Text;
using UglyToad.PdfPig;

namespace PortfolioRag.Api.Services;

public record PageText(int PageNumber, string Text);
public record TextChunk(int PageNumber, int ChunkIndex, string Text);

/// <summary>Step 1 of RAG ingestion: turn a PDF into page text, then into overlapping chunks.</summary>
public static class TextChunker
{
    public static List<PageText> ExtractPages(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return doc.GetPages()
            .Select(p => new PageText(p.Number, Normalize(string.Join(" ", p.GetWords().Select(w => w.Text)))))
            .Where(p => p.Text.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Splits each page into ~maxChars chunks on sentence boundaries, with a small overlap
    /// so a fact that crosses a boundary is still found. Page numbers are kept for citations.
    /// </summary>
    public static List<TextChunk> Chunk(IEnumerable<PageText> pages, int maxChars = 1000, int overlapChars = 150)
    {
        var chunks = new List<TextChunk>();
        foreach (var page in pages)
        {
            var sentences = SplitSentences(page.Text);
            var current = new StringBuilder();
            var index = 0;

            foreach (var s in sentences)
            {
                if (current.Length + s.Length > maxChars && current.Length > 0)
                {
                    var text = current.ToString().Trim();
                    chunks.Add(new TextChunk(page.PageNumber, index++, text));
                    var tail = text.Length > overlapChars ? text[^overlapChars..] : text;
                    current.Clear().Append(tail).Append(' ');
                }
                current.Append(s).Append(' ');
            }
            if (current.ToString().Trim().Length > 0)
                chunks.Add(new TextChunk(page.PageNumber, index, current.ToString().Trim()));
        }
        return chunks;
    }

    private static IEnumerable<string> SplitSentences(string text) =>
        System.Text.RegularExpressions.Regex.Split(text, @"(?<=[\.\!\?])\s+").Where(s => s.Length > 0);

    private static string Normalize(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
}
