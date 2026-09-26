using Microsoft.EntityFrameworkCore;
using YoPay.Application.Abstractions;
using YoPay.Application.Webhooks;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;
using YoPay.Infrastructure.Persistence;

namespace YoPay.Infrastructure.Webhooks;

public sealed class EfWebhookEndpointStore(
    YoPayDbContext db, ISecretProtector protector, IClock clock) : IWebhookEndpointStore
{
    public async Task<IReadOnlyList<EndpointView>> ListAsync(
        Guid merchantId, CancellationToken ct = default) =>
        await db.WebhookEndpoints
            .AsNoTracking()
            .Where(e => e.MerchantId == merchantId)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new EndpointView
            {
                EndpointId = e.Id,
                Url = e.Url,
                State = e.State,
                IsActive = e.IsActive,
                LastFailureReason = e.LastFailureReason,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<Guid> RegisterAsync(
        Guid merchantId, string url, string secret, CancellationToken ct = default)
    {
        var endpoint = new WebhookEndpoint
        {
            MerchantId = merchantId,
            Url = url,
            SecretEncrypted = protector.Protect(secret),

            // The shape check has passed, which is all Valid claims: the dispatcher may
            // try this address. Whether anything answers is settled by the first delivery,
            // and the connect callback still gets the last word at dial time.
            State = WebhookEndpointState.Valid,
            LastCheckedAt = clock.UtcNow,
            IsActive = true,
        };

        db.WebhookEndpoints.Add(endpoint);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return endpoint.Id;
    }

    public async Task<bool> SetActiveAsync(
        Guid merchantId, Guid endpointId, bool isActive, CancellationToken ct = default)
    {
        // Scoped by merchant in the WHERE clause rather than checked first and updated
        // after: one statement cannot be raced, and a merchant cannot pause somebody
        // else's endpoint by guessing an id.
        var updated = await db.WebhookEndpoints
            .Where(e => e.Id == endpointId && e.MerchantId == merchantId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsActive, isActive), ct)
            .ConfigureAwait(false);

        return updated == 1;
    }
}
