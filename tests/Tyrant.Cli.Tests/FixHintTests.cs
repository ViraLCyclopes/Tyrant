using Tyrant.Cli;
using Tyrant.Core.Errors;

namespace Tyrant.Cli.Tests;

public class FixHintTests
{
    [Fact]
    public void A_stale_asset_index_points_at_the_index_command()
    {
        Assert.Contains("tyrant assets index", CliServices.FixHint(FixAction.ReindexAssets));
    }

    [Fact]
    public void No_fix_has_no_hint()
    {
        Assert.Equal("", CliServices.FixHint(FixAction.None));
    }
}
