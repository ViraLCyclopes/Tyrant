using Tyrant.Core.Models;

namespace Tyrant.Core.Tests;

public class PrefabComponentReaderTests
{
    [Fact]
    public void Components_are_grouped_by_object_and_numbered_per_type()
    {
        var objects = PrefabComponentReader.Group([
            ("", "Animal", "PrehistoricKingdom", "Assembly-CSharp", "{}"),
            ("", "AnimalPhysics", null, "Assembly-CSharp", "{}"),
            ("IK", "FABRIKComponentAnimal", null, "Assembly-CSharp", """{"a":1}"""),
            ("IK", "FABRIKComponentAnimal", null, "Assembly-CSharp", """{"a":2}"""),
            ("", "LODGroup", null, null, "{}"),
        ]);

        Assert.Equal(["", "IK"], objects.Select(o => o.Path));
        Assert.Equal(["Animal", "AnimalPhysics", "LODGroup"], objects[0].Components.Select(c => c.Type));
        Assert.Equal([0, 1], objects[1].Components.Select(c => c.Index));
        Assert.Equal("""{"a":2}""", objects[1].Components[1].FieldsJson);
    }
}
