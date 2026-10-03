using Tyrant.Core.Errors;

namespace Tyrant.Core.Tests;

public class TyrantErrorTests
{
    [Theory]
    [InlineData(TyrantErrorCode.GameNotFound, "GAME_NOT_FOUND")]
    [InlineData(TyrantErrorCode.FrameworkMissing, "FRAMEWORK_MISSING")]
    [InlineData(TyrantErrorCode.ModIdInvalid, "MOD_ID_INVALID")]
    [InlineData(TyrantErrorCode.WorkspaceInvalid, "WORKSPACE_INVALID")]
    [InlineData(TyrantErrorCode.WorkspaceInGameFolder, "WORKSPACE_IN_GAME_FOLDER")]
    [InlineData(TyrantErrorCode.DecompileFailed, "DECOMPILE_FAILED")]
    [InlineData(TyrantErrorCode.AssetIndexMissing, "ASSET_INDEX_MISSING")]
    [InlineData(TyrantErrorCode.OutputInGameFolder, "OUTPUT_IN_GAME_FOLDER")]
    public void Wire_code_is_screaming_snake_case(TyrantErrorCode code, string expected)
    {
        Assert.Equal(expected, code.ToWire());
    }

    [Fact]
    public void Exception_carries_code_fix_and_message()
    {
        var ex = new TyrantException(TyrantErrorCode.GameNotFound, "nope", FixAction.PickGameFolder);
        Assert.Equal(TyrantErrorCode.GameNotFound, ex.Code);
        Assert.Equal(FixAction.PickGameFolder, ex.Fix);
        Assert.Equal("nope", ex.Message);
    }

    [Fact]
    public void Fix_defaults_to_none()
    {
        Assert.Equal(FixAction.None, new TyrantException(TyrantErrorCode.WorkspaceStale, "x").Fix);
    }

    [Fact]
    public void Fix_actions_have_a_wire_form()
    {
        Assert.Equal("PICK_GAME_FOLDER", FixAction.PickGameFolder.ToWire());
        Assert.Null(FixAction.None.ToWire());
        Assert.Equal("JOB_RUNNING", TyrantErrorCode.JobRunning.ToWire());
    }
}
