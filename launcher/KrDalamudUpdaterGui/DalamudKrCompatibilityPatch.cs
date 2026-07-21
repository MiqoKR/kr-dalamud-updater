using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;

namespace KrDalamudUpdaterGui;

internal static class DalamudKrCompatibilityPatch
{
    private const string SupportedGameVersion = "2026.06.18.0000.0000";
    private const string ClientStructsFileName = "FFXIVClientStructs.dll";
    private const string ClientStructsFileVersion = "7.51.0.8319";
    private const string ClientStructsSha256 = "58EAD56B7CBC5D8D76D736CA0A104BB7EC0341FCCFB3F73F8BF1A1E5EE5C78A0";
    private const string ClientStructsResourceName = "KrDalamudUpdaterGui.Compatibility.2026.06.18.0000.0000.FFXIVClientStructs.7.51.0.8319.dll.gz";

    public static void Apply(string hookRoot)
    {
        hookRoot = Path.GetFullPath(hookRoot);
        RequireSupportedGameVersion(hookRoot);

        var temporaryRoot = Path.Combine(Path.GetTempPath(), "KrDalamudUpdater", $"clientstructs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var payloadPath = Path.Combine(temporaryRoot, ClientStructsFileName);
            ExtractClientStructs(payloadPath);
            VerifyClientStructsFile(payloadPath);
            VerifyCoreCompatibility(hookRoot, temporaryRoot);

            DalamudKrLanguagePatch.Apply(hookRoot);
            InstallClientStructs(hookRoot, payloadPath);
            UpdateAssemblyHash(Path.Combine(hookRoot, "hashes.json"), Path.Combine(hookRoot, ClientStructsFileName));
            WriteMarker(hookRoot);
            Verify(hookRoot);
        }
        finally
        {
            try
            {
                Directory.Delete(temporaryRoot, true);
            }
            catch
            {
                // The system temporary directory can clean up an in-use file later.
            }
        }
    }

    public static void Verify(string hookRoot)
    {
        hookRoot = Path.GetFullPath(hookRoot);
        RequireSupportedGameVersion(hookRoot);
        DalamudKrLanguagePatch.Verify(hookRoot);

        var clientStructsPath = Path.Combine(hookRoot, ClientStructsFileName);
        VerifyClientStructsFile(clientStructsPath);
        VerifyCoreCompatibility(hookRoot, hookRoot);
    }

    private static void InstallClientStructs(string hookRoot, string payloadPath)
    {
        var targetPath = Path.Combine(hookRoot, ClientStructsFileName);
        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException($"{ClientStructsFileName}을 찾지 못했습니다.", targetPath);
        }

        if (ComputeSha256(targetPath).Equals(ClientStructsSha256, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var backupRoot = Path.Combine(hookRoot, "kr-clientstructs-backup");
        Directory.CreateDirectory(backupRoot);
        var backupPath = Path.Combine(backupRoot, $"{ClientStructsFileName}.official");
        if (!File.Exists(backupPath))
        {
            File.Copy(targetPath, backupPath);
        }

        var temporaryPath = targetPath + ".kr-patching";
        File.Copy(payloadPath, temporaryPath, true);
        VerifyClientStructsFile(temporaryPath);
        File.Move(temporaryPath, targetPath, true);
    }

    private static void ExtractClientStructs(string destinationPath)
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ClientStructsResourceName)
            ?? throw new InvalidDataException("내장된 KR FFXIVClientStructs 호환 파일을 찾지 못했습니다.");
        using var decompressor = new GZipStream(resource, CompressionMode.Decompress);
        using var destination = File.Create(destinationPath);
        decompressor.CopyTo(destination);
    }

    private static void VerifyClientStructsFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{ClientStructsFileName}을 찾지 못했습니다.", path);
        }

        var actualHash = ComputeSha256(path);
        if (!actualHash.Equals(ClientStructsSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"KR FFXIVClientStructs 해시가 예상과 다릅니다.\n예상: {ClientStructsSha256}\n실제: {actualHash}");
        }

        var actualVersion = FileVersionInfo.GetVersionInfo(path).FileVersion;
        if (!string.Equals(actualVersion, ClientStructsFileVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"KR FFXIVClientStructs 버전이 예상과 다릅니다. 예상: {ClientStructsFileVersion}, 실제: {actualVersion ?? "없음"}");
        }
    }

    private static void VerifyCoreCompatibility(string hookRoot, string clientStructsDirectory)
    {
        var dalamudPath = Path.Combine(hookRoot, "Dalamud.dll");
        if (!File.Exists(dalamudPath))
        {
            throw new FileNotFoundException("Dalamud.dll을 찾지 못했습니다.", dalamudPath);
        }

        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(clientStructsDirectory);
        if (!Path.GetFullPath(clientStructsDirectory).Equals(hookRoot, StringComparison.OrdinalIgnoreCase))
        {
            resolver.AddSearchDirectory(hookRoot);
        }

        using var assembly = AssemblyDefinition.ReadAssembly(dalamudPath, new ReaderParameters
        {
            InMemory = true,
            AssemblyResolver = resolver,
        });

        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var reference in assembly.MainModule.GetTypeReferences().Where(IsClientStructsReference))
        {
            try
            {
                if (reference.Resolve() is null)
                {
                    missing.Add(reference.FullName);
                }
            }
            catch
            {
                missing.Add(reference.FullName);
            }
        }

        foreach (var reference in assembly.MainModule.GetMemberReferences()
                     .Where(reference => IsClientStructsReference(reference.DeclaringType)))
        {
            try
            {
                if (reference.Resolve() is null)
                {
                    missing.Add(reference.FullName);
                }
            }
            catch
            {
                missing.Add(reference.FullName);
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidDataException(
                $"이 Dalamud 빌드는 KR FFXIVClientStructs {ClientStructsFileVersion}와 호환되지 않습니다. " +
                $"누락 참조 {missing.Count}개:\n{string.Join(Environment.NewLine, missing.Take(10))}");
        }
    }

    private static bool IsClientStructsReference(TypeReference reference)
    {
        while (reference is TypeSpecification specification)
        {
            reference = specification.ElementType;
        }

        return string.Equals(reference.Scope?.Name, "FFXIVClientStructs", StringComparison.OrdinalIgnoreCase);
    }

    private static void RequireSupportedGameVersion(string hookRoot)
    {
        var versionPath = Path.Combine(hookRoot, "version.json");
        if (!File.Exists(versionPath))
        {
            throw new FileNotFoundException("version.json을 찾지 못해 KR 호환 버전을 확인할 수 없습니다.", versionPath);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(versionPath));
        var supportedVersion = document.RootElement.EnumerateObject()
            .FirstOrDefault(property => property.Name.Equals("supportedGameVer", StringComparison.OrdinalIgnoreCase))
            .Value.GetString();
        if (!string.Equals(supportedVersion, SupportedGameVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"이 게임 버전의 KR FFXIVClientStructs 호환 파일은 아직 검증되지 않았습니다.\n" +
                $"지원: {SupportedGameVersion}\n대상: {supportedVersion ?? "확인 불가"}");
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void UpdateAssemblyHash(string hashesPath, string assemblyPath)
    {
        var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(hashesPath))
            ?? throw new InvalidDataException("hashes.json을 읽지 못했습니다.");
        var fileName = Path.GetFileName(assemblyPath);
        var key = hashes.Keys.SingleOrDefault(key => key.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"hashes.json에 {fileName} 항목이 없습니다.");
        using var stream = File.OpenRead(assemblyPath);
        hashes[key] = Convert.ToHexString(MD5.HashData(stream));
        File.WriteAllText(hashesPath, JsonSerializer.Serialize(hashes, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteMarker(string hookRoot)
    {
        var marker = new
        {
            Patch = "Dalamud KR Compatibility",
            Version = 1,
            AppliedAtUtc = DateTimeOffset.UtcNow,
            SupportedGameVersion,
            ClientStructsFileVersion,
            ClientStructsSha256,
        };
        File.WriteAllText(
            Path.Combine(hookRoot, "Dalamud.KR.Compatibility.Patch.json"),
            JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
    }
}
