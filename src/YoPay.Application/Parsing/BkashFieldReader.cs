using System.Globalization;

namespace YoPay.Application.Parsing;

/// <summary>
/// Turns the text bKash prints into numbers and instants.
///
/// Three things here are not obvious and all three were found in real messages rather
/// than assumed:
///
/// - Amounts appear as "Tk 1,881.86" in transaction messages and "Tk.50.00" in OTP
///   messages. Same provider, same day, two formats.
/// - Timestamps carry no timezone. They are Dhaka local time, and treating them as UTC
///   would shift every payment six hours and quietly break every window comparison.
/// - Thousands separators are commas, so parsing must allow them or a Tk 15,000 deposit
///   silently becomes Tk 15.
/// </summary>
public static class BkashFieldReader
{
    private const string TimestampFormat = "dd/MM/yyyy HH:mm";

    /// <summary>Bangladesh has observed no daylight saving since 2009, so the fixed
    /// offset is a safe fallback when the platform has no tz database entry.</summary>
    private static readonly TimeSpan DhakaOffset = TimeSpan.FromHours(6);

    public static decimal? ReadAmount(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return decimal.TryParse(
            raw,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    public static DateTimeOffset? ReadTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!DateTime.TryParseExact(
                raw,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var local))
        {
            return null;
        }

        return new DateTimeOffset(local, DhakaOffset);
    }

    /// <summary>Normalised for hashing and comparison; provider ids are upper case.</summary>
    public static string? ReadTrxId(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToUpperInvariant();
}
