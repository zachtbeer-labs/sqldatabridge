using Zachtbeer.SqlDataBridge.Models;

namespace Zachtbeer.SqlDataBridge.Internal;

internal sealed record SchemaPackage(
    string PackageType,
    string PackageName,
    string PackageSha256,
    DateTimeOffset CreatedAtUtc,
    string? SourceDatabaseName,
    string? DacFxVersion,
    DacpacSchemaScope SchemaScope,
    byte[] Payload);
