using YoPay.Application.Devices;

namespace YoPay.Domain.Tests;

public class HeartbeatPolicyTests
{
    [Fact]
    public void An_idle_phone_is_allowed_to_stay_quiet_longer_than_a_busy_one()
    {
        var policy = HeartbeatPolicy.Default;

        Assert.True(policy.SilenceThreshold(hasOpenSession: false)
            > policy.SilenceThreshold(hasOpenSession: true));
    }

    [Fact]
    public void A_phone_mid_payment_is_flagged_within_four_minutes()
    {
        var policy = HeartbeatPolicy.Default;

        Assert.True(policy.SilenceThreshold(hasOpenSession: true) <= TimeSpan.FromMinutes(4));
    }

    [Fact]
    public void A_never_seen_device_counts_as_offline()
    {
        Assert.True(HeartbeatPolicy.Default.IsOffline(null, DateTimeOffset.UtcNow, true));
    }

    [Fact]
    public void A_phone_that_reported_a_moment_ago_is_online()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.False(HeartbeatPolicy.Default.IsOffline(now.AddSeconds(-45), now, hasOpenSession: true));
    }
}
