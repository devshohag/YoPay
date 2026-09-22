using YoPay.Application.Devices;

namespace YoPay.Ingest.Api.Auth;

/// <summary>Which handset the current request belongs to. Set once by the middleware.</summary>
public sealed class DeviceContext
{
    public DeviceIdentity? Device { get; private set; }

    public bool IsAuthenticated => Device is not null;

    public void Set(DeviceIdentity device) => Device = device;
}
