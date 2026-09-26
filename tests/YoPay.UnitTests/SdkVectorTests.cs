using System.Text.Json;
using YoPay.Application.Security;
using YoPay.Application.Webhooks;

namespace YoPay.UnitTests;

/// <summary>
/// Pins the signatures the SDKs are checked against.
///
/// sdk/php/tests/vectors.json was generated from this code, and the PHP SDK's own test
/// suite verifies itself against that file. These tests are the other half of the pact:
/// if anything here changes - the canonical string, the hash, the header layout - one of
/// them fails, and the message says the SDKs have to be regenerated.
///
/// Without this the drift is silent and the symptom is unreadable. Every SDK call starts
/// answering 401, both sides look correct in isolation, and nobody can tell which one
/// moved. That is a support ticket measured in days.
///
/// The vectors deliberately include a body with Bangla text in it. UTF-8 is where two
/// implementations of the same MAC usually part company, and the merchants using this
/// write in Bangla.
/// </summary>
public class SdkVectorTests
{
    private sealed record Vector(
        string Kind, string? Method, string? Path, string Body, long Ts,
        string? Nonce, string? Canonical, string? Signature, string? Header);

    private sealed record VectorFile(string Secret, IReadOnlyList<Vector> Cases);

    private static readonly VectorFile File_ = Load();

    private static VectorFile Load()
    {
        var json = File.ReadAllText(Path.Combine("fixtures", "vectors.json"));

        return JsonSerializer.Deserialize<VectorFile>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    public static TheoryData<int> RequestCases() => Indexes("request");

    public static TheoryData<int> WebhookCases() => Indexes("webhook");

    private static TheoryData<int> Indexes(string kind)
    {
        var data = new TheoryData<int>();

        for (var i = 0; i < File_.Cases.Count; i++)
        {
            if (File_.Cases[i].Kind == kind)
            {
                data.Add(i);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RequestCases))]
    public void The_canonical_request_string_has_not_moved(int index)
    {
        var vector = File_.Cases[index];

        var canonical = RequestSignature.Canonicalise(
            vector.Method!, vector.Path!,
            DateTimeOffset.FromUnixTimeSeconds(vector.Ts), vector.Nonce!, vector.Body);

        Assert.Equal(vector.Canonical, canonical);
    }

    [Theory]
    [MemberData(nameof(RequestCases))]
    public void The_request_signature_has_not_moved(int index)
    {
        var vector = File_.Cases[index];

        var signature = RequestSignature.Sign(File_.Secret, new SignedRequest
        {
            KeyId = "k_live_1",
            Method = vector.Method!,
            PathAndQuery = vector.Path!,
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(vector.Ts),
            Nonce = vector.Nonce!,
            Body = vector.Body,
            Signature = "",
        });

        Assert.Equal(vector.Signature, signature, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(WebhookCases))]
    public void The_webhook_header_has_not_moved(int index)
    {
        var vector = File_.Cases[index];
        var at = DateTimeOffset.FromUnixTimeSeconds(vector.Ts);

        Assert.Equal(vector.Header, WebhookSignature.Sign(File_.Secret, at, vector.Body));
    }

    [Theory]
    [MemberData(nameof(WebhookCases))]
    public void And_still_verifies(int index)
    {
        var vector = File_.Cases[index];
        var at = DateTimeOffset.FromUnixTimeSeconds(vector.Ts);

        Assert.True(WebhookSignature.Verify(File_.Secret, vector.Header!, vector.Body, at));
    }

    [Fact]
    public void A_signature_from_another_language_may_be_upper_or_lower_case()
    {
        // Hex case carries no information, and half the languages an SDK might be written
        // in produce lower case by default while .NET produces upper. Rejecting a correct
        // signature over that costs an integrator a night and buys nothing.
        var vector = File_.Cases.First(c => c.Kind == "webhook");
        var at = DateTimeOffset.FromUnixTimeSeconds(vector.Ts);

        var shouted = vector.Header!.Replace("v1=", "v1=", StringComparison.Ordinal);
        var parts = shouted.Split(',');
        parts[1] = parts[1].ToUpperInvariant().Replace("V1=", "v1=", StringComparison.Ordinal);

        Assert.True(WebhookSignature.Verify(
            File_.Secret, string.Join(',', parts), vector.Body, at));
    }
}
