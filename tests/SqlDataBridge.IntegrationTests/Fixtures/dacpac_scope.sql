EXEC('CREATE SCHEMA tenant');

CREATE TABLE dbo.SelectedParent (
    ParentId INT IDENTITY(1,1) NOT NULL,
    ParentCode NVARCHAR(20) NOT NULL,
    ParentName NVARCHAR(50) NOT NULL,
    CONSTRAINT PK_SelectedParent PRIMARY KEY CLUSTERED (ParentId),
    CONSTRAINT UQ_SelectedParent_Code UNIQUE (ParentCode),
    CONSTRAINT CK_SelectedParent_Code CHECK (LEN(ParentCode) > 0)
);

CREATE INDEX IX_SelectedParent_Name ON dbo.SelectedParent(ParentName);

CREATE TABLE dbo.SelectedChild (
    ChildId INT IDENTITY(1,1) NOT NULL,
    ParentId INT NOT NULL,
    Qty INT NOT NULL CONSTRAINT DF_SelectedChild_Qty DEFAULT 1,
    UnitPrice DECIMAL(18,2) NOT NULL,
    ExtendedPrice AS (Qty * UnitPrice),
    CONSTRAINT PK_SelectedChild PRIMARY KEY CLUSTERED (ChildId),
    CONSTRAINT CK_SelectedChild_Qty CHECK (Qty > 0),
    CONSTRAINT FK_SelectedChild_SelectedParent FOREIGN KEY (ParentId) REFERENCES dbo.SelectedParent(ParentId)
);

CREATE INDEX IX_SelectedChild_ParentId ON dbo.SelectedChild(ParentId);

CREATE TABLE dbo.UnselectedTable (
    UnselectedId INT IDENTITY(1,1) NOT NULL,
    ValueText NVARCHAR(50) NOT NULL,
    CONSTRAINT PK_UnselectedTable PRIMARY KEY CLUSTERED (UnselectedId)
);

CREATE TABLE dbo.CrossScopeChild (
    CrossScopeChildId INT IDENTITY(1,1) NOT NULL,
    UnselectedId INT NOT NULL,
    ChildName NVARCHAR(50) NOT NULL,
    CONSTRAINT PK_CrossScopeChild PRIMARY KEY CLUSTERED (CrossScopeChildId),
    CONSTRAINT FK_CrossScopeChild_UnselectedTable FOREIGN KEY (UnselectedId) REFERENCES dbo.UnselectedTable(UnselectedId)
);

CREATE TABLE tenant.SelectedThing (
    ThingId INT IDENTITY(1,1) NOT NULL,
    ThingName NVARCHAR(50) NOT NULL,
    CONSTRAINT PK_SelectedThing PRIMARY KEY CLUSTERED (ThingId),
    CONSTRAINT UQ_SelectedThing_Name UNIQUE (ThingName)
);

INSERT INTO dbo.SelectedParent (ParentCode, ParentName)
VALUES (N'P1', N'Parent One'), (N'P2', N'Parent Two');

INSERT INTO dbo.SelectedChild (ParentId, Qty, UnitPrice)
VALUES (1, 2, 12.50), (1, 3, 4.00), (2, 1, 99.99);

INSERT INTO dbo.UnselectedTable (ValueText)
VALUES (N'outside one'), (N'outside two');

INSERT INTO dbo.CrossScopeChild (UnselectedId, ChildName)
VALUES (1, N'cross one'), (2, N'cross two');

INSERT INTO tenant.SelectedThing (ThingName)
VALUES (N'Thing One'), (N'Thing Two');
