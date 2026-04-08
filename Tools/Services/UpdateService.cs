using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BlogTools.Services
{
    /// <summary>
    /// 基于"执行文件重命名法"的软件自热替换更新服务。
    /// 无任何外部依赖，纯原生 .NET 实现。
    /// </summary>
    public static class UpdateService
    {
        private const string GitHubApi = "https://api.github.com/repos/Metahumanz/JekyllCli/releases/latest";
        private const string AssetName = "JekyllCli-win-x64-minimal.zip";
        private static readonly HttpClient _http = new HttpClient();

        static UpdateService()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("JekyllCli-Updater");
        }

        /// <summary>
        /// 获取当前运行的应用版本。
        /// </summary>
        public static Version GetCurrentVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        }

        /// <summary>
        /// 检查 GitHub Release 是否有更新。
        /// 返回 (是否有更新, 最新版本号, 下载链接)。
        /// </summary>
        public static async Task<(bool HasUpdate, string LatestVersion, string DownloadUrl)> CheckForUpdateAsync()
        {
            try
            {
                var response = await _http.GetStringAsync(GitHubApi);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                var tagName = root.GetProperty("tag_name").GetString() ?? "";
                var versionStr = tagName.TrimStart('v', 'V');

                if (!Version.TryParse(versionStr, out var remoteVersion))
                    return (false, tagName, "");

                var currentVersion = GetCurrentVersion();

                // 比较 Major.Minor.Build
                var current = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build);
                var remote = new Version(remoteVersion.Major, remoteVersion.Minor, remoteVersion.Build);

                if (remote <= current)
                    return (false, tagName, "");

                // 在 assets 数组中找到目标下载文件
                string downloadUrl = "";
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.Equals(AssetName, StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            break;
                        }
                    }
                }

                return (true, tagName, downloadUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] CheckForUpdate failed: {ex.Message}");
                return (false, "", "");
            }
        }

        /// <summary>
        /// 下载更新 ZIP 文件到临时目录。
        /// 支持通过 progress 回调报告 0-100 进度。
        /// </summary>
        public static async Task<string> DownloadUpdateAsync(string downloadUrl, IProgress<int>? progress = null)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "JekyllCli_Update");
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);

            var zipPath = Path.Combine(tempDir, AssetName);

            using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            long downloadedBytes = 0;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                downloadedBytes += bytesRead;
                if (totalBytes > 0)
                {
                    progress?.Report((int)(downloadedBytes * 100 / totalBytes));
                }
            }

            progress?.Report(100);
            return zipPath;
        }

        /// <summary>
        /// 执行文件重命名法热替换更新 + 自动重启：
        /// 1. 将当前运行的 EXE 重命名为 .old
        /// 2. 从 ZIP 中提取新版本 EXE 到原位
        /// 3. 启动新版本
        /// 4. 退出当前进程
        /// </summary>
        public static void ApplyUpdate(string zipPath)
        {
            var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("无法确定当前程序路径");
            var appDir = Path.GetDirectoryName(currentExe)!;
            var exeName = Path.GetFileName(currentExe);
            var oldExe = currentExe + ".old";

            // 先清理残留
            if (File.Exists(oldExe))
            {
                try { File.Delete(oldExe); } catch { }
            }

            // 解压 ZIP 到临时目录
            var extractDir = Path.Combine(Path.GetTempPath(), "JekyllCli_Extract");
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            // 查找新版 EXE
            var newExePath = Path.Combine(extractDir, exeName);
            if (!File.Exists(newExePath))
            {
                throw new FileNotFoundException($"更新包中未找到 {exeName}");
            }

            // 核心替换操作：重命名当前 EXE → 移入新 EXE
            File.Move(currentExe, oldExe);
            File.Copy(newExePath, currentExe, true);

            // 启动新版本
            Process.Start(new ProcessStartInfo
            {
                FileName = currentExe,
                UseShellExecute = true
            });

            // 退出当前进程
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                System.Windows.Application.Current.Shutdown();
            });
        }

        /// <summary>
        /// 在程序启动时调用，清理上一次更新残留的 .old 文件。
        /// </summary>
        public static void CleanupOldVersion()
        {
            try
            {
                var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (currentExe == null) return;

                var oldExe = currentExe + ".old";
                if (File.Exists(oldExe))
                {
                    File.Delete(oldExe);
                    Debug.WriteLine("[UpdateService] Cleaned up old version file.");
                }

                // 清理临时更新文件夹
                var tempUpdate = Path.Combine(Path.GetTempPath(), "JekyllCli_Update");
                if (Directory.Exists(tempUpdate))
                    Directory.Delete(tempUpdate, true);
                var tempExtract = Path.Combine(Path.GetTempPath(), "JekyllCli_Extract");
                if (Directory.Exists(tempExtract))
                    Directory.Delete(tempExtract, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Cleanup failed: {ex.Message}");
            }
        }
    }
}
