using Microsoft.EntityFrameworkCore;
using SocialMeter.Application.Social;
using SocialMeter.Domain.Models;

namespace SocialMeter.Infrastructure.Data;

public class SocialMeterDbContext : DbContext
{
    public SocialMeterDbContext(DbContextOptions<SocialMeterDbContext> options)
        : base(options)
    {
    }

    public DbSet<SocialPost> SocialPosts => Set<SocialPost>();
    public DbSet<TitleSnapshot> TitleSnapshots => Set<TitleSnapshot>();
    public DbSet<TrackedTitle> TrackedTitles => Set<TrackedTitle>();
    public DbSet<ScoreHistory> ScoreHistory => Set<ScoreHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SocialPost>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.MediaType).HasConversion<string>().HasMaxLength(10).IsRequired();
            entity.Property(e => e.Platform).IsRequired().HasMaxLength(20);
            entity.Property(e => e.ExternalId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Author).IsRequired().HasMaxLength(100);
            entity.Property(e => e.AuthorId).HasMaxLength(64);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.MediaJson).HasMaxLength(2000);
            entity.HasIndex(e => e.Platform).HasDatabaseName("IX_SocialPosts_Platform");
            entity.Property(e => e.ContentHash).IsRequired().HasMaxLength(32);
            entity.Property(e => e.Sentiment).HasConversion<string>().HasMaxLength(10).IsRequired();
            entity.Property(e => e.Analyzer).IsRequired().HasMaxLength(20);
            entity.Property(e => e.AnalyzerNote).HasMaxLength(120);
            entity.Property(e => e.ExclusionReason).HasMaxLength(60);
            entity.Property(e => e.SourceUrl).HasMaxLength(500);
            entity.Property(e => e.Community).HasMaxLength(100);

            entity.HasIndex(e => new { e.TmdbId, e.MediaType }).HasDatabaseName("IX_SocialPosts_Title");
            entity.HasIndex(e => new { e.Platform, e.ExternalId }).IsUnique().HasDatabaseName("UX_SocialPosts_Platform_ExternalId");
            entity.HasIndex(e => e.ContentHash).HasDatabaseName("IX_SocialPosts_ContentHash");

            entity.HasData(SeedPosts());
        });

        modelBuilder.Entity<TrackedTitle>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MediaType).HasConversion<string>().HasMaxLength(10).IsRequired();
            entity.Property(e => e.Reason).IsRequired().HasMaxLength(40);
            entity.HasIndex(e => new { e.TmdbId, e.MediaType }).IsUnique().HasDatabaseName("UX_TrackedTitles_Title");
        });

        modelBuilder.Entity<ScoreHistory>(entity =>
        {
            entity.ToTable("ScoreHistory");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MediaType).HasConversion<string>().HasMaxLength(10).IsRequired();
            entity.HasIndex(e => new { e.TmdbId, e.MediaType, e.Day }).IsUnique().HasDatabaseName("UX_ScoreHistory_Title_Day");
        });

        modelBuilder.Entity<TitleSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MediaType).HasConversion<string>().HasMaxLength(10).IsRequired();
            entity.Property(e => e.Label).IsRequired().HasMaxLength(20);
            entity.HasIndex(e => new { e.TmdbId, e.MediaType }).IsUnique().HasDatabaseName("UX_TitleSnapshots_Title");
        });
    }

    // A handful of hand-labelled posts so Dune: Part Two (693134) and Gladiator II (558449)
    // show a reading before any social credentials are configured. Authors are long-standing accounts
    // so they count at full weight.
    private static SocialPost[] SeedPosts()
    {
        var fetched = new DateTime(2024, 3, 18, 0, 0, 0, DateTimeKind.Utc);

        SocialPost Seed(int id, int tmdbId, string author, string content, Sentiment sentiment, double score,
            int upvotes, int comments, DateTime createdAt, string community) => new()
        {
            Id = id,
            TmdbId = tmdbId,
            MediaType = MediaType.Movie,
            Platform = "reddit",
            ExternalId = $"seed-{id}",
            Author = author,
            AuthorId = $"seed-user-{id}",
            AuthorCreatedAt = new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(id * 37),
            AuthorReputation = 1200 + id * 310,
            Content = content,
            ContentHash = TrustPolicy.ContentHash(content),
            Sentiment = sentiment,
            SentimentScore = score,
            Analyzer = "seed",
            TrustWeight = 1,
            Substance = SubstanceHeuristic.Score(content),
            Excluded = false,
            Upvotes = upvotes,
            Comments = comments,
            CreatedAt = createdAt,
            FetchedAt = fetched,
            SourceUrl = $"https://www.reddit.com/r/{community}/",
            Community = community
        };

        return new[]
        {
            Seed(1, 693134, "movie_fan_2024", "Just watched Dune 2, absolutely incredible! The visuals were stunning and the story was so engaging.", Sentiment.Positive, 0.85, 234, 45, new DateTime(2024, 3, 15, 10, 30, 0, DateTimeKind.Utc), "movies"),
            Seed(2, 693134, "cinema_critic", "Honestly disappointed. The pacing felt off and I found myself bored in the middle.", Sentiment.Negative, -0.7, 67, 23, new DateTime(2024, 3, 14, 15, 20, 0, DateTimeKind.Utc), "TrueFilm"),
            Seed(3, 693134, "scifi_lover", "This is what cinema is all about! Villeneuve is a master. Best film of the year so far.", Sentiment.Positive, 0.7, 456, 89, new DateTime(2024, 3, 16, 9, 15, 0, DateTimeKind.Utc), "movies"),
            Seed(4, 693134, "casual_viewer", "It was okay I guess. Not as mind-blowing as everyone says but decent.", Sentiment.Neutral, 0.05, 12, 5, new DateTime(2024, 3, 13, 20, 45, 0, DateTimeKind.Utc), "moviesuggestions"),
            Seed(5, 693134, "dune_superfan", "AMAZING! Everything I hoped for and more. The score, the acting, the cinematography - perfection!", Sentiment.Positive, 0.9, 789, 134, new DateTime(2024, 3, 17, 14, 0, 0, DateTimeKind.Utc), "dune"),
            Seed(6, 693134, "book_reader", "As a book fan, I was really let down. They changed too much and it lost the essence.", Sentiment.Negative, -0.6, 45, 67, new DateTime(2024, 3, 12, 11, 30, 0, DateTimeKind.Utc), "books"),
            Seed(7, 693134, "weekend_watcher", "Solid movie. Good action, good visuals. Would watch again.", Sentiment.Positive, 0.6, 123, 18, new DateTime(2024, 3, 15, 19, 20, 0, DateTimeKind.Utc), "movies"),
            Seed(8, 693134, "film_student", "The cinematography alone makes this worth watching. Roger Deakins would be proud.", Sentiment.Positive, 0.45, 234, 34, new DateTime(2024, 3, 16, 8, 45, 0, DateTimeKind.Utc), "cinematography"),
            Seed(9, 558449, "history_buff", "Disappointing sequel. Doesn't live up to the original at all.", Sentiment.Negative, -0.7, 345, 78, new DateTime(2024, 11, 22, 10, 0, 0, DateTimeKind.Utc), "movies"),
            Seed(10, 558449, "action_fan", "The action sequences were epic! Really enjoyed it.", Sentiment.Positive, 0.75, 156, 23, new DateTime(2024, 11, 23, 14, 30, 0, DateTimeKind.Utc), "movies")
        };
    }
}
