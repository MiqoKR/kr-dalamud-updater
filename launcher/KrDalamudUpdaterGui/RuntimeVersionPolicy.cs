namespace KrDalamudUpdaterGui;

internal static class RuntimeVersionPolicy
{
    public static bool IsCompatible(string? localVersion, string? requiredVersion)
    {
        if (!Version.TryParse(localVersion, out var local) ||
            !Version.TryParse(requiredVersion, out var required))
        {
            return false;
        }

        return local.Major == required.Major && local >= required;
    }
}
