using System.Text.Json;

namespace KrDalamudUpdaterGui;

internal enum ProfileState
{
    Missing,
    EmptyUnmanaged,
    ExistingUnmanaged,
    OwnedIncomplete,
    OwnedReady,
}

internal sealed record ProfilePreparationResult(
    string ProfileRoot,
    ProfileState InitialState,
    ProfileState CurrentState,
    bool Initialized,
    bool IsOwned);

internal static class SafeProfileManager
{
    public const string MarkerFileName = "KR.Dalamud.Updater.Profile.json";
    private const string LegacyMarkerFileName = "KR.Dalamud.FirstInstall.Profile.json";
    private const string OwnerName = "KR-Dalamud-Updater";

    private static readonly string[] RequiredDirectories =
    {
        Path.Combine("addon", "Hooks"),
        "dalamudAssets",
        "installedPlugins",
        "devPlugins",
    };

    public static ProfilePreparationResult Prepare(
        string configuredProfileRoot,
        bool initializeEmptyProfile,
        bool requireIsolatedProfile,
        string distributionLabel,
        bool useSystemDotnet)
    {
        var profileRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredProfileRoot));
        RequireSafeProfileRoot(profileRoot, requireIsolatedProfile);

        var initialState = Inspect(profileRoot);
        var mayInitialize = initialState is ProfileState.Missing or ProfileState.EmptyUnmanaged or
            ProfileState.OwnedIncomplete or ProfileState.OwnedReady;
        var initialized = false;

        if (initializeEmptyProfile && mayInitialize)
        {
            Directory.CreateDirectory(profileRoot);
            foreach (var relativePath in RequiredDirectories)
            {
                Directory.CreateDirectory(Path.Combine(profileRoot, relativePath));
            }

            var configPath = Path.Combine(profileRoot, "dalamudConfig.json");
            if (!File.Exists(configPath))
            {
                AtomicFile.WriteAllText(configPath, "{}" + Environment.NewLine);
            }
            var marker = new
            {
                Owner = OwnerName,
                SchemaVersion = 1,
                ProfileRoot = profileRoot,
                Distribution = distributionLabel,
                UsesSystemDotnet = useSystemDotnet,
                InitializedAtUtc = DateTimeOffset.UtcNow,
            };
            AtomicFile.WriteAllText(
                Path.Combine(profileRoot, MarkerFileName),
                JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
            initialized = true;
        }

        var currentState = Inspect(profileRoot);
        return new ProfilePreparationResult(
            profileRoot,
            initialState,
            currentState,
            initialized,
            currentState is ProfileState.OwnedIncomplete or ProfileState.OwnedReady);
    }

    public static ProfileState Inspect(string profileRoot)
    {
        profileRoot = Path.GetFullPath(profileRoot);
        if (!Directory.Exists(profileRoot))
        {
            return ProfileState.Missing;
        }

        var owned = HasValidMarker(Path.Combine(profileRoot, MarkerFileName)) ||
                    HasLegacyMarker(Path.Combine(profileRoot, LegacyMarkerFileName));
        if (!owned && !HasMeaningfulContent(profileRoot))
        {
            return ProfileState.EmptyUnmanaged;
        }

        if (!owned)
        {
            return ProfileState.ExistingUnmanaged;
        }

        var directoriesReady = RequiredDirectories.All(path => Directory.Exists(Path.Combine(profileRoot, path)));
        var configPath = Path.Combine(profileRoot, "dalamudConfig.json");
        return directoriesReady && IsJsonObject(configPath)
            ? ProfileState.OwnedReady
            : ProfileState.OwnedIncomplete;
    }

    private static void RequireSafeProfileRoot(string profileRoot, bool requireIsolatedProfile)
    {
        var appData = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        if (profileRoot.Equals(appData, StringComparison.OrdinalIgnoreCase) ||
            profileRoot.Equals(Path.GetPathRoot(profileRoot), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"프로필 루트로 사용할 수 없는 경로입니다: {profileRoot}");
        }

        if (!requireIsolatedProfile)
        {
            return;
        }

        var mainProfile = Path.GetFullPath(Path.Combine(appData, "XIVLauncherKR"));
        if (profileRoot.Equals(mainProfile, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("격리 테스트판은 메인 XIVLauncherKR 프로필을 사용할 수 없습니다.");
        }
    }

    private static bool HasMeaningfulContent(string profileRoot)
        => Directory.EnumerateFileSystemEntries(profileRoot)
            .Select(Path.GetFileName)
            .Any(name => !string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(name, MarkerFileName, StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(name, LegacyMarkerFileName, StringComparison.OrdinalIgnoreCase));

    private static bool HasValidMarker(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("Owner", out var owner) &&
                   owner.GetString()?.Equals(OwnerName, StringComparison.Ordinal) == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasLegacyMarker(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsJsonObject(string path)
    {
        try
        {
            ValidateJsonObject(path, Path.GetFileName(path));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ValidateJsonObject(string path, string displayName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{displayName}의 루트는 JSON 객체여야 합니다.");
        }
    }
}

internal sealed class DirectoryReplacement : IDisposable
{
    private readonly string target;
    private readonly string? backup;
    private bool committed;
    private bool activated;

    private DirectoryReplacement(string target, string? backup)
    {
        this.target = target;
        this.backup = backup;
    }

    public string? BackupPath => backup;

    public static DirectoryReplacement Activate(
        string profileRoot,
        string preparedDirectory,
        string target,
        string kind,
        string version,
        Action<string> verify)
    {
        profileRoot = Path.GetFullPath(profileRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        target = Path.GetFullPath(target);
        if (!target.StartsWith(profileRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("프로필 밖의 폴더는 교체할 수 없습니다.");
        }

        var parent = Path.GetDirectoryName(target) ?? throw new InvalidOperationException("대상 상위 폴더가 없습니다.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".krdu-staging-{Path.GetFileName(target)}-{Guid.NewGuid():N}");
        string? backup = null;

        try
        {
            CopyDirectory(preparedDirectory, staging);
            verify(staging);

            if (Directory.Exists(target))
            {
                var backupRoot = Path.Combine(profileRoot, "kr-dalamud-backups", kind);
                Directory.CreateDirectory(backupRoot);
                backup = Path.Combine(
                    backupRoot,
                    $"{version}-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");
                Directory.Move(target, backup);
            }

            var replacement = new DirectoryReplacement(target, backup);
            try
            {
                Directory.Move(staging, target);
                replacement.activated = true;
                verify(target);
                return replacement;
            }
            catch
            {
                replacement.Restore();
                throw;
            }
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    public void Commit() => committed = true;

    public void Dispose()
    {
        if (!committed)
        {
            Restore();
        }
    }

    private void Restore()
    {
        if (activated && Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        if (backup is not null && Directory.Exists(backup) && !Directory.Exists(target))
        {
            Directory.Move(backup, target);
        }

        activated = false;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var sourcePath in Directory.EnumerateFileSystemEntries(source))
        {
            var attributes = File.GetAttributes(sourcePath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"패키지에 재분석 지점이 포함되어 있습니다: {sourcePath}");
            }

            var destinationPath = Path.Combine(destination, Path.GetFileName(sourcePath));
            if ((attributes & FileAttributes.Directory) != 0)
            {
                CopyDirectory(sourcePath, destinationPath);
            }
            else
            {
                File.Copy(sourcePath, destinationPath, overwrite: false);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // A failed staging cleanup must not hide the original install error.
        }
    }
}

internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents, string? backupPath = null)
    {
        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("파일 상위 폴더가 없습니다.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, contents);
            if (backupPath is not null && File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(backupPath))!);
                File.Copy(path, backupPath, overwrite: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // The destination has already been committed or cleanup can wait.
            }
        }
    }
}

internal sealed class UpdateRollbackRecord
{
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string PreviousHookVersion { get; set; } = "";
    public string PreviousAssetVersion { get; set; } = "";
    public string InstalledHookVersion { get; set; } = "";
    public string InstalledAssetVersion { get; set; } = "";

    public static string GetPath(string profileRoot)
        => Path.Combine(profileRoot, "kr-dalamud-backups", "last-successful-update.json");

    public static UpdateRollbackRecord? Load(string profileRoot)
    {
        try
        {
            var path = GetPath(profileRoot);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<UpdateRollbackRecord>(File.ReadAllText(path))
                : null;
        }
        catch
        {
            return null;
        }
    }

    public void Save(string profileRoot)
    {
        AtomicFile.WriteAllText(
            GetPath(profileRoot),
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
