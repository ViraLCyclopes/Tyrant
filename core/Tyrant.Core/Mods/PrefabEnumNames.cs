using Mono.Cecil;
using Tyrant.Core.Models;

namespace Tyrant.Core.Mods;

/// <summary>Enum names of a game script's fields, read from the game's DLLs (Managed folder) with Cecil; null when not an enum.</summary>
public sealed class PrefabEnumNames(string managedDir) : IDisposable
{
    private readonly DefaultAssemblyResolver _resolver = CreateResolver(managedDir);
    private readonly Dictionary<string, AssemblyDefinition?> _assemblies = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<PrefabEnumValue>? For(PrefabComponent component, string fieldPath)
    {
        if (component.Assembly is null) return null;
        var type = Find(component.Assembly, component.Namespace, component.Type);
        foreach (var name in fieldPath.Split('.'))
        {
            if (type is null) return null;
            var field = Fields(type).FirstOrDefault(f => f.Name == name);
            if (field is null) return null;
            var fieldType = field.FieldType;
            if (fieldType is ArrayType array) fieldType = array.ElementType;
            else if (fieldType is GenericInstanceType generic && generic.ElementType.Name == "List`1") fieldType = generic.GenericArguments[0];
            type = Resolve(fieldType);
        }
        if (type is null || !type.IsEnum) return null;
        return type.Fields.Where(f => f.IsStatic && f.HasConstant).Select(f => new PrefabEnumValue(f.Name, ToLong(f.Constant))).OrderBy(v => v.Value).ToList();
    }

    /// <summary>A ulong constant above long.MaxValue wraps instead of throwing, so the read never fails.</summary>
    private static long ToLong(object constant) => constant is ulong u ? unchecked((long)u) : Convert.ToInt64(constant);

    private static DefaultAssemblyResolver CreateResolver(string dir)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(dir);
        return resolver;
    }

    private static IEnumerable<FieldDefinition> Fields(TypeDefinition type)
    {
        for (TypeDefinition? t = type; t is not null; t = Resolve(t.BaseType))
            foreach (var f in t.Fields) yield return f;
    }

    private static TypeDefinition? Resolve(TypeReference? reference)
    {
        try { return reference?.Resolve(); }
        catch (Exception e) when (e is not OperationCanceledException) { return null; }
    }

    private TypeDefinition? Find(string assembly, string? ns, string name)
    {
        var file = assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? assembly : assembly + ".dll";
        if (!_assemblies.TryGetValue(file, out var definition))
        {
            var path = Path.Combine(managedDir, file);
            try { definition = File.Exists(path) ? AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = _resolver }) : null; }
            catch (Exception e) when (e is not OperationCanceledException) { definition = null; }
            _assemblies[file] = definition;
        }
        return definition?.MainModule.Types.FirstOrDefault(t => t.Name == name && (ns is null || t.Namespace == ns));
    }

    public void Dispose()
    {
        foreach (var a in _assemblies.Values) a?.Dispose();
        _resolver.Dispose();
    }
}
