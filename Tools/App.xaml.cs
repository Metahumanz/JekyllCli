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

            if (string.IsNullOrEmpty(blogPath) || !Directory.Exists(blogPath) || !File.Exists(Path.Combine(blogPath, "_config.yml")))
            {
                var setupWindow = new SetupWindow();
                setupWindow.ShowDialog();

                if (!setupWindow.IsSetupSuccessful)
                {
                    Shutdown();
                    return;
                }
                
                blogPath = setupWindow.SelectedBlogPath;
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
