using AssetsTools.NET;
using Tyrant.Core.Models;
using static Tyrant.Core.Tests.MeshFields;

namespace Tyrant.Core.Tests;

public class MaterialReaderTests
{
    private static AssetTypeValueField PPtr(string name, int fileId, long pathId) =>
        Obj(name, I32("m_FileID", fileId), Prim("m_PathID", AssetValueType.Int64, pathId));

    private static AssetTypeValueField Slot(string slot, int fileId, long pathId) =>
        Obj("data", Str("first", slot), Obj("second", PPtr("m_Texture", fileId, pathId), Obj("m_Scale", F("x", 1), F("y", 1))));

    private static AssetTypeValueField Material(string name, params AssetTypeValueField[] slots) =>
        Obj("Base", Str("m_Name", name), Obj("m_SavedProperties", Vector("m_TexEnvs", slots)));

    [Fact]
    public void Texture_slots_name_the_file_that_holds_each_texture()
    {
        string[] externals = ["archive:/CAB-shader/CAB-shader", "archive:/CAB-tex/CAB-tex"];

        var material = MaterialReader.Read(Material("Acro", Slot("_AdultDiffuse", 2, 17), Slot("_DetailNormal", 0, 5)), externals);

        Assert.Equal("Acro", material.Name);
        Assert.Equal(new[] { new TextureSlot("_AdultDiffuse", "archive:/CAB-tex/CAB-tex", 17), new TextureSlot("_DetailNormal", null, 5) },
            material.Textures);
    }

    [Fact]
    public void Empty_slots_and_files_the_material_does_not_list_are_left_out()
    {
        var material = MaterialReader.Read(Material("Acro", Slot("_IllnessTexture", 0, 0), Slot("_WoundsTexture", 4, 9)), ["archive:/CAB-a/CAB-a"]);

        Assert.Empty(material.Textures);
    }

    [Fact]
    public void Reads_the_shader_reference_keywords_and_floats()
    {
        var material = Obj("Base", Str("m_Name", "Acro"), PPtr("m_Shader", 2, 99),
            Vector("m_ValidKeywords", Str("data", "_FURMASKING_NONE"), Str("data", "_EXTRAFEATURES_ON")),
            Obj("m_SavedProperties", Vector("m_TexEnvs", Slot("_AdultDiffuse", 0, 17)),
                Vector("m_Floats", Obj("data", Str("first", "_Cutoff"), F("second", 0.5f)), Obj("data", Str("first", "_EyeSmoothness"), F("second", 0.94f)))));

        var read = MaterialReader.Read(material, ["CAB-a", "archive:/CAB-shaders/CAB-shaders"]);

        Assert.Equal(("archive:/CAB-shaders/CAB-shaders", 99L), (read.ShaderArchive, read.ShaderPathId));
        Assert.Equal(["_FURMASKING_NONE", "_EXTRAFEATURES_ON"], read.Keywords);
        Assert.Equal(0.5f, read.Floats["_Cutoff"]);
    }

    [Fact]
    public void Older_materials_keep_their_keywords_in_one_string()
    {
        var material = Obj("Base", Str("m_Name", "Rock"), PPtr("m_Shader", 0, 5), Str("m_ShaderKeywords", "_ALPHATEST_ON _NORMALMAP"));

        var read = MaterialReader.Read(material, []);

        Assert.Equal(["_ALPHATEST_ON", "_NORMALMAP"], read.Keywords);
        Assert.Equal((null as string, 5L), (read.ShaderArchive, read.ShaderPathId));
    }

    [Fact]
    public void A_material_without_saved_properties_has_no_textures()
    {
        var material = MaterialReader.Read(Obj("Base", Str("m_Name", "Plain")), []);

        Assert.Equal("Plain", material.Name);
        Assert.Empty(material.Textures);
    }
}
