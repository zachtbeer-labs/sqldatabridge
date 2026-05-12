CREATE TABLE dbo.UnsupportedPayloads (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    PayloadVariant SQL_VARIANT NULL
);

INSERT INTO dbo.UnsupportedPayloads (PayloadVariant)
VALUES (CAST(123 AS INT));

INSERT INTO dbo.UnsupportedPayloads (PayloadVariant)
VALUES (CAST('abc' AS NVARCHAR(10)));
