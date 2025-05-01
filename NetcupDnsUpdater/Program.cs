// =====================================================================================
// NetcupDnsUpdater – C# 12 / .NET 8 (nullable enabled)
// =====================================================================================
// Keeps the chosen Netcup **A‑records** in sync with the host’s WAN IPv4.  Handles
// Netcup’s quirks (numeric fields as strings, mandatory flags) and survives
// transient errors.
//
// ╭─────────────────────────── ENVIRONMENT VARIABLES ───────────────────────────-╮
// │  INTERVAL_SECONDS      optional – check cycle in seconds (default 60)        │
// │  NETCUP_DOMAIN_NAME    authoritative zone (e.g. "example.com") *and*         │
// │                        the FQDN that must resolve to the current WAN IP      │
// │  ZONE_HOSTS            hostnames to update (","‑separated, e.g. "*,@")       │
// │  NETCUP_CUSTOMER_ID    customer number                                       │
// │  NETCUP_API_KEY        API key (CCP)                                         │
// │  NETCUP_API_PASSWORD   API password (CCP)                                    │
// ╰──────────────────────────────────────────────────────────────────────────────╯
//
// Build & run:
//   docker build -t netcup-dns-updater .
//   docker run -d --restart unless-stopped \
//        -e NETCUP_DOMAIN_NAME=example.com -e ZONE_HOSTS="*,@" \
//        -e NETCUP_CUSTOMER_ID=123456 -e NETCUP_API_KEY=XXX -e NETCUP_API_PASSWORD=YYY \
//        netcup-dns-updater
// =====================================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetcupDnsUpdater.Models;
using NetcupDnsUpdater.Services;

Host.CreateDefaultBuilder(args)
    .ConfigureLogging(b => b.ClearProviders().AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    }))
    .ConfigureServices((ctx, services) =>
    {
        services.AddHttpClient();
        services.AddSingleton<NetcupApiClient>();
        services.AddHostedService<DnsUpdaterService>();
    })
    .Build()
    .Run();