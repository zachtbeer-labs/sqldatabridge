-- A system-versioned temporal table with a foreign key to a non-temporal parent, to verify the suspend
-- ceremony coexists with FK-based import ordering (parent loads before the temporal child). After this script:
--   dbo.Office -> 2 rows (non-temporal)
--   dbo.Worker -> 2 current rows, dbo.WorkerHistory -> 1 history row
CREATE TABLE dbo.Office (
    OfficeId   INT IDENTITY (1, 1) NOT NULL CONSTRAINT PK_Office PRIMARY KEY CLUSTERED,
    OfficeName NVARCHAR (100) NOT NULL
);

CREATE TABLE dbo.Worker (
    WorkerId   INT IDENTITY (1, 1) NOT NULL CONSTRAINT PK_Worker PRIMARY KEY CLUSTERED,
    OfficeId   INT NOT NULL CONSTRAINT FK_Worker_Office FOREIGN KEY REFERENCES dbo.Office (OfficeId),
    WorkerName NVARCHAR (100) NOT NULL,
    ValidFrom  DATETIME2 (7) GENERATED ALWAYS AS ROW START NOT NULL,
    ValidTo    DATETIME2 (7) GENERATED ALWAYS AS ROW END   NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.WorkerHistory));

INSERT INTO dbo.Office (OfficeName) VALUES (N'HQ'), (N'Branch');
INSERT INTO dbo.Worker (OfficeId, WorkerName) VALUES (1, N'Ann'), (2, N'Bob');
UPDATE dbo.Worker SET WorkerName = N'Ann-2' WHERE OfficeId = 1;
