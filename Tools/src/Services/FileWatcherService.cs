using System;
using System.IO;
using System.Threading;

namespace BlogTools.Services
{
    /// <summary>
    /// Watches the blog directory for changes to _posts/*.md and _config.yml,
    /// firing an event when external modifications are detected.
    /// Uses debouncing to avoid excessive notifications.
    /// </summary>
    public class FileWatcherService : IDisposable
    {
        private FileSystemWatcher? _postsWatcher;
        private FileSystemWatcher? _configWatcher;
        private Timer? _debounceTimer;
        private readonly int _debounceMs;

        /// <summary>
        /// Fired when blog files have changed on disk (debounced).
        /// Always fire on the caller's thread — UI dispatch is the subscriber's responsibility.
        /// </summary>
        public event Action? FilesChanged;

        public FileWatcherService(string blogPath, int debounceMs = 500)
        {
            _debounceMs = debounceMs;

            // Watch _posts directory for .md file changes
            var postsDir = Path.Combine(blogPath, "_posts");
            if (Directory.Exists(postsDir))
            {
                _postsWatcher = new FileSystemWatcher(postsDir, "*.md")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                _postsWatcher.Changed += OnFileEvent;
                _postsWatcher.Created += OnFileEvent;
                _postsWatcher.Deleted += OnFileEvent;
                _postsWatcher.Renamed += (s, e) => OnFileEvent(s, e);
            }

            // Watch _config.yml
            if (File.Exists(Path.Combine(blogPath, "_config.yml")))
            {
                _configWatcher = new FileSystemWatcher(blogPath, "_config.yml")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                _configWatcher.Changed += OnFileEvent;
            }
        }

        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            // Debounce: reset the timer every time an event arrives
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ => FilesChanged?.Invoke(), null, _debounceMs, Timeout.Infinite);
        }

        public void Dispose()
        {
            _postsWatcher?.Dispose();
            _configWatcher?.Dispose();
            _debounceTimer?.Dispose();
        }
    }
}
