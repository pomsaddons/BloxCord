using System;

namespace BloxCord.Client.Helpers;

internal static class FlagEmoji
{
    public static string FromCountryCode(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
            return string.Empty;

        var code = countryCode.Trim().ToUpperInvariant();
        if (code.Length != 2)
            return string.Empty;

        var a = code[0];
        var b = code[1];
        if (a < 'A' || a > 'Z' || b < 'A' || b > 'Z')
            return string.Empty;

        // Regional Indicator Symbols: U+1F1E6 ('A') ... U+1F1FF ('Z')
        var first = char.ConvertFromUtf32(0x1F1E6 + (a - 'A'));
        var second = char.ConvertFromUtf32(0x1F1E6 + (b - 'A'));
        return first + second;
    }
}
