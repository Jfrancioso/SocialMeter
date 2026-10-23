using SocialMeter.Domain.Models;

namespace SocialMeter.Contracts.Seo;

/// <summary>A title page worth listing in the sitemap: it has a reading. LastModified is when its sources were last read.</summary>
public sealed record SitemapTitleDto(MediaType Type, int Id, DateTime LastModified);
