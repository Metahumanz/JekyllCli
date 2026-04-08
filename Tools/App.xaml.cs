using System;
using System.Windows;
using System.IO;
using BlogTools.Models;
using BlogTools.Services;
using Microsoft.Win32;
using System.Threading.Tasks;


namespace BlogTools
{
    public partial class App : Application
    {
        public static JekyllService JekyllContext { get; internal set; } = null!;
        public static GitService GitContext { get; internal set; } = null!;
        public static BlogPost? CurrentEditPost { get; set; } = null;
        private static FileWatcherService? _fileWatcher;

        /// <summary>
        /// Fired when _posts/*.md or _config.yml changes on disk.
        /// Subscribers must dispatch to UI thread themselves.
        /// </summary>
        public static event Action? BlogFilesChanged;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 清理上次更新残留的 .old 文件
            UpdateService.CleanupOldVersion();

            var settings = StorageService.Load();
            string? blogPath = settings.BlogPath;

            if (string.IsNullOrEmpty(blogPath) || !Directory.Exists(blogPath) || !File.Exists(Path.Combine(blogPath, "_config.yml")))
            {
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var setupWindow = new SetupWindow();
                setupWindow.ShowDialog();

                if (!setupWindow.IsSetupSuccessful)
                {
                    Application.Current.Shutdown();
                    return;
                }
                
                blogPath = setupWindow.SelectedBlogPath;
                Application.Current.ShutdownMode = ShutdownMode.OnLastWindowClose;
            }

            // Save valid path
            settings.BlogPath = blogPath;
            StorageService.Save(settings);

            // Initialize Services
            JekyllContext = new JekyllService(blogPath);
            GitContext = new GitService(blogPath);

            // Start file watcher for hot reload
            StartFileWatcher(blogPath);

            // Show MainWindow
            var mainWindow = new MainWindow();
            mainWindow.Show();

            // 启动后延迟自动检查更新（不阻塞主窗口显示）
            _ = AutoCheckUpdateAsync(mainWindow);
        }

        private async Task AutoCheckUpdateAsync(MainWindow mainWindow)
        {
            try
            {
                // 延迟 3 秒再检查，让主窗口有足够的时间完成初始化和加载
                await Task.Delay(3000);

                var (hasUpdate, latestVersion, downloadUrl) = await UpdateService.CheckForUpdateAsync();
                if (!hasUpdate || string.IsNullOrEmpty(downloadUrl))
                    return;

                // 在 UI 线程弹出对话框
                await Dispatcher.InvokeAsync(async () =>
                {
                    var current = UpdateService.GetCurrentVersion();
                    var msg = new Wpf.Ui.Controls.MessageBox
                    {
                        Title = "发现新版本",
                        Content = $"发现新版本 {latestVersion}（当前: v{current.Major}.{current.Minor}.{current.Build}），是否前往设置页下载？",
                        PrimaryButtonText = "前往更新",
                        CloseButtonText = "稍后再说"
                    };
                    var result = await msg.ShowDialogAsync();
                    if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                    {
                        // 导航到设置页并触发更新检查
                        mainWindow.RootNavigation?.Navigate(typeof(SettingsPage));
                    }
                });
            }
            catch
            {
                // 静默失败，不打扰用户
            }
        }

        public static void StartFileWatcher(string blogPath)
        {
            _fileWatcher?.Dispose();
            _fileWatcher = new FileWatcherService(blogPath);
            _fileWatcher.FilesChanged += () => BlogFilesChanged?.Invoke();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _fileWatcher?.Dispose();
            base.OnExit(e);
        }
    }
}

