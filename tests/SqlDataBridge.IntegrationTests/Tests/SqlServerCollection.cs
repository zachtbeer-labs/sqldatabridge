using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Xunit;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

[CollectionDefinition(nameof(SqlServerCollection))]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture>
{
}
