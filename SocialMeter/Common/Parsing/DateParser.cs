namespace SocialMeter.Common.Parsing;

public static class DateParser
{
    public static DateOnly? TryParseDateOnly(string? value)
        => DateOnly.TryParse(value, out var d) ? d : null;
}
