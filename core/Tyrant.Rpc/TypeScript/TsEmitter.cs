using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tyrant.Rpc.Jobs;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.TypeScript;

/// <summary>Emits TypeScript declarations for the protocol from the C# DTOs (spec §4.2: the DTOs are the single source of truth).</summary>
public static class TsEmitter
{
    private static readonly Type[] NotificationTypes =
        [typeof(JobProgressNotification), typeof(JobDoneNotification), typeof(JobFailedNotification)];

    public static string Emit(IReadOnlyList<RpcMethodInfo> methods)
    {
        var emitter = new Emitter();
        foreach (var method in methods)
        {
            if (method.ParamsType is not null) emitter.Collect(method.ParamsType);
            emitter.Collect(method.ResultType);
            if (method.JobResultType is not null) emitter.Collect(method.JobResultType);
        }
        foreach (var type in NotificationTypes) emitter.Collect(type);

        var ts = new StringBuilder();
        ts.Append("// Generated from the C# DTOs in core/Tyrant.Rpc by `tyrant rpc --emit-ts`. Do not edit by hand:\n");
        ts.Append("// run `npm run gen:types` in studio/ (a test fails while this file is out of date).\n\n");
        foreach (var type in emitter.Types.Values)
            ts.Append(type.IsEnum ? emitter.EnumDeclaration(type) : emitter.InterfaceDeclaration(type)).Append('\n');

        var ordered = methods.OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
        ts.Append("export interface RpcMethods {\n");
        foreach (var m in ordered)
            ts.Append($"  \"{m.Name}\": {{ params: {(m.ParamsType is null ? "void" : emitter.TypeName(m.ParamsType))}; result: {emitter.TypeName(m.ResultType)} }};\n");
        ts.Append("}\n\n");
        ts.Append("/** What job.done delivers for each job method. */\n");
        ts.Append("export interface RpcJobs {\n");
        foreach (var m in ordered.Where(m => m.JobResultType is not null))
            ts.Append($"  \"{m.Name}\": {emitter.TypeName(m.JobResultType!)};\n");
        ts.Append("}\n\n");
        ts.Append("export interface RpcNotifications {\n");
        ts.Append($"  \"{JobManager.ProgressMethod}\": JobProgressNotification;\n");
        ts.Append($"  \"{JobManager.DoneMethod}\": JobDoneNotification;\n");
        ts.Append($"  \"{JobManager.FailedMethod}\": JobFailedNotification;\n");
        ts.Append("}\n");
        return ts.ToString();
    }

    private sealed class Emitter
    {
        private readonly NullabilityInfoContext _nullability = new();

        public SortedDictionary<string, Type> Types { get; } = new(StringComparer.Ordinal);

        public void Collect(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (IsBuiltIn(type)) return;
            if (ElementType(type) is { } element)
            {
                Collect(element);
                return;
            }
            if (DictionaryValueType(type) is { } value)
            {
                Collect(value);
                return;
            }
            if (Types.TryGetValue(type.Name, out var existing))
            {
                if (existing != type)
                    throw new InvalidOperationException($"Two protocol types are named {type.Name}: {existing.FullName} and {type.FullName}.");
                return;
            }
            Types[type.Name] = type;
            if (!type.IsEnum)
                foreach (var property in Properties(type)) Collect(property.PropertyType);
        }

        public string EnumDeclaration(Type type) =>
            $"export type {type.Name} = {string.Join(" | ", Enum.GetNames(type).Select(n => $"\"{JsonNamingPolicy.CamelCase.ConvertName(n)}\""))};\n";

        public string InterfaceDeclaration(Type type)
        {
            var optional = OptionalNames(type);
            var ts = new StringBuilder($"export interface {type.Name} {{\n");
            foreach (var property in Properties(type))
            {
                var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                var mark = optional.Contains(property.Name) ? "?" : "";
                var nullable = IsNullable(property) ? " | null" : "";
                ts.Append($"  {name}{mark}: {TypeName(property.PropertyType)}{nullable};\n");
            }
            return ts.Append("}\n").ToString();
        }

        public string TypeName(Type type)
        {
            if (Nullable.GetUnderlyingType(type) is { } inner) return TypeName(inner); // "| null" is added by the caller
            if (type == typeof(string) || type == typeof(DateTimeOffset) || type == typeof(DateTime) || type == typeof(Guid)) return "string";
            if (type == typeof(bool)) return "boolean";
            if (type == typeof(JsonElement) || type == typeof(object)) return "unknown";
            if (type == typeof(void) || type == typeof(Task)) return "null";
            if (IsNumber(type)) return "number";
            if (ElementType(type) is { } element) return TypeName(element) + "[]";
            if (DictionaryValueType(type) is { } value) return $"Record<string, {TypeName(value)}>";
            return type.Name;
        }

        private bool IsNullable(PropertyInfo property) =>
            property.PropertyType.IsValueType
                ? Nullable.GetUnderlyingType(property.PropertyType) is not null
                : _nullability.Create(property).ReadState == NullabilityState.Nullable;

        private static IEnumerable<PropertyInfo> Properties(Type type) =>
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.GetIndexParameters().Length == 0 && p.GetCustomAttribute<JsonIgnoreAttribute>() is null);

        /// <summary>Record parameters with a default value may be left out by the caller.</summary>
        private static HashSet<string> OptionalNames(Type type)
        {
            var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            return constructor?.GetParameters().Where(p => p.HasDefaultValue).Select(p => p.Name!).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        }

        private static bool IsBuiltIn(Type type) =>
            type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTimeOffset) || type == typeof(DateTime)
            || type == typeof(Guid) || type == typeof(JsonElement) || type == typeof(object) || type == typeof(void) || type == typeof(Task);

        private static bool IsNumber(Type type) =>
            type == typeof(int) || type == typeof(long) || type == typeof(double) || type == typeof(float) || type == typeof(decimal)
            || type == typeof(short) || type == typeof(byte) || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort)
            || type == typeof(sbyte);

        private static Type? ElementType(Type type)
        {
            if (type == typeof(string) || DictionaryValueType(type) is not null) return null;
            if (type.IsArray) return type.GetElementType();
            var enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? type
                : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            return enumerable?.GetGenericArguments()[0];
        }

        private static Type? DictionaryValueType(Type type)
        {
            var candidates = type.IsInterface ? type.GetInterfaces().Append(type) : type.GetInterfaces();
            var dictionary = candidates.FirstOrDefault(i => i.IsGenericType
                && (i.GetGenericTypeDefinition() == typeof(IDictionary<,>) || i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
            return dictionary is not null && dictionary.GetGenericArguments()[0] == typeof(string) ? dictionary.GetGenericArguments()[1] : null;
        }
    }
}
