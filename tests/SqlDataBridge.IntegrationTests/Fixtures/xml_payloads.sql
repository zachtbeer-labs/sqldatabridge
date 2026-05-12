CREATE TABLE dbo.XmlPayloads (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    PayloadName NVARCHAR(40) NOT NULL,
    PayloadXml XML NULL
);

INSERT INTO dbo.XmlPayloads (PayloadName, PayloadXml)
VALUES
(
    N'element-attribute',
    CAST(N'<root><item id="1">alpha</item></root>' AS XML)
),
(
    N'namespaced',
    CAST(N'<ns:root xmlns:ns="urn:test"><ns:item name="beta">value</ns:item></ns:root>' AS XML)
),
(
    N'mixed-content',
    CAST(N'<root>leading <b>bold</b> trailing</root>' AS XML)
),
(
    N'null-payload',
    NULL
);
