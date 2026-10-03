using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.ProjectDecompiler;
using ICSharpCode.Decompiler.Metadata;
using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Decompile;

/// <summary>Decompiles managed assemblies into SDK-style C# projects.</summary>
public sealed class DecompileService
{
    public static readonly IReadOnlyList<string> DefaultAssemblies =
        ["Assembly-CSharp", "Assembly-CSharp-firstpass", "PKEnums", "PKInterfaces", "PKMath", "PKUtility"];

    /// <summary>Decompiles one assembly into outputDir (replacing it). Failures are returned, not thrown.</summary>
    public AssemblyDecompileResult DecompileAssembly(string assemblyPath, IEnumerable<string> searchDirs, string outputDir, CancellationToken ct)
    {
        var name = Path.GetFileNameWithoutExtension(assemblyPath);
        try
        {
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
            Directory.CreateDirectory(outputDir);

            using var module = new PEFile(assemblyPath);
            var resolver = new UniversalAssemblyResolver(assemblyPath, throwOnError: false, module.DetectTargetFrameworkId());
            foreach (var dir in searchDirs) resolver.AddSearchDirectory(dir);

            var settings = new DecompilerSettings(LanguageVersion.Latest)
            {
                ThrowOnAssemblyResolveErrors = false,
                UseSdkStyleProjectFormat = true,
            };
            var decompiler = new WholeProjectDecompiler(settings, resolver, projectWriter: null, assemblyReferenceClassifier: null, debugInfoProvider: null);
            decompiler.DecompileProject(module, outputDir, ct);
            return new AssemblyDecompileResult(name, true, outputDir, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AssemblyDecompileResult(name, false, null, ex.Message);
        }
    }

    /// <summary>Decompiles game assemblies into &lt;workspace&gt;/source/&lt;name&gt;/ and stamps the "source" output.</summary>
    public DecompileResult DecompileGame(GameInstall install, Workspace ws, IEnumerable<string>? assemblies,
        IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var names = (assemblies ?? DefaultAssemblies).ToList();
        var fingerprint = GameFingerprint.Compute(install);
        var results = new List<AssemblyDecompileResult>();
        for (var i = 0; i < names.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var name = StripDllExtension(names[i]);
            progress?.Report(new JobProgress((double)i / names.Count, $"Decompiling {name}"));
            if (!IsPlainAssemblyName(name))
            {
                // Names become paths under Managed/ and source/; anything else could escape them (and the
                // output folder is deleted before writing), so reject before touching the filesystem.
                results.Add(new AssemblyDecompileResult(names[i], false, null,
                    $"'{names[i]}' is not a plain assembly name; use a name like 'Assembly-CSharp' (no folders or paths)."));
                continue;
            }
            var path = Path.Combine(install.ManagedDir, name + ".dll");
            if (!File.Exists(path))
            {
                results.Add(new AssemblyDecompileResult(name, false, null, $"Assembly '{name}.dll' not found in {install.ManagedDir}."));
                continue;
            }
            var result = DecompileAssembly(path, [install.ManagedDir], Path.Combine(ws.SourceDir, name), ct);
            results.Add(result);
            if (result.Success)
            {
                ws.StampOutput(SourceOutputName(name), fingerprint);
                WriteOutputStamp(result.OutputDir!, SourceOutputName(name), fingerprint);
            }
            else ws.RemoveOutput(SourceOutputName(name)); // its previous output was deleted
        }
        progress?.Report(new JobProgress(1.0, "Done"));
        return new DecompileResult(results);
    }

    /// <summary>Workspace output name for one decompiled assembly, e.g. "source/Assembly-CSharp".</summary>
    public static string SourceOutputName(string assemblyName) => $"source/{assemblyName}";

    /// <summary>Which game build an output came from, kept next to it (spec 2.3), so it stays known without the workspace file.</summary>
    private static void WriteOutputStamp(string dir, string output, GameFingerprint fingerprint)
    {
        var stamp = new { output, buildGuid = fingerprint.BuildGuid, createdUtc = DateTime.UtcNow.ToString("o") };
        File.WriteAllText(Path.Combine(dir, "tyrant-output.json"), System.Text.Json.JsonSerializer.Serialize(stamp, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static string StripDllExtension(string name) =>
        name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    private static bool IsPlainAssemblyName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name is not ("." or "..")
        && Path.GetFileName(name) == name
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}
