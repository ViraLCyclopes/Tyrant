using PK.Core.Errors;

namespace PK.Core.Tests;

public class PkErrorTests
{
    [Theory]
    [InlineData(PkErrorCode.GameNotFound, "GAME_NOT_FOUND")]
    [InlineData(PkErrorCode.WorkspaceInvalid, "WORKSPACE_INVALID")]
    [InlineData(PkErrorCode.WorkspaceInGameFolder, "WORKSPACE_IN_GAME_FOLDER")]
    [InlineData(PkErrorCode.DecompileFailed, "DECOMPILE_FAILED")]
    [InlineData(PkErrorCode.AssetIndexMissing, "ASSET_INDEX_MISSING")]
    [InlineData(PkErrorCode.OutputInGameFolder, "OUTPUT_IN_GAME_FOLDER")]
    public void Wire_code_is_screaming_snake_case(PkErrorCode code, string expected)
    {
        Assert.Equal(expected, code.ToWire());
    }

    [Fact]
    public void Exception_carries_code_fix_and_message()
    {
        var ex = new PkException(PkErrorCode.GameNotFound, "nope", FixAction.PickGameFolder);
        Assert.Equal(PkErrorCode.GameNotFound, ex.Code);
        Assert.Equal(FixAction.PickGameFolder, ex.Fix);
        Assert.Equal("nope", ex.Message);
    }

    [Fact]
    public void Fix_defaults_to_none()
    {
        Assert.Equal(FixAction.None, new PkException(PkErrorCode.WorkspaceStale, "x").Fix);
    }
}
