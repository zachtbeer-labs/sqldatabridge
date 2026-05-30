# Security Policy

## Maintainers

SqlDataBridge is maintained by Zachtbeer Labs B.V., the owner of the `Zachtbeer.SqlDataBridge` NuGet package.

## Reporting a Vulnerability

If you discover a security vulnerability, please report it responsibly:

1. **Do not open a public issue.**
2. Preferred: use [GitHub's private security advisory](https://github.com/zachtbeer-labs/sqldatabridge/security/advisories/new) to report the vulnerability.
3. Backup: email the maintainer security contact (TODO: `security@zachtbeer.<domain>`, to be confirmed).

## What to Expect

- Acknowledgment within 48 hours.
- A fix or mitigation plan within a reasonable timeframe depending on severity.
- Credit in the release notes unless you prefer to remain anonymous.

## Scope

Security fixes are considered for the latest stable major version.

This library connects to SQL Server databases using credentials you provide, writes local package files, and can optionally capture or deploy schema packages. It does not intentionally store, transmit, or log connection strings or credentials.
