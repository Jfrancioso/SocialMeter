using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;

namespace SocialMeter.Application.Events;

/// <summary>
/// Counts clicks on links that leave the site, per day, kind of link and destination site. This is the measuring half of
/// the site's single door for outbound links: before an affiliate or sponsor deal exists, it shows which links visitors
/// actually use. Nothing identifies the visitor; anything that doesn't look like a known kind and a plain host is dropped.
/// </summary>
public sealed partial class OutboundClicks
{
    /// <summary>The kinds of outbound link the site has.</summary>
    public static readonly IReadOnlyList<string> Kinds = new[] { "post", "rating", "trailer", "store", "watch" };

    private readonly SocialMeterDbContext _db;
    private readonly ILogger<OutboundClicks> _logger;

    public OutboundClicks(SocialMeterDbContext db, ILogger<OutboundClicks> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>The kind and host as stored (our own copy of the kind, a lower-cased plain host), or null to drop the click.</summary>
    public static (string Kind, string Host)? Normalize(string? kind, string? host)
    {
        var known = Kinds.FirstOrDefault(k => k == kind);
        if (known is null || string.IsNullOrWhiteSpace(host)) return null;

        var h = host.Trim().ToLowerInvariant();
        return h.Length <= 100 && HostPattern().IsMatch(h) ? (known, h) : null;
    }

    /// <summary>Adds one to today's count. A lost click (a race or a database hiccup) is acceptable; an error page is not.</summary>
    public async Task RecordAsync(string kind, string host, DateTime nowUtc, CancellationToken ct = default)
    {
        var day = DateOnly.FromDateTime(nowUtc);
        try
        {
            var row = await _db.OutboundClicks.SingleOrDefaultAsync(r => r.Day == day && r.Kind == kind && r.Host == host, ct);
            if (row is null) _db.OutboundClicks.Add(new OutboundClickDay { Day = day, Kind = kind, Host = host, Count = 1 });
            else row.Count++;
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogInformation(ex, "Outbound click not counted (concurrent first click of the day)");
        }
    }

    // Letters, digits, dots and hyphens, at least one dot, no leading or trailing dot.
    [GeneratedRegex(@"^[a-z0-9-]+(\.[a-z0-9-]+)+$")]
    private static partial Regex HostPattern();
}
