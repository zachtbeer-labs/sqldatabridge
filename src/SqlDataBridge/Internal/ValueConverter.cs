using System.Globalization;
using System.Data.SqlTypes;
using Microsoft.Data.SqlTypes;
using Microsoft.Data.Sqlite;
using Zachtbeer.SqlDataBridge.Models;

namespace Zachtbeer.SqlDataBridge.Internal;

internal static class ValueConverter
{
    private static readonly HashSet<string> IntegerTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bigint", "int", "smallint", "tinyint", "bit"
    };

    private static readonly HashSet<string> RealTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "float", "real"
    };

    private static readonly HashSet<string> TextTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "char", "varchar", "text", "nchar", "nvarchar", "ntext", "date", "datetime", "datetime2",
        "datetimeoffset", "smalldatetime", "time", "decimal", "numeric", "money", "smallmoney",
        "uniqueidentifier", "xml", "json"
    };

    private static readonly HashSet<string> BlobTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "binary", "varbinary", "image"
    };

    public static bool IsUnsupported(string typeName)
    {
        return typeName.Equals("sql_variant", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("timestamp", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("rowversion", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("geography", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("geometry", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("hierarchyid", StringComparison.OrdinalIgnoreCase);
    }

    public static string SqliteTypeFor(ColumnMetadata column)
    {
        if (IntegerTypes.Contains(column.SqlServerTypeName))
        {
            return "INTEGER";
        }

        if (RealTypes.Contains(column.SqlServerTypeName))
        {
            return "REAL";
        }

        if (BlobTypes.Contains(column.SqlServerTypeName))
        {
            return "BLOB";
        }

        if (TextTypes.Contains(column.SqlServerTypeName))
        {
            return "TEXT";
        }

        throw new BridgeException($"Unsupported SQL Server type '{column.SqlServerTypeName}' on {column.Table.FullName}.{column.Name}.");
    }

    public static object? ToSqliteValue(object value, ColumnMetadata column)
    {
        if (value is DBNull)
        {
            return DBNull.Value;
        }

        var type = column.SqlServerTypeName;
        if (type.Equals("bit", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? 1 : 0;
        }

        if (type.Equals("uniqueidentifier", StringComparison.OrdinalIgnoreCase))
        {
            return value is Guid guid ? guid.ToString("D") : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        if (type.Equals("xml", StringComparison.OrdinalIgnoreCase))
        {
            return value is SqlXml sqlXml ? sqlXml.Value : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        if (type.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            return value is SqlJson sqlJson ? sqlJson.Value : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        if (type.Equals("decimal", StringComparison.OrdinalIgnoreCase)
            || type.Equals("numeric", StringComparison.OrdinalIgnoreCase)
            || type.Equals("money", StringComparison.OrdinalIgnoreCase)
            || type.Equals("smallmoney", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }

        if (type.Equals("date", StringComparison.OrdinalIgnoreCase)
            || type.Equals("datetime", StringComparison.OrdinalIgnoreCase)
            || type.Equals("datetime2", StringComparison.OrdinalIgnoreCase)
            || type.Equals("smalldatetime", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToDateTime(value, CultureInfo.InvariantCulture).ToString("O", CultureInfo.InvariantCulture);
        }

        if (type.Equals("datetimeoffset", StringComparison.OrdinalIgnoreCase))
        {
            return value is DateTimeOffset dto
                ? dto.ToString("O", CultureInfo.InvariantCulture)
                : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        if (type.Equals("time", StringComparison.OrdinalIgnoreCase))
        {
            return value is TimeSpan span ? span.ToString("c", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        return value;
    }

    public static object? FromSqliteValue(object? value, ColumnMetadata column)
    {
        if (value is null || value is DBNull)
        {
            return DBNull.Value;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return column.SqlServerTypeName.ToLowerInvariant() switch
        {
            "tinyint" => Convert.ToByte(value, CultureInfo.InvariantCulture),
            "smallint" => Convert.ToInt16(value, CultureInfo.InvariantCulture),
            "int" => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            "bigint" => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            "bit" => Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0,
            "real" => Convert.ToSingle(value, CultureInfo.InvariantCulture),
            "float" => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            "uniqueidentifier" => Guid.Parse(text!),
            "decimal" or "numeric" or "money" or "smallmoney" => decimal.Parse(text!, CultureInfo.InvariantCulture),
            "date" or "datetime" or "datetime2" or "smalldatetime" => DateTime.Parse(text!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            "datetimeoffset" => DateTimeOffset.Parse(text!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            "time" => TimeSpan.Parse(text!, CultureInfo.InvariantCulture),
            "xml" => text,
            "json" => text,
            _ => value
        };
    }

    public static void BindSqliteParameter(SqliteParameter parameter, object? value)
    {
        parameter.Value = value ?? DBNull.Value;
    }
}
