using System.Text.Json;
using Tyrant.Rpc.Jobs;
using Tyrant.Rpc.Protocol;
using Tyrant.Rpc.TypeScript;

namespace Tyrant.Rpc.Tests;

public enum SampleColor
{
    DeepRed,
    Blue,
}

public sealed record SampleChild(int Size);

public sealed record SampleParams(string Name, string? Note = null, int Count = 3);

public sealed record SampleResult(IReadOnlyList<SampleChild> Children, Dictionary<string, int> Counts, SampleColor Color, DateTimeOffset When,
    JsonElement Raw, bool? Flag, string? Maybe, IReadOnlyList<IReadOnlyList<string>> Grid);

public class TsEmitterTests
{
    private static string Emit() => TsEmitter.Emit([
        new RpcMethodInfo("sample.get", typeof(SampleParams), typeof(SampleResult), null),
        new RpcMethodInfo("sample.job", null, typeof(JobStarted), typeof(SampleChild)),
    ]);

    [Fact]
    public void Enums_become_unions_of_camel_case_names()
    {
        Assert.Contains("export type SampleColor = \"deepRed\" | \"blue\";", Emit());
    }

    [Fact]
    public void Defaulted_parameters_are_optional_and_nullable_references_allow_null()
    {
        Assert.Contains("export interface SampleParams {\n  name: string;\n  note?: string | null;\n  count?: number;\n}", Emit());
    }

    [Fact]
    public void Collections_dates_json_and_nullables_map_to_typescript()
    {
        var ts = Emit();

        Assert.Contains("  children: SampleChild[];\n", ts);
        Assert.Contains("  counts: Record<string, number>;\n", ts);
        Assert.Contains("  color: SampleColor;\n", ts);
        Assert.Contains("  when: string;\n", ts);
        Assert.Contains("  raw: unknown;\n", ts);
        Assert.Contains("  flag: boolean | null;\n", ts);
        Assert.Contains("  maybe: string | null;\n", ts);
        Assert.Contains("  grid: string[][];\n", ts);
        Assert.Contains("export interface SampleChild {\n  size: number;\n}", ts);
    }

    [Fact]
    public void Methods_jobs_and_notifications_are_listed()
    {
        var ts = Emit();

        Assert.Contains("  \"sample.get\": { params: SampleParams; result: SampleResult };\n", ts);
        Assert.Contains("  \"sample.job\": { params: void; result: JobStarted };\n", ts);
        Assert.Contains("export interface RpcJobs {\n  \"sample.job\": SampleChild;\n}", ts);
        Assert.Contains("  \"job.failed\": JobFailedNotification;\n", ts);
        Assert.Contains("export interface RpcErrorObject {\n  code: number;\n  message: string;\n  data: RpcErrorData | null;\n}", ts);
    }

    [Fact]
    public void Output_is_stable()
    {
        Assert.Equal(Emit(), Emit());
    }
}
