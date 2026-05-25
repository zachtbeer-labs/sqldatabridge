namespace Zachtbeer.SqlDataBridge.IntegrationTests.Harness;

internal static class TargetSchemaScripts
{
    public static string ComputedInvoiceLines()
    {
        return """
            CREATE TABLE dbo.InvoiceLines (
                InvoiceLineId INT IDENTITY(1,1) PRIMARY KEY,
                Qty INT NOT NULL,
                UnitPrice DECIMAL(18,2) NOT NULL,
                ExtendedPrice AS (Qty * UnitPrice)
            );
            """;
    }

    public static string IdentityForeignKeys(bool includeChild = true)
    {
        var child = includeChild
            ? """

            CREATE TABLE dbo.Child (
                ChildId INT IDENTITY(1,1) PRIMARY KEY,
                ParentId INT NOT NULL,
                ChildName NVARCHAR(50) NOT NULL,
                CONSTRAINT FK_Child_Parent FOREIGN KEY (ParentId) REFERENCES dbo.Parent(ParentId)
            );
            """
            : string.Empty;

        return $$"""
            CREATE TABLE dbo.Parent (
                ParentId INT IDENTITY(1,1) PRIMARY KEY,
                ParentName NVARCHAR(50) NOT NULL
            );
            {{child}}
            """;
    }

    public static string IncludeMe(
        bool includeKeepCol = true,
        bool includeSkipCol = true,
        bool nullableExtra = false,
        bool defaultedExtra = false,
        bool requiredExtra = false)
    {
        var columns = new List<string>
        {
            "Id INT IDENTITY(1,1) PRIMARY KEY"
        };

        if (includeKeepCol)
        {
            columns.Add("KeepCol NVARCHAR(50) NOT NULL");
        }

        if (includeSkipCol)
        {
            columns.Add("SkipCol NVARCHAR(50) NULL");
        }

        if (nullableExtra)
        {
            columns.Add("NullableExtra NVARCHAR(50) NULL");
        }

        if (defaultedExtra)
        {
            columns.Add("DefaultedExtra INT NOT NULL DEFAULT 42");
        }

        if (requiredExtra)
        {
            columns.Add("RequiredExtra INT NOT NULL");
        }

        return $"""
            CREATE TABLE dbo.IncludeMe (
                {string.Join("," + Environment.NewLine + "                ", columns)}
            );
            """;
    }

    public static string TypeSamples()
    {
        return """
            CREATE TABLE dbo.TypeSamples (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                TinyValue TINYINT NOT NULL,
                SmallValue SMALLINT NOT NULL,
                IntValue INT NOT NULL,
                BigValue BIGINT NOT NULL,
                RealValue REAL NOT NULL,
                FloatValue FLOAT NOT NULL,
                CharValue CHAR(3) NOT NULL,
                VarCharValue VARCHAR(20) NOT NULL,
                NCharValue NCHAR(3) NOT NULL,
                NVarCharValue NVARCHAR(20) NOT NULL,
                NumericValue NUMERIC(12,4) NOT NULL,
                DecimalValue DECIMAL(18,6) NOT NULL,
                MoneyValue MONEY NOT NULL,
                SmallMoneyValue SMALLMONEY NOT NULL,
                DateValue DATE NOT NULL,
                DateTimeValue DATETIME NOT NULL,
                DateTime2Value DATETIME2(7) NOT NULL,
                DateTimeOffsetValue DATETIMEOFFSET(3) NOT NULL,
                TimeValue TIME(4) NOT NULL,
                GuidValue UNIQUEIDENTIFIER NOT NULL,
                BlobValue VARBINARY(16) NOT NULL,
                NullableText NVARCHAR(20) NULL,
                NullableInt INT NULL,
                NullableDate DATETIME2(3) NULL
            );
            """;
    }

    public static string XmlPayloads()
    {
        return """
            CREATE TABLE dbo.XmlPayloads (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                PayloadName NVARCHAR(40) NOT NULL,
                PayloadXml XML NULL
            );
            """;
    }

    public static string JsonPayloads()
    {
        return """
            CREATE TABLE dbo.JsonPayloads (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                PayloadName NVARCHAR(40) NOT NULL,
                PayloadJson JSON NULL
            );
            """;
    }

    public static string RvAudit(bool includeRowversion = true)
    {
        var rv = includeRowversion ? "," + Environment.NewLine + "                Rv ROWVERSION" : string.Empty;
        return $$"""
            CREATE TABLE dbo.RvAudit (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                Name NVARCHAR(50) NOT NULL{{rv}}
            );
            """;
    }

    public static string SparseRows()
    {
        return """
            CREATE TABLE dbo.SparseRows (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                A NVARCHAR(50) NULL,
                B NVARCHAR(50) NULL,
                C NVARCHAR(50) NULL,
                D INT NULL,
                E DECIMAL(18,4) NULL,
                F DATETIME2(7) NULL,
                G VARBINARY(64) NULL,
                H UNIQUEIDENTIFIER NULL
            );
            """;
    }

    public static string UnicodeRows()
    {
        return """
            CREATE TABLE dbo.UnicodeRows (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                Label NVARCHAR(200) NOT NULL
            );
            """;
    }

    public static string DateExtremes()
    {
        return """
            CREATE TABLE dbo.DateExtremes (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                Dt2Min DATETIME2(7) NOT NULL,
                Dt2Max DATETIME2(7) NOT NULL,
                DtoMin DATETIMEOFFSET(7) NOT NULL,
                DtoMax DATETIMEOFFSET(7) NOT NULL,
                Dt2NoFrac DATETIME2(0) NOT NULL,
                DecBig DECIMAL(28,10) NOT NULL,
                DecTight DECIMAL(5,5) NOT NULL
            );
            """;
    }
}
