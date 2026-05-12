CREATE TABLE dbo.FirstCycle (
    FirstCycleId INT NOT NULL PRIMARY KEY,
    SecondCycleId INT NOT NULL
);

CREATE TABLE dbo.SecondCycle (
    SecondCycleId INT NOT NULL PRIMARY KEY,
    FirstCycleId INT NOT NULL,
    CONSTRAINT FK_SecondCycle_FirstCycle FOREIGN KEY (FirstCycleId) REFERENCES dbo.FirstCycle(FirstCycleId)
);

ALTER TABLE dbo.FirstCycle
ADD CONSTRAINT FK_FirstCycle_SecondCycle FOREIGN KEY (SecondCycleId) REFERENCES dbo.SecondCycle(SecondCycleId);
