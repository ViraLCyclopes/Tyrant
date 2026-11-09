using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class PngListingTests
{
    private sealed class Lister
    {
        public int Calls;
        public List<string> Files = [];

        public IReadOnlyList<string> List(IReadOnlyList<string> folders)
        {
            Calls++;
            return Files.ToList();
        }
    }

    [Fact]
    public void The_same_folders_are_listed_once_a_second()
    {
        var lister = new Lister { Files = [@"C:\m\a.png"] };
        var now = 3.0;
        var listing = new PngListing(lister.List, () => now);

        listing.Offered([@"C:\m"], null);
        now = 3.9;
        listing.Offered([@"C:\m"], null);
        Assert.Equal(1, lister.Calls);

        lister.Files.Add(@"C:\m\b.png"); // a new file shows within a second
        now = 4.0;
        Assert.Equal([@"C:\m\a.png", @"C:\m\b.png"], listing.Offered([@"C:\m"], null));
        Assert.Equal(2, lister.Calls);
    }

    [Fact]
    public void Other_folders_or_a_forget_list_again_at_once()
    {
        var lister = new Lister { Files = [@"C:\m\a.png"] };
        var listing = new PngListing(lister.List, () => 1.0);

        listing.Offered([@"C:\m"], null);
        listing.Offered([@"C:\mod\iridescence", @"C:\m"], null);
        Assert.Equal(2, lister.Calls);

        lister.Files.Add(@"C:\m\new.png");
        listing.Forget();
        Assert.Contains(@"C:\m\new.png", listing.Offered([@"C:\mod\iridescence", @"C:\m"], null));
        Assert.Equal(3, lister.Calls);
    }

    [Fact]
    public void The_file_in_use_comes_first_when_it_is_not_listed()
    {
        var lister = new Lister { Files = [@"C:\m\a.png", @"C:\m\b.png"] };
        var listing = new PngListing(lister.List, () => 1.0);

        Assert.Equal([@"C:\elsewhere\x.png", @"C:\m\a.png", @"C:\m\b.png"], listing.Offered([@"C:\m"], @"C:\elsewhere\x.png"));
        Assert.Equal([@"C:\m\a.png", @"C:\m\b.png"], listing.Offered([@"C:\m"], @"C:\M\B.PNG")); // the same file, as Windows names go
        Assert.Equal([@"C:\m\a.png", @"C:\m\b.png"], listing.Offered([@"C:\m"], null));
    }

    [Fact]
    public void Each_ask_gets_its_own_list()
    {
        var lister = new Lister { Files = [@"C:\m\a.png"] };
        var listing = new PngListing(lister.List, () => 1.0);
        var first = listing.Offered([@"C:\m"], null);
        first.Add("changed by the caller");

        Assert.Equal([@"C:\m\a.png"], listing.Offered([@"C:\m"], null));
    }

    [Fact]
    public void A_real_folder_is_listed_like_LookFiles_Pngs()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tyrant-png-listing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "b.png"), []);
            File.WriteAllBytes(Path.Combine(folder, "a.PNG"), []);
            File.WriteAllBytes(Path.Combine(folder, "c.txt"), []);
            var listing = new PngListing();

            Assert.Equal(LookFiles.Pngs([folder]), listing.Offered([folder], null));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
