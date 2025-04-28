// ===========================
// NetcupDnsUpdater – C# 12 / .NET 8
// ===========================
// A minimal Docker‑friendly console service that keeps the selected netcup DNS A‑records
// (e.g. "*" and "@") in sync with the machine's current WAN IPv4 address.
//
// ────────────────────────────────────────────────────────────────────────────────
// ENVIRONMENT VARIABLES (all caps) ──────────────────────────────────────────────
// - INTERVAL_SECONDS      optional, default "60" (check cycle)
// - TARGET_DOMAIN         fqdn that should resolve to the current WAN IP (used for the ping)
// - NETCUP_CUSTOMER_ID    numeric customer number
// - NETCUP_API_KEY        API key generated in CCP
// - NETCUP_API_PASSWORD   API password generated in CCP
// - ZONE_DOMAIN           authoritative zone (e.g. "google.com")
// - ZONE_HOSTS            comma separated host names that shall be updated (e.g. "*,@")
// - RECORD_TTL            optional, default "300" (seconds)
//
// Build & run:
//   docker build -t netcup-dns-updater .
//   docker run -e TARGET_DOMAIN=example.com -e NETCUP_CUSTOMER_ID=123456 -e NETCUP_API_KEY=XXX \
//              -e NETCUP_API_PASSWORD=XXX -e ZONE_DOMAIN=example.com -e ZONE_HOSTS="*,@" \
//              --restart unless-stopped netcup-dns-updater
//
// ────────────────────────────────────────────────────────────────────────────────

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