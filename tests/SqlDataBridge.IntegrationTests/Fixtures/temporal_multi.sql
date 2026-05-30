-- Two independent system-versioned temporal pairs in one database, to verify the importer suspends and
-- restores every affected table. After this script:
--   dbo.Region -> 2 current rows, dbo.RegionHistory -> 1 history row
--   dbo.Team   -> 1 current row,  dbo.TeamHistory   -> 1 history row
CREATE TABLE dbo.Region (
    RegionId   INT IDENTITY (1, 1) NOT NULL CONSTRAINT PK_Region PRIMARY KEY CLUSTERED,
    RegionName NVARCHAR (100) NOT NULL,
    ValidFrom  DATETIME2 (7) GENERATED ALWAYS AS ROW START NOT NULL,
    ValidTo    DATETIME2 (7) GENERATED ALWAYS AS ROW END   NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.RegionHistory));

CREATE TABLE dbo.Team (
    TeamId    INT IDENTITY (1, 1) NOT NULL CONSTRAINT PK_Team PRIMARY KEY CLUSTERED,
    TeamName  NVARCHAR (100) NOT NULL,
    ValidFrom DATETIME2 (7) GENERATED ALWAYS AS ROW START NOT NULL,
    ValidTo   DATETIME2 (7) GENERATED ALWAYS AS ROW END   NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.TeamHistory));

INSERT INTO dbo.Region (RegionName) VALUES (N'North'), (N'South');
UPDATE dbo.Region SET RegionName = N'North-2' WHERE RegionName = N'North';
INSERT INTO dbo.Team (TeamName) VALUES (N'Alpha');
UPDATE dbo.Team SET TeamName = N'Alpha-2' WHERE TeamName = N'Alpha';
