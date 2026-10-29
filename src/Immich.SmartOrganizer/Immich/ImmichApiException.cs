namespace Immich.SmartOrganizer.Immich;

internal enum ImmichApiFailureKind
{
    Authentication,
    Connectivity,
    IncompatibleApi,
    Transient
}

internal sealed class ImmichApiException(
    ImmichApiFailureKind kind,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public ImmichApiFailureKind Kind { get; } = kind;
}
