using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YoPay.Application.Devices;
using YoPay.Application.Security;

// Stands in for the Android app until T10 builds the real one.
//
// It does what the handset does and nothing more: generates a P-256 key it keeps to
// itself, pairs with a code, and uploads messages signed with that key. That makes the
// whole ingest path testable months before there is an app - and when the app arrives,
// this is the reference it has to match.
//
//   dotnet run --project tools/YoPay.DeviceSim -- pair ABCD-EFGH-JKLM
//   dotnet run --project tools/YoPay.DeviceSim -- send tests/fixtures/bkash/corpus.tsv
//   dotnet run --project tools/YoPay.DeviceSim -- heartbeat

var baseUrl = Environment.GetEnvironmentVariable("YOPAY_INGEST_URL") ?? "http://localhost:5081";
var statePath = Path.Combine(AppContext.BaseDirectory, "device-state.json");

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

switch (command)
{
    case "pair":
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: pair <pairing-code>");
            return 1;
        }

        return await PairAsync(args[1]);

    case "send":
        return await SendAsync(args.Length > 1 ? args[1] : "tests/fixtures/bkash/corpus.tsv");

    case "heartbeat":
        return await HeartbeatAsync();

    default:
        Console.WriteLine("commands: pair <code> | send [corpus.tsv] | heartbeat");
        return 0;
}

async Task<int> PairAsync(string token)
{
    // A real handset generates this inside the Android Keystore, where the private half
    // cannot be exported even from a rooted phone. Here it goes in a file, which is fine
    // for a simulator and would not be fine for anything else.
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    var response = await http.PostAsJsonAsync("/v1/ingest/pair", new
    {
        token,
        publicKey,
        model = "Simulator",
        appVersion = "sim-1.0",
    });

    var payload = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"pair failed: {(int)response.StatusCode} {payload}");
        return 1;
    }

    using var document = JsonDocument.Parse(payload);
    var deviceId = document.RootElement.GetProperty("deviceId").GetGuid();

    File.WriteAllText(statePath, JsonSerializer.Serialize(new DeviceState
    {
        DeviceId = deviceId,
        PrivateKey = Convert.ToBase64String(key.ExportPkcs8PrivateKey()),
    }));

    Console.WriteLine($"paired    device {deviceId}");
    Console.WriteLine($"wallet    {document.RootElement.GetProperty("walletNumber").GetString()}");
    Console.WriteLine($"merchant  {document.RootElement.GetProperty("merchantName").GetString()}");
    Console.WriteLine($"state     {statePath}");

    return 0;
}

async Task<int> SendAsync(string corpusPath)
{
    var state = LoadState();
    if (state is null)
    {
        return 1;
    }

    if (!File.Exists(corpusPath))
    {
        Console.Error.WriteLine($"corpus not found: {corpusPath}");
        return 1;
    }

    // Only the credits are worth uploading by default; the negatives already have unit
    // test coverage and sending them here just fills the ops queue.
    var events = new List<object>();
    var receivedAt = DateTimeOffset.UtcNow.AddMinutes(-1);

    foreach (var line in File.ReadAllLines(corpusPath))
    {
        if (line.Length == 0 || line.StartsWith('#'))
        {
            continue;
        }

        var parts = line.Split('\t', 2);
        if (parts.Length != 2)
        {
            continue;
        }

        events.Add(new
        {
            source = 1, // notification
            senderId = "bKash",
            body = parts[1].Replace("\\n", "\n"),
            receivedAt,
            dedupeHash = "", // recomputed server side; never trusted from here
        });
    }

    var body = JsonSerializer.Serialize(new
    {
        deviceFingerprint = state.DeviceId.ToString("N"),
        events,
    });

    var response = await SignedPostAsync(state, "/v1/ingest/events", body);
    Console.WriteLine($"{(int)response.StatusCode}  {await response.Content.ReadAsStringAsync()}");

    return response.IsSuccessStatusCode ? 0 : 1;
}

async Task<int> HeartbeatAsync()
{
    var state = LoadState();
    if (state is null)
    {
        return 1;
    }

    var body = JsonSerializer.Serialize(new
    {
        deviceFingerprint = state.DeviceId.ToString("N"),
        appVersion = "sim-1.0",
        permissionState = 1, // healthy
        batteryPercent = 87,
        networkType = "wifi",
        sentAt = DateTimeOffset.UtcNow,
    });

    var response = await SignedPostAsync(state, "/v1/ingest/heartbeat", body);
    Console.WriteLine($"{(int)response.StatusCode}  {await response.Content.ReadAsStringAsync()}");

    return response.IsSuccessStatusCode ? 0 : 1;
}

async Task<HttpResponseMessage> SignedPostAsync(DeviceState state, string path, string body)
{
    using var key = ECDsa.Create();
    key.ImportPkcs8PrivateKey(Convert.FromBase64String(state.PrivateKey), out _);

    var timestamp = DateTimeOffset.UtcNow;
    var nonce = Guid.NewGuid().ToString("N");

    var canonical = RequestSignature.Canonicalise("POST", path, timestamp, nonce, body);

    using var request = new HttpRequestMessage(HttpMethod.Post, path)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    request.Headers.Add("X-YoPay-Device", state.DeviceId.ToString());
    request.Headers.Add("X-YoPay-Timestamp", timestamp.ToUnixTimeSeconds().ToString());
    request.Headers.Add("X-YoPay-Nonce", nonce);
    request.Headers.Add("X-YoPay-Signature", DeviceSignature.Sign(key, canonical));

    return await http.SendAsync(request);
}

DeviceState? LoadState()
{
    if (!File.Exists(statePath))
    {
        Console.Error.WriteLine($"not paired yet. Run: pair <code>   (state file {statePath})");
        return null;
    }

    return JsonSerializer.Deserialize<DeviceState>(File.ReadAllText(statePath));
}

internal sealed class DeviceState
{
    [JsonPropertyName("deviceId")]
    public Guid DeviceId { get; set; }

    [JsonPropertyName("privateKey")]
    public string PrivateKey { get; set; } = "";
}
