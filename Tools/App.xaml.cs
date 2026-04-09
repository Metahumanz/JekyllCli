using System;
using System.Windows;
using System.IO;
using BlogTools.Helpers;
using BlogTools.Models;
using BlogTools.Services;
using Microsoft.Win32;
using System.Threading.Tasks;
using Wpf.Ui.Appearance;
using System.Linq;
using System.Globalization;


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
            HoverLiftHelper.Initialize();

            // 清理上次更新残留的 .old 文件
            UpdateService.CleanupOldVersion();

            // 监听全局主题变化，自动更新所有窗口图标
            ApplicationThemeManager.Changed += OnThemeChanged;

            var settings = StorageService.Load();
            string? blogPath = settings.BlogPath;

            // Apply Language
            ApplyLanguage(settings.AppLanguage);

            if (string.IsNullOrEmpty(blogPath) || !Directory.Exists(blogPath) || !File.Exists(Path.Combine(blogPath, "_config.yml")))
            {
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var setupWindow = new SetupWindow();
                ApplyThemeIcon(setupWindow);
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
            ApplyThemeIcon(mainWindow);
            mainWindow.Show();

            // 启动后延迟自动检查更新（不阻塞主窗口显示）
            _ = AutoCheckUpdateAsync(mainWindow);
        }

        /// <summary>
        /// 根据当前主题为窗口设置对应的深色/浅色图标。
        /// </summary>
        public static void ApplyThemeIcon(Window window)
        {
            var isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
            var iconUri = isDark
                ? new Uri("pack://application:,,,/Assets/app_icon_dark.png")
                : new Uri("pack://application:,,,/Assets/app_icon_light.png");
            window.Icon = new System.Windows.Media.Imaging.BitmapImage(iconUri);
        }

        /// <summary>
        /// 当应用主题改变时，更新所有已打开窗口的图标。
        /// </summary>
        private static void OnThemeChanged(ApplicationTheme currentTheme, System.Windows.Media.Color accent)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (Window window in Application.Current.Windows)
                {
                    ApplyThemeIcon(window);
                }
            });
        }

        public static void ApplyLanguage(string languageCode)
        {
            if (languageCode == "Auto" || string.IsNullOrEmpty(languageCode))
            {
                languageCode = CultureInfo.CurrentCulture.Name.StartsWith("zh") ? "zh-CN" : "en-US";
            }

            var dictPath = $"pack://application:,,,/src/Resources/Languages/{languageCode}.xaml";
            var dict = new ResourceDictionary() { Source = new Uri(dictPath) };

            // Find and replace the existing language dictionary
            var existingDict = Application.Current.Resources.MergedDictionaries.FirstOrDefault(d => 
                d.Source != null && d.Source.OriginalString.Contains("/src/Resources/Languages/"));
            
            if (existingDict != null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(existingDict);
            }
            
            Application.Current.Resources.MergedDictionaries.Add(dict);
        }

        private async Task AutoCheckUpdateAsync(MainWindow mainWindow)
        {
            try
            {
                // 延迟 3 秒再检查，让主窗口有足够的时间完成初始化和加载
                await Task.Delay(3000);

                var (hasUpdate, latestVersion, downloadUrl, _) = await UpdateService.CheckForUpdateAsync();
                if (!hasUpdate || string.IsNullOrEmpty(downloadUrl))
                    return;

                // 在 UI 线程弹出对话框
                await Dispatcher.InvokeAsync(async () =>
                {
                    var current = UpdateService.GetCurrentVersion();
                    var currentStr = $"v{current.Major}.{current.Minor}.{current.Build}";
                    var msg = new Wpf.Ui.Controls.MessageBox
                    {
                        Title = Application.Current.FindResource("SettingsMsgUpdateFound").ToString()!,
                        Content = string.Format(Application.Current.FindResource("AppMsgUpdatePrompt").ToString()!, latestVersion, currentStr),
                        PrimaryButtonText = Application.Current.FindResource("AppBtnGoToUpdate").ToString()!,
                        CloseButtonText = Application.Current.FindResource("SettingsBtnLater").ToString()!
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

