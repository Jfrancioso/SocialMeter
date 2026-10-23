namespace SocialMeter.Domain.Models;

/// <summary>
/// How many times visitors followed one kind of link to one site on one day (see OutboundClicks). An aggregate count and
/// nothing else: no visitor, no address, no page. It tells us which links people use before any affiliate deal exists.
/// </summary>
public sealed class OutboundClickDay
{
    public long Id { get; set; }

    /// <summary>The UTC day.</summary>
    public DateOnly Day { get; set; }

    /// <summary>Which kind of link (see OutboundClicks.Kinds): post, rating, trailer, store, watch.</summary>
    public string Kind { get; set; } = "";

    /// <summary>The destination site, e.g. "x.com" or "www.rottentomatoes.com".</summary>
    public string Host { get; set; } = "";

    public int Count { get; set; }
}
