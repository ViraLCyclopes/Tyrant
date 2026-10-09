using System.Text.RegularExpressions;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;

namespace Tyrant.Core.Tests;

public sealed class UnityRecoveredAssetsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tyrant-recovery", Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(root, "export", "Assets");
    private string Project => Path.Combine(root, "project");

    public UnityRecoveredAssetsTests()
    {
        var references = Path.Combine(Project, "Assets", "PKReferences");
        Directory.CreateDirectory(references);
        File.WriteAllText(Path.Combine(references, "PK.Authoring.Gameplay.dll.meta"), "guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n");
        Asset("Plugins/Assembly-CSharp.dll", "not imported", '1');
        Asset("Mesh/mesh.asset", "%YAML 1.1\nMesh:\n  m_Name: Body\n", '2');
        Asset("Shaders/animal.shader", "Shader \"PK/Animal\" { Properties {} }", '3');
        Asset("Material/body.mat", "%YAML 1.1\n--- !u!21 &21\nMaterial:\n  m_Name: Body\n  m_Shader: {fileID: 48, guid: " + new string('3', 32) + ", type: 3}\n", '6');
        Asset("unused.asset", "%YAML 1.1\nunused", '4');
        Asset("Animal.prefab", "%YAML 1.1\n--- !u!1 &1\nGameObject:\n  m_Name: Vanilla\n  m_IsActive: 1\n--- !u!114 &2\nMonoBehaviour:\n"
            + "  m_Script: {fileID: 123, guid: " + new string('1', 32) + ", type: 3}\n"
            + "  mesh: {fileID: 43, guid: " + new string('2', 32) + ", type: 2}\n"
            + "  shader: {fileID: 48, guid: " + new string('3', 32) + ", type: 3}\n"
            + "  material: {fileID: 21, guid: " + new string('6', 32) + ", type: 2}\n"
            + "  type: {class: Settings, ns: PK, asm: Assembly-CSharp}\n"
            + "--- !u!1 &3\nGameObject:\n  m_Name: Child\n  m_IsActive: 1\n", '5');
    }

    private void Asset(string relative, string content, char guid)
    {
        var path = Path.Combine(Source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.WriteAllText(path + ".meta", "fileFormatVersion: 2\nguid: " + new string(guid, 32) + "\n");
    }

    [Fact]
    public void Import_preserves_wiring_binds_existing_DLL_and_forks_only_reachable_assets()
    {
        var original = File.ReadAllText(Path.Combine(Source, "Animal.prefab"));
        var first = UnityRecoveredAssets.Import(Source, Project, "Assets/Animal.prefab", "mirabilis", "Name: \"quoted\"");
        Assert.Equal(4, first.Assets);
        var prefab = Path.Combine(Project, first.Prefab);
        var text = File.ReadAllText(prefab);
        Assert.Contains("m_Script: {fileID: 123, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}", text);
        Assert.Contains("asm: PK.Authoring.Gameplay", text);
        Assert.Contains("m_IsActive: 0", text);
        Assert.Contains("m_Name: Child\n  m_IsActive: 1", text);
        Assert.DoesNotContain(new string('2', 32), text);
        var folder = Path.GetDirectoryName(prefab)!;
        Assert.Contains("Shader \"Tyrant/Recovered/PK/Animal\"", File.ReadAllText(Path.Combine(folder, "Shaders", "animal.shader")));
        Assert.Contains("m_Name: \"Tyrant/RecoveredMaterial/Body\"", File.ReadAllText(Path.Combine(folder, "Material", "body.mat")));
        Assert.False(File.Exists(Path.Combine(folder, "unused.asset")));
        Assert.False(Directory.Exists(Path.Combine(folder, "Plugins")));
        var meshGuid = Regex.Match(File.ReadAllText(Path.Combine(folder, "Mesh", "mesh.asset.meta")), "guid: ([a-f0-9]{32})").Groups[1].Value;
        Assert.Contains("guid: " + meshGuid, text);
        Assert.Equal(original, File.ReadAllText(Path.Combine(Source, "Animal.prefab")));
        var second = UnityRecoveredAssets.Import(Source, Project, "Assets/Animal.prefab", "mirabilis", "Second");
        Assert.NotEqual(first.Prefab, second.Prefab);
        Assert.Equal(text, File.ReadAllText(prefab));
        Assert.DoesNotContain(meshGuid, File.ReadAllText(Path.Combine(Project, second.Prefab)));
    }

    [Fact]
    public void Missing_dependencies_fail_without_installing_a_partial_prefab()
    {
        File.Delete(Path.Combine(Source, "Mesh", "mesh.asset.meta"));
        Assert.Throws<TyrantException>(() => UnityRecoveredAssets.Import(Source, Project, "Assets/Animal.prefab", "mirabilis", "Animal"));
        Assert.False(Directory.Exists(Path.Combine(Project, "Assets", "Animals")));
    }

    [Fact]
    public void Import_refuses_an_open_project_and_escaping_prefab_paths()
    {
        Assert.Throws<TyrantException>(() => UnityRecoveredAssets.Import(Source, Project, "Assets/../Animal.prefab", "mirabilis", "Animal"));
        Directory.CreateDirectory(Path.Combine(Project, "Temp"));
        File.WriteAllText(Path.Combine(Project, "Temp", "UnityLockfile"), "");
        Assert.Throws<TyrantException>(() => UnityRecoveredAssets.Import(Source, Project, "Assets/Animal.prefab", "mirabilis", "Animal"));
    }

    public void Dispose() => Directory.Delete(root, true);
}
