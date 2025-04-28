using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace NetcupDnsUpdater.Models;

public sealed class NetcupApiClient(IHttpClientFactory factory, ILogger<NetcupApiClient> logger)
{
    private readonly ILogger<NetcupApiClient> _logger = logger;
    private readonly HttpClient _http = factory.CreateClient();

    // credentials -----------------------------------------------------------
    private readonly string _customerId = Environment.GetEnvironmentVariable("NETCUP_CUSTOMER_ID")
        ?? throw new InvalidOperationException("NETCUP_CUSTOMER_ID is required");
    private readonly string _apiKey = Environment.GetEnvironmentVariable("NETCUP_API_KEY")
        ?? throw new InvalidOperationException("NETCUP_API_KEY is required");
    private readonly string _apiPw = Environment.GetEnvironmentVariable("NETCUP_API_PASSWORD")
        ?? throw new InvalidOperationException("NETCUP_API_PASSWORD is required");

    private string? _sessionId;
    private DateTimeOffset _sessionValidUntil;

    private const string Endpoint = "https://ccp.netcup.net/run/webservice/servers/endpoint.php?JSON";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    // public surface --------------------------------------------------------
    public async Task EnsureLoggedInAsync(CancellationToken ct)
    {
        if (_sessionId is not null && _sessionValidUntil > DateTimeOffset.UtcNow) return;
        await LoginAsync(ct);
    }

    public async Task UpdateARecordsAsync(string zoneDomain, IEnumerable<string> hostnames, string newIp, int ttl, CancellationToken ct)
    {
        await EnsureLoggedInAsync(ct);

        // 1) fetch existing records ----------------------------------------
        var infoDoc = await SendAsync("infoDnsRecords", new
        {
            domainname = zoneDomain
        }, ct);

        var records = infoDoc.RootElement.GetProperty("responsedata")
                                         .GetProperty("dnsrecords")
                                         .EnumerateArray();

        static int JInt(JsonElement el) => el.ValueKind == JsonValueKind.Number
            ? el.GetInt32()
            : int.TryParse(el.GetString(), out var v) ? v : throw new InvalidOperationException("Numeric expected");

        var wanted = hostnames.Select(h => h.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var updateSet = records
            .Where(r => wanted.Contains(r.GetProperty("hostname").GetString()!))
            .Where(r => string.Equals(r.GetProperty("type").GetString(), "A", StringComparison.OrdinalIgnoreCase))
            .Select(r => new
            {
                id = JInt(r.GetProperty("id")),
                hostname = r.GetProperty("hostname").GetString(),
                type = "A",
                destination = newIp,
                ttl
            })
            .ToArray();

        if (updateSet.Length == 0)
        {
            _logger.LogWarning("No matching A-records found for hosts {hosts}", string.Join(',', wanted));
            return;
        }

        // 2) push update ----------------------------------------------------
        await SendAsync("updateDnsRecords", new
        {
            domainname = zoneDomain,
            dnsrecordset = updateSet
        }, ct);

        _logger.LogInformation("Updated {n} record(s) → {ip}", updateSet.Length, newIp);
    }

    // helpers ---------------------------------------------------------------
    private async Task LoginAsync(CancellationToken ct)
    {
        var doc = await SendAsync("login", new
        {
            customernumber = _customerId,
            apipassword = _apiPw,
            apikey = _apiKey
        }, ct, includeSession: false); // no session yet

        _sessionId = doc.RootElement.GetProperty("responsedata").GetProperty("apisessionid").GetString();
        _sessionValidUntil = DateTimeOffset.UtcNow.AddMinutes(14);
        _logger.LogInformation("Logged in – session valid until {until:u}", _sessionValidUntil);
    }

    private async Task<JsonDocument> SendAsync(string action, object param, CancellationToken ct, bool includeSession = true)
    {
        // merge auth into param object (anonymous type merge via Expando)
        var auth = new
        {
            customernumber = _customerId,
            apikey = _apiKey,
            apisessionid = includeSession ? _sessionId : null
        };

        var fullParam = MergeObjects(param, auth);

        var payload = new { action, param = fullParam };

        using var resp = await _http.PostAsJsonAsync(Endpoint, payload, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!string.Equals(doc.RootElement.GetProperty("status").GetString(), "success", StringComparison.OrdinalIgnoreCase))
        {
            var msg = doc.RootElement.TryGetProperty("longmessage", out var lm) ? lm.GetString() : "Unknown";
            throw new InvalidOperationException($"Netcup API error: {msg}");
        }

        return doc;
    }

    // quick & dirty anonymous‑object merge ----------------------------------
    private static object MergeObjects(object a, object b)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in a.GetType().GetProperties()) dict[p.Name] = p.GetValue(a);
        foreach (var p in b.GetType().GetProperties()) dict[p.Name] = p.GetValue(b);
        return dict;
    }
}
