using System.Collections;
using System.Data;
using Microsoft.Data.Sqlite;

namespace Zachtbeer.SqlDataBridge.Internal;

internal sealed class SqliteCoercingDataReader : IDataReader
{
    private readonly SqliteDataReader _inner;
    private readonly IReadOnlyList<ColumnMetadata> _columns;
    private readonly Action _onRowRead;

    public SqliteCoercingDataReader(SqliteDataReader inner, IReadOnlyList<ColumnMetadata> columns, Action onRowRead)
    {
        _inner = inner;
        _columns = columns;
        _onRowRead = onRowRead;
    }

    public int FieldCount => _columns.Count;
    public object this[int i] => GetValue(i);
    public object this[string name] => GetValue(GetOrdinal(name));
    public int Depth => _inner.Depth;
    public bool IsClosed => _inner.IsClosed;
    public int RecordsAffected => _inner.RecordsAffected;

    public bool Read()
    {
        var hasRow = _inner.Read();
        if (hasRow)
        {
            _onRowRead();
        }

        return hasRow;
    }

    public object GetValue(int i)
    {
        var value = _inner.GetValue(i);
        return ValueConverter.FromSqliteValue(value, _columns[i]) ?? DBNull.Value;
    }

    public int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }

        return count;
    }

    public string GetName(int i) => _columns[i].Name;
    public int GetOrdinal(string name)
    {
        for (var i = 0; i < _columns.Count; i++)
        {
            if (string.Equals(_columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public bool IsDBNull(int i) => _inner.IsDBNull(i);
    public string GetDataTypeName(int i) => _columns[i].SqlServerTypeName;
    public Type GetFieldType(int i)
    {
        return _columns[i].SqlServerTypeName.ToLowerInvariant() switch
        {
            "bit" => typeof(bool),
            "tinyint" => typeof(byte),
            "smallint" => typeof(short),
            "int" => typeof(int),
            "bigint" => typeof(long),
            "real" => typeof(float),
            "float" => typeof(double),
            "decimal" or "numeric" or "money" or "smallmoney" => typeof(decimal),
            "date" or "datetime" or "datetime2" or "smalldatetime" => typeof(DateTime),
            "datetimeoffset" => typeof(DateTimeOffset),
            "time" => typeof(TimeSpan),
            "uniqueidentifier" => typeof(Guid),
            "binary" or "varbinary" or "image" => typeof(byte[]),
            _ => typeof(string)
        };
    }
    public DataTable? GetSchemaTable() => null;
    public bool NextResult() => false;
    public void Close() => _inner.Close();
    public void Dispose() => _inner.Dispose();

    public bool GetBoolean(int i) => Convert.ToBoolean(GetValue(i));
    public byte GetByte(int i) => Convert.ToByte(GetValue(i));
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => _inner.GetBytes(i, fieldOffset, buffer, bufferoffset, length);
    public char GetChar(int i) => Convert.ToChar(GetValue(i));
    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();
    public IDataReader GetData(int i) => throw new NotSupportedException();
    public DateTime GetDateTime(int i) => Convert.ToDateTime(GetValue(i));
    public decimal GetDecimal(int i) => Convert.ToDecimal(GetValue(i));
    public double GetDouble(int i) => Convert.ToDouble(GetValue(i));
    public float GetFloat(int i) => Convert.ToSingle(GetValue(i));
    public Guid GetGuid(int i) => (Guid)GetValue(i);
    public short GetInt16(int i) => Convert.ToInt16(GetValue(i));
    public int GetInt32(int i) => Convert.ToInt32(GetValue(i));
    public long GetInt64(int i) => Convert.ToInt64(GetValue(i));
    public string GetString(int i) => Convert.ToString(GetValue(i))!;
    public IEnumerator GetEnumerator()
    {
        while (Read())
        {
            yield return this;
        }
    }
}
