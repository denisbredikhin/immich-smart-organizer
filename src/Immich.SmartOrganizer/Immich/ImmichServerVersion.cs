namespace Immich.SmartOrganizer.Immich;

internal sealed record ImmichServerVersion(int Major, int Minor, int Patch, int Prerelease)
{
    public bool SupportsSearchApiV2 =>
        Major == 3
        && (Minor > 2 || Minor == 2 && Patch >= 0)
        && Prerelease == 0;

    public override string ToString() =>
        Prerelease == 0
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-prerelease.{Prerelease}";
}
