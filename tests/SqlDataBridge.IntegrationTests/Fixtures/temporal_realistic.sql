-- A system-versioned temporal table with non-default period column names and a finite retention period,
-- mirroring the shape of real-world temporal tables (period columns LastUpdateDate/LastUpdateValidTo,
-- HISTORY_RETENTION_PERIOD set, underscore-suffixed history table). Not based on any real database.
-- After this script: dbo.Subscription -> 2 current rows, dbo.Subscription_History -> 1 history row.
CREATE TABLE dbo.Subscription (
    SubscriptionId    INT IDENTITY (1, 1) NOT NULL CONSTRAINT PK_Subscription PRIMARY KEY CLUSTERED,
    CustomerId        INT NOT NULL,
    PlanName          NVARCHAR (100) NOT NULL,
    LastUpdateDate    DATETIME2 (7) GENERATED ALWAYS AS ROW START NOT NULL,
    LastUpdateValidTo DATETIME2 (7) GENERATED ALWAYS AS ROW END   NOT NULL,
    PERIOD FOR SYSTEM_TIME (LastUpdateDate, LastUpdateValidTo)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.Subscription_History, HISTORY_RETENTION_PERIOD = 3 MONTHS));

-- WAITFOR forces a real clock tick so the closed row version gets a non-zero period (ValidFrom < ValidTo);
-- SQL Server discards zero-duration history rows, which on a fast host would empty the history table.
INSERT INTO dbo.Subscription (CustomerId, PlanName) VALUES (1, N'Basic'), (2, N'Pro');
WAITFOR DELAY '00:00:00.050';
UPDATE dbo.Subscription SET PlanName = N'Premium' WHERE CustomerId = 1;
