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

            // Dynamic icon from blog directory avatar
            SetIconFromAvatar(config);
        }

        private void SetIconFromAvatar(System.Collections.Generic.Dictionary<string, object> config)
        {
            try
            {
                if (config.TryGetValue("avatar", out var avatarObj) && avatarObj is string avatarPath && !string.IsNullOrWhiteSpace(avatarPath))
                {
                    // avatar might be "/assets/img/avatar.png" or "assets/img/avatar.png"
                    avatarPath = avatarPath.TrimStart('/');
                    var fullAvatarPath = Path.Combine(App.JekyllContext.BlogPath, avatarPath);

                    if (File.Exists(fullAvatarPath))
                    {
                        var uri = new Uri(fullAvatarPath, UriKind.Absolute);
                        Icon = new System.Windows.Media.Imaging.BitmapImage(uri);
                        return;
                    }
                }

                // Avatar not found - show a one-time hint after window loads
                Loaded += async (_, _) =>
                {
                    var msg = new Wpf.Ui.Controls.MessageBox { Title = "BlogTools 提示", Content = "未检测到站点头像 (Avatar)，是否前往「引擎设置」配置？", PrimaryButtonText = "确定", CloseButtonText = "取消" };
                    var result = await msg.ShowDialogAsync();
                    
                    if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                    {
                        RootNavigation.Navigate(typeof(SettingsPage));
                    }
                };
            }
            catch { }
        }

        private void ApplyGlobalFont()
        {
            var font = App.JekyllContext.LoadConfig().TryGetValue("blogtools_font", out var val) ? val?.ToString() : "Microsoft YaHei UI";
            if (!string.IsNullOrWhiteSpace(font)) FontFamily = new System.Windows.Media.FontFamily(font);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RootNavigation.Navigate(typeof(DashboardPage));
        }
    }
}