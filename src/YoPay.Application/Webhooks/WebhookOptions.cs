namespace YoPay.Application.Webhooks;

/// <summary>
/// The one switch that loosens the outbound guard.
///
/// It exists because of a real and unavoidable situation: a developer runs YoPay and
/// their own shop on one laptop, the webhook URL is http://localhost:5000/yopay, and
/// every defence in OutboundAddressPolicy is correctly standing in the way. Without a
/// switch, the developer edits the guard by hand to get through the afternoon - and that
/// edit is what reaches production, permanently, with nobody remembering it is there.
///
/// So: off by default, named so it cannot be mistaken for anything else, read in exactly
/// one place (the infrastructure layer, which owns configuration), and announced out loud
/// by the host at startup when it is on.
///
/// A plain object with no configuration binding in it, because this project references no
/// packages at all - which is what lets every rule in it be exercised without a host.
/// </summary>
public sealed class WebhookOptions
{
    public const string AllowPrivateEndpointsKey = "Webhooks:AllowPrivateEndpoints";

    public bool AllowPrivateEndpoints { get; init; }

    /// <summary>The sentence a host logs at startup. Deliberately blunt.</summary>
    public const string Warning =
        "Webhooks:AllowPrivateEndpoints is ON. Private, loopback and cloud-metadata " +
        "addresses are reachable from this server. This is for local development only - " +
        "never enable it on a deployed instance.";
}
