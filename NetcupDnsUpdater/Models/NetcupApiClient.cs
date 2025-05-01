using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetcupDnsUpdater.Models;

public sealed class NetcupApiClient(IHttpClientFactory f, ILogger<NetcupApiClient> log)
{
    private readonly ILogger<NetcupApiClient> _log = log;
    private readonly HttpClient _http = f.CreateClient();

    private readonly string _cust = Env("NETCUP_CUSTOMER_ID");
    private readonly string _key = Env("NETCUP_API_KEY");
    private readonly string _pwd = Env("NETCUP_API_PASSWORD");

    private string? _sid;
    private DateTimeOffset _validUntil;

    private const string EP = "https://ccp.netcup.net/run/webservice/servers/endpoint.php?JSON";

    private static readonly JsonSerializerOptions JOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public async Task EnsureLoggedInAsync(CancellationToken ct)
    {
        if (_sid is null || _validUntil < DateTimeOffset.UtcNow)
            await LoginAsync(ct);
    }

    public record DnsRecordDto(
        string Id,
        string Hostname,
        string Type,
        string? Priority,
        string Destination,
        bool DeleteRecord
    );

    public async Task UpdateARecordsAsync(string zone, IEnumerable<string> hosts, string ip, CancellationToken ct)
    {
        await EnsureLoggedInAsync(ct);

        // 1) Fetch the raw info payload
        var info = await CallAsync("infoDnsRecords", new { domainname = zone }, ct);
        var respData = info.GetProperty("responsedata");

        // 2) Drill into the right property depending on the JSON shape
        JsonElement recordsElement;
        if (respData
            .TryGetProperty("dnsrecordset", out var recordSetObj) &&
            recordSetObj.TryGetProperty("dnsrecords", out recordsElement))
        {
            // New shape: responsedata.dnsrecordset.dnsrecords
        }
        else if (respData.TryGetProperty("dnsrecords", out recordsElement))
        {
            // Old shape: responsedata.dnsrecords
        }
        else
        {
            _log.LogError("Unexpected JSON shape from infoDnsRecords: no dnsrecords found for zone {Zone}", zone);
            return;
        }

        // 3) Filter to the A-records you care about
        var wanted = new HashSet<string>(hosts, StringComparer.OrdinalIgnoreCase);
        var updates = recordsElement
        .EnumerateArray()
        // match only wanted hostnames
        .Where(r => wanted.Contains(r.GetProperty("hostname").GetString()!))
        // only A-records
        .Where(r => r.GetProperty("type").GetString() == "A")
        // **new**: only if the existing destination differs
        .Where(r => !string.Equals(
            r.GetProperty("destination").GetString(),
            ip,
            StringComparison.Ordinal))
        .Select(r =>
        {
            r.TryGetProperty("id", out var idProp);
            r.TryGetProperty("hostname", out var hostProp);
            r.TryGetProperty("priority", out var priProp);

            return new
            {
                id = idProp.GetString()!,
                hostname = hostProp.GetString()!,
                type = "A",
                destination = ip,
                priority = priProp.ValueKind == JsonValueKind.Null
                                 ? null
                                 : priProp.GetString(),
                deleterecord = false
            };
        })
        .ToArray();

        if (updates.Length == 0)
        {
            _log.LogWarning(
                "No A-records to update for {Hosts} (all already at {Ip})",
                string.Join(',', hosts),
                ip);
            return;
        }

        // 4) Wrap in the new payload shape and send the update
        var payload = new
        {
            domainname = zone,
            dnsrecordset = new
            {
                dnsrecords = updates
            }
        };

        await CallAsync("updateDnsRecords", payload, ct);
        _log.LogInformation("Updated {Count} A-record(s) to {Ip}", updates.Length, ip);
    }



    // ─── internals ─────────────────────────────────────────────────────────
    private async Task LoginAsync(CancellationToken ct)
    {
        var doc = await CallAsync("login", new { customernumber = _cust, apikey = _key, apipassword = _pwd }, ct, false);
        _sid = doc.GetProperty("responsedata").GetProperty("apisessionid").GetString();
        _validUntil = DateTimeOffset.UtcNow.AddSeconds(10);
        _log.LogInformation("Logged in – session valid until {u:u}", _validUntil);
    }

    private async Task<JsonElement> CallAsync(string action, object param, CancellationToken ct, bool withSession = true)
    {
        //_log.LogInformation("Starting Netcup Post with action '{action}' and parameters:\n{parameters}\n", action, 
        //    JsonSerializer.Serialize(param, new JsonSerializerOptions 
        //    { 
        //        WriteIndented = true,
        //        PropertyNameCaseInsensitive = true,
        //        NumberHandling = JsonNumberHandling.AllowReadingFromString
        //    }));

        // merge auth fields into param
        object mergedParam = withSession
            ? Merge(param, new { customernumber = _cust, apikey = _key, apisessionid = _sid })
            : param;

        var payload = new { action, param = mergedParam };

        // initial request
        using var resp = await _http.PostAsJsonAsync(EP, payload, JOpts, ct);
        resp.EnsureSuccessStatusCode();
        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct);

        var root = doc.RootElement;
        var status = root.GetProperty("status").GetString();
        var codeEl = root.GetProperty("statuscode");
        var code = codeEl.ValueKind == JsonValueKind.Number
            ? codeEl.GetInt32()
            : int.TryParse(codeEl.GetString(), out var v) ? v : -1;

        // if session invalid, re-login and retry once
        if (!string.Equals(status, "success", StringComparison.OrdinalIgnoreCase) && code == 4001)
        {
            _sid = null;
            _log.LogWarning("Session invalid (4001), re-authenticating...");
            await EnsureLoggedInAsync(ct);

            using var retryResp = await _http.PostAsJsonAsync(EP, payload, JOpts, ct);
            retryResp.EnsureSuccessStatusCode();
            await using var s2 = await retryResp.Content.ReadAsStreamAsync(ct);
            using var doc2 = await JsonDocument.ParseAsync(s2, cancellationToken: ct);
            var root2 = doc2.RootElement;
            if (root2.GetProperty("status").GetString()?.Equals("success", StringComparison.OrdinalIgnoreCase) == false)
            {
                var msg2 = root2.TryGetProperty("longmessage", out var lm2) ? lm2.GetString() : "<unknown>";
                throw new InvalidOperationException($"Netcup API error after retry: {msg2}");
            }
            return root2.Clone();
        }

        // handle other errors
        if (!string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
        {
            var msg = root.TryGetProperty("longmessage", out var lm) ? lm.GetString() : "<unknown>";
            throw new InvalidOperationException($"Netcup API error (code {code}): {msg}");
        }

        // success
        return root.Clone();
    }

    // merge two anonymous objects into one
    private static Dictionary<string, object?> Merge(object a, object b)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in a.GetType().GetProperties()) dict[p.Name] = p.GetValue(a);
        foreach (var p in b.GetType().GetProperties()) dict[p.Name] = p.GetValue(b);
        return dict;
    }

    private static string Env(string k) => Environment.GetEnvironmentVariable(k)
        ?? throw new InvalidOperationException($"{k} is required");
}



