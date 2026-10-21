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
}
