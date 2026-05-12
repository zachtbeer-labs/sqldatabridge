CREATE TABLE dbo.Parent (
    ParentId INT IDENTITY(1,1) PRIMARY KEY,
    ParentName NVARCHAR(50) NOT NULL
);

CREATE TABLE dbo.Child (
    ChildId INT IDENTITY(1,1) PRIMARY KEY,
    ParentId INT NOT NULL,
    ChildName NVARCHAR(50) NOT NULL,
    CONSTRAINT FK_Child_Parent FOREIGN KEY (ParentId) REFERENCES dbo.Parent(ParentId)
);

INSERT INTO dbo.Parent (ParentName)
VALUES ('P1'), ('P2'), ('P3');

INSERT INTO dbo.Child (ParentId, ChildName)
VALUES
(1, 'C1'),
(1, 'C2'),
(2, 'C3'),
(3, 'C4');
