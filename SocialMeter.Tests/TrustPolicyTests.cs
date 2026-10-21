using Microsoft.Extensions.Options;
using SocialMeter.Application.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Options;
using Xunit;

namespace SocialMeter.Tests;

public class TrustPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
    private readonly TrustPolicy _sut = new(Microsoft.Extensions.Options.Options.Create(new SocialMeterOptions()));

    private static SocialPostCandidate Post(string content = "This movie had a great third act and the villain finally made sense.",
        DateTime? created = null, int? reputation = 500, int? playtime = null, bool? verified = null, bool free = false, bool? verdict = null)
        => new("x", Guid.NewGuid().ToString("N"), "someone", content, 10, 2, Now.AddDays(-1), null, null,
            "author-1", created, reputation, null, false, verdict, playtime, verified, free);

    [Fact]
    public void Brand_new_accounts_are_excluded()
    {
        var verdict = _sut.Evaluate(Post(created: Now.AddDays(-3)), Now);
        Assert.True(verdict.Excluded);
        Assert.Equal(TrustPolicy.ReasonTooNew, verdict.ExclusionReason);
    }

    [Fact]
    public void Established_accounts_count_in_full()
    {
        var verdict = _sut.Evaluate(Post(created: Now.AddYears(-5)), Now);
        Assert.False(verdict.Excluded);
        Assert.Equal(1, verdict.Weight);
    }

    [Fact]
    public void Young_accounts_ramp_up()
    {
        var sixMonths = _sut.Evaluate(Post(created: Now.AddDays(-180)), Now).Weight;
        var twoMonths = _sut.Evaluate(Post(created: Now.AddDays(-60)), Now).Weight;
        Assert.True(twoMonths < sixMonths && sixMonths < 1);
    }

    [Fact]
    public void Unknown_account_age_is_discounted_not_excluded()
    {
        var verdict = _sut.Evaluate(Post(created: null), Now);
        Assert.False(verdict.Excluded);
        Assert.True(verdict.Weight < 1);
    }

    [Fact]
    public void Low_reputation_halves_the_weight()
    {
        var normal = _sut.Evaluate(Post(created: Now.AddYears(-3), reputation: 500), Now).Weight;
        var low = _sut.Evaluate(Post(created: Now.AddYears(-3), reputation: 2), Now).Weight;
        Assert.Equal(normal * 0.5, low, 3);
    }

    [Fact]
    public void Too_short_posts_are_excluded_unless_they_carry_a_verdict()
    {
        Assert.Equal(TrustPolicy.ReasonTooShort, _sut.Evaluate(Post("🔥🔥🔥", created: Now.AddYears(-2)), Now).ExclusionReason);
        Assert.False(_sut.Evaluate(Post("10/10", verdict: true, playtime: 600), Now).Excluded);
    }

    [Fact]
    public void Playtime_replaces_account_age_and_reputation()
    {
        // Private Steam profiles report 0 games owned; with playtime known that must not halve the review.
        var verdict = _sut.Evaluate(Post(created: null, reputation: null, playtime: 600, verified: true, verdict: true), Now);
        Assert.False(verdict.Excluded);
        Assert.Equal(1, verdict.Weight);
    }

    [Fact]
    public void Refund_window_reviews_are_excluded()
    {
        var verdict = _sut.Evaluate(Post(playtime: 15, verdict: false), Now);
        Assert.Equal(TrustPolicy.ReasonTooLittlePlaytime, verdict.ExclusionReason);
    }

    [Fact]
    public void Free_keys_and_unverified_ownership_are_discounted()
    {
        var owned = _sut.Evaluate(Post(playtime: 600, verified: true, verdict: true), Now).Weight;
        var free = _sut.Evaluate(Post(playtime: 600, verified: true, free: true, verdict: true), Now).Weight;
        var unverified = _sut.Evaluate(Post(playtime: 600, verified: false, verdict: true), Now).Weight;
        Assert.True(free < owned);
        Assert.True(unverified < owned);
    }

    [Fact]
    public void Link_spam_is_halved()
    {
        var spam = Post("Buy keys here https://a.io/x https://b.io/y https://c.io/z cheapest ever", created: Now.AddYears(-2));
        Assert.True(_sut.Evaluate(spam, Now).Weight <= 0.5);
    }

    // ---- cross-post rules ----

    private static SocialPost Stored(string author, string content, int upvotes, DateTime created, bool? verdict = null) => new()
    {
        Platform = "x", ExternalId = Guid.NewGuid().ToString("N"), Author = author, AuthorId = author,
        Content = content, ContentHash = TrustPolicy.ContentHash(content), Upvotes = upvotes, CreatedAt = created,
        TrustWeight = 1, VerdictUp = verdict
    };

    [Fact]
    public void One_counted_post_per_account_per_title()
    {
        var posts = new[]
        {
            Stored("fan", "Loved the finale, the last episode was perfect.", 5, Now.AddDays(-2)),
            Stored("fan", "Still thinking about that finale, perfect ending.", 50, Now.AddDays(-1)),
            Stored("fan", "Finale was perfect, fight me.", 1, Now)
        };

        TrustPolicy.ApplyCrossPostRules(posts, Now);

        Assert.Single(posts, p => !p.Excluded);
        Assert.False(posts[1].Excluded); // the one the community engaged with most
        Assert.All(posts.Where(p => p.Excluded), p => Assert.Equal(TrustPolicy.ReasonSameAccount, p.ExclusionReason));
    }

    [Fact]
    public void Identical_text_from_different_accounts_counts_once_for_the_first()
    {
        var text = "This show changed my life, everyone must watch it immediately!!!";
        var posts = new[]
        {
            Stored("b", text, 1, Now.AddHours(-1)),
            Stored("a", text, 1, Now.AddHours(-5)),
            Stored("c", text.ToUpperInvariant(), 1, Now)
        };

        TrustPolicy.ApplyCrossPostRules(posts, Now);

        Assert.False(posts[1].Excluded);
        Assert.True(posts[0].Excluded && posts[2].Excluded);
    }

    [Fact]
    public void Short_verdict_reviews_are_not_treated_as_duplicates()
    {
        var posts = new[]
        {
            Stored("p1", "10/10", 1, Now.AddHours(-3), verdict: true),
            Stored("p2", "10/10", 1, Now.AddHours(-2), verdict: true),
            Stored("p3", "10/10", 1, Now.AddHours(-1), verdict: true)
        };

        TrustPolicy.ApplyCrossPostRules(posts, Now);

        Assert.All(posts, p => Assert.False(p.Excluded));
    }

    [Fact]
    public void Cross_post_rules_can_be_rerun_safely()
    {
        var posts = new[]
        {
            Stored("fan", "First take", 1, Now.AddDays(-2)),
            Stored("fan", "Second take with more upvotes", 9, Now.AddDays(-1))
        };

        TrustPolicy.ApplyCrossPostRules(posts, Now);
        posts[1].Upvotes = 0; // the other post is now the engaged one
        TrustPolicy.ApplyCrossPostRules(posts, Now);

        Assert.False(posts[0].Excluded);
        Assert.True(posts[1].Excluded);
    }
}
