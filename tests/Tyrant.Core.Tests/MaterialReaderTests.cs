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
    public void A_material_without_saved_properties_has_no_textures()
    {
        var material = MaterialReader.Read(Obj("Base", Str("m_Name", "Plain")), []);

        Assert.Equal("Plain", material.Name);
        Assert.Empty(material.Textures);
    }
}
