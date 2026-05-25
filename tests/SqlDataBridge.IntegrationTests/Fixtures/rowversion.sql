CREATE TABLE dbo.RvAudit (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(50) NOT NULL,
    Rv ROWVERSION
);

INSERT INTO dbo.RvAudit (Name) VALUES (N'alpha'), (N'beta'), (N'gamma');
