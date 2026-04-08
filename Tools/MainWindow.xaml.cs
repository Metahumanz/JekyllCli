using System;
using System.IO;
using System.Windows;
using Wpf.Ui.Controls;

namespace BlogTools
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
            ApplyGlobalFont();
            ApplyDynamicTitleAndIcon();
            Loaded += MainWindow_Loaded;
        }

        private void ApplyDynamicTitleAndIcon()
        {
            var config = App.JekyllContext.LoadConfig();

            // Dynamic title from _config.yml
            if (config.TryGetValue("title", out var titleObj) && titleObj is string siteTitle && !string.IsNullOrWhiteSpace(siteTitle))
            {
                Title = $"{siteTitle} - BlogTools";
                AppTitleBar.Title = siteTitle;
            }
        }

        private void ApplyGlobalFont()
        {
            var settings = Services.StorageService.Load();
            var font = settings.AppFontFamily;

            if (string.IsNullOrWhiteSpace(font) && App.JekyllContext.LoadConfig().TryGetValue("blogtools_font", out var val))
            {
                font = val?.ToString();
            }

            if (!string.IsNullOrWhiteSpace(font))
            {
                FontFamily = new System.Windows.Media.FontFamily(font);
            }

            RootNavigation.FontFamily = SystemFonts.MessageFontFamily;
            AppTitleBar.FontFamily = SystemFonts.MessageFontFamily;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RootNavigation.Navigate(typeof(DashboardPage));
        }
    }
}
