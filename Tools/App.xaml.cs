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
using System.Diagnostics;


namespace BlogTools
{
    public partial class App : Application
    {
        public const string ThemeModeAuto = "Auto";
        public const string ThemeModeDark = "Dark";
        public const string ThemeModeLight = "Light";

        public static JekyllService JekyllContext { get; internal set; } = null!;
        public static GitService GitContext { get; internal set; } = null!;
        public static BlogPost? CurrentEditPost { get; set; } = null;
        private static FileWatcherService? _fileWatcher;
        private static string _themeMode = ThemeModeAuto;

        /// <summary>
        /// Fired when _posts/*.md or _config.yml changes on disk.
        /// Subscribers must dispatch to UI thread themselves.
        /// </summary>
        public static event Action? BlogFilesChanged;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            HoverLiftHelper.Initialize();

            bool isDocCaptureMode = TryGetDocScreenshotOptions(e.Args, out var captureOutputDir, out var captureBlogPath);

            // 清理上次更新残留的 .old 文件
            UpdateService.CleanupOldVersion();

            // 监听全局主题变化，自动更新所有窗口图标
            ApplicationThemeManager.Changed += OnThemeChanged;

            var settings = StorageService.Load();
            string? blogPath = isDocCaptureMode ? captureBlogPath : settings.BlogPath;

            // Apply Language
            ApplyLanguage(isDocCaptureMode ? "zh-CN" : settings.AppLanguage);
            ApplyThemeMode(isDocCaptureMode ? ThemeModeLight : settings.ThemeMode);

            if (string.IsNullOrEmpty(blogPath) || !Directory.Exists(blogPath) || !File.Exists(Path.Combine(blogPath, "_config.yml")))
            {
                if (isDocCaptureMode)
                {
                    blogPath = ResolveBundledBlogPath();
                    if (string.IsNullOrEmpty(blogPath) || !File.Exists(Path.Combine(blogPath, "_config.yml")))
                    {
                        throw new InvalidOperationException("Unable to resolve a valid bundled blog path for documentation screenshots.");
                    }
                }
                else
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
            }

            // Save valid path
            if (!isDocCaptureMode)
            {
                settings.BlogPath = blogPath;
                StorageService.Save(settings);
            }

            // Initialize Services
            JekyllContext = new JekyllService(blogPath);
            GitContext = new GitService(blogPath);

            // Start file watcher for hot reload
            StartFileWatcher(blogPath);

            // Show MainWindow
            var mainWindow = new MainWindow();
            ApplyThemeIcon(mainWindow);
            mainWindow.Show();

            if (isDocCaptureMode)
            {
                _ = RunDocScreenshotCaptureAsync(mainWindow, captureOutputDir);
                return;
            }

            // 启动后延迟自动检查更新（不阻塞主窗口显示）
            _ = AutoCheckUpdateAsync(mainWindow);
        }

        private static bool TryGetDocScreenshotOptions(string[] args, out string outputDir, out string? blogPath)
        {
            outputDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "docs", "images", "real"));
            blogPath = null;
            bool enabled = false;

            foreach (var arg in args)
            {
                if (arg.Equals("--capture-doc-screenshots", StringComparison.OrdinalIgnoreCase))
                {
                    enabled = true;
                }
                else if (arg.StartsWith("--capture-doc-screenshots=", StringComparison.OrdinalIgnoreCase))
                {
                    enabled = true;
                    outputDir = Path.GetFullPath(arg["--capture-doc-screenshots=".Length..].Trim('"'));
                }
                else if (arg.StartsWith("--capture-blog=", StringComparison.OrdinalIgnoreCase))
                {
                    blogPath = Path.GetFullPath(arg["--capture-blog=".Length..].Trim('"'));
                }
            }

            return enabled;
        }

        private static string ResolveBundledBlogPath()
        {
            string defaultPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Blog"));
            if (Directory.Exists(defaultPath))
            {
                return defaultPath;
            }

            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Blog"));
        }

        private async Task RunDocScreenshotCaptureAsync(MainWindow mainWindow, string outputDir)
        {
            int exitCode = 0;
            try
            {
                await DocScreenshotCaptureService.CaptureAsync(mainWindow, outputDir);
            }
            catch (Exception ex)
            {
                exitCode = 1;
                try
                {
                    Directory.CreateDirectory(outputDir);
                    File.WriteAllText(Path.Combine(outputDir, "capture-error.txt"), ex.ToString());
                }
                catch
                {
                }

                Debug.WriteLine(ex);
            }
            finally
            {
                Shutdown(exitCode);
            }
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

        public static string NormalizeThemeMode(string? themeMode)
        {
            if (string.Equals(themeMode, ThemeModeDark, StringComparison.OrdinalIgnoreCase))
            {
                return ThemeModeDark;
            }

            if (string.Equals(themeMode, ThemeModeLight, StringComparison.OrdinalIgnoreCase))
            {
                return ThemeModeLight;
            }

            return ThemeModeAuto;
        }

        public static void ApplyThemeMode(string? themeMode)
        {
            _themeMode = NormalizeThemeMode(themeMode);
            ApplicationThemeManager.Apply(ResolveApplicationTheme(_themeMode));

            if (Current == null)
            {
                return;
            }

            foreach (Window window in Current.Windows)
            {
                ConfigureThemeWindow(window);
            }
        }

        public static void ConfigureThemeWindow(Window window)
        {
            try
            {
                SystemThemeWatcher.UnWatch(window);
            }
            catch
            {
            }

            if (_themeMode == ThemeModeAuto)
            {
                SystemThemeWatcher.Watch(window);
            }

            ApplyThemeIcon(window);
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

        private static ApplicationTheme ResolveApplicationTheme(string themeMode)
        {
            return NormalizeThemeMode(themeMode) switch
            {
                ThemeModeDark => ApplicationTheme.Dark,
                ThemeModeLight => ApplicationTheme.Light,
                _ => ResolveSystemApplicationTheme()
            };
        }

        private static ApplicationTheme ResolveSystemApplicationTheme()
        {
            if (ApplicationThemeManager.IsSystemHighContrast())
            {
                return ApplicationTheme.HighContrast;
            }

            return ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light;
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
                {
                    var current = UpdateService.GetCurrentVersion();
                    var currentStr = $"v{current.Major}.{current.Minor}.{current.Build}";
                    var msg = new Wpf.Ui.Controls.MessageBox
                    {
                        Title = Application.Current.FindResource("SettingsMsgUpdateFound").ToString()!,
                        Content = string.Join(
                            Environment.NewLine,
                            string.Format(Application.Current.FindResource("SettingsMsgAskDownload").ToString()!, latestVersion),
                            string.Format(Application.Current.FindResource("CommonVersionCurrent").ToString()!, currentStr)),
                        PrimaryButtonText = Application.Current.FindResource("SettingsBtnDownloadNow").ToString()!,
                        CloseButtonText = Application.Current.FindResource("SettingsBtnLater").ToString()!
                    };
                    var result = await msg.ShowDialogAsync();
                    if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                    {
                        // 导航到设置页并触发更新检查
                        await DownloadAndApplyUpdateAsync(mainWindow, downloadUrl);
                    }
                }
            }
            catch
            {
                // 静默失败，不打扰用户
            }
        }

        private async Task DownloadAndApplyUpdateAsync(Window owner, string downloadUrl)
        {
            UpdateProgressWindow? progressWindow = null;

            try
            {
                progressWindow = new UpdateProgressWindow(owner);
                progressWindow.UpdateProgress(
                    string.Format(Application.Current.FindResource("SettingsMsgUpdateDownloading").ToString()!, 0),
                    0);
                progressWindow.Show();

                var progress = new Progress<int>(percent =>
                {
                    progressWindow.UpdateProgress(
                        string.Format(Application.Current.FindResource("SettingsMsgUpdateDownloading").ToString()!, percent),
                        percent);
                });

                var zipPath = await UpdateService.DownloadUpdateAsync(downloadUrl, progress);

                progressWindow.UpdateStatus(
                    Application.Current.FindResource("SettingsMsgDownloadComplete").ToString()!,
                    isIndeterminate: false,
                    value: 100);

                var settings = StorageService.Load();
                if (settings.SilentUpdate)
                {
                    progressWindow.UpdateStatus(
                        Application.Current.FindResource("SettingsMsgSilentUpdating").ToString()!,
                        isIndeterminate: true);
                    await Task.Delay(500);
                    progressWindow.AllowClose = true;
                    progressWindow.Close();
                    UpdateService.ApplyUpdate(zipPath);
                    return;
                }

                progressWindow.AllowClose = true;
                progressWindow.Close();

                var askApply = new Wpf.Ui.Controls.MessageBox
                {
                    Title = Application.Current.FindResource("SettingsMsgDownloadComplete").ToString()!,
                    Content = Application.Current.FindResource("SettingsMsgAskApply").ToString()!,
                    PrimaryButtonText = Application.Current.FindResource("SettingsBtnApplyNow").ToString()!,
                    CloseButtonText = Application.Current.FindResource("SettingsBtnLater").ToString()!
                };
                var applyResult = await askApply.ShowDialogAsync();
                if (applyResult == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    progressWindow = new UpdateProgressWindow(owner);
                    progressWindow.UpdateStatus(
                        Application.Current.FindResource("SettingsMsgSilentUpdating").ToString()!,
                        isIndeterminate: true);
                    progressWindow.Show();
                    await Task.Delay(500);
                    progressWindow.AllowClose = true;
                    progressWindow.Close();
                    UpdateService.ApplyUpdate(zipPath);
                }
            }
            catch (Exception ex)
            {
                if (progressWindow != null)
                {
                    progressWindow.AllowClose = true;
                    progressWindow.Close();
                }

                var msg = new Wpf.Ui.Controls.MessageBox
                {
                    Title = Application.Current.FindResource("CommonError").ToString()!,
                    Content = string.Format(Application.Current.FindResource("SettingsMsgUpdateError").ToString()!, ex.Message),
                    CloseButtonText = Application.Current.FindResource("CommonConfirm").ToString()!
                };
                await msg.ShowDialogAsync();
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

