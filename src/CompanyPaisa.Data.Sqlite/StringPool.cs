namespace CompanyPaisa.Data.Sqlite;

/// <summary>
/// Hands out one shared instance per distinct string while a data set loads. The reader builds a fresh string for every
/// cell, so a column like a company id, a sector, a job title or an executive's name arrives as thousands of separate
/// copies of the same handful of values — 59,000 pay rows hold only ~16,000 distinct names, and every financial row
/// repeats its company's id. Pooling them leaves one copy of each in memory.
/// </summary>
/// <remarks>
/// Matching is exact (<see cref="StringComparer.Ordinal"/>), so a pooled string is always character-for-character the
/// string that was read — casing and spacing included. Used for load only, then dropped with the pool itself.
/// </remarks>
internal sealed class StringPool
{
    private readonly Dictionary<string, string> _pool = new(StringComparer.Ordinal);

    /// <summary>The shared instance of <paramref name="value"/>, or the first one seen if this is a repeat.</summary>
    public string Of(string value)
    {
        if (_pool.TryGetValue(value, out var shared)) return shared;
        _pool[value] = value;
        return value;
    }

    /// <summary><see cref="Of"/> for a column that can be null.</summary>
    public string? OrNull(string? value) => value is null ? null : Of(value);

    /// <summary>How many distinct strings the pool holds (for the load log).</summary>
    public int Count => _pool.Count;
}
