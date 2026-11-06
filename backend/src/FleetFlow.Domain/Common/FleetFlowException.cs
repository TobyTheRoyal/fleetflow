namespace FleetFlow.Domain.Common;

public enum FailureKind { Validation, Conflict, NotFound }

public sealed class FleetFlowException(string code, string message, FailureKind kind = FailureKind.Validation) : Exception(message)
{
    public string Code { get; } = code;
    public FailureKind Kind { get; } = kind;

    public static FleetFlowException Conflict(string code, string message) => new(code, message, FailureKind.Conflict);
    public static FleetFlowException NotFound(string resource) => new("not_found", $"The {resource} was not found.", FailureKind.NotFound);
}
