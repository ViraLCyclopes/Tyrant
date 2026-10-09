using Tyrant.Core.Models;

namespace Tyrant.Core.Tests;

public class PrefabComponentReaderTests
{
    [Fact]
    public void Components_are_grouped_by_object_and_numbered_per_type()
    {
        var objects = PrefabComponentReader.Group([
            ("", "Animal", "PrehistoricKingdom", "Assembly-CSharp", "{}", false),
            ("", "AnimalPhysics", null, "Assembly-CSharp", "{}", false),
            ("IK", "FABRIKComponentAnimal", null, "Assembly-CSharp", """{"a":1}""", false),
            ("IK", "FABRIKComponentAnimal", null, "Assembly-CSharp", """{"a":2}""", false),
            ("", "LODGroup", null, null, "{}", false),
        ]);

        Assert.Equal(["", "IK"], objects.Select(o => o.Path));
        Assert.Equal(["Animal", "AnimalPhysics", "LODGroup"], objects[0].Components.Select(c => c.Type));
        Assert.Equal([0, 1], objects[1].Components.Select(c => c.Index));
        Assert.Equal("""{"a":2}""", objects[1].Components[1].FieldsJson);
    }

    [Fact]
    public void A_duplicate_path_flag_is_kept_on_the_component()
    {
        var objects = PrefabComponentReader.Group([
            ("Legs", "A", null, null, "{}", false),
            ("Legs", "B", null, null, "{}", true),
        ]);

        Assert.Equal([false, true], objects.Single().Components.Select(c => c.DuplicatePath));
    }
}
