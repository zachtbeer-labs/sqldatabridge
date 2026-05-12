CREATE TABLE dbo.TenantCustomers (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    TenantId INT NOT NULL,
    Active BIT NOT NULL,
    Name NVARCHAR(50) NOT NULL
);

CREATE TABLE dbo.TenantOrders (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    TenantId INT NOT NULL,
    Amount DECIMAL(10,2) NOT NULL
);

CREATE TABLE dbo.GlobalSettings (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(50) NOT NULL
);

INSERT INTO dbo.TenantCustomers (TenantId, Active, Name)
VALUES
    (123, 1, 'Alice'),
    (123, 0, 'Alex'),
    (456, 1, 'Bob');

INSERT INTO dbo.TenantOrders (TenantId, Amount)
VALUES
    (123, 10.00),
    (123, 20.00),
    (456, 30.00);

INSERT INTO dbo.GlobalSettings (Name)
VALUES ('Theme'), ('Locale');
