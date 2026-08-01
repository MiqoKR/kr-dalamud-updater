using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KrDalamudUpdaterGui;

internal static class SimpleHeelsStabilityGuard
{
    private const string SupportedVersion = "0.11.1.7";
    private const string PreferenceListType = "System.Collections.Generic.List`1[[Dalamud.Configuration.Internal.AutoUpdatePreference, Dalamud]], System.Private.CoreLib";
    private const string PreferenceType = "Dalamud.Configuration.Internal.AutoUpdatePreference, Dalamud";

    public static bool Protect(DalamudLayout layout)
    {
        var pluginDirectory = Path.Combine(layout.PluginRoot, "SimpleHeels", SupportedVersion);
        var pluginPath = Path.Combine(pluginDirectory, "SimpleHeels.dll");
        var manifestPath = Path.Combine(pluginDirectory, "SimpleHeels.json");
        var markerPath = Path.Combine(pluginDirectory, "SimpleHeels.KR.Stability.Patch.json");

        if (!Directory.Exists(pluginDirectory))
        {
            return false;
        }

        if (!File.Exists(pluginPath) || !File.Exists(manifestPath) || !File.Exists(markerPath))
        {
            throw new InvalidDataException(
                $"Simple Heels {SupportedVersion} KR 안정화 파일이 불완전합니다. SimpleHeels KR 패처를 다시 실행해 주세요.");
        }

        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject
                       ?? throw new InvalidDataException("Simple Heels manifest를 읽을 수 없습니다.");
        var version = manifest["AssemblyVersion"]?.GetValue<string>();
        var workingPluginIdText = manifest["WorkingPluginId"]?.GetValue<string>();
        if (!string.Equals(version, SupportedVersion, StringComparison.Ordinal) ||
            !Guid.TryParse(workingPluginIdText, out var workingPluginId))
        {
            throw new InvalidDataException("Simple Heels 안정판의 버전 또는 WorkingPluginId가 올바르지 않습니다.");
        }

        var marker = JsonNode.Parse(File.ReadAllText(markerPath)) as JsonObject
                     ?? throw new InvalidDataException("Simple Heels KR 안정화 마커를 읽을 수 없습니다.");
        var patchedHash = marker["PatchedSha256"]?.GetValue<string>();
        var markerVersion = marker["SimpleHeelsVersion"]?.GetValue<string>();
        if (!string.Equals(markerVersion, SupportedVersion, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(patchedHash))
        {
            throw new InvalidDataException("Simple Heels KR 안정화 마커의 버전 또는 해시가 올바르지 않습니다.");
        }

        var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pluginPath)));
        if (!string.Equals(actualHash, patchedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Simple Heels {SupportedVersion} DLL이 검증된 KR 안정판과 다릅니다. SimpleHeels KR 패처를 다시 실행해 주세요.");
        }

        var config = JsonNode.Parse(
                         File.ReadAllText(layout.ConfigPath),
                         new JsonNodeOptions { PropertyNameCaseInsensitive = false },
                         new JsonDocumentOptions
                         {
                             AllowTrailingCommas = true,
                             CommentHandling = JsonCommentHandling.Skip,
                         }) as JsonObject
                     ?? throw new InvalidDataException("Dalamud 설정을 읽을 수 없습니다.");

        var preferences = EnsurePreferences(config);
        var values = (JsonArray)preferences["$values"]!;
        var matches = new List<int>();
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is not JsonObject preference ||
                !Guid.TryParse(preference["WorkingPluginId"]?.GetValue<string>(), out var existingId) ||
                existingId != workingPluginId)
            {
                continue;
            }

            preference["$type"] ??= PreferenceType;
            preference["Kind"] = 0;
            matches.Add(index);
        }

        if (matches.Count == 0)
        {
            values.Add(new JsonObject
            {
                ["$type"] = PreferenceType,
                ["WorkingPluginId"] = workingPluginId.ToString(),
                ["Kind"] = 0,
            });
        }
        else
        {
            for (var index = matches.Count - 1; index >= 1; index--)
            {
                values.RemoveAt(matches[index]);
            }
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var temporaryPath = layout.ConfigPath + ".simpleheels.tmp";
        File.WriteAllText(temporaryPath, config.ToJsonString(options));
        File.Move(temporaryPath, layout.ConfigPath, true);
        return true;
    }

    private static JsonObject EnsurePreferences(JsonObject config)
    {
        if (config["PluginAutoUpdatePreferences"] is not JsonObject preferences)
        {
            preferences = new JsonObject
            {
                ["$type"] = PreferenceListType,
                ["$values"] = new JsonArray(),
            };
            config["PluginAutoUpdatePreferences"] = preferences;
        }

        preferences["$type"] ??= PreferenceListType;
        if (preferences["$values"] is not JsonArray)
        {
            preferences["$values"] = new JsonArray();
        }

        return preferences;
    }
}
