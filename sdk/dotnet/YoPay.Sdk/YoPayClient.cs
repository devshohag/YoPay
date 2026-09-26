using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YoPay.Sdk;

/// <summary>
/// Talks to the YoPay API.
///
/// Takes an HttpClient rather than making one, so it works with IHttpClientFactory in
/// ASP.NET Core and with a static client in .NET Framework. A library that news up its
/// own HttpClient per call exhausts sockets under load, and one that hides a static one
/// cannot be given a proxy, a timeout or a handler by the application hosting it.
///
/// Thread safe. Register it as a singleton, or let the factory hand you one.
/// </summary>
public sealed class YoPayClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly string _keyId;
    private readonly string _secret;
    private readonly string _baseUrl;

    public YoPayClient(HttpClient http, string keyId, string secret, string baseUrl = "https://api.yopay.com.bd")
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _keyId = keyId ?? throw new ArgumentNullException(nameof(keyId));
        _secret = secret ?? throw new ArgumentNullException(nameof(secret));
        _baseUrl = (baseUrl ?? throw new ArgumentNullException(nameof(baseUrl))).TrimEnd('/');
    }

    /// <summary>
    /// Creates an invoice and returns it, with the checkout URL to send the customer to.
    ///
    /// Safe to call again with the same OrderRef: a repeat answers with the invoice that
    /// already exists rather than making a second one. That is what makes a double-clicked
    /// checkout button, or a retry after a timeout, harmless - you do not need a lock.
    /// </summary>
    public Task<Invoice> CreateInvoiceAsync(CreateInvoiceRequest request, CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        return SendAsync<Invoice>(HttpMethod.Post, "/v1/payment/create", request, ct);
    }

    /// <summary>
    /// Asks the server what actually happened.
    ///
    /// Call this before fulfilling an order, even when a webhook has already told you the
    /// invoice is paid. The webhook is a message from outside your system; this is the
    /// record. That difference is what stands between a shop and goods shipped on a forged
    /// POST from anyone who found the callback URL.
    /// </summary>
    public Task<PaymentStatus> VerifyAsync(
        string? orderRef = null, Guid? invoiceId = null, CancellationToken ct = default)
    {
        if (orderRef is null && invoiceId is null)
        {
            throw new ArgumentException("Give either an order reference or an invoice id.", nameof(orderRef));
        }

        var payload = new Dictionary<string, object>();
        if (orderRef is not null) payload["orderRef"] = orderRef;
        if (invoiceId is not null) payload["invoiceId"] = invoiceId.Value;

        return SendAsync<PaymentStatus>(HttpMethod.Post, "/v1/payment/verify", payload, ct);
    }

    public Task<Invoice> GetInvoiceAsync(Guid invoiceId, CancellationToken ct = default) =>
        SendAsync<Invoice>(HttpMethod.Get, "/v1/payment/" + invoiceId.ToString("D"), null, ct);

    /// <summary>
    /// Calls off an unpaid invoice and frees the amount it was holding.
    ///
    /// Worth doing on an abandoned cart. Every open invoice reserves one amount on the
    /// wallet, and a shop that never cancels slowly runs out of usable amounts.
    /// </summary>
    public Task<Invoice> CancelAsync(
        string? orderRef = null, Guid? invoiceId = null, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object>();
        if (orderRef is not null) payload["orderRef"] = orderRef;
        if (invoiceId is not null) payload["invoiceId"] = invoiceId.Value;

        return SendAsync<Invoice>(HttpMethod.Post, "/v1/payment/cancel", payload, ct);
    }

    /// <summary>The response carries the signing secret. It is the only time anything will.</summary>
    public Task<WebhookEndpoint> RegisterWebhookAsync(string url, CancellationToken ct = default) =>
        SendAsync<WebhookEndpoint>(
            HttpMethod.Post, "/v1/webhooks/endpoints", new Dictionary<string, object> { ["url"] = url }, ct);

    public Task<List<WebhookEndpoint>> ListWebhooksAsync(CancellationToken ct = default) =>
        SendAsync<List<WebhookEndpoint>>(HttpMethod.Get, "/v1/webhooks/endpoints", null, ct);

    private async Task<T> SendAsync<T>(
        HttpMethod method, string path, object? payload, CancellationToken ct)
    {
        var body = payload is null ? string.Empty : JsonSerializer.Serialize(payload, Json);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = NewNonce();

        using var request = new HttpRequestMessage(method, _baseUrl + path);

        if (payload is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        request.Headers.TryAddWithoutValidation(YoPaySignature.KeyHeader, _keyId);
        request.Headers.TryAddWithoutValidation(
            YoPaySignature.TimestampHeader, timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(YoPaySignature.NonceHeader, nonce);
        request.Headers.TryAddWithoutValidation(
            YoPaySignature.SignatureHeader,
            YoPaySignature.SignRequest(_secret, method.Method, path, timestamp, nonce, body));

        HttpResponseMessage response;

        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Nothing answered, so the outcome is genuinely unknown - the call may well
            // have arrived and been acted on. Status 0 says exactly that, and both create
            // and cancel are idempotent so retrying is the right move.
            throw new YoPayException("Could not reach YoPay: " + ex.Message, 0, ex);
        }

        using (response)
        {
            var text = await ReadAsync(response, ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;

            if (status >= 400)
            {
                throw new YoPayException(TitleOf(text, status), status);
            }

            var value = JsonSerializer.Deserialize<T>(text, Json);

            return value ?? throw new YoPayException("YoPay returned an empty body.", status);
        }
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
#if NET
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
#else
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
    }

    /// <summary>The API answers problem+json, whose "title" is the sentence worth showing.</summary>
    private static string TitleOf(string text, int status)
    {
        try
        {
            using var document = JsonDocument.Parse(text);

            if (document.RootElement.TryGetProperty("title", out var title) &&
                title.GetString() is { Length: > 0 } message)
            {
                return message;
            }
        }
        catch (JsonException)
        {
            // A proxy or a load balancer answering with HTML. Nothing to read.
        }

        return "YoPay answered " + status.ToString(CultureInfo.InvariantCulture) + ".";
    }

    /// <summary>
    /// Random, never repeated. The server refuses a nonce it has already seen, and that -
    /// not the clock window - is what stops a captured request being replayed.
    /// </summary>
    private static string NewNonce()
    {
        var bytes = new byte[16];

        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }

        var builder = new StringBuilder(32);

        foreach (var b in bytes)
        {
            builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
