using System.Diagnostics.Tracing;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Shouldly;
using Xunit;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

/// <summary>
/// A best-effort, in-process regression check on the "no network beyond the SQL Server you
/// specify" claim. A representative export and import run against a loopback SQL Server
/// container while an <see cref="EventListener"/> watches the runtime sockets and
/// name-resolution event sources. The test asserts the expected loopback SQL endpoint is
/// actually contacted (so the listener is demonstrably not blind) and that nothing resolves
/// or connects to a non-private address, which is what a telemetry call, an update check, or
/// a phone-home would look like.
///
/// This is not a proof of network isolation: anything that reaches the network below the
/// managed Sockets layer (P/Invoke, a native dependency), or a remote address the listener
/// cannot decode, is invisible here. Proving the negative outright would require network-layer
/// isolation (a restricted container network or an egress firewall), which this test does not do.
/// The assembly forces Microsoft.Data.SqlClient onto managed networking (see the .csproj) so the
/// SQL connects actually surface through the Sockets event source on Windows as well as Linux.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class NetworkIsolationTests
{
    private readonly SqlServerContainerFixture _fixture;

    public NetworkIsolationTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RoundTrip_ContactsOnlyTheLoopbackSqlServer()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("computed_column.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.ComputedInvoiceLines());
        await using var sqlite = new SqliteTempFileHarness();

        // Source and target live in the same container, so they share one host:port endpoint.
        var expectedSqlPort = ParseSqlServerPort(source.ConnectionString);

        using var monitor = new OutboundConnectionMonitor(expectedSqlPort);

        // The fixture setup above already opened (and pooled) connections to these exact
        // connection strings, so without clearing the pool the export/import would reuse the
        // pooled sockets and the monitor would observe no ConnectStart at all. Clearing forces
        // real physical connects inside the monitored window.
        SqlConnection.ClearAllPools();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var result = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        result.RowCount.ShouldBe(3);

        monitor.PublicHostResolutions.ShouldBeEmpty(
            "Export and import resolved hostnames other than the loopback SQL Server, which suggests an unexpected outbound call.");
        monitor.PublicConnections.ShouldBeEmpty(
            "Export and import opened socket connections to non-private addresses, which suggests an unexpected outbound call.");
        monitor.ConnectionsToExpectedSqlEndpoint.ShouldBeGreaterThan(
            0,
            "The monitor observed no connection to the loopback SQL endpoint, so it cannot vouch for the no-network claim.");
    }

    // Testcontainers yields a DataSource like "127.0.0.1,49123"; the port is the segment after
    // the comma. Falls back to 1433 if the connection string omits an explicit port.
    private static int ParseSqlServerPort(string connectionString)
    {
        var dataSource = new SqlConnectionStringBuilder(connectionString).DataSource;
        var separator = dataSource.LastIndexOf(',');
        return separator >= 0 && int.TryParse(dataSource[(separator + 1)..], out var port) ? port : 1433;
    }

    private sealed class OutboundConnectionMonitor : EventListener
    {
        private static readonly Regex BraceBytes = new(@"\{([0-9,\s]+)\}", RegexOptions.Compiled);

        private readonly List<string> _publicHostResolutions = new();
        private readonly List<string> _publicConnections = new();
        private readonly object _gate = new();
        private readonly int _expectedSqlPort;
        private int _totalConnectionsObserved;
        private int _connectionsToExpectedSqlEndpoint;

        public OutboundConnectionMonitor(int expectedSqlPort)
        {
            _expectedSqlPort = expectedSqlPort;
        }

        public IReadOnlyList<string> PublicHostResolutions
        {
            get { lock (_gate) { return _publicHostResolutions.ToArray(); } }
        }

        public IReadOnlyList<string> PublicConnections
        {
            get { lock (_gate) { return _publicConnections.ToArray(); } }
        }

        public int TotalConnectionsObserved => Volatile.Read(ref _totalConnectionsObserved);

        // Positive-attribution guard: a connect that decoded to the loopback SQL endpoint
        // (host + the container's mapped port). Proves the listener was not blind, without
        // being satisfiable by unrelated process traffic on some other port.
        public int ConnectionsToExpectedSqlEndpoint => Volatile.Read(ref _connectionsToExpectedSqlEndpoint);

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name is "System.Net.Sockets" or "System.Net.NameResolution")
            {
                EnableEvents(eventSource, EventLevel.Informational);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            // OnEventSourceCreated runs inside the base constructor, before these fields are
            // initialized, so an event arriving on another thread could land here early.
            if (_gate is null)
            {
                return;
            }

            if (eventData.EventName == "ResolutionStart")
            {
                var host = FirstStringPayload(eventData);
                if (host is not null && !IsLocalHost(host))
                {
                    lock (_gate)
                    {
                        _publicHostResolutions.Add(host);
                    }
                }

                return;
            }

            if (eventData.EventName == "ConnectStart")
            {
                Interlocked.Increment(ref _totalConnectionsObserved);

                var address = FirstStringPayload(eventData);
                if (address is null)
                {
                    return;
                }

                foreach (var endpoint in ExtractCandidateEndpoints(address))
                {
                    if (IsRoutablePublic(endpoint.Address))
                    {
                        lock (_gate)
                        {
                            _publicConnections.Add($"{address} -> {endpoint.Address}");
                        }
                    }
                    else if (IPAddress.IsLoopback(endpoint.Address) && endpoint.Port == _expectedSqlPort)
                    {
                        Interlocked.Increment(ref _connectionsToExpectedSqlEndpoint);
                    }
                }
            }
        }

        private static string? FirstStringPayload(EventWrittenEventArgs eventData)
        {
            return eventData.Payload is { Count: > 0 } ? eventData.Payload[0]?.ToString() : null;
        }

        private static bool IsLocalHost(string host)
        {
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IPAddress.TryParse(host, out var ip) && !IsRoutablePublic(ip);
        }

        // Best-effort decode of the connect endpoint. The sockets event source reports the
        // remote endpoint as SocketAddress.ToString(), e.g. "InterNetwork:16:{0,80,93,184,...}".
        // A clean "ip:port" form is also accepted in case a runtime ever emits one. Anything
        // that cannot be decoded is treated as benign rather than failing the test. Port is -1
        // when it cannot be recovered.
        private static IEnumerable<(IPAddress Address, int Port)> ExtractCandidateEndpoints(string addressText)
        {
            // Endpoint-string form, e.g. "127.0.0.1:49123" or "[::1]:49123".
            if (IPEndPoint.TryParse(addressText, out var endpoint) && endpoint.Port > 0)
            {
                yield return (endpoint.Address, endpoint.Port);
            }

            var braces = BraceBytes.Match(addressText);
            if (!braces.Success)
            {
                yield break;
            }

            var bytes = braces.Groups[1].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => byte.TryParse(value, out var parsed) ? (byte?)parsed : null)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToArray();

            if (bytes.Length < 2)
            {
                yield break;
            }

            // SocketAddress.ToString() names the address family in the text prefix and lists
            // the SOCKADDR buffer from offset 2 in the braces (the 2-byte family is omitted),
            // so the brace bytes begin at the port: [port(2, network order)][addr...].
            var port = (bytes[0] << 8) | bytes[1];

            if (addressText.StartsWith("InterNetworkV6", StringComparison.OrdinalIgnoreCase))
            {
                // SOCKADDR_IN6 from offset 2: [port(2)][flowinfo(4)][addr(16)][scopeid(4)].
                if (bytes.Length >= 22)
                {
                    yield return (new IPAddress(bytes.AsSpan(6, 16).ToArray()), port);
                }
            }
            else
            {
                // SOCKADDR_IN from offset 2: [port(2)][addr(4)].
                if (bytes.Length >= 6)
                {
                    yield return (new IPAddress(bytes.AsSpan(2, 4).ToArray()), port);
                }
            }
        }

        private static bool IsRoutablePublic(IPAddress ip)
        {
            if (IPAddress.IsLoopback(ip))
            {
                return false;
            }

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var octets = ip.GetAddressBytes();
                if (octets[0] == 10)
                {
                    return false;
                }

                if (octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31)
                {
                    return false;
                }

                if (octets[0] == 192 && octets[1] == 168)
                {
                    return false;
                }

                if (octets[0] == 169 && octets[1] == 254)
                {
                    return false;
                }

                if (octets[0] == 127)
                {
                    return false;
                }

                return true;
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal)
                {
                    return false;
                }

                if (ip.IsIPv4MappedToIPv6)
                {
                    return IsRoutablePublic(ip.MapToIPv4());
                }

                return true;
            }

            return false;
        }
    }
}
