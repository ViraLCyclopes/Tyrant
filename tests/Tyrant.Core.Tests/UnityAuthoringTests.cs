using AssetsTools.NET;
using Mono.Cecil;
using Tyrant.Core.Mods;

namespace Tyrant.Core.Tests;

public class UnityAuthoringTests
{
    [Fact]
    public void Preparing_references_renames_reserved_assemblies_and_updates_cross_references()
    {
        var root = Path.Combine(Path.GetTempPath(), "tyrant-unity-test", Guid.NewGuid().ToString("N"));
        var managed = Path.Combine(root, "managed");
        var project = Path.Combine(root, "project");
        Directory.CreateDirectory(managed);
        using (var first = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Assembly-CSharp-firstpass", new Version(1, 0)), "Assembly-CSharp-firstpass.dll", ModuleKind.Dll))
            first.Write(Path.Combine(managed, "Assembly-CSharp-firstpass.dll"));
        using (var main = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Assembly-CSharp", new Version(1, 0)), "Assembly-CSharp.dll", ModuleKind.Dll))
        {
            main.MainModule.AssemblyReferences.Add(new AssemblyNameReference("Assembly-CSharp-firstpass", new Version(1, 0)));
            main.Write(Path.Combine(managed, "Assembly-CSharp.dll"));
        }
        var before = File.ReadAllBytes(Path.Combine(managed, "Assembly-CSharp.dll"));
        UnityAuthoringReferences.Prepare(project, managed);
        var references = Path.Combine(project, "Assets", "PKReferences");
        using var authored = AssemblyDefinition.ReadAssembly(Path.Combine(references, "PK.Authoring.Gameplay.dll"));
        Assert.Equal("PK.Authoring.Gameplay", authored.Name.Name);
        Assert.Contains(authored.MainModule.AssemblyReferences, r => r.Name == "PK.Authoring.Firstpass");
        Assert.False(File.Exists(Path.Combine(references, "Assembly-CSharp.dll")));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(managed, "Assembly-CSharp.dll")));
        var meta = File.ReadAllText(Path.Combine(references, "PK.Authoring.Gameplay.dll.meta"));
        Assert.Contains("Editor: Editor\n    second:\n      enabled: 1", meta);
        UnityAuthoringReferences.Prepare(project, managed);
        Assert.Equal(meta, File.ReadAllText(Path.Combine(references, "PK.Authoring.Gameplay.dll.meta")));
    }

    [Fact]
    public void Managed_reference_assembly_and_nested_data_names_are_restored_together()
    {
        var nested = new AssetTypeValueField
        {
            TemplateField = new AssetTypeTemplateField { Name = "asm", Type = "string" },
            Value = new AssetTypeValue(AssetValueType.String, "PK.Authoring.Firstpass"), Children = [],
        };
        var reference = new AssetTypeReferencedObject
        {
            type = new AssetTypeReference { ClassName = "AnimalConfig", Namespace = "Example", AsmName = "PK.Authoring.Gameplay" },
            data = nested,
        };
        var field = new AssetTypeValueField
        {
            TemplateField = new AssetTypeTemplateField { Name = "references", Type = "ManagedReferencesRegistry" },
            Value = new AssetTypeValue(AssetValueType.ManagedReferencesRegistry, new ManagedReferencesRegistry { references = [reference] }), Children = [],
        };
        UnityPrefabBundle.RewriteFields(field, UnityAuthoringReferences.Names.ToDictionary(p => p.Value, p => p.Key));
        Assert.Equal("Assembly-CSharp", reference.type.AsmName);
        Assert.Equal("Assembly-CSharp-firstpass", nested.AsString);
    }
}
