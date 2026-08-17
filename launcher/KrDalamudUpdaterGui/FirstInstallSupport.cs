using System.Diagnostics;

namespace KrDalamudUpdaterGui;

internal static class FirstInstallProfile
{
    public static ProfilePreparationResult Initialize(UpdaterSettings settings)
        => SafeProfileManager.Prepare(
            settings.ProfileRoot,
            settings.InitializeEmptyProfile,
            settings.RequireIsolatedProfile,
            settings.DistributionLabel,
            settings.UseSystemDotnet);
}

internal static class DotnetRuntimeResolver
{
    public static string ResolveDotnetPath(UpdaterSettings settings)
    {
        var profileRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.ProfileRoot));
        if (!settings.UseSystemDotnet)
        {
            return Path.Combine(profileRoot, "runtime", "dotnet.exe");
        }

        var candidates = new List<string>();
        AddRootCandidate(candidates, Environment.GetEnvironmentVariable("DOTNET_ROOT"));
        AddRootCandidate(candidates, Environment.GetEnvironmentVariable("DOTNET_ROOT_X64"));
        var programFilesCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet",
            "dotnet.exe");
        candidates.Add(programFilesCandidate);

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var directory in pathValue.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                candidates.Add(Path.Combine(directory.Trim('"'), "dotnet.exe"));
            }
        }

        return candidates
                   .Select(candidate => Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate)))
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .FirstOrDefault(File.Exists)
               ?? programFilesCandidate;
    }

    public static string? GetHighestRuntimeVersion(string dotnetPath, string frameworkName)
    {
        if (!File.Exists(dotnetPath))
        {
            return null;
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = dotnetPath,
                Arguments = "--list-runtimes",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"시스템 dotnet 런타임 확인에 실패했습니다. 종료 코드: {process.ExitCode}\n{error}");
        }

        return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(frameworkName + " ", StringComparison.OrdinalIgnoreCase))
            .Select(line => line[(frameworkName.Length + 1)..].Split(' ', 2)[0])
            .Select(text => Version.TryParse(text, out var version) ? version : null)
            .Where(version => version is not null)
            .OrderByDescending(version => version)
            .Select(version => version!.ToString())
            .FirstOrDefault();
    }

    private static void AddRootCandidate(ICollection<string> candidates, string? root)
    {
        if (!string.IsNullOrWhiteSpace(root))
        {
            candidates.Add(Path.Combine(root, "dotnet.exe"));
        }
    }
}
