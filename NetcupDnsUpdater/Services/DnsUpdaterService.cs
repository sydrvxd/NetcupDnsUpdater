using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetcupDnsUpdater.Models;
using System.Net;
using System.Net.Sockets;

namespace NetcupDnsUpdater.Services;

public sealed class DnsUpdaterService(ILogger<DnsUpdaterService> logger,
                                       IHttpClientFactory httpFactory,
                                       NetcupApiClient api) : BackgroundService
{
    private readonly ILogger<DnsUpdaterService> _log = logger;
    private readonly IHttpClientFactory _http = httpFactory;
    private readonly NetcupApiClient _api = api;

    private readonly string _domain = Env("NETCUP_DOMAIN_NAME");
    private readonly string[] _hosts = (Environment.GetEnvironmentVariable("ZONE_HOSTS") ?? "@")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private readonly TimeSpan _interval = TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable("INTERVAL_SECONDS"), out var s) && s > 0 ? s : 60);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("Netcup DNS updater started; interval {s}s", _interval.TotalSeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var wan = await GetWanIpAsync(ct);
                var dns = await ResolveAsync(_domain, ct);

                if (wan is null)
                    _log.LogWarning("WAN IP unknown – skipping");
                else if (!string.Equals(wan, dns, StringComparison.Ordinal))
                {
                    _log.LogInformation("Mismatch WAN {wan} / DNS {dns} – updating", wan, dns ?? "<none>");
                    await _api.EnsureLoggedInAsync(ct);
                    await _api.UpdateARecordsAsync(_domain, _hosts, wan, ct);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogError(ex, "Cycle failed – retry in {s}s", _interval.TotalSeconds);
            }

            await Task.Delay(_interval, ct);
        }
    }

    private static string Env(string key) => Environment.GetEnvironmentVariable(key)
        ?? throw new InvalidOperationException($"{key} is required");

    private async Task<string?> GetWanIpAsync(CancellationToken ct)
    {
        try
        {
            using var http = _http.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            return await http.GetStringAsync("https://api.ipify.org", ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "WAN lookup failed");
            return null;
        }
    }

    private static async Task<string?> ResolveAsync(string domain, CancellationToken ct)
    {
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(domain, ct);
            return addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
        }
        catch (SocketException)
        {
            return null;
        }
    }
}