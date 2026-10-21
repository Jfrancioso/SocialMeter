using Microsoft.EntityFrameworkCore;
using SocialMeter.Domain.Models;

namespace SocialMeter.Infrastructure.Data;

public class SocialMeterDbContext : DbContext
{
    public SocialMeterDbContext(DbContextOptions<SocialMeterDbContext> options)
    : base(options)
    {
    }

    public DbSet<SocialPost> SocialPosts => Set<SocialPost>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure SocialPost entity
        modelBuilder.Entity<SocialPost>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Platform)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Author)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Content)
                .IsRequired();

            entity.Property(e => e.Sentiment)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.SourceUrl)
                .HasMaxLength(500);

            entity.Property(e => e.Subreddit)
                .HasMaxLength(100);

            // Create index for faster queries
            entity.HasIndex(e => e.MovieId)
                .HasDatabaseName("IX_SocialPosts_MovieId");

            // Seed some initial data
            entity.HasData(
                new SocialPost
                {
                    Id = 1,
                    MovieId = 693134,
                    Platform = "reddit",
                    Author = "movie_fan_2024",
                    Content = "Just watched Dune 2, absolutely incredible! The visuals were stunning and the story was so engaging.",
                    Sentiment = "positive",
                    Upvotes = 234,
                    Comments = 45,
                    CreatedAt = new DateTime(2024, 3, 15, 10, 30, 0),
                    SourceUrl = "https://reddit.com/r/movies/example1",
                    Subreddit = "movies"
                },
                new SocialPost
                {
                    Id = 2,
                    MovieId = 693134,
                    Platform = "reddit",
                    Author = "cinema_critic",
                    Content = "Honestly disappointed. The pacing felt off and I found myself bored in the middle.",
                    Sentiment = "negative",
                    Upvotes = 67,
                    Comments = 23,
                    CreatedAt = new DateTime(2024, 3, 14, 15, 20, 0),
                    SourceUrl = "https://reddit.com/r/TrueFilm/example2",
                    Subreddit = "TrueFilm"
                },
                new SocialPost
                {
                    Id = 3,
                    MovieId = 693134,
                    Platform = "reddit",
                    Author = "scifi_lover",
                    Content = "This is what cinema is all about! Villeneuve is a master. Best film of the year so far.",
                    Sentiment = "positive",
                    Upvotes = 456,
                    Comments = 89,
                    CreatedAt = new DateTime(2024, 3, 16, 9, 15, 0),
                    SourceUrl = "https://reddit.com/r/movies/example3",
                    Subreddit = "movies"
                },
                new SocialPost
                {
                    Id = 4,
                    MovieId = 693134,
                    Platform = "reddit",
                    Author = "casual_viewer",
                    Content = "It was okay I guess. Not as mind-blowing as everyone says but decent.",
                    Sentiment = "neutral",
                    Upvotes = 12,
                    Comments = 5,
                    CreatedAt = new DateTime(2024, 3, 13, 20, 45, 0),
                    SourceUrl = "https://reddit.com/r/moviesuggestions/example4",
                    Subreddit = "moviesuggestions"
                },
                new SocialPost
                {
                    Id = 5,
                    MovieId = 693134,
                    Platform = "reddit",
                    Author = "dune_superfan",
                    Content = "AMAZING! Everything I hoped for and more. The score, the acting, the cinematography - perfection!",
                    Sentiment = "positive",
                    Upvotes = 789,
                    Comments = 134,
                    CreatedAt = new DateTime(2024, 3, 17, 14, 0, 0),
                    SourceUrl = "https://reddit.com/r/dune/example5",
                    Subreddit = "dune"
                },
                new SocialPost
                {
                    Id = 6,
                    MovieId = 693134,
                    Platform = "reddit",
                    Author = "book_reader",
                    Content = "As a book fan, I was really let down. They changed too much and it lost the essence.",
                    Sentiment = "negative",
                    Upvotes = 45,
                    Comments = 67,
                    CreatedAt = new DateTime(2024, 3, 12, 11, 30, 0),
                    SourceUrl = "https://reddit.com/r/books/example6",
                    Subreddit = "books"
                },
                new SocialPost
                {
                    Id = 7,
                    MovieId = 693134,
                    Platform = "reddit",
                    Author = "weekend_watcher",
                    Content = "Solid movie. Good action, good visuals. Would watch again.",
                    Sentiment = "positive",
                    Upvotes = 123,
                    Comments = 18,
                    CreatedAt = new DateTime(2024, 3, 15, 19, 20, 0),
                    SourceUrl = "https://reddit.com/r/movies/example7",
                    Subreddit = "movies"
                },
                new SocialPost
                {
                    Id = 8,
                    MovieId = 693134,
                    Platform = "reddit",
                    Author = "film_student",
                    Content = "The cinematography alone makes this worth watching. Roger Deakins would be proud.",
                    Sentiment = "positive",
                    Upvotes = 234,
                    Comments = 34,
                    CreatedAt = new DateTime(2024, 3, 16, 8, 45, 0),
                    SourceUrl = "https://reddit.com/r/cinematography/example8",
                    Subreddit = "cinematography"
                },
                new SocialPost
                {
                    Id = 9,
                    MovieId = 558449,
                    Platform = "reddit",
                    Author = "history_buff",
                    Content = "Disappointing sequel. Doesn't live up to the original at all.",
                    Sentiment = "negative",
                    Upvotes = 345,
                    Comments = 78,
                    CreatedAt = new DateTime(2024, 11, 22, 10, 0, 0),
                    SourceUrl = "https://reddit.com/r/movies/example9",
                    Subreddit = "movies"
                },
                new SocialPost
                {
                    Id = 10,
                    MovieId = 558449,
                    Platform = "reddit",
                    Author = "action_fan",
                    Content = "The action sequences were epic! Really enjoyed it.",
                    Sentiment = "positive",
                    Upvotes = 156,
                    Comments = 23,
                    CreatedAt = new DateTime(2024, 11, 23, 14, 30, 0),
                    SourceUrl = "https://reddit.com/r/movies/example10",
                    Subreddit = "movies"
                }
            );
        });
    }
}