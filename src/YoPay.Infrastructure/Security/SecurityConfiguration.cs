using Microsoft.Extensions.Configuration;
using YoPay.Application.Security;

namespace YoPay.Infrastructure.Security;

/// <summary>
/// Reads the key ring out of configuration.
///
/// Bound by hand rather than through the options binder so the application layer keeps
/// no dependency on configuration types, and so a missing or malformed ring produces a
/// sentence an operator can act on instead of a null property discovered three layers
/// later.
///
/// Expected shape - in a Docker secret or the environment, never in appsettings.json:
///
///   Security__ActiveKeyId=k1
///   Security__Keys__k1=&lt;openssl rand -base64 32&gt;
///   Security__Keys__k0=&lt;the previous key, kept until the rotation sweep finishes&gt;
/// </summary>
public static class SecurityConfiguration
{
    public const string SectionName = "Security";

    public static SecretProtectionOptions ReadSecretProtection(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);

        var activeKeyId = section["ActiveKeyId"]
            ?? throw new InvalidOperationException(
                "Security:ActiveKeyId is not configured. Generate a key with " +
                "'openssl rand -base64 32' and set Security__ActiveKeyId and Security__Keys__<id>.");

        var keys = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in section.GetSection("Keys").GetChildren())
        {
            if (entry.Value is { Length: > 0 } material)
            {
                keys[entry.Key] = material;
            }
        }

        var options = new SecretProtectionOptions
        {
            ActiveKeyId = activeKeyId,
            Keys = keys,
        };

        // Fails here, at startup, rather than on the first merchant request.
        options.Validate();

        return options;
    }
}
