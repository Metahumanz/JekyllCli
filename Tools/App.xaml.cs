using System;
using System.Windows;
using System.IO;
using BlogTools.Models;
using BlogTools.Services;
using Microsoft.Win32;

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

            var settings = StorageService.Load();
            string? blogPath = settings.BlogPath;

            while (string.IsNullOrEmpty(blogPath) || !Directory.Exists(blogPath) || !File.Exists(Path.Combine(blogPath, "_config.yml")))
            {
                if (!string.IsNullOrEmpty(blogPath))
                {
                    MessageBox.Show("所选目录不是有效的 Jekyll 博客根目录（须包含 _config.yml 文件），请重新选择。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                OpenFolderDialog dialog = new OpenFolderDialog
                {
                    Title = "请选择 Jekyll 博客本地根目录"
                };

                if (dialog.ShowDialog() == true)
                {
                    blogPath = dialog.FolderName;
                }
                else
                {
                    // User cancelled initial setup
                    Shutdown();
                    return;
                }
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
            new MainWindow().Show();
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
