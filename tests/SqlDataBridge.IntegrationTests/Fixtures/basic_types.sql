CREATE TABLE dbo.Customers (
    CustomerId INT IDENTITY(1,1) PRIMARY KEY,
    ExternalId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    CreditLimit DECIMAL(18,2) NOT NULL,
    IsActive BIT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL
);

CREATE TABLE dbo.Orders (
    OrderId INT IDENTITY(1,1) PRIMARY KEY,
    CustomerId INT NOT NULL,
    OrderTotal DECIMAL(18,2) NOT NULL,
    OrderedAt DATETIME2(3) NOT NULL,
    CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(CustomerId)
);

CREATE TABLE dbo.Documents (
    DocumentId INT IDENTITY(1,1) PRIMARY KEY,
    CustomerId INT NOT NULL,
    BlobValue VARBINARY(64) NOT NULL,
    Label NVARCHAR(50) NOT NULL,
    CONSTRAINT FK_Documents_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(CustomerId)
);

;WITH n AS (
    SELECT TOP (10) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS rn
    FROM sys.all_objects
)
INSERT INTO dbo.Customers (ExternalId, Name, CreditLimit, IsActive, CreatedAt)
SELECT NEWID(), CONCAT('Customer-', rn), CAST(rn * 100.50 AS DECIMAL(18,2)), CASE WHEN rn % 2 = 0 THEN 1 ELSE 0 END, DATEADD(day, rn, '2024-01-01')
FROM n;

;WITH n AS (
    SELECT TOP (12) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS rn
    FROM sys.all_objects
)
INSERT INTO dbo.Orders (CustomerId, OrderTotal, OrderedAt)
SELECT ((rn - 1) % 10) + 1, CAST(rn * 12.25 AS DECIMAL(18,2)), DATEADD(hour, rn, '2024-02-01')
FROM n;

;WITH n AS (
    SELECT TOP (10) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS rn
    FROM sys.all_objects
)
INSERT INTO dbo.Documents (CustomerId, BlobValue, Label)
SELECT rn, CONVERT(VARBINARY(64), CONCAT('blob-', rn)), CONCAT('Doc-', rn)
FROM n;
