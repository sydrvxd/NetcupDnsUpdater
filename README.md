# Netcup DNS Updater

> **Keep your dynamic WAN IP in sync with your Netcup DNS A‑records – automatically.**

`NetcupDnsUpdater` is a lightweight .NET 8 background service packaged for Docker. It periodically checks the public (WAN) IPv4 address of the host and updates the selected **A‑records** of your Netcup DNS zone when a change is detected.

---

## ✨ Features

| Feature | Details |
|---------|---------|
| **Auto‑detect WAN IP** | Uses `https://api.ipify.org` (or your own endpoint) to fetch the current public IPv4. |
| **Selective record update** | Point multiple hosts (e.g. `*`, `@`, `home`, …) to the new IP in one API call. |
| **Config via environment** | Perfect for container platforms &amp; GitOps. |
| **Pluggable interval** | Default 60 s, override with `INTERVAL_SECONDS`. |
| **Minimal footprint** | Published as a self‑contained Alpine image (&lt; 40 MB). |
| **Typed, nullable‑enabled C# 12** | Built with the latest .NET 8 LTS. |

---

## 🚀 Quick Start (Docker)

```bash
# build once (or pull from your image registry)
docker build -t netcup-dns-updater .

# run – replace placeholders with your data
docker run -d --name dns-updater \
  -e TARGET_DOMAIN=your.domain.tld \
  -e NETCUP_CUSTOMER_ID=123456 \
  -e NETCUP_API_KEY=XXXXXXXXXXXXXXXX \
  -e NETCUP_API_PASSWORD=XXXXXXXXXXXXXXXX \
  -e ZONE_DOMAIN=your.domain.tld \
  -e ZONE_HOSTS="*,@" \
  --restart unless-stopped netcup-dns-updater
```

Or with **docker‑compose**:

```yaml
services:
  dns-updater:
    image: netcup-dns-updater:latest
    restart: unless-stopped
    environment:
      TARGET_DOMAIN: "your.domain.tld"
      NETCUP_CUSTOMER_ID: "123456"
      NETCUP_API_KEY: "${NETCUP_API_KEY}"
      NETCUP_API_PASSWORD: "${NETCUP_API_PASSWORD}"
      ZONE_DOMAIN: "your.domain.tld"
      ZONE_HOSTS: "*,@"
      INTERVAL_SECONDS: "60"            # optional
      RECORD_TTL: "300"                 # optional
```

> **Tip:** Store secrets in an `.env` file or your orchestrator’s secret store (Docker Secrets, Kubernetes Secrets, etc.).

---

## ⚙️ Configuration

| Variable             | Required | Default | Description |
|----------------------|----------|---------|-------------|
| `TARGET_DOMAIN`      | ✅       | –       | FQDN that should resolve to the current WAN IP (used to decide whether an update is needed). |
| `INTERVAL_SECONDS`   | ❌       | `60`    | How often to check (seconds). |
| `NETCUP_CUSTOMER_ID` | ✅       | –       | Your Netcup customer number. |
| `NETCUP_API_KEY`     | ✅       | –       | API key generated in the Netcup CCP. |
| `NETCUP_API_PASSWORD`| ✅       | –       | API password generated in the Netcup CCP. |
| `ZONE_DOMAIN`        | ✅       | –       | Authoritative DNS zone (e.g. `example.com`). |
| `ZONE_HOSTS`         | ✅       | –       | Comma‑separated list of hostnames to update ("*", "@", "home" …). |
| `RECORD_TTL`         | ❌       | `300`   | TTL for the updated records (seconds). |

---

## 🛠  Development

```bash
# run locally (requires .NET 8 SDK)
dotnet run --project NetcupDnsUpdater

# set env vars for your session, e.g.
export TARGET_DOMAIN=example.com
# … (other exports)
```

### Project Layout

```
NetcupDnsUpdater/
├── NetcupDnsUpdater.csproj    – SDK project file
├── Dockerfile                 – multi‑stage build/publish image
├── Program.cs                 – host builder & DI wiring
├── Models/
│   └── NetcupApiClient.cs     – thin wrapper around Netcup JSON API
└── Services/
    └── DnsUpdaterService.cs   – background loop & DNS logic
```

---

## 🔐 Security Notes

* The Netcup API session is cached for 15 minutes and automatically renewed.
* Avoid storing credentials in plain‐text. Use a secret manager or inject them at runtime.
* Logs may contain IP addresses; adjust log level as needed (`Logging:LogLevel:Default`).

---

## 📋 Roadmap

* IPv6 support (AAAA records)
* Pluggable WAN IP provider endpoint
* Health‑check endpoint (`/healthz`) for orchestration
* Alpine Distroless image

Feel free to open issues & PRs! 🙌

---

## 📝 License

This project is released under the **MIT License**. See [`LICENSE`](LICENSE) for details.

