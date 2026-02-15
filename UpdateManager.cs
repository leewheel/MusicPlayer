using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TikTokMusicPlayer
{
    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";
        
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        
        [JsonPropertyName("body")]
        public string Body { get; set; } = "";
        
        [JsonPropertyName("published_at")]
        public DateTime PublishedAt { get; set; }
        
        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";
        
        [JsonPropertyName("assets")]
        public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
    }

    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        
        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
        
        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public class UpdateInfo
    {
        public bool HasUpdate { get; set; }
        public string CurrentVersion { get; set; } = "";
        public string LatestVersion { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public string ReleaseUrl { get; set; } = "";
        public long FileSize { get; set; }
        public string AssetName { get; set; } = "";
    }

    public class UpdateManager
    {
        private static readonly string ApiUrl = "https://api.github.com/repos/leewheel/MusicPlayer/releases/latest";
        private static readonly HttpClient client = new HttpClient();
        
        static UpdateManager()
        {
            client.DefaultRequestHeaders.Add("User-Agent", "TikTokMusicPlayer");
            client.Timeout = TimeSpan.FromSeconds(30);
        }

        public static string GetCurrentVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
            {
                return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            }
            return "1.0.0.0";
        }

        public static async Task<GitHubRelease?> GetLatestReleaseAsync()
        {
            try
            {
                Debug.WriteLine($"正在获取最新版本信息: {ApiUrl}");
                var response = await client.GetAsync(ApiUrl);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"API响应: {content.Substring(0, Math.Min(500, content.Length))}...");
                
                var release = JsonSerializer.Deserialize<GitHubRelease>(content);
                return release;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取最新版本失败: {ex.Message}");
                return null;
            }
        }

        public static string ExtractVersionNumber(string tagName)
        {
            if (string.IsNullOrEmpty(tagName))
                return "0.0.0.0";

            var version = tagName.TrimStart('V', 'v');
            
            if (string.IsNullOrEmpty(version))
                return "0.0.0.0";

            var parts = version.Split('.');
            
            int major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0;
            int minor = parts.Length > 1 && int.TryParse(parts[1], out var mi) ? mi : 0;
            int build = parts.Length > 2 && int.TryParse(parts[2], out var b) ? b : 0;
            int revision = parts.Length > 3 && int.TryParse(parts[3], out var r) ? r : 0;

            return $"{major}.{minor}.{build}.{revision}";
        }

        public static int CompareVersions(string version1, string version2)
        {
            try
            {
                var v1 = new Version(version1);
                var v2 = new Version(version2);
                return v1.CompareTo(v2);
            }
            catch
            {
                return string.Compare(version1, version2, StringComparison.Ordinal);
            }
        }

        public static async Task<UpdateInfo?> CheckForUpdateAsync()
        {
            try
            {
                var release = await GetLatestReleaseAsync();
                if (release == null)
                {
                    Debug.WriteLine("无法获取Release信息");
                    return null;
                }

                string currentVersion = GetCurrentVersion();
                string latestVersion = ExtractVersionNumber(release.TagName);

                Debug.WriteLine($"当前版本: {currentVersion}");
                Debug.WriteLine($"最新版本: {latestVersion} (Tag: {release.TagName})");

                int comparison = CompareVersions(currentVersion, latestVersion);
                Debug.WriteLine($"版本比较结果: {comparison}");

                if (comparison >= 0)
                {
                    Debug.WriteLine("当前版本已是最新");
                    return new UpdateInfo
                    {
                        HasUpdate = false,
                        CurrentVersion = currentVersion,
                        LatestVersion = latestVersion
                    };
                }

                string? downloadUrl = null;
                long fileSize = 0;
                string assetName = "";

                Debug.WriteLine($"检查Assets，共 {release.Assets.Length} 个文件");
                foreach (var asset in release.Assets)
                {
                    Debug.WriteLine($"Asset: {asset.Name}, URL: {asset.BrowserDownloadUrl}");
                    
                    if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.BrowserDownloadUrl;
                        fileSize = asset.Size;
                        assetName = asset.Name;
                        break;
                    }
                    else if (asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && 
                             asset.Name.Contains("TikTokMusicPlayer", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.BrowserDownloadUrl;
                        fileSize = asset.Size;
                        assetName = asset.Name;
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    Debug.WriteLine("未找到更新包（需要zip或exe文件）");
                    return null;
                }

                Debug.WriteLine($"找到更新包: {assetName}, URL: {downloadUrl}");

                return new UpdateInfo
                {
                    HasUpdate = true,
                    CurrentVersion = currentVersion,
                    LatestVersion = latestVersion,
                    DownloadUrl = downloadUrl,
                    ReleaseNotes = release.Body,
                    ReleaseUrl = release.HtmlUrl,
                    FileSize = fileSize,
                    AssetName = assetName
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"检查更新失败: {ex.Message}");
                return null;
            }
        }

        public static async Task<string?> DownloadUpdateAsync(string downloadUrl, IProgress<(long downloaded, long total, double speed)>? progress = null)
        {
            try
            {
                string downloadDir = Path.Combine(Path.GetTempPath(), "TikTokMusicPlayerUpdate");
                if (Directory.Exists(downloadDir))
                {
                    Directory.Delete(downloadDir, true);
                }
                Directory.CreateDirectory(downloadDir);

                string fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
                string filePath = Path.Combine(downloadDir, fileName);

                using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;

                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

                var downloadedBytes = 0L;
                var buffer = new byte[8192];
                var bytesRead = 0;
                var lastUpdateTime = DateTime.Now;
                var lastDownloadedBytes = 0L;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    downloadedBytes += bytesRead;

                    var now = DateTime.Now;
                    var elapsed = (now - lastUpdateTime).TotalSeconds;
                    if (elapsed >= 0.5 && progress != null)
                    {
                        var bytesInPeriod = downloadedBytes - lastDownloadedBytes;
                        var speedKBps = bytesInPeriod / elapsed / 1024.0;

                        progress.Report((downloadedBytes, totalBytes, speedKBps));

                        lastUpdateTime = now;
                        lastDownloadedBytes = downloadedBytes;
                    }
                }

                progress?.Report((downloadedBytes, totalBytes, 0));

                return filePath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"下载更新失败: {ex.Message}");
                return null;
            }
        }

        public static bool ApplyUpdate(string filePath, string assetName)
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string extractDir;
                
                if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    extractDir = Path.Combine(Path.GetTempPath(), "TikTokMusicPlayerUpdate_" + Guid.NewGuid().ToString());
                    Directory.CreateDirectory(extractDir);
                    ZipFile.ExtractToDirectory(filePath, extractDir);
                }
                else if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    extractDir = Path.Combine(Path.GetTempPath(), "TikTokMusicPlayerUpdate_" + Guid.NewGuid().ToString());
                    Directory.CreateDirectory(extractDir);
                    File.Copy(filePath, Path.Combine(extractDir, assetName), true);
                }
                else
                {
                    Debug.WriteLine($"不支持的文件格式: {assetName}");
                    return false;
                }

                string updaterPath = Path.Combine(appDir, "Updater.exe");
                string updaterArgs = $"\"{extractDir}\" \"{appDir}\"";

                Debug.WriteLine($"启动Updater: {updaterPath} {updaterArgs}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = updaterPath,
                    Arguments = updaterArgs,
                    UseShellExecute = true,
                    WorkingDirectory = appDir
                };

                Process.Start(startInfo);
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"应用更新失败: {ex.Message}");
                return false;
            }
        }
    }
}
