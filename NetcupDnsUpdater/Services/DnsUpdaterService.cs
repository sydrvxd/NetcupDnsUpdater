using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetcupDnsUpdater.Models;

namespace NetcupDnsUpdater.Services;

public sealed class DnsUpdaterService(ILogger<DnsUpdaterService> logger, IHttpClientFactory httpFactory, NetcupApiClient api)
    : BackgroundService
{
    private readonly ILogger<DnsUpdaterService> _logger = logger;
    private readonly IHttpClientFactory _httpFactory = httpFactory;
    private readonly NetcupApiClient _api = api;
    private readonly string _netcupDomain = Environment.GetEnvironmentVariable("NETCUP_DOMAIN_NAME") ??
                                            throw new InvalidOperationException("NETCUP_DOMAIN_NAME is required");
    private readonly string[] _hosts = (Environment.GetEnvironmentVariable("ZONE_HOSTS") ?? "@")
                                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private readonly TimeSpan _interval = TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable("INTERVAL_SECONDS"), out var s) && s > 0 ? s : 60);
    private readonly int _ttl = int.TryParse(Environment.GetEnvironmentVariable("RECORD_TTL"), out var t) && t > 0 ? t : 300;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Netcup DNS updater started; interval {Interval} sec", _interval.TotalSeconds);

        // Authenticate once – the session expires after 15 minutes; we re‑login on failure automatically inside client
        await _api.EnsureLoggedInAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var currentWanIp = await GetWanIpAsync(stoppingToken);
                var resolvedIp = await ResolveDomainAsync(_netcupDomain, stoppingToken);

                _logger.LogDebug("WAN IP = {Wan}, DNS resolves {Domain} -> {Resolved}", currentWanIp, _netcupDomain, resolvedIp);

                if (currentWanIp is null)
                {
                    _logger.LogWarning("Could not determine WAN IP – skipping cycle");
                }
                else if (!currentWanIp.Equals(resolvedIp))
                {
                    _logger.LogInformation("Detected IP mismatch (WAN {Wan} / DNS {Dns}) → updating...", currentWanIp, resolvedIp ?? "<none>");

                    await _api.UpdateARecordsAsync(_netcupDomain, _hosts, currentWanIp, _ttl, stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Cycle failed: {Message}", ex.Message);
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task<string?> GetWanIpAsync(CancellationToken ct)
    {
        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);
        try
        {
            return await http.GetStringAsync("https://api.ipify.org", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query WAN IP");
            return null;
        }
    }

    private async Task<string?> ResolveDomainAsync(string domain, CancellationToken ct)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(domain, ct);
            return addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString();
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "DNS resolution failed for {Domain}", domain);
            return null;
        }
    }
}
