-- A system-versioned temporal table whose period columns are HIDDEN. The exporter reads columns from
-- sys.columns (not SELECT *), so hidden period columns are still captured and must round-trip.
-- After this script: dbo.Flag -> 2 current rows, dbo.FlagHistory -> 1 history row.
CREATE TABLE dbo.Flag (
    FlagId    INT IDENTITY (1, 1) NOT NULL CONSTRAINT PK_Flag PRIMARY KEY CLUSTERED,
    FlagName  NVARCHAR (100) NOT NULL,
    ValidFrom DATETIME2 (7) GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    ValidTo   DATETIME2 (7) GENERATED ALWAYS AS ROW END   HIDDEN NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.FlagHistory));

-- WAITFOR forces a real clock tick so the closed row version gets a non-zero period (ValidFrom < ValidTo);
-- SQL Server discards zero-duration history rows, which on a fast host would empty the history table.
INSERT INTO dbo.Flag (FlagName) VALUES (N'x'), (N'y');
WAITFOR DELAY '00:00:00.050';
UPDATE dbo.Flag SET FlagName = N'x2' WHERE FlagName = N'x';
