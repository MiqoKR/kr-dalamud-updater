using KrDalamudUpdaterGui;

var testRoot = Path.Combine(Path.GetTempPath(), "KrDalamudUpdaterSafetyTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);

try
{
    TestMissingProfileInitialization();
    TestExistingProfilePreservation();
    TestOwnedIncompleteRepair();
    TestReplacementRollbackOnPostActivationFailure();
    TestCommittedReplacementPreservesPlugins();
    TestAtomicFileBackup();
    Console.WriteLine("PASS: all profile safety scenarios");
    return 0;
}
finally
{
    if (Directory.Exists(testRoot))
    {
        Directory.Delete(testRoot, recursive: true);
    }
}

void TestMissingProfileInitialization()
{
    var root = Path.Combine(testRoot, "missing-profile");
    var result = SafeProfileManager.Prepare(root, true, false, "", true);
    Assert(result.InitialState == ProfileState.Missing, "missing profile state");
    Assert(result.CurrentState == ProfileState.OwnedReady, "initialized profile state");
    Assert(File.Exists(Path.Combine(root, SafeProfileManager.MarkerFileName)), "ownership marker");
    Assert(File.ReadAllText(Path.Combine(root, "dalamudConfig.json")).Trim() == "{}", "minimum config");
}

void TestExistingProfilePreservation()
{
    var root = Path.Combine(testRoot, "existing-profile");
    var pluginRoot = Path.Combine(root, "installedPlugins", "KeepPlugin");
    Directory.CreateDirectory(pluginRoot);
    var configPath = Path.Combine(root, "dalamudConfig.json");
    var pluginPath = Path.Combine(pluginRoot, "keep.txt");
    const string config = "{\"KeepSetting\":true,\"ThirdPartyRepoList\":[\"https://example.invalid/repo.json\"]}";
    File.WriteAllText(configPath, config);
    File.WriteAllText(pluginPath, "keep-plugin");

    var result = SafeProfileManager.Prepare(root, true, false, "", true);
    Assert(result.InitialState == ProfileState.ExistingUnmanaged, "existing unmanaged state");
    Assert(!result.Initialized, "existing profile must not be initialized");
    Assert(File.ReadAllText(configPath) == config, "existing config preserved");
    Assert(File.ReadAllText(pluginPath) == "keep-plugin", "existing plugin preserved");
    Assert(!File.Exists(Path.Combine(root, SafeProfileManager.MarkerFileName)), "no ownership claim");
}

void TestOwnedIncompleteRepair()
{
    var root = Path.Combine(testRoot, "owned-incomplete");
    Directory.CreateDirectory(root);
    File.WriteAllText(
        Path.Combine(root, SafeProfileManager.MarkerFileName),
        "{\"Owner\":\"KR-Dalamud-Updater\",\"SchemaVersion\":1}");
    var configPath = Path.Combine(root, "dalamudConfig.json");
    const string config = "{\"KeepSetting\":true}";
    File.WriteAllText(configPath, config);

    var result = SafeProfileManager.Prepare(root, true, false, "", true);
    Assert(result.InitialState == ProfileState.OwnedIncomplete, "owned incomplete state");
    Assert(result.CurrentState == ProfileState.OwnedReady, "owned profile repaired");
    Assert(File.ReadAllText(configPath) == config, "owned config preserved");
}

void TestReplacementRollbackOnPostActivationFailure()
{
    var root = Path.Combine(testRoot, "replacement-rollback");
    var target = Path.Combine(root, "addon", "Hooks", "15.0.3.2");
    var prepared = Path.Combine(testRoot, "prepared-failure");
    Directory.CreateDirectory(target);
    Directory.CreateDirectory(prepared);
    File.WriteAllText(Path.Combine(target, "old.txt"), "old");
    File.WriteAllText(Path.Combine(prepared, "new.txt"), "new");
    var verificationCount = 0;

    try
    {
        using var replacement = DirectoryReplacement.Activate(
            root,
            prepared,
            target,
            "Hooks",
            "15.0.3.2",
            path =>
            {
                verificationCount++;
                Assert(File.Exists(Path.Combine(path, "new.txt")), "staged replacement exists");
                if (verificationCount == 2)
                {
                    throw new InvalidDataException("simulated final verification failure");
                }
            });
        throw new InvalidOperationException("replacement should have failed");
    }
    catch (InvalidDataException)
    {
        // Expected: failure after activation must restore the old target.
    }

    Assert(File.ReadAllText(Path.Combine(target, "old.txt")) == "old", "old target restored");
    Assert(!File.Exists(Path.Combine(target, "new.txt")), "failed target removed");
}

void TestCommittedReplacementPreservesPlugins()
{
    var root = Path.Combine(testRoot, "replacement-commit");
    var target = Path.Combine(root, "dalamudAssets", "437");
    var prepared = Path.Combine(testRoot, "prepared-success");
    var pluginPath = Path.Combine(root, "installedPlugins", "KeepPlugin", "keep.txt");
    Directory.CreateDirectory(target);
    Directory.CreateDirectory(prepared);
    Directory.CreateDirectory(Path.GetDirectoryName(pluginPath)!);
    File.WriteAllText(Path.Combine(target, "old.txt"), "old");
    File.WriteAllText(Path.Combine(prepared, "new.txt"), "new");
    File.WriteAllText(pluginPath, "keep-plugin");

    using (var replacement = DirectoryReplacement.Activate(
               root,
               prepared,
               target,
               "Assets",
               "437",
               path => Assert(File.Exists(Path.Combine(path, "new.txt")), "replacement verified")))
    {
        replacement.Commit();
    }

    Assert(File.ReadAllText(Path.Combine(target, "new.txt")) == "new", "new target committed");
    Assert(File.ReadAllText(pluginPath) == "keep-plugin", "unrelated plugin preserved");
}

void TestAtomicFileBackup()
{
    var root = Path.Combine(testRoot, "atomic-file");
    var path = Path.Combine(root, "settings.json");
    var backup = Path.Combine(root, "backup", "settings.json.bak");
    Directory.CreateDirectory(root);
    File.WriteAllText(path, "old");
    AtomicFile.WriteAllText(path, "new", backup);
    Assert(File.ReadAllText(path) == "new", "atomic destination");
    Assert(File.ReadAllText(backup) == "old", "atomic backup");
}

static void Assert(bool condition, string scenario)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {scenario}");
    }
}
