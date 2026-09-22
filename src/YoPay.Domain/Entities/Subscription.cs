using YoPay.Domain.Common;
using YoPay.Domain.Enums;

namespace YoPay.Domain.Entities;

/// <summary>
/// What a merchant is paying for.
///
/// MonthlyEventAllowance exists because the API is rate limited: promising "unlimited
/// transactions" while the architecture enforces limits is a promise that breaks in
/// public. Plans state a fair-use figure instead.
/// </summary>
public class Subscription : MerchantEntity
{
    public PlanCode Plan { get; set; } = PlanCode.Trial;
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trialing;

    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }

    /// <summary>Kept live for a short period after a failed renewal so merchants do not
    /// lose payment detection over a billing hiccup.</summary>
    public DateTimeOffset? GraceUntil { get; set; }

    public int MonthlyEventAllowance { get; set; }
    public int MaxWallets { get; set; } = 1;
    public int MaxDevices { get; set; } = 1;

    public Merchant Merchant { get; set; } = null!;
}
