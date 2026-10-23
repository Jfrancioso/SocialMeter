namespace SocialMeter.Domain.Models;

/// <summary>Cached SocialMeter result for a title, refreshed when it goes stale. Persisted.</summary>
public sealed class TitleSnapshot
{
    public int Id { get; set; }

    public int TmdbId { get; set; }
    public MediaType MediaType { get; set; }

    public int Score { get; set; }
    public string Label { get; set; } = "";
    public double Confidence { get; set; }

    public int TotalPosts { get; set; }

    /// <summary>Posts the trust policy excluded (new accounts, duplicates, pile-ons). Not part of TotalPosts.</summary>
    public int FilteredPosts { get; set; }

    public int Positive { get; set; }
    public int Neutral { get; set; }
    public int Negative { get; set; }

    public int RawScore { get; set; }
    public double EffectivePosts { get; set; }

    /// <summary>95 % half-width in points.</summary>
    public int Margin { get; set; } = 50;

    public DateTime LastRefreshedAt { get; set; }

    /// <summary>The critics' score on the same 0-100 scale (Tomatometer and Metascore, see CriticsBaseline); null for games and when unknown.</summary>
    public int? CriticsScore { get; set; }

    /// <summary>When the critics score was last looked up.</summary>
    public DateTime? CriticsCheckedAt { get; set; }
}
