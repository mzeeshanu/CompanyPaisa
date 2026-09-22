using System.IO.Compression;
using System.Text;
using System.Xml;

namespace CompanyPaisa.Importer.Salaries;

/// <summary>
/// Reads the first sheet of an .xlsx file row by row without loading it: the Department of Labor's disclosure files are a
/// million rows (1.6 GB of sheet XML) each. Only the named columns are returned, keyed by their header.
/// </summary>
public static class XlsxRows
{
    public static IEnumerable<IReadOnlyDictionary<string, string>> Read(string path, IReadOnlySet<string> columns)
    {
        using var zip = ZipFile.OpenRead(path);
        var strings = SharedStrings(zip);
        var sheet = zip.GetEntry("xl/worksheets/sheet1.xml") ?? throw new InvalidDataException($"{path} has no first sheet.");
        using var stream = sheet.Open();
        using var xml = XmlReader.Create(stream, new XmlReaderSettings { IgnoreWhitespace = true });

        Dictionary<int, string>? wanted = null;   // column index → header
        while (xml.ReadToFollowing("row", Main))
        {
            var cells = ReadRow(xml, strings, wanted);
            if (wanted is null)
            {
                wanted = cells.Where(c => columns.Contains(c.Value.Trim())).ToDictionary(c => c.Key, c => c.Value.Trim());
                var missing = columns.Except(wanted.Values).ToList();
                if (missing.Count > 0) throw new InvalidDataException($"{Path.GetFileName(path)} has no column {string.Join(", ", missing)}.");
                continue;
            }
            yield return cells.ToDictionary(c => wanted[c.Key], c => c.Value);
        }
    }

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>One row's cells by column index (only the wanted ones once the header is known).</summary>
    private static Dictionary<int, string> ReadRow(XmlReader xml, string[] strings, Dictionary<int, string>? wanted)
    {
        var cells = new Dictionary<int, string>();
        using var row = xml.ReadSubtree();
        row.Read();
        while (row.ReadToFollowing("c", Main))
        {
            var column = ColumnIndex(row.GetAttribute("r"));
            var type = row.GetAttribute("t");
            if (wanted is not null && !wanted.ContainsKey(column)) continue;
            string? value = null;
            using (var cell = row.ReadSubtree())
            {
                while (cell.Read())
                {
                    if (cell.NodeType != XmlNodeType.Element) continue;
                    if (cell.LocalName == "v") { value = cell.ReadElementContentAsString(); break; }
                    if (cell.LocalName == "is") { value = Text(cell); break; }
                }
            }
            if (value is null) continue;
            cells[column] = type == "s" && int.TryParse(value, out var i) && i < strings.Length ? strings[i] : value;
        }
        return cells;
    }

    private static string[] SharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        var list = new List<string>();
        using var stream = entry.Open();
        using var xml = XmlReader.Create(stream);
        while (xml.ReadToFollowing("si", Main)) list.Add(Text(xml));
        return [.. list];
    }

    /// <summary>The text of a shared or inline string, rich-text runs joined.</summary>
    private static string Text(XmlReader reader)
    {
        var sb = new StringBuilder();
        using var sub = reader.ReadSubtree();
        while (sub.Read())
            if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "t") sb.Append(sub.ReadElementContentAsString());
        return sb.ToString();
    }

    /// <summary>"CT123" → 97 (zero-based).</summary>
    internal static int ColumnIndex(string? reference)
    {
        var n = 0;
        foreach (var ch in reference ?? "")
        {
            if (ch is < 'A' or > 'Z') break;
            n = n * 26 + (ch - 'A' + 1);
        }
        return n - 1;
    }
}
