namespace CompanyPaisa.Data;

/// <summary>
/// Rows grouped by a key, held as one array plus the stretch of it each key owns. A dictionary of lists costs a List
/// object and its slack array for every key — tens of thousands of them across the data set; this costs one array of
/// references and one pair of ints per key. Lookup is still a single dictionary hit.
/// </summary>
/// <remarks>
/// Rows keep the order they arrived in within a group, matching what <c>GroupBy</c> gave before. Keys ignore case.
/// </remarks>
public sealed class GroupedRows<T>
{
    private readonly T[] _rows;
    private readonly Dictionary<string, (int Start, int Count)> _ranges;

    /// <summary>Shared answer for a key with no rows. Not <c>[]</c>: as an <see cref="ArraySegment{T}"/> that would be a
    /// segment over a null array, which throws the moment anything enumerates it.</summary>
    private static readonly IReadOnlyList<T> None = Array.Empty<T>();

    private GroupedRows(T[] rows, Dictionary<string, (int Start, int Count)> ranges)
    {
        _rows = rows;
        _ranges = ranges;
    }

    public static GroupedRows<T> Empty { get; } = new([], new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase));

    /// <summary>How many rows in total (every group together).</summary>
    public int Count => _rows.Length;

    /// <summary>How many distinct keys.</summary>
    public int Keys => _ranges.Count;

    /// <summary>The rows under <paramref name="key"/>, or an empty list if there are none.</summary>
    public IReadOnlyList<T> this[string key] =>
        _ranges.TryGetValue(key, out var r) ? new ArraySegment<T>(_rows, r.Start, r.Count) : None;

    public bool ContainsKey(string key) => _ranges.ContainsKey(key);

    /// <summary>Groups <paramref name="items"/> by <paramref name="key"/> in one pass over the rows per step.</summary>
    public static GroupedRows<T> Build(IReadOnlyList<T> items, Func<T, string> key)
    {
        if (items.Count == 0) return Empty;

        // 1. How many rows each key has.
        var sizes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var k = key(item);
            sizes[k] = sizes.TryGetValue(k, out var n) ? n + 1 : 1;
        }

        // 2. Hand each key the stretch it owns. Cursor starts at the stretch's beginning and walks along it as rows land.
        var ranges = new Dictionary<string, (int Start, int Count)>(sizes.Count, StringComparer.OrdinalIgnoreCase);
        var cursors = new Dictionary<string, int>(sizes.Count, StringComparer.OrdinalIgnoreCase);
        var offset = 0;
        foreach (var (k, size) in sizes)
        {
            ranges[k] = (offset, size);
            cursors[k] = offset;
            offset += size;
        }

        // 3. Place the rows, keeping the order they came in.
        var rows = new T[items.Count];
        foreach (var item in items)
        {
            var k = key(item);
            rows[cursors[k]++] = item;
        }
        return new GroupedRows<T>(rows, ranges);
    }
}
