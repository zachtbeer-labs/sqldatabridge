-- A plain, non-temporal source table. Paired with TargetSchemaScripts.LedgerTemporal() so the import target
-- is system-versioned but the package carries no period columns. The importer must leave versioning on and
-- let SQL Server auto-populate the period (no suspend, no extra-column validation error).
CREATE TABLE dbo.Ledger (
    LedgerId INT IDENTITY (1, 1) NOT NULL CONSTRAINT PK_Ledger PRIMARY KEY CLUSTERED,
    Note     NVARCHAR (100) NOT NULL
);

INSERT INTO dbo.Ledger (Note) VALUES (N'one'), (N'two'), (N'three');
