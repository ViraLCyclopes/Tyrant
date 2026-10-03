using Tyrant.Core.Errors;

namespace Tyrant.Rpc.Protocol;

/// <summary>JSON-RPC error object; <see cref="Data"/> carries PK's stable error code and an optional fix action.</summary>
public sealed record RpcErrorObject(int Code, string Message, RpcErrorData? Data);

public sealed record RpcErrorData(string Code, string? Fix);

public static class RpcErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
    public const int ToolError = -32000;
    public const int Cancelled = -32001;
}

/// <summary>A protocol-level failure: malformed request, unknown method, bad params.</summary>
public sealed class RpcException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

public static class RpcErrors
{
    /// <summary>Maps an exception to the error sent to Studio; <paramref name="unexpected"/> marks bugs the caller should log.</summary>
    public static RpcErrorObject From(Exception ex, out bool unexpected)
    {
        unexpected = false;
        switch (ex)
        {
            case RpcException rpc:
                return new RpcErrorObject(rpc.Code, rpc.Message, null);
            case TyrantException known:
                return new RpcErrorObject(RpcErrorCodes.ToolError, known.Message, new RpcErrorData(known.Code.ToWire(), known.Fix.ToWire()));
            case OperationCanceledException:
                return new RpcErrorObject(RpcErrorCodes.Cancelled, "Cancelled.", new RpcErrorData("CANCELLED", null));
            case ArgumentException argument:
                return new RpcErrorObject(RpcErrorCodes.InvalidParams, argument.Message, null);
            case IOException or UnauthorizedAccessException:
                return new RpcErrorObject(RpcErrorCodes.ToolError, ex.Message, new RpcErrorData("IO_ERROR", null));
            default:
                unexpected = true;
                return new RpcErrorObject(RpcErrorCodes.InternalError, $"Internal error ({ex.GetType().Name}): {ex.Message}", null);
        }
    }
}
