using PK.Dumper.Serialization;

namespace PK.Dumper.Tests;

// Attributes matched by name, exactly like Unity's SerializeField and Odin's OdinSerializeAttribute.
[AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
[AttributeUsage(AttributeTargets.Field)] public sealed class OdinSerializeAttribute : Attribute { }

/// <summary>Stands in for UnityEngine.Object.</summary>
public abstract class FakeEngineObject
{
    public string name = "";
    public long id;
    public bool destroyed;
}

/// <summary>Stands in for ScriptableObject (dumped as its own file).</summary>
public class FakeAsset : FakeEngineObject { }

/// <summary>Stands in for a texture/prefab (reference only).</summary>
public sealed class FakeTexture : FakeEngineObject { }

/// <summary>Stands in for PKLocalizationAsset (inlined).</summary>
public sealed class FakeLocalization : FakeAsset
{
    public string english = "";
}

[Flags] public enum Diet { None = 0, Plants = 1, Meat = 2 }
public enum Period { Jurassic, Cretaceous }

public struct Float3 { public float x, y, z; }

public sealed class Preferences
{
    public float min;
    public float max;
}

public sealed class TestAnimalData : FakeAsset
{
    public string speciesID = "";
    public Period period;
    public Diet diet;
    public Float3 size;
    public List<int> excavations = [];
    public Dictionary<string, float> weights = [];
    public Preferences? preferences;
    public FakeTexture? heroRender;
    public FakeLocalization? description;
    public TestAnimalData? relative;
    public float ratio;
    [SerializeField] private int cost;
    [OdinSerialize] private Dictionary<Period, int> odinOnly = [];
    private int secret = 42;
    [NonSerialized] public int runtimeOnly = 7;
    public static int StaticValue = 9;
    [field: SerializeField] public int Points { get; set; }

    public void SetCost(int value) => cost = value;
    public void AddOdin(Period p, int v) => odinOnly[p] = v;
    public int Secret => secret;
}

public sealed class TestDatabase : FakeAsset
{
    public List<TestAnimalData> animals = [];
}

/// <summary>Node whose Next chain can be made arbitrarily deep or cyclic.</summary>
public sealed class Node
{
    public Node? next;
    public int value;
}

public sealed class Thrower
{
    public int ok = 1;
    public Getterless bad = new();
}

public sealed class Getterless
{
    public int Value => throw new InvalidOperationException("boom");
}

public sealed class FakeAdapter : IDumpAdapter
{
    public bool IsEngineObject(object value) => value is FakeEngineObject;
    public bool IsEngineBaseType(Type type) => type == typeof(FakeEngineObject);
    public EngineObjectInfo Describe(object o)
    {
        var e = (FakeEngineObject)o;
        return new EngineObjectInfo(o.GetType().FullName ?? "?", e.destroyed ? "(destroyed)" : e.name, e.destroyed ? 0 : e.id);
    }
    public bool IsDumpable(object o) => o is FakeAsset a && !a.destroyed;
    public bool IsInline(object o) => o is FakeLocalization;
    public bool TryWriteSpecial(object value, JsonWriter writer)
    {
        if (value is not DateTime dt) return false;
        writer.String(dt.ToString("O"));
        return true;
    }
}
