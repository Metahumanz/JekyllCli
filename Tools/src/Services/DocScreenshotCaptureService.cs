using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BlogTools.Models;
using Wpf.Ui.Appearance;

namespace BlogTools.Services
{
    internal static class DocScreenshotCaptureService
    {
        private const int SwRestore = 9;

        public static async Task CaptureAsync(MainWindow mainWindow, string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            var temporaryPostPaths = EnsureSamplePosts();

            try
            {
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Width = 1200;
                mainWindow.Height = 760;
                mainWindow.Left = 80;
                mainWindow.Top = 80;

                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                mainWindow.Activate();
                mainWindow.Topmost = true;
                await Task.Delay(250);
                mainWindow.Topmost = false;
                mainWindow.Focus();

                await WaitForUiIdleAsync();
                await Task.Delay(2200);

                await CaptureCurrentWindowAsync(mainWindow, Path.Combine(outputDir, "dashboard-real.png"));

                await NavigateAndCaptureAsync<ManagePostsPage>(
                    mainWindow,
                    typeof(ManagePostsPage),
                    Path.Combine(outputDir, "manage-posts-real.png"),
                    waitAfterNavigateMs: 2200);

                var samplePost = App.JekyllContext.GetAllPosts().FirstOrDefault() ?? new BlogPost
                {
                    Title = "Hello JekyllCli",
                    Date = DateTime.Now,
                    Categories = { "Docs" },
                    Tags = { "guide", "sample" },
                    Description = "This is a sample post for documentation screenshots.",
                    Content = "# Hello JekyllCli\n\nThis screenshot demonstrates the actual editor page."
                };

                App.CurrentEditPost = samplePost;
                await NavigateAndCaptureAsync<EditorPage>(
                    mainWindow,
                    typeof(EditorPage),
                    Path.Combine(outputDir, "editor-real.png"),
                    onPageReady: page =>
                    {
                        page.MetadataExpander.IsExpanded = true;
                        page.UpdateLayout();
                    },
                    waitAfterNavigateMs: 5200);

                await NavigateAndCaptureAsync<SettingsPage>(
                    mainWindow,
                    typeof(SettingsPage),
                    Path.Combine(outputDir, "settings-real.png"),
                    waitAfterNavigateMs: 2200);

                await NavigateAndCaptureAsync<AppSettingsPage>(
                    mainWindow,
                    typeof(AppSettingsPage),
                    Path.Combine(outputDir, "app-settings-real.png"),
                    waitAfterNavigateMs: 2200);
            }
            finally
            {
                CleanupTemporaryPosts(temporaryPostPaths);
            }
        }

        private static async Task NavigateAndCaptureAsync<TPage>(
            MainWindow mainWindow,
            Type targetPageType,
            string outputPath,
            Action<TPage>? onPageReady = null,
            int waitAfterNavigateMs = 2000)
            where TPage : FrameworkElement
        {
            mainWindow.RootNavigation.Navigate(targetPageType);

            var page = await WaitForVisualAsync<TPage>(mainWindow, timeoutMs: 15000);
            await WaitForUiIdleAsync();

            onPageReady?.Invoke(page);

            if (page is EditorPage editorPage)
            {
                await WaitForEditorReadyAsync(editorPage);
            }

            await Task.Delay(waitAfterNavigateMs);
            await CaptureCurrentWindowAsync(mainWindow, outputPath);
        }

        private static async Task WaitForEditorReadyAsync(EditorPage page)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                if (page.EditorWebView.CoreWebView2 != null && page.PreviewWebView.CoreWebView2 != null)
                {
                    return;
                }

                await Task.Delay(250);
            }
        }

        private static async Task<T> WaitForVisualAsync<T>(DependencyObject root, int timeoutMs)
            where T : DependencyObject
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                var visual = FindDescendant<T>(root);
                if (visual != null)
                {
                    return visual;
                }

                await Task.Delay(120);
            }

            throw new InvalidOperationException($"Timed out waiting for visual '{typeof(T).Name}'.");
        }

        private static T? FindDescendant<T>(DependencyObject root)
            where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                {
                    return match;
                }

                var nested = FindDescendant<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static async Task WaitForUiIdleAsync()
        {
            await Application.Current.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);
        }

        private static async Task CaptureCurrentWindowAsync(Window window, string outputPath)
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Main window handle is unavailable.");
            }

            NativeMethods.ShowWindow(handle, SwRestore);
            NativeMethods.SetForegroundWindow(handle);

            await Task.Delay(350);

            if (!NativeMethods.GetWindowRect(handle, out var rect))
            {
                throw new InvalidOperationException("Failed to retrieve window bounds for screenshot capture.");
            }

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;

            using var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size);
            bitmap.Save(outputPath, ImageFormat.Png);
        }

        private static string[] EnsureSamplePosts()
        {
            var existingPosts = App.JekyllContext.GetAllPosts();
            if (existingPosts.Count > 0)
            {
                return Array.Empty<string>();
            }

            var samples = new[]
            {
                new BlogPost
                {
                    Title = "Welcome to JekyllCli",
                    Date = DateTime.Today.AddDays(-5).AddHours(10),
                    LastModifiedAt = DateTime.Today.AddDays(-2).AddHours(18),
                    Categories = { "Guide", "Getting Started" },
                    Tags = { "jekyll", "chirpy", "desktop" },
                    Description = "A sample post used for documentation screenshots.",
                    Content = "# Welcome to JekyllCli\n\nThis sample article helps render realistic dashboard and list views."
                },
                new BlogPost
                {
                    Title = "Writing Faster with the Editor",
                    Date = DateTime.Today.AddDays(-3).AddHours(9),
                    Categories = { "Writing" },
                    Tags = { "markdown", "editor" },
                    Description = "Exploring the built-in Markdown tools.",
                    Content = "# Writing Faster with the Editor\n\nUse the toolbar to insert headings, lists, tables and links."
                },
                new BlogPost
                {
                    Title = "Manage Existing Posts",
                    Date = DateTime.Today.AddDays(-1).AddHours(15),
                    Categories = { "Workflow" },
                    Tags = { "manage", "search", "publish" },
                    Description = "Review, search and update your published posts.",
                    Content = "# Manage Existing Posts\n\nThe manage page helps you search by title, file name and modification date."
                }
            };

            var createdPaths = new string[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                App.JekyllContext.SavePost(samples[i]);
                createdPaths[i] = samples[i].FullPath;
            }

            return createdPaths;
        }

        private static void CleanupTemporaryPosts(string[] createdPaths)
        {
            foreach (var path in createdPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                try
                {
                    App.JekyllContext.DeletePost(path);
                }
                catch
                {
                }
            }
        }

        private static class NativeMethods
        {
            [StructLayout(LayoutKind.Sequential)]
            internal struct Rect
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [DllImport("user32.dll")]
            internal static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

            [DllImport("user32.dll")]
            internal static extern bool SetForegroundWindow(IntPtr hWnd);

            [DllImport("user32.dll")]
            internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        }
    }
}
