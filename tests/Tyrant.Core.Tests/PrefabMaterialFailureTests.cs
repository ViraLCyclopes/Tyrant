using Tyrant.Core.Models;

namespace Tyrant.Core.Tests;

public class PrefabMaterialFailureTests
{
    [Fact]
    public void An_unreadable_material_becomes_a_plain_one_with_a_note()
    {
        var failures = new List<string>();

        var material = PrefabReader.ReadMaterialSafely(() => throw new InvalidDataException("bad field"), "Body", 1, failures);

        Assert.Equal(("", 0), (material.Name, material.Textures.Count));
        Assert.Equal(["Body: material 2 could not be read (bad field); it is drawn plain."], failures);
    }

    [Fact]
    public void A_readable_material_is_returned_as_is()
    {
        var failures = new List<string>();
        var ok = new MaterialModel("Acro", []);

        Assert.Same(ok, PrefabReader.ReadMaterialSafely(() => ok, "Body", 0, failures));
        Assert.Empty(failures);
    }
}
