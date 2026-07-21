using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("KR Dalamud Updater Bootstrap")]
[assembly: AssemblyDescription("Downloads, verifies, and starts the KR Dalamud Updater")]
[assembly: AssemblyVersion("0.4.2.0")]
[assembly: AssemblyFileVersion("0.4.2.0")]

namespace KrDalamudUpdaterBootstrap
{
    internal static class Program
    {
        private const string PayloadResourceName = "KrDalamudUpdaterBootstrap.Payload.KrDalamudUpdaterGui.zip";
        private const string GuiFileName = "Dalamud.Updater.Gui.exe";
        private const string PortableConfigFileName = "DalamudUpdaterConfig.json";
        private const string ReleaseConfigFileName = "UpdaterReleaseConfig.json";
        private const string SharedSettingsEnvironmentName = "KR_DALAMUD_SETTINGS_PATH";
        private const string BootstrapVersion = "0.4.2";
        private const string DesktopRuntimeDownloadUrl = "https://dotnet.microsoft.com/download/dotnet/10.0";
        private const long MaximumAssetBytes = 500L * 1024L * 1024L;

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                string executableRoot = AppDomain.CurrentDomain.BaseDirectory;
                string applicationRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KrDalamudUpdater");
                string versionsRoot = Path.Combine(applicationRoot, "versions");
                Directory.CreateDirectory(versionsRoot);

                string sharedSettingsPath = PrepareSharedSettings(executableRoot);
                string launchRoot = null;

                if (!ContainsArgument(args, "--offline"))
                {
                    try
                    {
                        launchRoot = TryInstallLatestRelease(executableRoot, applicationRoot, versionsRoot);
                    }
                    catch (Exception updateError)
                    {
                        Log(applicationRoot, "GitHub update check failed; using a local version. " + updateError);
                    }
                }

                if (string.IsNullOrWhiteSpace(launchRoot))
                {
                    launchRoot = TryGetCurrentVersion(applicationRoot, versionsRoot);
                }

                if (string.IsNullOrWhiteSpace(launchRoot))
                {
                    launchRoot = InstallEmbeddedPayload(versionsRoot);
                }

                if (ContainsArgument(args, "--extract-only"))
                {
                    return 0;
                }

                if (!HasNet10DesktopRuntime())
                {
                    ShowDesktopRuntimeRequired();
                    return 2;
                }

                StartGui(launchRoot, sharedSettingsPath, args);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Dalamud Updater files could not be prepared.\r\n\r\n" + ex.Message +
                    "\r\n\r\nMove the updater to a writable folder and try again.",
                    "Dalamud Updater",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }

        private static string TryInstallLatestRelease(string executableRoot, string applicationRoot, string versionsRoot)
        {
            ReleaseConfig config = LoadReleaseConfig(executableRoot);
            if (config == null || !config.Enabled)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(config.Repository) ||
                !Regex.IsMatch(config.Repository, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))
            {
                throw new InvalidDataException("UpdaterReleaseConfig.json has an invalid Repository value.");
            }

            if (string.IsNullOrWhiteSpace(config.AssetName) ||
                config.AssetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidDataException("UpdaterReleaseConfig.json has an invalid AssetName value.");
            }

            int timeoutSeconds = Math.Max(5, Math.Min(config.TimeoutSeconds, 120));
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string apiUrl = "https://api.github.com/repos/" + config.Repository + "/releases/latest";

            GitHubRelease release;
            using (TimeoutWebClient client = CreateWebClient(timeoutSeconds))
            {
                string json = client.DownloadString(apiUrl);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 4 * 1024 * 1024;
                release = serializer.Deserialize<GitHubRelease>(json);
            }

            if (release == null || string.IsNullOrWhiteSpace(release.tag_name))
            {
                throw new InvalidDataException("GitHub did not return a valid latest Release.");
            }

            GitHubAsset asset = null;
            if (release.assets != null)
            {
                foreach (GitHubAsset candidate in release.assets)
                {
                    if (candidate != null && string.Equals(candidate.name, config.AssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        asset = candidate;
                        break;
                    }
                }
            }

            if (asset == null)
            {
                throw new InvalidDataException("The latest GitHub Release does not contain " + config.AssetName + ".");
            }

            if (asset.size <= 0 || asset.size > MaximumAssetBytes)
            {
                throw new InvalidDataException("The GitHub Release asset has an invalid size.");
            }

            Uri downloadUri;
            if (!Uri.TryCreate(asset.browser_download_url, UriKind.Absolute, out downloadUri) ||
                !string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(downloadUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The GitHub Release asset has an invalid download URL.");
            }

            string expectedHash = ParseGitHubSha256(asset.digest);
            string folderName = MakeSafeFolderName(release.tag_name) + "-" + expectedHash.Substring(0, 8).ToLowerInvariant();
            string destinationRoot = GetChildPath(versionsRoot, folderName);
            string readyPath = Path.Combine(destinationRoot, ".ready");
            string expectedMarker = "sha256:" + expectedHash.ToLowerInvariant();

            if (File.Exists(Path.Combine(destinationRoot, GuiFileName)) &&
                File.Exists(readyPath) &&
                string.Equals(File.ReadAllText(readyPath).Trim(), expectedMarker, StringComparison.OrdinalIgnoreCase))
            {
                WriteCurrentVersion(applicationRoot, folderName);
                return destinationRoot;
            }

            string tempRoot = Path.Combine(applicationRoot, "temp");
            Directory.CreateDirectory(tempRoot);
            string downloadPath = Path.Combine(tempRoot, Guid.NewGuid().ToString("N") + ".zip");

            try
            {
                using (TimeoutWebClient client = CreateWebClient(timeoutSeconds))
                {
                    client.DownloadFile(downloadUri, downloadPath);
                }

                FileInfo downloadedFile = new FileInfo(downloadPath);
                if (downloadedFile.Length != asset.size)
                {
                    throw new InvalidDataException("The downloaded updater size does not match the GitHub Release asset.");
                }

                string actualHash = ComputeSha256(downloadPath);
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The downloaded updater SHA-256 digest does not match GitHub.");
                }

                InstallZip(downloadPath, destinationRoot, expectedMarker, versionsRoot);
                WriteCurrentVersion(applicationRoot, folderName);
                Log(applicationRoot, "Installed GitHub Release " + release.tag_name + ".");
                return destinationRoot;
            }
            finally
            {
                TryDeleteFile(downloadPath);
            }
        }

        private static ReleaseConfig LoadReleaseConfig(string executableRoot)
        {
            string path = Path.Combine(executableRoot, ReleaseConfigFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            return serializer.Deserialize<ReleaseConfig>(File.ReadAllText(path));
        }

        private static TimeoutWebClient CreateWebClient(int timeoutSeconds)
        {
            TimeoutWebClient client = new TimeoutWebClient(timeoutSeconds * 1000);
            client.Headers[HttpRequestHeader.UserAgent] = "KR-Dalamud-Updater/" + BootstrapVersion;
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            client.Headers["X-GitHub-Api-Version"] = "2022-11-28";
            return client;
        }

        private static string ParseGitHubSha256(string digest)
        {
            const string prefix = "sha256:";
            if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The GitHub Release asset does not have a SHA-256 digest.");
            }

            string hash = digest.Substring(prefix.Length).Trim();
            if (!Regex.IsMatch(hash, "^[0-9A-Fa-f]{64}$"))
            {
                throw new InvalidDataException("The GitHub Release asset SHA-256 digest is invalid.");
            }

            return hash.ToUpperInvariant();
        }

        private static string InstallEmbeddedPayload(string versionsRoot)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            byte[] payloadBytes;
            using (Stream payload = assembly.GetManifestResourceStream(PayloadResourceName))
            {
                if (payload == null)
                {
                    throw new InvalidOperationException("The embedded updater payload is missing.");
                }

                using (MemoryStream content = new MemoryStream())
                {
                    payload.CopyTo(content);
                    payloadBytes = content.ToArray();
                }
            }

            string hash = ComputeSha256(payloadBytes);
            string destinationRoot = GetChildPath(
                versionsRoot,
                "embedded-" + BootstrapVersion + "-" + hash.Substring(0, 8).ToLowerInvariant());
            string expectedMarker = "sha256:" + hash.ToLowerInvariant();

            if (File.Exists(Path.Combine(destinationRoot, GuiFileName)) &&
                File.Exists(Path.Combine(destinationRoot, ".ready")) &&
                string.Equals(File.ReadAllText(Path.Combine(destinationRoot, ".ready")).Trim(), expectedMarker, StringComparison.OrdinalIgnoreCase))
            {
                return destinationRoot;
            }

            string temporaryZip = Path.Combine(versionsRoot, ".embedded-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                File.WriteAllBytes(temporaryZip, payloadBytes);
                InstallZip(temporaryZip, destinationRoot, expectedMarker, versionsRoot);
                return destinationRoot;
            }
            finally
            {
                TryDeleteFile(temporaryZip);
            }
        }

        private static void InstallZip(string zipPath, string destinationRoot, string marker, string versionsRoot)
        {
            string temporaryRoot = GetChildPath(versionsRoot, ".install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);

            try
            {
                ExtractZipSafely(zipPath, temporaryRoot);
                if (!File.Exists(Path.Combine(temporaryRoot, GuiFileName)))
                {
                    throw new InvalidDataException("The updater package does not contain " + GuiFileName + ".");
                }

                File.WriteAllText(Path.Combine(temporaryRoot, ".ready"), marker, new UTF8Encoding(false));
                if (Directory.Exists(destinationRoot))
                {
                    string existingMarker = Path.Combine(destinationRoot, ".ready");
                    if (File.Exists(Path.Combine(destinationRoot, GuiFileName)) &&
                        File.Exists(existingMarker) &&
                        string.Equals(File.ReadAllText(existingMarker).Trim(), marker, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    throw new IOException("An incomplete updater version folder already exists: " + destinationRoot);
                }

                Directory.Move(temporaryRoot, destinationRoot);
            }
            finally
            {
                TryDeleteDirectory(temporaryRoot);
            }
        }

        private static void ExtractZipSafely(string zipPath, string destinationRoot)
        {
            string normalizedRoot = Path.GetFullPath(destinationRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }

                    if (string.Equals(entry.Name, PortableConfigFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string destination = Path.GetFullPath(Path.Combine(normalizedRoot, entry.FullName));
                    if (!destination.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("Invalid updater package path: " + entry.FullName);
                    }

                    string directory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    using (Stream input = entry.Open())
                    using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        input.CopyTo(output);
                    }
                }
            }
        }

        private static string TryGetCurrentVersion(string applicationRoot, string versionsRoot)
        {
            string currentPath = Path.Combine(applicationRoot, "current.txt");
            if (!File.Exists(currentPath))
            {
                return null;
            }

            try
            {
                string folderName = File.ReadAllText(currentPath).Trim();
                if (string.IsNullOrWhiteSpace(folderName) || folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    return null;
                }

                string candidate = GetChildPath(versionsRoot, folderName);
                return File.Exists(Path.Combine(candidate, GuiFileName)) && File.Exists(Path.Combine(candidate, ".ready"))
                    ? candidate
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static void WriteCurrentVersion(string applicationRoot, string folderName)
        {
            string temporaryPath = Path.Combine(applicationRoot, "current.txt.new");
            string currentPath = Path.Combine(applicationRoot, "current.txt");
            File.WriteAllText(temporaryPath, folderName, new UTF8Encoding(false));
            File.Copy(temporaryPath, currentPath, true);
            File.Delete(temporaryPath);
        }

        private static string PrepareSharedSettings(string executableRoot)
        {
            string settingsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KrDalamudUpdater");
            Directory.CreateDirectory(settingsRoot);
            string settingsPath = Path.Combine(settingsRoot, "settings.json");

            if (!File.Exists(settingsPath))
            {
                string portablePath = Path.Combine(executableRoot, PortableConfigFileName);
                if (File.Exists(portablePath))
                {
                    File.Copy(portablePath, settingsPath, false);
                }
            }

            return settingsPath;
        }

        private static void StartGui(string launchRoot, string sharedSettingsPath, string[] args)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = Path.Combine(launchRoot, GuiFileName);
            startInfo.WorkingDirectory = launchRoot;
            startInfo.UseShellExecute = false;
            startInfo.Arguments = BuildArguments(args);
            startInfo.EnvironmentVariables[SharedSettingsEnvironmentName] = sharedSettingsPath;
            Process.Start(startInfo);
        }

        private static bool HasNet10DesktopRuntime()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrWhiteSpace(programFiles))
            {
                return false;
            }

            string runtimeRoot = Path.Combine(programFiles, "dotnet", "shared", "Microsoft.WindowsDesktop.App");
            if (!Directory.Exists(runtimeRoot))
            {
                return false;
            }

            try
            {
                foreach (string directory in Directory.GetDirectories(runtimeRoot))
                {
                    Version version;
                    if (Version.TryParse(Path.GetFileName(directory), out version) && version.Major == 10)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static void ShowDesktopRuntimeRequired()
        {
            DialogResult result = MessageBox.Show(
                "이 실행기에는 Microsoft .NET 10 Desktop Runtime (x64)이 필요합니다.\r\n\r\n" +
                "먼저 런타임을 설치한 다음 실행기를 다시 시작하세요.\r\n\r\n" +
                "Microsoft 다운로드 페이지를 여시겠습니까?",
                "KR Dalamud Updater",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result != DialogResult.Yes)
            {
                return;
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = DesktopRuntimeDownloadUrl;
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
            }
            catch
            {
                MessageBox.Show(
                    DesktopRuntimeDownloadUrl,
                    ".NET 10 Desktop Runtime 다운로드",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private static string MakeSafeFolderName(string value)
        {
            string trimmed = value.Trim().TrimStart('v', 'V');
            string safe = Regex.Replace(trimmed, "[^0-9A-Za-z._-]", "-").Trim('-', '.');
            if (string.IsNullOrWhiteSpace(safe))
            {
                throw new InvalidDataException("The GitHub Release tag cannot be used as a version folder name.");
            }

            return safe.Length > 80 ? safe.Substring(0, 80) : safe;
        }

        private static string GetChildPath(string parentRoot, string childName)
        {
            string normalizedRoot = Path.GetFullPath(parentRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string childPath = Path.GetFullPath(Path.Combine(normalizedRoot, childName));
            if (!childPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Invalid updater folder path.");
            }

            return childPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return ToHex(sha.ComputeHash(stream));
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(bytes));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder result = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
            {
                result.Append(value.ToString("X2"));
            }

            return result.ToString();
        }

        private static void Log(string applicationRoot, string message)
        {
            try
            {
                Directory.CreateDirectory(applicationRoot);
                File.AppendAllText(
                    Path.Combine(applicationRoot, "bootstrap.log"),
                    DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch
            {
                // Updating must not fail only because diagnostic logging failed.
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
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
            }
        }

        private static bool ContainsArgument(string[] args, string expected)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildArguments(string[] args)
        {
            List<string> forwarded = new List<string>();
            foreach (string arg in args)
            {
                if (!string.Equals(arg, "--extract-only", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(arg, "--offline", StringComparison.OrdinalIgnoreCase))
                {
                    forwarded.Add(Quote(arg));
                }
            }

            return string.Join(" ", forwarded.ToArray());
        }

        private static string Quote(string value)
        {
            if (value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return value;
            }

            StringBuilder result = new StringBuilder();
            result.Append('"');
            int backslashes = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    result.Append('\\', backslashes * 2 + 1);
                    result.Append('"');
                    backslashes = 0;
                    continue;
                }

                result.Append('\\', backslashes);
                backslashes = 0;
                result.Append(character);
            }

            result.Append('\\', backslashes * 2);
            result.Append('"');
            return result.ToString();
        }
    }

    internal sealed class TimeoutWebClient : WebClient
    {
        private readonly int timeoutMilliseconds;

        public TimeoutWebClient(int timeoutMilliseconds)
        {
            this.timeoutMilliseconds = timeoutMilliseconds;
        }

        protected override WebRequest GetWebRequest(Uri address)
        {
            WebRequest request = base.GetWebRequest(address);
            request.Timeout = timeoutMilliseconds;
            return request;
        }
    }

    internal sealed class ReleaseConfig
    {
        public bool Enabled { get; set; }
        public string Repository { get; set; }
        public string AssetName { get; set; }
        public int TimeoutSeconds { get; set; }
    }

    internal sealed class GitHubRelease
    {
        public string tag_name { get; set; }
        public GitHubAsset[] assets { get; set; }
    }

    internal sealed class GitHubAsset
    {
        public string name { get; set; }
        public string browser_download_url { get; set; }
        public string digest { get; set; }
        public long size { get; set; }
    }
}
