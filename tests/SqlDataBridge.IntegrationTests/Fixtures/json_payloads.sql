CREATE TABLE dbo.JsonPayloads (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    PayloadName NVARCHAR(40) NOT NULL,
    PayloadJson JSON NULL
);

INSERT INTO dbo.JsonPayloads (PayloadName, PayloadJson)
VALUES
(
    N'object-array',
    N'{"id":1,"name":"alpha","tags":["one","two"]}'
),
(
    N'nested-object',
    N'{"id":2,"profile":{"active":true,"score":12.5}}'
),
(
    N'null-payload',
    NULL
);
