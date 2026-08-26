using System.Globalization;
using System.Text.RegularExpressions;

namespace Faxe.Core;

/// <summary>Unix-ms time helpers matching faxe_time.</summary>
public static partial class FaxeTime
{
    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static string ToIso8601(long? ms)
    {
        if (ms is null or 0) return string.Empty;
        return DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).UtcDateTime
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }

    public static long DurationToMs(string duration)
    {
        var m = DurationRegex().Match(duration.Trim());
        if (!m.Success)
            throw new FormatException($"Invalid duration '{duration}'");

        var value = long.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
        if (m.Groups["sign"].Value == "-") value = -value;
        return m.Groups["u"].Value switch
        {
            "ms" => value,
            "s" => value * 1000,
            "m" => value * 60_000,
            "h" => value * 3_600_000,
            "d" => value * 86_400_000,
            "w" => value * 604_800_000,
            _ => throw new FormatException($"Unknown duration unit in '{duration}'")
        };
    }

    public static long Align(long ts, long unitMs)
    {
        if (unitMs <= 0) return ts;
        return ts - (ts % unitMs);
    }

    [GeneratedRegex(@"^(?<sign>[+-])?(?<n>\d+)(?<u>ms|s|m|h|d|w)$")]
    private static partial Regex DurationRegex();
}
