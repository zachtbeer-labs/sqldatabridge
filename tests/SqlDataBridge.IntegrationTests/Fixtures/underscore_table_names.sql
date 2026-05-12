CREATE TABLE dbo.__AccountsBackup (
    AccountId INT IDENTITY(1,1) PRIMARY KEY,
    AccountName NVARCHAR(100) NOT NULL
);

INSERT INTO dbo.__AccountsBackup (AccountName)
VALUES (N'Primary'), (N'Archive');
