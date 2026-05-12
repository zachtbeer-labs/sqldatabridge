using System.Text;

namespace Zachtbeer.SqlDataBridge.Internal;

internal sealed record TableName(string Schema, string Name)
{
    public string FullName => $"{Schema}.{Name}";
}

internal static class BridgeIdentifier
{
    public static string QuoteSqlServerName(string value)
    {
        return $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    public static string QuoteSqlServerTable(TableName table)
    {
        return $"{QuoteSqlServerName(table.Schema)}.{QuoteSqlServerName(table.Name)}";
    }

    public static string QuoteSqliteName(string value)
    {
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    public static string ToSqliteDataTableName(TableName table, string? dataTablePrefix = "zsb_data")
    {
        var name = Sanitize(table.Schema) + "__" + Sanitize(table.Name);
        var prefix = NormalizeSqliteDataTablePrefix(dataTablePrefix);
        return prefix.Length == 0 ? name : prefix + "_" + name;
    }

    public static string NormalizeSqliteDataTablePrefix(string? value)
    {
        var prefix = value?.Trim() ?? string.Empty;
        if (prefix.Length == 0)
        {
            return string.Empty;
        }

        if (prefix.Any(ch => !IsAsciiLetterOrDigit(ch) && ch != '_'))
        {
            throw new BridgeException("DataTablePrefix can contain only letters, digits, and underscores.");
        }

        return prefix;
    }

    public static bool MatchesPattern(TableName table, string pattern)
    {
        var normalized = table.FullName;
        if (!pattern.Contains('*', StringComparison.Ordinal))
        {
            return string.Equals(normalized, pattern, StringComparison.OrdinalIgnoreCase)
                || string.Equals(table.Name, pattern, StringComparison.OrdinalIgnoreCase);
        }

        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(
            normalized,
            regexPattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    public static (string Schema, string Table, string Column) ParseColumnPath(string value)
    {
        var parts = value.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new BridgeException($"Column exclusion '{value}' is invalid. Use '<schema>.<table>.<column>', for example 'dbo.Customers.LegacyColumn'.");
        }

        return (parts[0], parts[1], parts[2]);
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        return builder.ToString().ToLowerInvariant();
    }

    private static bool IsAsciiLetterOrDigit(char value)
    {
        return value is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9';
    }
}
