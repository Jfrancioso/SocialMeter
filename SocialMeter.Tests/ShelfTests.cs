using SocialMeter.Application.Shelf;
using SocialMeter.Controllers;
using SocialMeter.Options;
using SocialMeter.Web.Filters;
using Xunit;

namespace SocialMeter.Tests;

/// <summary>The private reading shelf: who gets in, where sign-in can send them, and what the cover route will fetch.</summary>
public class ShelfTests
{
    private static readonly AdminOptions Admin = new() { Emails = new[] { "joey@example.com" } };
    private static readonly ShelfOptions Shelf = new() { Readers = new[] { "reader@example.com" } };

    [Theory]
    [InlineData("reader@example.com", "GET", true, true)]       // the shelf, reading
    [InlineData("READER@example.com ", "GET", true, true)]      // emails match whatever the case
    [InlineData("reader@example.com", "HEAD", true, true)]
    [InlineData("reader@example.com", "POST", true, false)]     // never a change, even on the shelf
    [InlineData("reader@example.com", "GET", false, false)]     // any endpoint not marked for readers: every admin one
    [InlineData("someone@example.com", "GET", true, false)]     // signed in through Access but on neither list
    [InlineData(null, "GET", true, false)]
    [InlineData("joey@example.com", "GET", true, false)]        // admins aren't let in here; the admin check decides them
    public void Readers_get_in_only_to_read_where_the_endpoint_allows_it(string? email, string method, bool readersToo, bool allowed)
        => Assert.Equal(allowed, RequireAdminAttribute.IsReaderAllowed(email, method, readersToo, Admin, Shelf));

    [Fact]
    public void Nobody_is_a_reader_until_someone_is_listed()
        => Assert.False(RequireAdminAttribute.IsReaderAllowed("reader@example.com", "GET", true, Admin, new ShelfOptions()));

    [Fact]
    public void The_reader_check_is_on_the_shelf_and_no_admin_endpoint()
    {
        var shelf = (RequireAdminAttribute)Attribute.GetCustomAttribute(typeof(ShelfController), typeof(RequireAdminAttribute))!;
        var admin = (RequireAdminAttribute)Attribute.GetCustomAttribute(typeof(AdminController), typeof(RequireAdminAttribute))!;
        Assert.True(shelf.ReadersToo);
        Assert.False(admin.ReadersToo);
    }

    [Theory]
    [InlineData("https://chatterbub.com/shelf", "/shelf", true)]
    [InlineData("https://chatterbub.com/admin", "/shelf", false)]
    [InlineData("https://chatterbub.com/shelfish", "/shelf", false)]
    [InlineData("https://evil.example/shelf", "/shelf", false)]
    [InlineData("https://chatterbub.com/admin", "/admin", true)]
    public void Shelf_sign_in_returns_only_to_the_shelf(string returnTo, string page, bool expected)
    {
        var origins = new[] { "https://chatterbub.com", "https://www.chatterbub.com" };
        Assert.Equal(expected, AdminController.SafeReturnOrigin(returnTo, origins, page) is not null);
    }

    [Fact]
    public void The_shelf_has_ten_series_each_with_its_books_in_order_and_its_own_cover()
    {
        Assert.Equal(10, ReadingShelf.Series.Count);
        Assert.Equal(10, ReadingShelf.Series.Select(s => s.CoverId).Distinct().Count());
        Assert.All(ReadingShelf.Series, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.False(string.IsNullOrWhiteSpace(s.Author));
            Assert.True(s.Books.Count >= 2);
            Assert.True(ReadingShelf.HasCover(s.CoverId));
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-14407898)]
    public void The_cover_route_fetches_only_the_shelfs_own_covers(int coverId)
        => Assert.False(ReadingShelf.HasCover(coverId));
}
