using System;
using System.Windows;
using System.IO;
using BlogTools.Models;
using BlogTools.Services;
using Microsoft.Win32;
using Velopack;

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

            try
            {
                VelopackApp.Build().Run();
            }
            catch { /* Ignore errors during Velopack initialization in dev environment */ }

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
