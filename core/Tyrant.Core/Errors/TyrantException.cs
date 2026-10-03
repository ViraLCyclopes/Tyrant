namespace Tyrant.Core.Errors;

/// <summary>An expected, user-facing failure with a stable code and an optional fix.</summary>
public sealed class TyrantException(TyrantErrorCode code, string message, FixAction fix = FixAction.None, Exception? inner = null)
    : Exception(message, inner)
{
    public TyrantErrorCode Code { get; } = code;
    public FixAction Fix { get; } = fix;
}
