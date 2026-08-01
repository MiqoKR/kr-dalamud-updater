using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;

namespace KrDalamudUpdaterGui;

internal static class DalamudKrCompatibilityPatch
{
    public const string SupportedGameVersion = "2026.07.16.0001.0000";

    private const string ClientStructsFileName = "FFXIVClientStructs.dll";
    private const string OfficialClientStructsFileVersion = "7.51.0.8681";
    private const string OfficialClientStructsSha256 =
        "070AE575722317FBD8577623A77F36CF0530E3B82F75EAE0B44ED093C6204F30";
    private const string CompatibleClientStructsFileVersion = "7.51.0.8667";
    private const string CompatibleClientStructsSha256 =
        "C1D168B51BB7624030ED34DCE46B020F8136ECDAA6E30E3A18015C36CBE92E67";
    private const string CompatibleClientStructsResourceName =
        "KrDalamudUpdaterGui.Compatibility.FFXIVClientStructs.7.51.0.8667.dll.gz";

    public static bool SupportsGameVersion(string? gameVersion)
        => string.Equals(gameVersion, SupportedGameVersion, StringComparison.OrdinalIgnoreCase);

    public static void Apply(string hookRoot)
    {
        hookRoot = Path.GetFullPath(hookRoot);
        RequireSupportedGameVersion(hookRoot);
        InstallCompatibleClientStructs(hookRoot);
        VerifyCoreCompatibility(hookRoot);

        DalamudKrLanguagePatch.Apply(hookRoot);
        DalamudKrSignaturePatch.Apply(hookRoot);

        // Refresh hashes after replacing ClientStructs and applying both Dalamud IL patches.
        var hashesPath = Path.Combine(hookRoot, "hashes.json");
        UpdateAssemblyHash(hashesPath, Path.Combine(hookRoot, ClientStructsFileName));
        UpdateAssemblyHash(hashesPath, Path.Combine(hookRoot, "Dalamud.dll"));

        WriteMarker(hookRoot);
        Verify(hookRoot);
    }

    public static void Verify(string hookRoot)
    {
        hookRoot = Path.GetFullPath(hookRoot);
        RequireSupportedGameVersion(hookRoot);
        VerifyCompatibleClientStructs(hookRoot);
        VerifyCoreCompatibility(hookRoot);
        DalamudKrLanguagePatch.Verify(hookRoot);
        DalamudKrSignaturePatch.Verify(hookRoot);
    }

    private static void InstallCompatibleClientStructs(string hookRoot)
    {
        var path = Path.Combine(hookRoot, ClientStructsFileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{ClientStructsFileName}을 찾지 못했습니다.", path);
        }

        var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion;
        var hash = ComputeSha256(path);
        if (string.Equals(version, CompatibleClientStructsFileVersion, StringComparison.OrdinalIgnoreCase) &&
            hash.Equals(CompatibleClientStructsSha256, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var isOfficial = string.Equals(version, OfficialClientStructsFileVersion, StringComparison.OrdinalIgnoreCase) &&
            hash.Equals(OfficialClientStructsSha256, StringComparison.OrdinalIgnoreCase);
        if (!isOfficial)
        {
            throw new InvalidDataException(
                $"검증되지 않은 FFXIVClientStructs 버전입니다. " +
                $"지원: 공식 {OfficialClientStructsFileVersion} 또는 한섭 호환 {CompatibleClientStructsFileVersion}, " +
                $"대상: {version ?? "확인 불가"} ({hash})");
        }

        var temporaryPath = path + ".krpatch.tmp";
        try
        {
            using var compressed = typeof(DalamudKrCompatibilityPatch).Assembly
                .GetManifestResourceStream(CompatibleClientStructsResourceName)
                ?? throw new InvalidDataException(
                    $"내장 한섭 호환 파일을 찾지 못했습니다: {CompatibleClientStructsResourceName}");
            using (var gzip = new GZipStream(compressed, CompressionMode.Decompress))
            using (var output = File.Create(temporaryPath))
            {
                gzip.CopyTo(output);
            }

            var extractedHash = ComputeSha256(temporaryPath);
            if (!extractedHash.Equals(CompatibleClientStructsSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"내장 한섭 호환 파일의 해시가 일치하지 않습니다.\n" +
                    $"예상: {CompatibleClientStructsSha256}\n실제: {extractedHash}");
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        VerifyCompatibleClientStructs(hookRoot);
    }

    private static void VerifyCompatibleClientStructs(string hookRoot)
    {
        var path = Path.Combine(hookRoot, ClientStructsFileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{ClientStructsFileName}을 찾지 못했습니다.", path);
        }

        var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion;
        if (!string.Equals(version, CompatibleClientStructsFileVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"한섭 호환 FFXIVClientStructs 버전이 아닙니다. " +
                $"지원: {CompatibleClientStructsFileVersion}, 대상: {version ?? "확인 불가"}");
        }

        var hash = ComputeSha256(path);
        if (!hash.Equals(CompatibleClientStructsSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"한섭 호환 FFXIVClientStructs 해시가 일치하지 않습니다.\n" +
                $"예상: {CompatibleClientStructsSha256}\n실제: {hash}");
        }
    }

    private static void VerifyCoreCompatibility(string hookRoot)
    {
        var dalamudPath = Path.Combine(hookRoot, "Dalamud.dll");
        if (!File.Exists(dalamudPath))
        {
            throw new FileNotFoundException("Dalamud.dll을 찾지 못했습니다.", dalamudPath);
        }

        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(hookRoot);
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
                $"공식 Dalamud와 FFXIVClientStructs 사이에 누락된 참조가 {missing.Count}개 있습니다.\n" +
                string.Join(Environment.NewLine, missing.Take(10)));
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
            throw new FileNotFoundException(
                "version.json을 찾지 못해 공식 지원 게임 버전을 확인할 수 없습니다.",
                versionPath);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(versionPath));
        var supportedVersion = document.RootElement.EnumerateObject()
            .FirstOrDefault(property =>
                property.Name.Equals("supportedGameVer", StringComparison.OrdinalIgnoreCase))
            .Value.GetString();
        if (!SupportsGameVersion(supportedVersion))
        {
            throw new InvalidDataException(
                $"아직 검증되지 않은 게임 버전입니다.\n" +
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
        var key = hashes.Keys.SingleOrDefault(candidate =>
            candidate.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"hashes.json에 {fileName} 항목이 없습니다.");
        using var stream = File.OpenRead(assemblyPath);
        hashes[key] = Convert.ToHexString(MD5.HashData(stream));
        File.WriteAllText(
            hashesPath,
            JsonSerializer.Serialize(hashes, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteMarker(string hookRoot)
    {
        var marker = new
        {
            Patch = "Dalamud KR Stable Compatibility",
            Version = 3,
            AppliedAtUtc = DateTimeOffset.UtcNow,
            SupportedGameVersion,
            OfficialClientStructsFileVersion,
            OfficialClientStructsSha256,
            CompatibleClientStructsFileVersion,
            CompatibleClientStructsSha256,
            SignaturePatch = "KR 7.55 ClientState",
            LanguagePatch = "KR ClientLanguage",
        };
        File.WriteAllText(
            Path.Combine(hookRoot, "Dalamud.KR.Compatibility.Patch.json"),
            JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
    }
}
