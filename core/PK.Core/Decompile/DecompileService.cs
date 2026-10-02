using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.ProjectDecompiler;
using ICSharpCode.Decompiler.Metadata;
using PK.Core.Install;
using PK.Core.Jobs;
using PK.Core.Workspaces;

namespace PK.Core.Decompile;

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
        var results = new List<AssemblyDecompileResult>();
        for (var i = 0; i < names.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new JobProgress((double)i / names.Count, $"Decompiling {names[i]}"));
            var path = Path.Combine(install.ManagedDir, names[i] + ".dll");
            results.Add(File.Exists(path)
                ? DecompileAssembly(path, [install.ManagedDir], Path.Combine(ws.SourceDir, names[i]), ct)
                : new AssemblyDecompileResult(names[i], false, null, $"Assembly '{names[i]}.dll' not found in {install.ManagedDir}."));
        }
        progress?.Report(new JobProgress(1.0, "Done"));
        if (results.Any(r => r.Success)) ws.StampOutput("source", GameFingerprint.Compute(install));
        return new DecompileResult(results);
    }
}
