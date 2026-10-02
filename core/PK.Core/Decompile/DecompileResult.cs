namespace PK.Core.Decompile;

public sealed record AssemblyDecompileResult(string AssemblyName, bool Success, string? OutputDir, string? Error);

public sealed record DecompileResult(IReadOnlyList<AssemblyDecompileResult> Assemblies)
{
    public bool AllSucceeded => Assemblies.All(a => a.Success);
}
