CREATE TABLE dbo.IncludeMe (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    KeepCol NVARCHAR(50) NOT NULL,
    SkipCol NVARCHAR(50) NULL
);

CREATE TABLE dbo.SkipMe (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(50) NOT NULL
);

INSERT INTO dbo.IncludeMe (KeepCol, SkipCol)
VALUES ('A', 'alpha'), ('B', 'beta'), ('C', 'gamma');

INSERT INTO dbo.SkipMe (Name)
VALUES ('X'), ('Y');
