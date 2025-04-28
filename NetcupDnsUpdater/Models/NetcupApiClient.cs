using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NetcupDnsUpdater.Models;

public sealed class NetcupApiClient(IHttpClientFactory factory, ILogger<NetcupApiClient> logger)
{
    private readonly ILogger<NetcupApiClient> _logger = logger;
    private readonly HttpClient _http = factory.CreateClient("netcup-api");

    private readonly string _customerId = Environment.GetEnvironmentVariable("NETCUP_CUSTOMER_ID") ??
                                          throw new InvalidOperationException("NETCUP_CUSTOMER_ID is required");
    private readonly string _apiKey = Environment.GetEnvironmentVariable("NETCUP_API_KEY") ??
                                          throw new InvalidOperationException("NETCUP_API_KEY is required");
    private readonly string _apiPw = Environment.GetEnvironmentVariable("NETCUP_API_PASSWORD") ??
                                          throw new InvalidOperationException("NETCUP_API_PASSWORD is required");

    private string? _sessionId;
    private DateTimeOffset _sessionValidUntil;

    private const string Endpoint = "https://ccp.netcup.net/run/webservice/servers/endpoint.php?JSON";

    public async Task EnsureLoggedInAsync(CancellationToken ct)
    {
        if (_sessionId is not null && _sessionValidUntil > DateTimeOffset.UtcNow.AddMinutes(-1)) return;
        await LoginAsync(ct);
    }

    private async Task LoginAsync(CancellationToken ct)
    {
        var payload = new
        {
            action = "login",
            param = new
            {
                customernumber = _customerId,
                apipassword = _apiPw,
                apikey = _apiKey
            }
        };

        var doc = await SendAsync(payload, ct);
        _sessionId = doc.RootElement.GetProperty("data").GetProperty("apisessionid").GetString();
        _sessionValidUntil = DateTimeOffset.UtcNow.AddMinutes(14); // 15 min timeout – stay on the safe side
        _logger.LogInformation("Logged in – session valid until {Until:u}", _sessionValidUntil);
    }

    public async Task UpdateARecordsAsync(string zoneDomain, IEnumerable<string> hostnames, string newIp, int ttl, CancellationToken ct)
    {
        await EnsureLoggedInAsync(ct);

        // 1. fetch current records
        var infoReq = new
        {
            action = "infoDnsRecords",
            param = new
            {
                domainname = zoneDomain,
                customernumber = _customerId,
                apikey = _apiKey,
                apisessionid = _sessionId
            }
        };
        var infoDoc = await SendAsync(infoReq, ct);
        var records = infoDoc.RootElement.GetProperty("data").GetProperty("dnsrecords").EnumerateArray();

        var recordsToUpdate = records
            .Where(r => hostnames.Contains(r.GetProperty("hostname").GetString()!, StringComparer.OrdinalIgnoreCase))
            .Where(r => r.GetProperty("type").GetString() == "A")
            .Select(r => new
            {
                id = r.GetProperty("id").GetInt32(),
                hostname = r.GetProperty("hostname").GetString()!,
                type = "A",
                destination = newIp,
                ttl
            })
            .ToArray();

        if (recordsToUpdate.Length == 0)
        {
            _logger.LogWarning("No matching A‑records found for hosts {Hosts}", string.Join(',', [.. hostnames]));
            return;
        }

        // 2. submit update
        var updateReq = new
        {
            action = "updateDnsRecords",
            param = new
            {
                domainname = zoneDomain,
                customernumber = _customerId,
                apikey = _apiKey,
                apisessionid = _sessionId,
                dnsrecordset = recordsToUpdate
            }
        };
        await SendAsync(updateReq, ct);
        _logger.LogInformation("Updated {Count} record(s) → {Ip}", recordsToUpdate.Length, newIp);
    }

    // ──────────────────────────────────────────────────────────────────────────
    private async Task<JsonDocument> SendAsync<T>(T payload, CancellationToken ct)
    {
        using var resp = await _http.PostAsJsonAsync(Endpoint, payload, ct);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("status", out var statusEl) || statusEl.GetInt32() != 200)
        {
            string msg = doc.RootElement.TryGetProperty("longmessage", out var longMsg) ? longMsg.GetString() ?? "<unknown>" : "<unknown>";
            throw new InvalidOperationException($"Netcup API error: {msg}");
        }

        return doc;
    }
}