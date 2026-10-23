namespace SocialMeter.Application.Common;

/// <summary>Cutting text to a length, the two ways the app needs.</summary>
public static class Text
{
    /// <summary>At most <paramref name="max"/> characters, cut hard. For values going into a database column with a length limit.</summary>
    public static string Clip(this string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>At most <paramref name="max"/> characters plus "…" when it was cut. For text a person or a model will read.</summary>
    public static string Ellipsize(this string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
