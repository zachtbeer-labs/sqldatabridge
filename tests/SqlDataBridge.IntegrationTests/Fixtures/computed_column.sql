CREATE TABLE dbo.InvoiceLines (
    InvoiceLineId INT IDENTITY(1,1) PRIMARY KEY,
    Qty INT NOT NULL,
    UnitPrice DECIMAL(18,2) NOT NULL,
    ExtendedPrice AS (Qty * UnitPrice)
);

INSERT INTO dbo.InvoiceLines (Qty, UnitPrice)
VALUES (2, 10.00), (5, 12.50), (3, 2.75);
