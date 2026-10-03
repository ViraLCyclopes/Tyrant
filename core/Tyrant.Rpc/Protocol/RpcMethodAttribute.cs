namespace Tyrant.Rpc.Protocol;

/// <summary>Exposes a public method to Studio under <see cref="Name"/>. It takes at most one parameter object (plus an optional CancellationToken).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RpcMethodAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    /// <summary>For job methods (returning a job id): the type delivered later in job.done.</summary>
    public Type? JobResult { get; init; }
}
