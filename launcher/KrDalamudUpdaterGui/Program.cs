using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace KrDalamudUpdaterGui;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0].Equals("--patch-hook", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                DalamudKrCompatibilityPatch.Apply(args[1]);
                Console.WriteLine("Dalamud KR compatibility patch applied and verified.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        if (OperatingSystem.IsWindows() &&
            !args.Contains("--no-elevate", StringComparer.OrdinalIgnoreCase) &&
            !IsAdministrator())
        {
            RelaunchElevated(args);
            return 0;
        }

        using var instanceMutex = new Mutex(true, "Local\\KrDalamudUpdaterGui.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Dalamud Updater is already running in the system tray.",
                "Dalamud Updater",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new UpdaterForm(args));
        return 0;
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RelaunchElevated(string[] args)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            MessageBox.Show("실행 파일 경로를 확인하지 못했습니다.", "달라무드 업데이터", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = string.Join(" ", args.Select(Quote)),
            WorkingDirectory = Directory.GetCurrentDirectory(),
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            Process.Start(startInfo);
        }
        catch
        {
            MessageBox.Show("관리자 권한 실행이 취소되었습니다.", "달라무드 업데이터", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string Quote(string value)
        => value.Contains(' ') ? "\"" + value.Replace("\"", "\\\"") + "\"" : value;
}

internal sealed class UpdaterForm : Form
{
    private const string ReleaseInfoUrl = "https://kamori.goats.dev/Dalamud/Release/VersionInfo?track=release&bucket=Control";
    private const string AssetMetaUrl = "https://kamori.goats.dev/Dalamud/Asset/Meta";
    private static readonly HttpClient UpdateClient = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static readonly JsonSerializerOptions WebJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly UpdaterSettings settings;
    private readonly System.Windows.Forms.Timer monitorTimer = new();
    private readonly NotifyIcon notifyIcon = new();
    private readonly Icon trayIcon;
    private readonly Label dalamudVersionLabel = new();
    private readonly Button checkUpdateButton = new();
    private readonly ComboBox hookCombo = new();
    private readonly CheckBox autoStartCheck = new();
    private readonly CheckBox autoApplyCheck = new();
    private readonly CheckBox disablePluginsCheck = new();
    private readonly CheckBox disableCustomRepoCheck = new();
    private readonly NumericUpDown delayInput = new();
    private readonly Button applyButton = new();
    private readonly Label updaterVersionLabel = new();
    private readonly LinkLabel discordLink = new();
    private readonly ProgressBar progressBar = new();
    private readonly Label statusLabel = new();

    private int? pendingPid;
    private DateTime pendingApplyAt;
    private bool applying;
    private bool exiting;

    public UpdaterForm(string[] args)
    {
        settings = UpdaterSettings.Load();
        ApplyArgs(args);
        ResolveInstalledHookVersion();

        trayIcon = LoadApplicationIcon();
        BuildUi();
        BuildTray();
        LoadHookVersions();
        ApplySettingsToUi();

        monitorTimer.Interval = 1000;
        monitorTimer.Tick += (_, _) => MonitorTick();
        if (settings.AutoStart)
        {
            monitorTimer.Start();
            SetStatus("게임 프로세스를 기다리는 중...");
        }
        else
        {
            SetStatus("대기 중");
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            notifyIcon.Visible = true;
            notifyIcon.ShowBalloonTip(1500, "달라무드 업데이터", "트레이에서 계속 실행 중입니다.", ToolTipIcon.Info);
            return;
        }

        SaveSettingsFromUi();
        settings.Save();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        trayIcon.Dispose();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        var accentColor = Color.FromArgb(25, 128, 104);
        var accentHoverColor = Color.FromArgb(20, 109, 88);
        var headerColor = Color.FromArgb(43, 47, 54);
        var surfaceColor = Color.White;
        var canvasColor = Color.FromArgb(245, 246, 248);
        var primaryTextColor = Color.FromArgb(36, 39, 44);
        var secondaryTextColor = Color.FromArgb(103, 109, 118);
        var borderColor = Color.FromArgb(218, 222, 228);
        var updaterVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "확인 불가";

        Text = "KR Dalamud Updater";
        ClientSize = new Size(460, 590);
        MinimumSize = new Size(476, 629);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        BackColor = canvasColor;
        Icon = trayIcon;

        var header = new Panel
        {
            BackColor = headerColor,
            Dock = DockStyle.Top,
            Height = 88,
        };
        var titleLabel = new Label
        {
            Text = "KR Dalamud Updater",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 16F),
            Location = new Point(20, 14),
            AutoSize = true,
        };
        var subtitleLabel = new Label
        {
            Text = "한국 서버용 Dalamud 실행 및 호환성 관리",
            ForeColor = Color.FromArgb(193, 199, 207),
            Font = new Font("Segoe UI", 9F),
            Location = new Point(22, 51),
            AutoSize = true,
        };
        var versionBadge = new Panel
        {
            BackColor = Color.FromArgb(61, 66, 74),
            Location = new Point(344, 25),
            Size = new Size(92, 31),
        };
        var versionBadgeLabel = new Label
        {
            Text = $"v{updaterVersion}",
            ForeColor = Color.FromArgb(219, 224, 230),
            Font = new Font("Segoe UI Semibold", 8.5F),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
        };
        versionBadge.Controls.Add(versionBadgeLabel);
        header.Controls.Add(titleLabel);
        header.Controls.Add(subtitleLabel);
        header.Controls.Add(versionBadge);
        Controls.Add(header);

        var versionCard = new Panel
        {
            BackColor = surfaceColor,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(18, 106),
            Size = new Size(424, 126),
        };
        var versionSectionLabel = new Label
        {
            Text = "DALAMUD CORE",
            ForeColor = secondaryTextColor,
            Font = new Font("Segoe UI Semibold", 8F),
            Location = new Point(15, 11),
            AutoSize = true,
        };
        dalamudVersionLabel.SetBounds(15, 32, 390, 28);
        dalamudVersionLabel.Text = $"설치 버전  {settings.HookVersion}";
        dalamudVersionLabel.ForeColor = primaryTextColor;
        dalamudVersionLabel.Font = new Font("Segoe UI Semibold", 13F);
        versionCard.Controls.Add(versionSectionLabel);
        versionCard.Controls.Add(dalamudVersionLabel);

        checkUpdateButton.SetBounds(224, 65, 180, 34);
        checkUpdateButton.Text = "업데이트 확인";
        checkUpdateButton.BackColor = surfaceColor;
        checkUpdateButton.ForeColor = accentColor;
        checkUpdateButton.Font = new Font("Segoe UI Semibold", 9F);
        checkUpdateButton.FlatStyle = FlatStyle.Flat;
        checkUpdateButton.FlatAppearance.BorderColor = accentColor;
        checkUpdateButton.FlatAppearance.BorderSize = 1;
        checkUpdateButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 247, 243);
        checkUpdateButton.Click += async (_, _) => await CheckUpdateAsync();
        versionCard.Controls.Add(checkUpdateButton);

        hookCombo.SetBounds(15, 66, 199, 30);
        hookCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        hookCombo.FlatStyle = FlatStyle.Flat;
        hookCombo.Font = new Font("Segoe UI", 9.5F);
        hookCombo.SelectedIndexChanged += (_, _) =>
        {
            if (hookCombo.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected))
            {
                settings.HookVersion = selected;
                dalamudVersionLabel.Text = $"설치 버전  {settings.HookVersion}";
                settings.Save();
            }
        };
        versionCard.Controls.Add(hookCombo);
        var versionHintLabel = new Label
        {
            Text = "공식 Stable 패키지 · KR 호환성 검증",
            ForeColor = secondaryTextColor,
            Location = new Point(16, 101),
            Size = new Size(388, 18),
        };
        versionCard.Controls.Add(versionHintLabel);
        Controls.Add(versionCard);

        var settingsCard = new Panel
        {
            BackColor = surfaceColor,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(18, 246),
            Size = new Size(424, 166),
        };
        var settingsSectionLabel = new Label
        {
            Text = "실행 설정",
            ForeColor = primaryTextColor,
            Font = new Font("Segoe UI Semibold", 10F),
            Location = new Point(15, 12),
            AutoSize = true,
        };
        settingsCard.Controls.Add(settingsSectionLabel);

        autoStartCheck.SetBounds(15, 42, 175, 24);
        autoStartCheck.Text = "자동 시작";
        autoStartCheck.FlatStyle = FlatStyle.Flat;
        autoStartCheck.ForeColor = primaryTextColor;
        autoStartCheck.CheckedChanged += (_, _) =>
        {
            settings.AutoStart = autoStartCheck.Checked;
            if (settings.AutoStart)
            {
                monitorTimer.Start();
                SetStatus("게임 프로세스를 기다리는 중...");
            }
            else
            {
                monitorTimer.Stop();
                ClearPending();
                SetStatus("자동 시작 중지됨");
            }

            settings.Save();
        };
        settingsCard.Controls.Add(autoStartCheck);

        autoApplyCheck.SetBounds(218, 42, 175, 24);
        autoApplyCheck.Text = "자동 적용";
        autoApplyCheck.FlatStyle = FlatStyle.Flat;
        autoApplyCheck.ForeColor = primaryTextColor;
        autoApplyCheck.CheckedChanged += (_, _) =>
        {
            settings.AutoApply = autoApplyCheck.Checked;
            settings.Save();
        };
        settingsCard.Controls.Add(autoApplyCheck);

        disablePluginsCheck.SetBounds(15, 70, 175, 24);
        disablePluginsCheck.Text = "모드 플러그인 비활성화";
        disablePluginsCheck.FlatStyle = FlatStyle.Flat;
        disablePluginsCheck.ForeColor = primaryTextColor;
        disablePluginsCheck.CheckedChanged += (_, _) =>
        {
            settings.DisablePlugins = disablePluginsCheck.Checked;
            settings.Save();
        };
        settingsCard.Controls.Add(disablePluginsCheck);

        disableCustomRepoCheck.SetBounds(218, 70, 175, 24);
        disableCustomRepoCheck.Text = "커스텀 리포 비활성화";
        disableCustomRepoCheck.FlatStyle = FlatStyle.Flat;
        disableCustomRepoCheck.ForeColor = primaryTextColor;
        disableCustomRepoCheck.CheckedChanged += (_, _) =>
        {
            settings.DisableCustomRepoPlugins = disableCustomRepoCheck.Checked;
            settings.Save();
        };
        settingsCard.Controls.Add(disableCustomRepoCheck);

        var divider = new Panel
        {
            BackColor = borderColor,
            Location = new Point(15, 103),
            Size = new Size(389, 1),
        };
        settingsCard.Controls.Add(divider);

        var delayLabel = new Label
        {
            Text = "적용 딜레이",
            ForeColor = primaryTextColor,
            Location = new Point(15, 116),
            Size = new Size(72, 24),
        };
        settingsCard.Controls.Add(delayLabel);

        delayInput.SetBounds(91, 112, 58, 26);
        delayInput.Minimum = 0;
        delayInput.Maximum = 30;
        delayInput.ValueChanged += (_, _) =>
        {
            settings.DelaySeconds = (int)delayInput.Value;
            settings.Save();
        };
        settingsCard.Controls.Add(delayInput);

        var secondsLabel = new Label
        {
            Text = "초",
            ForeColor = primaryTextColor,
            Location = new Point(155, 116),
            Size = new Size(22, 24),
        };
        settingsCard.Controls.Add(secondsLabel);

        var warningLabel = new Label
        {
            Text = "검은 화면이 지속되면 딜레이를 늘려주세요.",
            ForeColor = Color.FromArgb(190, 67, 67),
            Location = new Point(15, 143),
            Size = new Size(389, 18),
        };
        settingsCard.Controls.Add(warningLabel);
        Controls.Add(settingsCard);

        applyButton.SetBounds(18, 430, 424, 58);
        applyButton.Text = "달라무드 적용";
        applyButton.BackColor = accentColor;
        applyButton.ForeColor = Color.White;
        applyButton.Font = new Font("Segoe UI Semibold", 11F);
        applyButton.FlatStyle = FlatStyle.Flat;
        applyButton.FlatAppearance.BorderSize = 0;
        applyButton.FlatAppearance.MouseOverBackColor = accentHoverColor;
        applyButton.Cursor = Cursors.Hand;
        applyButton.Click += async (_, _) => await ApplyDalamudAsync(manual: true, cleanOverride: null);
        Controls.Add(applyButton);

        var footerCard = new Panel
        {
            BackColor = surfaceColor,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(18, 506),
            Size = new Size(424, 68),
        };
        statusLabel.SetBounds(14, 9, 394, 22);
        statusLabel.ForeColor = secondaryTextColor;
        statusLabel.AutoEllipsis = true;
        statusLabel.Text = "";
        footerCard.Controls.Add(statusLabel);

        updaterVersionLabel.SetBounds(14, 38, 190, 18);
        updaterVersionLabel.Text = $"업데이터 버전 : {updaterVersion}";
        updaterVersionLabel.ForeColor = secondaryTextColor;
        footerCard.Controls.Add(updaterVersionLabel);

        discordLink.SetBounds(289, 38, 119, 18);
        discordLink.Text = "Discord 커뮤니티";
        discordLink.TextAlign = ContentAlignment.MiddleRight;
        discordLink.LinkColor = accentColor;
        discordLink.ActiveLinkColor = accentHoverColor;
        discordLink.VisitedLinkColor = accentColor;
        discordLink.LinkBehavior = LinkBehavior.HoverUnderline;
        discordLink.Cursor = Cursors.Hand;
        discordLink.LinkClicked += (_, _) => OpenUrl("https://discord.gg/Me3sJ2CXp");
        footerCard.Controls.Add(discordLink);

        progressBar.SetBounds(0, 62, 422, 5);
        progressBar.Visible = false;
        footerCard.Controls.Add(progressBar);
        Controls.Add(footerCard);
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("열기", null, (_, _) => ShowFromTray());
        menu.Items.Add("달라무드 적용", null, async (_, _) => await ApplyDalamudAsync(manual: true, cleanOverride: null));
        menu.Items.Add("클린 적용", null, async (_, _) => await ApplyDalamudAsync(manual: true, cleanOverride: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) =>
        {
            exiting = true;
            Close();
        });

        notifyIcon.Icon = trayIcon;
        notifyIcon.Text = "달라무드 업데이터";
        notifyIcon.ContextMenuStrip = menu;
        notifyIcon.Visible = true;
        notifyIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            Hide();
            notifyIcon.Visible = true;
        }
    }

    private void ApplySettingsToUi()
    {
        autoStartCheck.Checked = settings.AutoStart;
        autoApplyCheck.Checked = settings.AutoApply;
        disablePluginsCheck.Checked = settings.DisablePlugins;
        disableCustomRepoCheck.Checked = settings.DisableCustomRepoPlugins;
        delayInput.Value = Math.Clamp(settings.DelaySeconds, 0, 30);
    }

    private void LoadHookVersions()
    {
        hookCombo.Items.Clear();
        var hooksRoot = Path.Combine(ExpandPath(settings.ProfileRoot), "addon", "Hooks");
        if (Directory.Exists(hooksRoot))
        {
            foreach (var dir in Directory.GetDirectories(hooksRoot).Select(Path.GetFileName).Where(name => !string.IsNullOrWhiteSpace(name)).OrderDescending())
            {
                hookCombo.Items.Add(dir!);
            }
        }

        if (!hookCombo.Items.Contains(settings.HookVersion))
        {
            hookCombo.Items.Add(settings.HookVersion);
        }

        hookCombo.SelectedItem = settings.HookVersion;
    }

    private void ResolveInstalledHookVersion()
    {
        var profileRoot = ExpandPath(settings.ProfileRoot);
        var configuredInjector = Path.Combine(
            profileRoot,
            "addon",
            "Hooks",
            settings.HookVersion,
            "Dalamud.Injector.dll");
        if (File.Exists(configuredInjector))
        {
            return;
        }

        var hooksRoot = Path.Combine(profileRoot, "addon", "Hooks");
        if (!Directory.Exists(hooksRoot))
        {
            return;
        }

        var candidates = Directory.GetDirectories(hooksRoot)
            .Where(dir => File.Exists(Path.Combine(dir, "Dalamud.Injector.dll")))
            .Select(dir => new
            {
                Name = Path.GetFileName(dir),
                Version = Version.TryParse(Path.GetFileName(dir), out var parsed) ? parsed : null,
            })
            .ToArray();

        var selected = candidates
            .Where(candidate => candidate.Version is not null)
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => candidate.Name)
            .FirstOrDefault()
            ?? candidates
                .OrderByDescending(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .Select(candidate => candidate.Name)
                .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        settings.HookVersion = selected;
        settings.Save();
    }

    private async Task CheckUpdateAsync()
    {
        SaveSettingsFromUi();
        SetBusy(true, "Checking Update...");
        try
        {
            if (FindTarget() is not null)
            {
                SetStatus("게임 종료 필요");
                MessageBox.Show("게임을 완전히 종료한 뒤 업데이트해 주세요.", "달라무드 업데이터", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetStatus("공식 버전 확인 중...");
            var releaseJson = await UpdateClient.GetStringAsync(ReleaseInfoUrl);
            var release = JsonSerializer.Deserialize<DalamudReleaseInfo>(releaseJson, WebJsonOptions)
                ?? throw new InvalidDataException("공식 Dalamud 버전 정보를 읽지 못했습니다.");
            if (string.IsNullOrWhiteSpace(release.AssemblyVersion) || string.IsNullOrWhiteSpace(release.DownloadUrl))
            {
                throw new InvalidDataException("공식 Dalamud 버전 정보에 필수 항목이 없습니다.");
            }

            var profileRoot = ExpandPath(settings.ProfileRoot);
            var current = TryLoadReleaseInfo(Path.Combine(profileRoot, "addon", "Hooks", settings.HookVersion, "version.json"));
            if (current is not null &&
                !string.IsNullOrWhiteSpace(current.SupportedGameVer) &&
                !string.Equals(current.SupportedGameVer, release.SupportedGameVer, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("게임 버전 변경 감지");
                MessageBox.Show(
                    $"공식 지원 게임 버전이 변경되었습니다.\n\n현재: {current.SupportedGameVer}\n공식: {release.SupportedGameVer}\n\n한섭 호환성을 먼저 확인해야 하므로 자동 업데이트를 중단합니다.",
                    "달라무드 업데이터",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var localRuntimeVersion = ReadTrimmed(Path.Combine(profileRoot, "runtime", "version"));
            if (!string.Equals(localRuntimeVersion, release.RuntimeVersion, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("런타임 업데이트 필요");
                MessageBox.Show(
                    $"Dalamud 런타임 업데이트가 필요합니다.\n\n현재: {localRuntimeVersion ?? "없음"}\n공식: {release.RuntimeVersion}\n\n런타임은 별도 검증 후 갱신해야 하므로 자동 업데이트를 중단합니다.",
                    "달라무드 업데이터",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var assetJson = await UpdateClient.GetStringAsync(AssetMetaUrl);
            var assets = JsonSerializer.Deserialize<DalamudAssetInfo>(assetJson, WebJsonOptions)
                ?? throw new InvalidDataException("공식 Dalamud 자산 정보를 읽지 못했습니다.");

            var hookInstalled = IsHookInstalled(profileRoot, release.AssemblyVersion);
            var assetsInstalled = IsAssetInstalled(profileRoot, assets.Version);
            if (hookInstalled && assetsInstalled)
            {
                SelectHookVersion(release.AssemblyVersion);
                SetStatus($"최신 버전 {release.AssemblyVersion}");
                MessageBox.Show($"이미 최신 Stable 버전입니다.\n\nDalamud {release.AssemblyVersion}\nAssets {assets.Version}", "달라무드 업데이터", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var answer = MessageBox.Show(
                $"공식 Stable 업데이트를 설치하시겠습니까?\n\nDalamud: {settings.HookVersion} -> {release.AssemblyVersion}\nAssets: {ReadTrimmed(Path.Combine(profileRoot, "dalamudAssets", "asset.ver")) ?? "없음"} -> {assets.Version}\n\n기존 버전은 삭제하지 않습니다.",
                "달라무드 업데이트",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                SetStatus("업데이트 취소됨");
                return;
            }

            if (!hookInstalled)
            {
                SetStatus($"Dalamud {release.AssemblyVersion} 다운로드 중...");
                await InstallHookAsync(profileRoot, release, releaseJson);
            }

            if (!assetsInstalled)
            {
                SetStatus($"Assets {assets.Version} 다운로드 중...");
                await InstallAssetsAsync(profileRoot, assets);
            }

            SelectHookVersion(release.AssemblyVersion);
            SetStatus($"업데이트 완료 {release.AssemblyVersion}");
            MessageBox.Show(
                $"업데이트가 완료되었습니다.\n\nDalamud {release.AssemblyVersion}\nAssets {assets.Version}\n\n기존 Dalamud 버전은 복구용으로 유지했습니다.",
                "달라무드 업데이터",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetStatus("업데이트 확인 실패");
            MessageBox.Show(ex.Message, "달라무드 업데이터", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, "Checking Update...");
        }
    }

    private void SelectHookVersion(string version)
    {
        settings.HookVersion = version;
        settings.Save();
        LoadHookVersions();
        hookCombo.SelectedItem = version;
        dalamudVersionLabel.Text = $"설치 버전  {version}";
    }

    private static DalamudReleaseInfo? TryLoadReleaseInfo(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<DalamudReleaseInfo>(File.ReadAllText(path), WebJsonOptions)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadTrimmed(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsHookInstalled(string profileRoot, string version)
    {
        var root = Path.Combine(profileRoot, "addon", "Hooks", version);
        try
        {
            DalamudKrCompatibilityPatch.Verify(root);
            VerifyHookPackage(root);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAssetInstalled(string profileRoot, int version)
    {
        var localVersion = ReadTrimmed(Path.Combine(profileRoot, "dalamudAssets", "asset.ver"));
        var root = Path.Combine(profileRoot, "dalamudAssets", version.ToString());
        return localVersion == version.ToString() &&
               Directory.Exists(root) &&
               File.Exists(Path.Combine(root, "UIRes", "logo.png"));
    }

    private async Task InstallHookAsync(string profileRoot, DalamudReleaseInfo release, string releaseJson)
    {
        var tempRoot = CreateTemporaryDirectory("hook");
        try
        {
            var archivePath = Path.Combine(tempRoot, "dalamud.zip");
            var extractedPath = Path.Combine(tempRoot, "extracted");
            await DownloadFileAsync(release.DownloadUrl, archivePath);
            ZipFile.ExtractToDirectory(archivePath, extractedPath);
            VerifyHookPackage(extractedPath);
            File.WriteAllText(Path.Combine(extractedPath, "version.json"), releaseJson);
            DalamudKrCompatibilityPatch.Apply(extractedPath);
            VerifyHookPackage(extractedPath);

            var hooksRoot = Path.Combine(profileRoot, "addon", "Hooks");
            Directory.CreateDirectory(hooksRoot);
            var target = Path.Combine(hooksRoot, release.AssemblyVersion);
            BackupExistingDirectory(profileRoot, target, "Hooks", release.AssemblyVersion);
            Directory.Move(extractedPath, target);
            VerifyHookPackage(target);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private async Task InstallAssetsAsync(string profileRoot, DalamudAssetInfo assets)
    {
        if (string.IsNullOrWhiteSpace(assets.PackageUrl))
        {
            throw new InvalidDataException("공식 자산 패키지 주소가 없습니다.");
        }

        var tempRoot = CreateTemporaryDirectory("assets");
        try
        {
            var archivePath = Path.Combine(tempRoot, "assets.zip");
            var extractedPath = Path.Combine(tempRoot, "extracted");
            await DownloadFileAsync(assets.PackageUrl, archivePath);
            ZipFile.ExtractToDirectory(archivePath, extractedPath);
            VerifyAssetPackage(extractedPath, assets);

            var assetsRoot = Path.Combine(profileRoot, "dalamudAssets");
            Directory.CreateDirectory(assetsRoot);
            var target = Path.Combine(assetsRoot, assets.Version.ToString());
            BackupExistingDirectory(profileRoot, target, "Assets", assets.Version.ToString());
            Directory.Move(extractedPath, target);
            VerifyAssetPackage(target, assets);
            File.WriteAllText(Path.Combine(assetsRoot, "asset.ver"), assets.Version.ToString());
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static async Task DownloadFileAsync(string url, string destination)
    {
        using var response = await UpdateClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(target);
    }

    private static void VerifyHookPackage(string root)
    {
        foreach (var required in new[] { "Dalamud.dll", "Dalamud.Injector.dll", "Dalamud.Injector.exe", "hashes.json" })
        {
            if (!File.Exists(Path.Combine(root, required)))
            {
                throw new InvalidDataException($"Dalamud 패키지 필수 파일이 없습니다: {required}");
            }
        }

        var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "hashes.json")))
            ?? throw new InvalidDataException("Dalamud hashes.json을 읽지 못했습니다.");
        foreach (var entry in hashes)
        {
            var path = ResolvePackagePath(root, entry.Key);
            if (!File.Exists(path))
            {
                throw new InvalidDataException($"Dalamud 패키지 파일이 없습니다: {entry.Key}");
            }

            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(MD5.HashData(stream));
            if (!actual.Equals(entry.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Dalamud 패키지 해시가 일치하지 않습니다: {entry.Key}");
            }
        }
    }

    private static void VerifyAssetPackage(string root, DalamudAssetInfo assets)
    {
        foreach (var asset in assets.Assets.Where(asset => !string.IsNullOrWhiteSpace(asset.Hash)))
        {
            var path = ResolvePackagePath(root, asset.FileName);
            if (!File.Exists(path))
            {
                throw new InvalidDataException($"Dalamud 자산 파일이 없습니다: {asset.FileName}");
            }

            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA1.HashData(stream));
            if (!actual.Equals(asset.Hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Dalamud 자산 해시가 일치하지 않습니다: {asset.FileName}");
            }
        }
    }

    private static string ResolvePackagePath(string root, string relativePath)
    {
        var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("패키지에 허용되지 않은 경로가 포함되어 있습니다.");
        }

        return fullPath;
    }

    private static string CreateTemporaryDirectory(string kind)
    {
        var root = Path.Combine(Path.GetTempPath(), "KrDalamudUpdater", $"{kind}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void BackupExistingDirectory(string profileRoot, string target, string kind, string version)
    {
        if (!Directory.Exists(target))
        {
            return;
        }

        var backupRoot = Path.Combine(profileRoot, "kr-dalamud-backups", kind);
        Directory.CreateDirectory(backupRoot);
        var backup = Path.Combine(backupRoot, $"{version}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.Move(target, backup);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Temporary files can be cleaned up on the next system cleanup.
        }
    }

    private void MonitorTick()
    {
        if (applying || !settings.AutoStart)
        {
            return;
        }

        var target = FindTarget();
        if (target is null)
        {
            ClearPending();
            SetStatus("게임 프로세스를 기다리는 중...");
            return;
        }

        if (IsDuplicateLaunch(DalamudLayout.FromSettings(settings), target))
        {
            ClearPending();
            SetStatus($"이미 적용됨: PID {target.ProcessId}");
            return;
        }

        if (!settings.AutoApply)
        {
            SetStatus($"게임 감지됨: PID {target.ProcessId}");
            return;
        }

        if (pendingPid != target.ProcessId)
        {
            pendingPid = target.ProcessId;
            pendingApplyAt = DateTime.Now.AddSeconds(settings.DelaySeconds);
        }

        var remaining = (int)Math.Ceiling((pendingApplyAt - DateTime.Now).TotalSeconds);
        if (remaining > 0)
        {
            SetStatus($"{remaining}초 후 자동 적용");
            return;
        }

        _ = ApplyDalamudAsync(manual: false, cleanOverride: null);
    }

    private async Task ApplyDalamudAsync(bool manual, bool? cleanOverride)
    {
        if (applying)
        {
            return;
        }

        SaveSettingsFromUi();
        var layout = DalamudLayout.FromSettings(settings);
        var missing = layout.GetMissingParts().ToArray();
        if (missing.Length > 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine, missing), "필수 파일 누락", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SetStatus("필수 파일 누락");
            return;
        }

        try
        {
            DalamudKrCompatibilityPatch.Verify(layout.HookRoot);
        }
        catch (Exception ex)
        {
            SetStatus("KR 호환 패치 필요");
            MessageBox.Show(
                $"선택한 Dalamud Hook의 KR 호환 패치 검증에 실패했습니다.\n\n{ex.Message}\n\n게임에 적용하지 않았습니다.",
                "달라무드 업데이터",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var target = FindTarget();
        if (target is null)
        {
            SetStatus("게임 프로세스 없음");
            if (manual)
            {
                MessageBox.Show("FFXIV 프로세스가 없습니다. 공식 한섭 런처로 게임을 먼저 실행해 주세요.", "달라무드 업데이터", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return;
        }

        if (IsDuplicateLaunch(layout, target))
        {
            SetStatus("이미 적용된 프로세스");
            if (manual)
            {
                MessageBox.Show("이 게임 프로세스에는 이미 적용 기록이 있습니다. 다시 적용하려면 게임을 재시작해 주세요.", "달라무드 업데이터", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return;
        }

        applying = true;
        applyButton.Enabled = false;
        SetBusy(true, "Applying...");
        try
        {
            var disablePlugins = cleanOverride ?? settings.DisablePlugins;
            var args = BuildInjectorArguments(layout, target, disablePlugins, settings.DisableCustomRepoPlugins);
            SetStatus($"적용 중: PID {target.ProcessId}");
            var exitCode = await RunInjectorAsync(layout.DotnetPath, args);
            if (exitCode == 0)
            {
                SaveLaunchState(layout, target, disablePlugins);
                SetStatus("적용 완료");
                notifyIcon.ShowBalloonTip(1200, "달라무드 업데이터", "달라무드 적용이 완료되었습니다.", ToolTipIcon.Info);
            }
            else
            {
                SetStatus($"적용 실패: {exitCode}");
                MessageBox.Show($"Dalamud Injector 종료 코드: {exitCode}", "달라무드 적용 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            SetStatus("적용 실패");
            MessageBox.Show(ex.Message, "달라무드 적용 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            ClearPending();
            SetBusy(false, "Applying...");
            applyButton.Enabled = true;
            applying = false;
        }
    }

    private static async Task<int> RunInjectorAsync(string dotnetPath, IReadOnlyList<string> args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = dotnetPath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static List<string> BuildInjectorArguments(DalamudLayout layout, TargetInfo target, bool disablePlugins, bool disableCustomRepoPlugins)
    {
        var args = new List<string>
        {
            layout.InjectorDllPath,
            "inject",
            "-v",
            target.ProcessId.ToString(),
            $"--dalamud-working-directory={layout.HookRoot}",
            $"--dalamud-configuration-path={layout.ConfigPath}",
            $"--dalamud-plugin-directory={layout.PluginRoot}",
            $"--dalamud-asset-directory={layout.AssetRoot}",
            "--dalamud-delay-initialize=1000",
            $"--logpath={layout.ProfileRoot}",
            "--logname=kr-gui",
            "--dalamud-client-language=6",
        };

        if (Directory.Exists(layout.DevPluginRoot))
        {
            args.Add($"--dalamud-dev-plugin-directory={layout.DevPluginRoot}");
        }

        if (disablePlugins)
        {
            args.Add("--no-plugin");
            args.Add("--no-3rd-plugin");
        }
        else if (disableCustomRepoPlugins)
        {
            args.Add("--no-3rd-plugin");
        }

        return args;
    }

    private static TargetInfo? FindTarget()
        => Process.GetProcesses()
            .Where(process => process.ProcessName.Equals("ffxiv_dx11", StringComparison.OrdinalIgnoreCase) ||
                              process.ProcessName.Equals("ffxiv", StringComparison.OrdinalIgnoreCase))
            .OrderBy(process => process.Id)
            .Select(TargetInfo.FromProcess)
            .FirstOrDefault();

    private static bool IsDuplicateLaunch(DalamudLayout layout, TargetInfo target)
    {
        try
        {
            if (!File.Exists(layout.StatePath))
            {
                return false;
            }

            var state = JsonSerializer.Deserialize<LaunchState>(File.ReadAllText(layout.StatePath));
            return state?.Targets.Any(entry =>
                entry.ProcessId == target.ProcessId &&
                !string.IsNullOrWhiteSpace(entry.StartTimeUtc) &&
                entry.StartTimeUtc == target.StartTimeUtc) == true;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveLaunchState(DalamudLayout layout, TargetInfo target, bool pluginsDisabled)
    {
        var state = new LaunchState
        {
            TimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
            PluginsDisabled = pluginsDisabled,
            Targets =
            [
                new LaunchTarget
                {
                    ProcessId = target.ProcessId,
                    StartTimeUtc = target.StartTimeUtc,
                },
            ],
        };

        File.WriteAllText(layout.StatePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void SaveSettingsFromUi()
    {
        settings.AutoStart = autoStartCheck.Checked;
        settings.AutoApply = autoApplyCheck.Checked;
        settings.DisablePlugins = disablePluginsCheck.Checked;
        settings.DisableCustomRepoPlugins = disableCustomRepoCheck.Checked;
        settings.DelaySeconds = (int)delayInput.Value;
        if (hookCombo.SelectedItem is string selected)
        {
            settings.HookVersion = selected;
        }

        settings.Save();
    }

    private void ApplyArgs(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--profile" when i + 1 < args.Length:
                    settings.ProfileRoot = args[++i];
                    break;
                case "--no-auto":
                    settings.AutoStart = false;
                    settings.AutoApply = false;
                    break;
                case "--clean":
                    settings.DisablePlugins = true;
                    break;
            }
        }
    }

    private void SetBusy(bool busy, string text)
    {
        progressBar.Visible = busy;
        progressBar.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
        if (busy)
        {
            statusLabel.Text = text;
        }
    }

    private void SetStatus(string text)
    {
        statusLabel.Text = text;
        notifyIcon.Text = text.Length > 0 && text.Length < 64 ? $"달라무드 업데이터 - {text}" : "달라무드 업데이터";
    }

    private void ClearPending()
    {
        pendingPid = null;
        pendingApplyAt = DateTime.MinValue;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // Link is optional.
        }
    }

    private static string ExpandPath(string path)
        => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));

    private static Icon LoadApplicationIcon()
    {
        try
        {
            var executablePath = Application.ExecutablePath;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                var icon = Icon.ExtractAssociatedIcon(executablePath);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch
        {
            // Fall back to the generated simple icon below.
        }

        return CreateFallbackTrayIcon();
    }

    private static Icon CreateFallbackTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var bg = new SolidBrush(Color.White);
        graphics.FillEllipse(bg, 2, 2, 28, 28);
        using var pen = new Pen(Color.FromArgb(224, 64, 64), 3);
        graphics.DrawArc(pen, 8, 8, 16, 16, 25, 290);
        graphics.DrawLine(pen, 18, 9, 25, 5);
        graphics.DrawLine(pen, 18, 9, 23, 14);
        return Icon.FromHandle(bitmap.GetHicon());
    }
}

internal sealed class DalamudReleaseInfo
{
    public string AssemblyVersion { get; set; } = "";
    public string RuntimeVersion { get; set; } = "";
    public string SupportedGameVer { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
}

internal sealed class DalamudAssetInfo
{
    public int Version { get; set; }
    public string PackageUrl { get; set; } = "";
    public List<DalamudAssetEntry> Assets { get; set; } = [];
}

internal sealed class DalamudAssetEntry
{
    public string FileName { get; set; } = "";
    public string? Hash { get; set; }
}

internal sealed class UpdaterSettings
{
    public string ProfileRoot { get; set; } = "%APPDATA%\\XIVLauncherKR";
    public string HookVersion { get; set; } = "15.0.2.3";
    public bool AutoStart { get; set; } = true;
    public bool AutoApply { get; set; } = true;
    public bool DisablePlugins { get; set; }
    public bool DisableCustomRepoPlugins { get; set; }
    public int DelaySeconds { get; set; } = 1;

    private static string SettingsPath
    {
        get
        {
            var sharedPath = Environment.GetEnvironmentVariable("KR_DALAMUD_SETTINGS_PATH");
            if (!string.IsNullOrWhiteSpace(sharedPath))
            {
                var fullSharedPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(sharedPath));
                var sharedDirectory = Path.GetDirectoryName(fullSharedPath);
                if (!string.IsNullOrWhiteSpace(sharedDirectory))
                {
                    Directory.CreateDirectory(sharedDirectory);
                }

                return fullSharedPath;
            }

            var portablePath = Path.Combine(AppContext.BaseDirectory, "DalamudUpdaterConfig.json");
            if (File.Exists(portablePath))
            {
                return portablePath;
            }

            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KrDalamudUpdater");
            Directory.CreateDirectory(root);
            return Path.Combine(root, "settings.json");
        }
    }

    public static UpdaterSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<UpdaterSettings>(File.ReadAllText(SettingsPath)) ?? new UpdaterSettings();
            }
        }
        catch
        {
            // Fall back to defaults if the local settings file is malformed.
        }

        return new UpdaterSettings();
    }

    public void Save()
    {
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal sealed class DalamudLayout
{
    public required string ProfileRoot { get; init; }
    public required string HookRoot { get; init; }
    public required string DotnetPath { get; init; }
    public required string InjectorDllPath { get; init; }
    public required string ConfigPath { get; init; }
    public required string PluginRoot { get; init; }
    public required string DevPluginRoot { get; init; }
    public required string AssetRoot { get; init; }
    public required string StatePath { get; init; }

    public static DalamudLayout FromSettings(UpdaterSettings settings)
    {
        var profileRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.ProfileRoot));
        var hookRoot = Path.Combine(profileRoot, "addon", "Hooks", settings.HookVersion);
        var assetsRoot = Path.Combine(profileRoot, "dalamudAssets");
        var assetVersion = ReadText(Path.Combine(assetsRoot, "asset.ver"));
        var assetRoot = string.IsNullOrWhiteSpace(assetVersion)
            ? Path.Combine(assetsRoot, "dev")
            : Path.Combine(assetsRoot, assetVersion);
        if (!Directory.Exists(assetRoot))
        {
            assetRoot = Path.Combine(assetsRoot, "dev");
        }

        if (!Directory.Exists(assetRoot))
        {
            assetRoot = assetsRoot;
        }

        return new DalamudLayout
        {
            ProfileRoot = profileRoot,
            HookRoot = hookRoot,
            DotnetPath = Path.Combine(profileRoot, "runtime", "dotnet.exe"),
            InjectorDllPath = Path.Combine(hookRoot, "Dalamud.Injector.dll"),
            ConfigPath = Path.Combine(profileRoot, "dalamudConfig.json"),
            PluginRoot = Path.Combine(profileRoot, "installedPlugins"),
            DevPluginRoot = Path.Combine(profileRoot, "devPlugins"),
            AssetRoot = assetRoot,
            StatePath = Path.Combine(profileRoot, "kr-dalamud-launch-state.json"),
        };
    }

    private static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    public IEnumerable<string> GetMissingParts()
    {
        if (!Directory.Exists(ProfileRoot))
        {
            yield return $"프로필 폴더 없음: {ProfileRoot}";
        }

        if (!File.Exists(DotnetPath))
        {
            yield return $"dotnet.exe 없음: {DotnetPath}";
        }

        if (!File.Exists(InjectorDllPath))
        {
            yield return $"Dalamud.Injector.dll 없음: {InjectorDllPath}";
        }

        if (!File.Exists(ConfigPath))
        {
            yield return $"dalamudConfig.json 없음: {ConfigPath}";
        }

        if (!Directory.Exists(PluginRoot))
        {
            yield return $"installedPlugins 폴더 없음: {PluginRoot}";
        }

        if (!Directory.Exists(AssetRoot))
        {
            yield return $"dalamudAssets 폴더 없음: {AssetRoot}";
        }
    }
}

internal sealed class TargetInfo
{
    public required int ProcessId { get; init; }
    public required string StartTimeUtc { get; init; }

    public static TargetInfo FromProcess(Process process)
    {
        var startTimeUtc = "";
        try
        {
            startTimeUtc = process.StartTime.ToUniversalTime().ToString("O");
        }
        catch
        {
            // PID-only operation still works if process metadata is unavailable.
        }

        return new TargetInfo
        {
            ProcessId = process.Id,
            StartTimeUtc = startTimeUtc,
        };
    }
}

internal sealed class LaunchState
{
    public string TimestampUtc { get; set; } = "";
    public bool PluginsDisabled { get; set; }
    public List<LaunchTarget> Targets { get; set; } = [];
}

internal sealed class LaunchTarget
{
    public int ProcessId { get; set; }
    public string StartTimeUtc { get; set; } = "";
}
