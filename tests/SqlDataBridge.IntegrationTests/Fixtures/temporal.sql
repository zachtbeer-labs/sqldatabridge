-- System-versioned temporal table with an explicitly named history table. INSERT then UPDATE/DELETE
-- so the history table ends up with closed row versions to round-trip. This is the canonical textbook
-- temporal example from the Microsoft docs (Department / DepartmentHistory) and is not based on any
-- real database. After this script:
--   dbo.Department         -> 2 current rows (Sales, Engineering)
--   dbo.DepartmentHistory  -> 3 history rows (Sales original, Engineering original, Support original)
CREATE TABLE dbo.Department (
    DepartmentId   INT IDENTITY (1, 1) NOT NULL CONSTRAINT PK_Department PRIMARY KEY CLUSTERED,
    DepartmentName NVARCHAR (100) NOT NULL,
    ManagerId      INT NULL,
    ValidFrom      DATETIME2 (7) GENERATED ALWAYS AS ROW START NOT NULL,
    ValidTo        DATETIME2 (7) GENERATED ALWAYS AS ROW END   NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.DepartmentHistory));

INSERT INTO dbo.Department (DepartmentName, ManagerId) VALUES (N'Sales', 10), (N'Engineering', 20), (N'Support', 30);
UPDATE dbo.Department SET ManagerId = 11 WHERE DepartmentName = N'Sales';
UPDATE dbo.Department SET ManagerId = 21 WHERE DepartmentName = N'Engineering';
DELETE FROM dbo.Department WHERE DepartmentName = N'Support';
