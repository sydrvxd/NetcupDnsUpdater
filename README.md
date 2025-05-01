# Netcup DNS Updater

NetcupDnsUpdater is a .NET 8 (C# 12) background service that keeps your Netcup **A‑records** in sync with your host’s WAN IPv4 address. It handles Netcup’s quirks (numeric fields as strings, mandatory flags) and survives transient errors.

## Features

- Synchronizes specified A‑records for a domain zone with the machine’s external IPv4.
- Handles Netcup API quirks: numeric fields as strings, mandatory flags, and session re‑authentication.
- Robust retry logic for transient network, DNS, and API errors.
- Configurable check interval via environment variable.
- Packaged as a Docker container for easy deployment.

## Requirements

- .NET 8 SDK (for local build) or Docker.
- Netcup CCP API credentials (customer ID, API key, password).

## Environment Variables

| Variable               | Description                                                                                         | Default |
|------------------------|-----------------------------------------------------------------------------------------------------|---------|
| `INTERVAL_SECONDS`     | Optional – check cycle interval in seconds.                                                         | `60`    |
| `NETCUP_DOMAIN_NAME`   | Authoritative zone (e.g. `example.com`) and the FQDN to update.                                     | —       |
| `ZONE_HOSTS`           | Hostnames to update (comma‑separated, e.g. `*,@`).                                                  | `@`     |
| `NETCUP_CUSTOMER_ID`   | Netcup customer number.                                                                             | —       |
| `NETCUP_API_KEY`       | Netcup CCP API key.                                                                                 | —       |
| `NETCUP_API_PASSWORD`  | Netcup CCP API password.                                                                            | —       |

## Usage

### Local Build & Run

```bash
dotnet build

dotnet run --project src/NetcupDnsUpdater
```

### Docker

Build the Docker image:

```bash
docker build -t netcup-dns-updater .
```

Run the container (detached, auto‑restart unless stopped):

```bash
docker run -d --restart unless-stopped \
  -e NETCUP_DOMAIN_NAME=example.com \
  -e ZONE_HOSTS="*,@" \
  -e NETCUP_CUSTOMER_ID=123456 \
  -e NETCUP_API_KEY=XXX \
  -e NETCUP_API_PASSWORD=YYY \
  netcup-dns-updater
```

## Implementation Details

- **Program.cs**: Configures the Generic Host, console logging, and registers the HTTP client, `NetcupApiClient`, and `DnsUpdaterService`.
- **NetcupApiClient.cs**:
  - Authenticates with Netcup CCP (login and session management).
  - Fetches DNS record details (`infoDnsRecords`) and updates A‑records only when the current destination differs.
  - Handles API quirks (string‑encoded numbers, mandatory flags) and retries on session expiration.
- **DnsUpdaterService.cs**:
  - Retrieves WAN IP from `https://api.ipify.org`
  - Resolves current DNS A‑record via `Dns.GetHostAddressesAsync`
  - Triggers `UpdateARecordsAsync` when a mismatch is detected.

## License

This project is released under the MIT License. Feel free to use, modify, and distribute.

