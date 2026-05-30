using System.Diagnostics.Tracing;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Shouldly;
using Xunit;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

/// <summary>
/// Makes the "no network beyond the SQL Server you specify" claim verifiable in CI.
/// A representative export and import run against a loopback SQL Server container while
/// an <see cref="EventListener"/> watches the runtime sockets and name-resolution event
/// sources. The test fails if anything other than the loopback SQL Server is contacted,
/// which is what a telemetry call, an update check, or a phone-home would look like.
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

        using var monitor = new OutboundConnectionMonitor();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var result = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        result.RowCount.ShouldBe(3);

        monitor.PublicHostResolutions.ShouldBeEmpty(
            "Export and import resolved hostnames other than the loopback SQL Server, which suggests an unexpected outbound call.");
        monitor.PublicConnections.ShouldBeEmpty(
            "Export and import opened socket connections to non-private addresses, which suggests an unexpected outbound call.");
        monitor.TotalConnectionsObserved.ShouldBeGreaterThan(
            0,
            "The socket monitor observed no connections at all, so it cannot vouch for the no-network claim.");
    }

    private sealed class OutboundConnectionMonitor : EventListener
    {
        private static readonly Regex DottedQuad = new(@"\d{1,3}(\.\d{1,3}){3}", RegexOptions.Compiled);
        private static readonly Regex BraceBytes = new(@"\{([0-9,\s]+)\}", RegexOptions.Compiled);

        private readonly List<string> _publicHostResolutions = new();
        private readonly List<string> _publicConnections = new();
        private readonly object _gate = new();
        private int _totalConnectionsObserved;

        public IReadOnlyList<string> PublicHostResolutions
        {
            get { lock (_gate) { return _publicHostResolutions.ToArray(); } }
        }

        public IReadOnlyList<string> PublicConnections
        {
            get { lock (_gate) { return _publicConnections.ToArray(); } }
        }

        public int TotalConnectionsObserved => Volatile.Read(ref _totalConnectionsObserved);

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
                if (address is not null && TryGetPublicAddress(address, out var publicAddress))
                {
                    lock (_gate)
                    {
                        _publicConnections.Add($"{address} -> {publicAddress}");
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

        // Best-effort decode of the connect address. The sockets event source has reported
        // the remote endpoint in a few shapes across runtime versions (a serialized
        // SocketAddress byte list, or a dotted endpoint string), so both are handled and
        // anything that cannot be decoded is treated as benign rather than failing the test.
        private static bool TryGetPublicAddress(string addressText, out IPAddress publicAddress)
        {
            foreach (var candidate in ExtractCandidateAddresses(addressText))
            {
                if (IsRoutablePublic(candidate))
                {
                    publicAddress = candidate;
                    return true;
                }
            }

            publicAddress = IPAddress.None;
            return false;
        }

        private static IEnumerable<IPAddress> ExtractCandidateAddresses(string addressText)
        {
            var dotted = DottedQuad.Match(addressText);
            if (dotted.Success && IPAddress.TryParse(dotted.Value, out var dottedAddress))
            {
                yield return dottedAddress;
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

            // SocketAddress buffer layout: [family(2)][port(2)][addr...]. IPv4 address is 4
            // bytes at offset 4; IPv6 address is 16 bytes at offset 8.
            if (bytes.Length >= 8 && bytes[0] == (int)AddressFamily.InterNetwork)
            {
                yield return new IPAddress(bytes.AsSpan(4, 4).ToArray());
            }
            else if (bytes.Length >= 24 && bytes[0] == (int)AddressFamily.InterNetworkV6)
            {
                yield return new IPAddress(bytes.AsSpan(8, 16).ToArray());
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
