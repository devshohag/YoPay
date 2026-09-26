using YoPay.Api.Auth;
using YoPay.Application.Webhooks;
using YoPay.Contracts.Api;

namespace YoPay.Api.Endpoints;

/// <summary>
/// Where a merchant tells us to send notifications.
///
/// In the API and not only in a dashboard because the people who integrate this are
/// setting up staging and production from a deploy script, and a step that can only be
/// done by hand in a browser is a step that gets skipped on the box nobody logs into.
/// </summary>
public static class WebhookEndpointApi
{
    public static void MapWebhookEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/v1/webhooks").RequireRateLimiting("merchant");

        group.MapGet("/endpoints", async (
            MerchantContext merchant,
            IWebhookEndpointStore store,
            CancellationToken ct) =>
        {
            if (!merchant.IsAuthenticated)
            {
                return Results.Unauthorized();
            }

            var endpoints = await store.ListAsync(merchant.MerchantId, ct).ConfigureAwait(false);

            // No Secret on any of these. It exists in the answer to the call that created
            // the endpoint and nowhere else, forever.
            return Results.Ok(endpoints.Select(e => new EndpointResponse
            {
                EndpointId = e.EndpointId,
                Url = e.Url,
                State = e.State,
                IsActive = e.IsActive,
                LastFailureReason = e.LastFailureReason,
            }));
        });

        group.MapPost("/endpoints", async (
            RegisterEndpointRequest request,
            MerchantContext merchant,
            IWebhookEndpointStore store,
            WebhookOptions options,
            CancellationToken ct) =>
        {
            if (!merchant.IsAuthenticated)
            {
                return Results.Unauthorized();
            }

            var check = WebhookEndpointRules.Check(request.Url, options.AllowPrivateEndpoints);
            if (!check.Accepted)
            {
                return Results.Problem(
                    title: check.Error,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var secret = WebhookEndpointRules.NewSecret();

            var id = await store
                .RegisterAsync(merchant.MerchantId, check.Url!.ToString(), secret, ct)
                .ConfigureAwait(false);

            return Results.Created($"/v1/webhooks/endpoints/{id}", new EndpointResponse
            {
                EndpointId = id,
                Url = check.Url.ToString(),
                State = Domain.Enums.WebhookEndpointState.Valid,
                IsActive = true,

                // The only time this is ever returned. Said plainly in the docs too,
                // because a merchant who assumes they can fetch it later will not store
                // it, and the first thing they will learn is that nothing verifies.
                Secret = secret,
            });
        });

        group.MapDelete("/endpoints/{endpointId:guid}", async (
            Guid endpointId,
            MerchantContext merchant,
            IWebhookEndpointStore store,
            CancellationToken ct) =>
        {
            if (!merchant.IsAuthenticated)
            {
                return Results.Unauthorized();
            }

            // Deactivated rather than deleted. Deliveries point at it, and a merchant
            // asking "what happened to the payments last Tuesday" needs the URL those
            // rows were sent to still to exist.
            var found = await store
                .SetActiveAsync(merchant.MerchantId, endpointId, false, ct)
                .ConfigureAwait(false);

            return found ? Results.NoContent() : Results.NotFound();
        });
    }
}
