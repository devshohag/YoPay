using System.Net.Http;
using System.Text;
using YoPay.Application.Abstractions;
using YoPay.Application.Webhooks;

namespace YoPay.Infrastructure.Webhooks;

/// <summary>
/// Posts one signed notification to one merchant URL.
///
/// The client comes from the factory under the name registered in DependencyInjection and
/// is never constructed here. That is the whole SSRF defence: the named client carries
/// the connect callback that filters resolved addresses and the handler that refuses
/// redirects. A second HttpClient built in this file would look identical, work, and
/// quietly undo both.
///
/// Nothing in here throws for a bad endpoint. A refused connection, a timeout, an expired
/// certificate and a 500 are all ordinary weather when the address belongs to someone
/// else; they come back as a SendResult so the pipeline can decide about retries, and so
/// one merchant's broken server cannot end a batch.
///
/// The response body is read but only the first line of it is kept. A merchant's error
/// page can be a megabyte of HTML, and the useful part of it - if any - is at the front.
/// </summary>
public sealed class HttpWebhookSender(IHttpClientFactory factory, IClock clock) : IWebhookSender
{
    private const int ErrorExcerptLength = 300;

    public async Task<SendResult> SendAsync(DueDelivery delivery, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        var client = factory.CreateClient(DependencyInjection.OutboundHttpClient);
        var sentAt = clock.UtcNow;

        using var request = new HttpRequestMessage(HttpMethod.Post, delivery.Url)
        {
            Content = new StringContent(delivery.PayloadJson, Encoding.UTF8, "application/json"),
        };

        request.Headers.TryAddWithoutValidation(
            WebhookSignature.HeaderName,
            WebhookSignature.Sign(delivery.Secret, sentAt, delivery.PayloadJson));

        request.Headers.TryAddWithoutValidation(
            WebhookSignature.EventHeaderName, delivery.EventType);

        // So a merchant can recognise a retry of something they already processed. Their
        // handler has to be idempotent anyway - we promise at least once, not exactly
        // once - and this is the key they can deduplicate on.
        request.Headers.TryAddWithoutValidation(
            WebhookSignature.DeliveryHeaderName, delivery.DeliveryId.ToString());

        try
        {
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            var code = (int)response.StatusCode;

            if (WebhookRetry.IsSuccess(code))
            {
                return new SendResult { ResponseCode = code };
            }

            return new SendResult
            {
                ResponseCode = code,
                Error = await DescribeAsync(response, code, ct).ConfigureAwait(false),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            // The client's own timeout, which arrives as a cancellation with no token.
            return new SendResult { Error = "The endpoint did not answer in time." };
        }
        catch (HttpRequestException ex)
        {
            return new SendResult { Error = Shorten(Innermost(ex)) };
        }
    }

    private static async Task<string> DescribeAsync(
        HttpResponseMessage response, int code, CancellationToken ct)
    {
        string body;

        try
        {
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            body = string.Empty;
        }

        var excerpt = Shorten(body.ReplaceLineEndings(" ").Trim());

        return excerpt.Length == 0
            ? $"The endpoint answered {code}."
            : $"The endpoint answered {code}: {excerpt}";
    }

    /// <summary>
    /// The innermost message, because the outer one is usually "An error occurred while
    /// sending the request" and the one underneath it says which certificate expired.
    /// </summary>
    private static string Innermost(Exception ex)
    {
        var current = ex;

        while (current.InnerException is { } inner)
        {
            current = inner;
        }

        return current.Message;
    }

    private static string Shorten(string text) =>
        text.Length <= ErrorExcerptLength ? text : text[..ErrorExcerptLength] + "…";
}
