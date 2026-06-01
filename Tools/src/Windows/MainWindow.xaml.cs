using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using BlogTools.Services;
using Wpf.Ui.Controls;

namespace BlogTools
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            App.ConfigureThemeWindow(this);
            RefreshShellFromCurrentBlogConfig();
            Loaded += MainWindow_Loaded;
        }

        public void RefreshShellFromCurrentBlogConfig()
        {
            ApplyDynamicTitleAndIcon();
            ApplyGlobalFont();
        }

        private void ApplyDynamicTitleAndIcon()
        {
            var defaultTitle = Application.Current.TryFindResource("AppTitle")?.ToString() ?? "JekyllCli";
            Title = defaultTitle;
            AppTitleBar.Title = defaultTitle;

            var config = App.JekyllContext.LoadConfig();

            // Dynamic title from _config.yml
            if (config.TryGetValue("title", out var titleObj) && titleObj is string siteTitle && !string.IsNullOrWhiteSpace(siteTitle))
            {
                Title = $"{siteTitle} - BlogTools";
                AppTitleBar.Title = siteTitle;
            }
        }

        public void ApplyGlobalFont(string? fontName = null)
        {
            var settings = StorageService.Load();
            var font = fontName;

            if (string.IsNullOrWhiteSpace(font))
            {
                font = settings.AppFontFamily;
            }

            if (string.IsNullOrWhiteSpace(font) && App.JekyllContext.LoadConfig().TryGetValue("blogtools_font", out var val))
            {
                font = val?.ToString();
            }

            if (!string.IsNullOrWhiteSpace(font))
            {
                FontFamily = new System.Windows.Media.FontFamily(font);
            }

            RootNavigation.FontFamily = FontFamily;
            AppTitleBar.FontFamily = FontFamily;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RootNavigation.Navigate(typeof(DashboardPage));

            // Show AI commit onboarding once (skip in doc screenshot mode)
            await ShowAiCommitOnboardingIfNeededAsync();
        }

        private async Task ShowAiCommitOnboardingIfNeededAsync()
        {
            try
            {
                // Skip if we're in doc screenshot mode
                var args = Environment.GetCommandLineArgs();
                foreach (var arg in args)
                {
                    if (arg.StartsWith("--capture-doc-screenshots", StringComparison.OrdinalIgnoreCase))
                        return;
                }

                var settings = StorageService.Load();

                // Only show once
                if (settings.AiCommitOnboardingShown)
                    return;

                settings.AiCommitOnboardingShown = true;
                StorageService.Save(settings);

                // Slight delay so the main window renders first
                await Task.Delay(800);

                var msg = new Wpf.Ui.Controls.MessageBox
                {
                    Title = Application.Current.TryFindResource("AiCommitOnboardingTitle")?.ToString() ?? "AI Commit Messages",
                    Content = Application.Current.TryFindResource("AiCommitOnboardingMsg")?.ToString() ?? "Enable AI-generated commit messages?",
                    PrimaryButtonText = Application.Current.TryFindResource("AiCommitOnboardingBtnSetup")?.ToString() ?? "Configure",
                    CloseButtonText = Application.Current.TryFindResource("AiCommitOnboardingBtnSkip")?.ToString() ?? "Not Now"
                };

                var result = await msg.ShowDialogAsync();

                if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    // Navigate to App Settings
                    RootNavigation.Navigate(typeof(AppSettingsPage));
                }
            }
            catch
            {
                // Silently ignore onboarding errors
            }
        }
    }
}
