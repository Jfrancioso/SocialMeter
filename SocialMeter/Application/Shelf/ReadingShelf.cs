using SocialMeter.Contracts.Shelf;

namespace SocialMeter.Application.Shelf;

/// <summary>
/// The private reading shelf: ten popular spicy romance and romantasy series, hand-picked as a starting list (not ranked
/// from posts yet; scoring books is a separate, later piece). Each series lists its main books in reading order. Covers
/// are Open Library's, by cover id, served through the API (see ShelfController) so the site's image policy stays as is.
/// </summary>
public static class ReadingShelf
{
    public static readonly IReadOnlyList<ShelfSeriesDto> Series = new ShelfSeriesDto[]
    {
        new("A Court of Thorns and Roses", "Sarah J. Maas", "Fae courts, a huntress and a High Lord: enemies to lovers.", 15254465,
            new[] { "A Court of Thorns and Roses", "A Court of Mist and Fury", "A Court of Wings and Ruin", "A Court of Frost and Starlight", "A Court of Silver Flames" }),
        new("The Empyrean", "Rebecca Yarros", "A war college for dragon riders, and the rival she shouldn't want.", 14407898,
            new[] { "Fourth Wing", "Iron Flame", "Onyx Storm" }),
        new("Crescent City", "Sarah J. Maas", "Angels, fae and shifters in a modern city: a murder mystery and a slow burn.", 9289603,
            new[] { "House of Earth and Blood", "House of Sky and Breath", "House of Flame and Shadow" }),
        new("Blood and Ash", "Jennifer L. Armentrout", "A sheltered Maiden, her guard, and a kingdom built on lies.", 14105538,
            new[] { "From Blood and Ash", "A Kingdom of Flesh and Fire", "The Crown of Gilded Bones", "The War of Two Queens", "A Soul of Ash and Blood" }),
        new("Fifty Shades", "E. L. James", "The billionaire romance that started the wave.", 12648183,
            new[] { "Fifty Shades of Grey", "Fifty Shades Darker", "Fifty Shades Freed" }),
        new("Twisted", "Ana Huang", "Contemporary, steamy, one couple per book: brothers' best friends and billionaires.", 12940491,
            new[] { "Twisted Love", "Twisted Games", "Twisted Hate", "Twisted Lies" }),
        new("Bridgerton", "Julia Quinn", "Regency London, one Bridgerton sibling per book.", 6492549,
            new[] { "The Duke and I", "The Viscount Who Loved Me", "An Offer from a Gentleman", "Romancing Mister Bridgerton", "To Sir Phillip, With Love", "When He Was Wicked", "It's in His Kiss", "On the Way to the Wedding" }),
        new("Kingdom of the Wicked", "Kerri Maniscalco", "A witch hunting her sister's killer makes a deal with a prince of Hell.", 10526129,
            new[] { "Kingdom of the Wicked", "Kingdom of the Cursed", "Kingdom of the Feared" }),
        new("The Plated Prisoner", "Raven Kennedy", "A gold-touched favourite in a gilded cage, and the commander who sees her.", 13290449,
            new[] { "Gild", "Glint", "Gleam", "Glow", "Gold" }),
        new("Cat and Mouse", "H. D. Carlton", "Dark romance with a stalker hero. Check the content warnings first.", 12992962,
            new[] { "Haunting Adeline", "Hunting Adeline" })
    };

    /// <summary>Only the shelf's own covers can be fetched through the API, so the cover route can't be used to fetch anything else.</summary>
    public static bool HasCover(int coverId) => Series.Any(s => s.CoverId == coverId);
}
