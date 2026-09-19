using System.Globalization;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace CompanyPaisa.Importer.Pk;

/// <summary>
/// One line of a PDF page: its text, with table cells separated by two spaces, and the horizontal centre of each cell
/// (so a column heading can be matched to the amounts printed under it).
/// </summary>
public sealed record PdfTextLine(string Text, IReadOnlyList<double> CellCenters)
{
    public static PdfTextLine Plain(string text) => new(text, []);

    /// <summary>"text␟x1,x2,…" (unit separator, char 31) — how the importer caches a report's lines.</summary>
    public string Serialise() => Text + Separator + string.Join(",", CellCenters.Select(c => c.ToString("0.#", CultureInfo.InvariantCulture)));

    public static PdfTextLine Deserialise(string line)
    {
        var at = line.LastIndexOf(Separator);
        if (at < 0) return Plain(line);
        var centers = new List<double>();
        foreach (var c in line[(at + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(c, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return Plain(line[..at]);
            centers.Add(x);
        }
        return new PdfTextLine(line[..at], centers);
    }

    private const char Separator = (char)31;
}

/// <summary>
/// A PDF's text as lines, the way a person reads a statement: words on the same baseline joined left to right, with the
/// gap to the next word kept as "  " when it's wide (so table columns stay apart). Scanned PDFs (images only) give nothing.
/// </summary>
public static class PdfLines
{
    public static IReadOnlyList<PdfTextLine> Read(byte[] pdf)
    {
        var lines = new List<PdfTextLine>();
        try
        {
            using var doc = PdfDocument.Open(pdf);
            foreach (var page in doc.GetPages())
                lines.AddRange(PageLines(page));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Damaged or encrypted files: treated like a scan (nothing readable).
        }
        return lines;
    }

    private static IEnumerable<PdfTextLine> PageLines(Page page)
    {
        var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        // A spread (two printed pages side by side on one landscape sheet): read the left page, then the right one,
        // so a statement isn't interleaved with the one printed next to it.
        if (page.Width > page.Height * 1.2)
        {
            var middle = page.Width / 2;
            return Lines(words.Where(w => w.BoundingBox.Centroid.X < middle).ToList())
                .Concat(Lines(words.Where(w => w.BoundingBox.Centroid.X >= middle).ToList()));
        }
        return Lines(words);
    }

    private static IEnumerable<PdfTextLine> Lines(List<Word> words)
    {
        // Words whose baselines are within a couple of points share a line.
        foreach (var row in words.GroupBy(w => Math.Round(w.BoundingBox.Bottom / 2.5)).OrderByDescending(g => g.Key))
        {
            var ordered = row.OrderBy(w => w.BoundingBox.Left).ToList();
            var sb = new StringBuilder();
            var centers = new List<double>();
            var cellLeft = ordered[0].BoundingBox.Left;
            for (var i = 0; i < ordered.Count; i++)
            {
                if (i > 0)
                {
                    var gap = ordered[i].BoundingBox.Left - ordered[i - 1].BoundingBox.Right;
                    var size = Math.Max(1, ordered[i].BoundingBox.Height);
                    var newCell = gap > size * 1.2;
                    if (newCell)
                    {
                        centers.Add((cellLeft + ordered[i - 1].BoundingBox.Right) / 2);
                        cellLeft = ordered[i].BoundingBox.Left;
                    }
                    sb.Append(newCell ? "  " : " ");
                }
                sb.Append(ordered[i].Text);
            }
            centers.Add((cellLeft + ordered[^1].BoundingBox.Right) / 2);
            // Ligatures ("ﬁ") as plain letters.
            // Control characters (fonts without a text mapping) as spaces.
            var text = new string(sb.ToString().Normalize(NormalizationForm.FormKC).Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray());
            yield return new PdfTextLine(text, centers);
        }
    }

    /// <summary>For the debug command: the lines, numbered.</summary>
    public static IEnumerable<string> Describe(byte[] pdf) =>
        Read(pdf).Select((l, i) => string.Create(CultureInfo.InvariantCulture, $"{i,4}: {l.Text}"));
}
