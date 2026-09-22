namespace YoPay.Api.Auth;

/// <summary>
/// Who the current request belongs to. Scoped, set once by the signing middleware, and
/// read by the endpoints - so no endpoint has to re-derive identity from headers and
/// none of them can disagree about it.
/// </summary>
public sealed class MerchantContext
{
    public Guid MerchantId { get; private set; }
    public string KeyId { get; private set; } = string.Empty;

    public bool IsAuthenticated => MerchantId != Guid.Empty;

    public void Set(Guid merchantId, string keyId)
    {
        MerchantId = merchantId;
        KeyId = keyId;
    }
}
