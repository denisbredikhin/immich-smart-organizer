namespace Immich.SmartOrganizer.Immich;

internal enum ImmichApiFailureKind
{
    Authentication,
    Connectivity,
    IncompatibleApi,
    Transient
}

internal sealed class ImmichApiException : Exception
{
    public ImmichApiException(
        ImmichApiFailureKind kind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public ImmichApiFailureKind Kind { get; }
}
