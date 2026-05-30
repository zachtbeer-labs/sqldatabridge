using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Shouldly;
using Xunit;
using Zachtbeer.SqlDataBridge.Internal;

namespace Zachtbeer.SqlDataBridge.Tests;

public sealed class DacpacEditorTests : IDisposable
{
    private const string ModelXmlSeed =
        """<DataSchemaModel><Model><Element Type="SqlDatabaseOptions"><Property Name="Containment" Value="1" /></Element></Model></DataSchemaModel>""";

    private const string OriginXmlSeedTemplate =
        """<DacOrigin><Checksums><Checksum Uri="/model.xml">{0}</Checksum></Checksums></DacOrigin>""";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"zsb-editor-{Guid.NewGuid():N}.zip");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Edit_MutatorReturnsFalse_LeavesArchiveUntouched()
    {
        WriteArchive(ModelXmlSeed, string.Format(OriginXmlSeedTemplate, "STALE"));
        var beforeModel = ReadEntryBytes("model.xml");
        var beforeOrigin = ReadEntryBytes("Origin.xml");

        DacpacEditor.Edit(_path, context =>
        {
            context.MutateXml("model.xml", _ => false).ShouldBeFalse();
        });

        ReadEntryBytes("model.xml").ShouldBe(beforeModel);
        ReadEntryBytes("Origin.xml").ShouldBe(beforeOrigin);
    }

    [Fact]
    public void Edit_MutatorReturnsTrue_RewritesEntryAndRecomputesChecksum()
    {
        WriteArchive(ModelXmlSeed, string.Format(OriginXmlSeedTemplate, "STALE"));

        DacpacEditor.Edit(_path, context =>
        {
            context.MutateXml("model.xml", RemoveContainmentProperty).ShouldBeTrue();
        });

        var modelBytes = ReadEntryBytes("model.xml");
        XDocument
            .Load(new MemoryStream(modelBytes))
            .Descendants()
            .Any(e => e.Name.LocalName == "Property" && (string?)e.Attribute("Name") == "Containment")
            .ShouldBeFalse();

        var expectedChecksum = Convert.ToHexString(SHA256.HashData(modelBytes));
        ReadModelChecksumFromOrigin().ShouldBe(expectedChecksum);
    }

    [Fact]
    public void Edit_MultipleMutationsSameEntry_BatchedOnce()
    {
        const string seed =
            """<DataSchemaModel><Model><Element Type="SqlDatabaseOptions"><Property Name="Containment" Value="1" /><Property Name="Collation" Value="X" /></Element></Model></DataSchemaModel>""";
        WriteArchive(seed, string.Format(OriginXmlSeedTemplate, "STALE"));

        DacpacEditor.Edit(_path, context =>
        {
            context.MutateXml("model.xml", RemoveContainmentProperty).ShouldBeTrue();
            context.MutateXml("model.xml", RemoveCollationProperty).ShouldBeTrue();
        });

        var modelBytes = ReadEntryBytes("model.xml");
        var modelDoc = XDocument.Load(new MemoryStream(modelBytes));
        modelDoc.Descendants().Any(e => (string?)e.Attribute("Name") == "Containment").ShouldBeFalse();
        modelDoc.Descendants().Any(e => (string?)e.Attribute("Name") == "Collation").ShouldBeFalse();

        var expectedChecksum = Convert.ToHexString(SHA256.HashData(modelBytes));
        ReadModelChecksumFromOrigin().ShouldBe(expectedChecksum);
    }

    [Fact]
    public void Edit_MissingEntry_MutateReturnsFalse()
    {
        WriteArchive(ModelXmlSeed, string.Format(OriginXmlSeedTemplate, "STALE"));
        var beforeModel = ReadEntryBytes("model.xml");

        DacpacEditor.Edit(_path, context =>
        {
            context.MutateXml("nonexistent.xml", _ => true).ShouldBeFalse();
        });

        ReadEntryBytes("model.xml").ShouldBe(beforeModel);
    }

    [Fact]
    public void Edit_NoOriginXml_StillCompletes()
    {
        WriteArchive(ModelXmlSeed, originXml: null);

        DacpacEditor.Edit(_path, context =>
        {
            context.MutateXml("model.xml", RemoveContainmentProperty).ShouldBeTrue();
        });

        var modelBytes = ReadEntryBytes("model.xml");
        XDocument
            .Load(new MemoryStream(modelBytes))
            .Descendants()
            .Any(e => (string?)e.Attribute("Name") == "Containment")
            .ShouldBeFalse();
    }

    private static bool RemoveContainmentProperty(XDocument document)
    {
        return RemovePropertyByName(document, "Containment");
    }

    private static bool RemoveCollationProperty(XDocument document)
    {
        return RemovePropertyByName(document, "Collation");
    }

    private static bool RemovePropertyByName(XDocument document, string propertyName)
    {
        var matches = document
            .Descendants()
            .Where(e => e.Name.LocalName == "Property" && (string?)e.Attribute("Name") == propertyName)
            .ToList();
        if (matches.Count == 0)
        {
            return false;
        }

        foreach (var match in matches)
        {
            match.Remove();
        }

        return true;
    }

    private void WriteArchive(string modelXml, string? originXml)
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        using var archive = ZipFile.Open(_path, ZipArchiveMode.Create);
        WriteEntry(archive, "model.xml", modelXml);
        if (originXml is not null)
        {
            WriteEntry(archive, "Origin.xml", originXml);
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private byte[] ReadEntryBytes(string name)
    {
        using var archive = ZipFile.OpenRead(_path);
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException($"Entry '{name}' not found.");
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private string ReadModelChecksumFromOrigin()
    {
        using var archive = ZipFile.OpenRead(_path);
        var entry = archive.GetEntry("Origin.xml") ?? throw new InvalidOperationException("Origin.xml not found.");
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document
            .Descendants()
            .First(e => e.Name.LocalName == "Checksum"
                        && string.Equals((string?)e.Attribute("Uri"), "/model.xml", StringComparison.OrdinalIgnoreCase))
            .Value;
    }
}
