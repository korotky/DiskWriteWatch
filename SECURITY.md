# Security Policy

DiskWriteWatch collects local disk-write activity and may store sensitive local metadata such as full paths, process names, disk models, and disk serial numbers.

## Supported versions

The project is currently in an early public release phase. Security fixes are expected to target the latest released version.

## Reporting a vulnerability

Please do not publish a vulnerability report as a public issue before maintainers have had a chance to review it. Use GitHub private vulnerability reporting if it is enabled for the repository; otherwise open a minimal public issue that does not include exploit details and ask for a private contact path.

## Operational security notes

- The dashboard has no authentication and is intended for `127.0.0.1` / `localhost` only.
- Do not expose the dashboard through a reverse proxy, LAN bind, VPN, or public tunnel.
- Treat screenshots, CSV exports, and the SQLite database as potentially sensitive.
- If you share diagnostics publicly, consider setting `Privacy.StoreFullPaths=false` and still review the output manually.
- DiskWriteWatch requires elevated ETW access. Install and run only binaries you built yourself or obtained from a trusted release.

