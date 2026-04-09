using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BlogTools.Services;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace BlogTools
{
    public partial class AppSettingsPage : Page
    {
        private bool _isLoading;
        private double _targetDropdownScrollOffset = -1;
        private double _currentDropdownScrollOffset = -1;
        private ScrollViewer? _activeDropdownScrollViewer;
        private double _targetPageScrollOffset = -1;
        private double _currentPageScrollOffset = -1;
        private ScrollViewer? _activePageScrollViewer;

        public AppSettingsPage()
        {
            InitializeComponent();
            Loaded += AppSettingsPage_Loaded;
            Unloaded += AppSettingsPage_Unloaded;

            FontComboBox.DropDownOpened += (_, _) => Helpers.ScrollViewerHelper.SuppressScrollBubble = true;
            FontComboBox.DropDownClosed += (_, _) => Helpers.ScrollViewerHelper.SuppressScrollBubble = false;
            FontComboBox.AddHandler(
                UIElement.PreviewMouseWheelEvent,
                new System.Windows.Input.MouseWheelEventHandler(FontComboBox_PreviewMouseWheel),
                true);

            CompositionTarget.Rendering += OnCompositionTargetRendering;
        }

        private void AppSettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            CompositionTarget.Rendering -= OnCompositionTargetRendering;
        }

        private void AppSettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoading = true;

            var parentScrollViewer = FindVisualParent<ScrollViewer>(this);
            parentScrollViewer?.ScrollToTop();

            var settings = StorageService.Load();
            var config = App.JekyllContext.LoadConfig();

            CurrentPathBlock.Text = App.JekyllContext.BlogPath;
            RememberMetadataToggle.IsChecked = settings.RememberMetadataExpanded;
            KeepToolboxToolWhenPinnedToggle.IsChecked = settings.KeepToolboxToolWhenPinned;
            AutoUpdateModifiedTimeToggle.IsChecked = settings.AutoUpdateModifiedTime;
            SilentUpdateToggle.IsChecked = settings.SilentUpdate;
            ThemeToggle.IsChecked = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

            foreach (ComboBoxItem item in LanguageComboBox.Items)
            {
                if (item.Tag?.ToString() == settings.AppLanguage)
                {
                    LanguageComboBox.SelectedItem = item;
                    break;
                }
            }

            if (LanguageComboBox.SelectedItem == null)
            {
                LanguageComboBox.SelectedIndex = 0;
            }

            FontComboBox.Items.Clear();
            foreach (var family in Fonts.SystemFontFamilies.OrderBy(f => f.Source))
            {
                FontComboBox.Items.Add(new ComboBoxItem { Content = family.Source, FontFamily = family });
            }

            var font = settings.AppFontFamily;
            if (string.IsNullOrWhiteSpace(font))
            {
                font = GetStringValue(config, "blogtools_font");
            }

            if (string.IsNullOrWhiteSpace(font))
            {
                font = "Microsoft YaHei UI";
            }

            foreach (ComboBoxItem item in FontComboBox.Items)
            {
                if (item.Content?.ToString() == font)
                {
                    FontComboBox.SelectedItem = item;
                    break;
                }
            }

            try
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                var versionText = $"{version?.Major}.{version?.Minor}.{version?.Build}";
                VersionBlock.Text = string.Format(Application.Current.FindResource("CommonVersionCurrent").ToString()!, versionText);
            }
            catch
            {
                VersionBlock.Text = Application.Current.FindResource("CommonVersionDev").ToString()!;
            }

            _isLoading = false;
        }

        private async void ChangeBlogPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = Application.Current.FindResource("SettingsBtnChangePath").ToString()!
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var newPath = dialog.FolderName;
            if (!File.Exists(Path.Combine(newPath, "_config.yml")))
            {
                var message = new Wpf.Ui.Controls.MessageBox
                {
                    Title = Application.Current.FindResource("CommonError").ToString()!,
                    Content = Application.Current.FindResource("SettingsMsgInvalidRoot").ToString()!,
                    CloseButtonText = Application.Current.FindResource("CommonConfirm").ToString()!
                };
                await message.ShowDialogAsync();
                return;
            }

            var settings = StorageService.Load();
            settings.BlogPath = newPath;
            StorageService.Save(settings);

            App.JekyllContext = new JekyllService(newPath);
            App.GitContext = new GitService(newPath);
            App.StartFileWatcher(newPath);

            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RefreshShellFromCurrentBlogConfig();
            }

            AppSettingsPage_Loaded(sender, e);

            StatusInfo.Message = Application.Current.FindResource("SettingsMsgPathChanged").ToString()!;
            StatusInfo.Severity = Wpf.Ui.Controls.InfoBarSeverity.Success;
            StatusInfo.IsOpen = true;
        }

        private void RememberMetadata_Checked(object sender, RoutedEventArgs e) => SaveBoolSetting(s => s.RememberMetadataExpanded = true);
        private void RememberMetadata_Unchecked(object sender, RoutedEventArgs e) => SaveBoolSetting(s => s.RememberMetadataExpanded = false);
        private void KeepToolboxToolWhenPinned_Checked(object sender, RoutedEventArgs e) => SaveBoolSetting(s => s.KeepToolboxToolWhenPinned = true);
        private void KeepToolboxToolWhenPinned_Unchecked(object sender, RoutedEventArgs e) => SaveBoolSetting(s => s.KeepToolboxToolWhenPinned = false);
        private void AutoUpdateModifiedTime_Checked(object sender, RoutedEventArgs e) => SaveBoolSetting(s => s.AutoUpdateModifiedTime = true);
        private void AutoUpdateModifiedTime_Unchecked(object sender, RoutedEventArgs e) => SaveBoolSetting(s => s.AutoUpdateModifiedTime = false);
        private void SilentUpdate_Checked(object sender, RoutedEventArgs e) => SaveBoolSetting(s => s.SilentUpdate = true);
        private void SilentUpdate_Unchecked(object sender, RoutedEventArgs e) => SaveBoolSetting(s => s.SilentUpdate = false);

        private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading)
            {
                return;
            }

            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        }

        private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isLoading)
            {
                return;
            }

            ApplicationThemeManager.Apply(ApplicationTheme.Light);
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading)
            {
                return;
            }

            var selectedTag = (LanguageComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(selectedTag))
            {
                return;
            }

            var settings = StorageService.Load();
            settings.AppLanguage = selectedTag;
            StorageService.Save(settings);
            App.ApplyLanguage(selectedTag);
        }

        private void FontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading)
            {
                return;
            }

            var selectedFont = (FontComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrWhiteSpace(selectedFont))
            {
                return;
            }

            var settings = StorageService.Load();
            settings.AppFontFamily = selectedFont;
            StorageService.Save(settings);

            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ApplyGlobalFont(selectedFont);
            }
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            await PerformUpdateCheckAsync(isManual: true);
        }

        public async Task PerformUpdateCheckAsync(bool isManual = false)
        {
            CheckUpdateButton.IsEnabled = false;
            VersionBlock.Text = Application.Current.FindResource("SettingsMsgUpdateChecking").ToString()!;

            try
            {
                var (hasUpdate, latestVersion, downloadUrl, errorMsg) = await UpdateService.CheckForUpdateAsync();

                if (!string.IsNullOrEmpty(errorMsg))
                {
                    VersionBlock.Text = Application.Current.FindResource("SettingsMsgUpdateFailed").ToString()!;
                    if (isManual)
                    {
                        StatusInfo.Message = string.Format(Application.Current.FindResource("SettingsMsgUpdateError").ToString()!, errorMsg);
                        StatusInfo.Severity = Wpf.Ui.Controls.InfoBarSeverity.Error;
                        StatusInfo.IsOpen = true;
                    }
                    return;
                }

                if (!hasUpdate)
                {
                    var current = UpdateService.GetCurrentVersion();
                    var currentText = $"v{current.Major}.{current.Minor}.{current.Build}";
                    VersionBlock.Text = string.Format(Application.Current.FindResource("SettingsMsgUpdateLatest").ToString()!, currentText);
                    if (isManual)
                    {
                        StatusInfo.Message = VersionBlock.Text;
                        StatusInfo.Severity = Wpf.Ui.Controls.InfoBarSeverity.Success;
                        StatusInfo.IsOpen = true;
                    }
                    return;
                }

                var currentVersion = UpdateService.GetCurrentVersion();
                var currentVersionText = $"v{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}";
                VersionBlock.Text = string.Format(Application.Current.FindResource("CommonVersionCurrent").ToString()!, currentVersionText) +
                                    $"  ->  {Application.Current.FindResource("SettingsMsgUpdateFound").ToString()!}: {latestVersion}";

                var askDownload = new Wpf.Ui.Controls.MessageBox
                {
                    Title = Application.Current.FindResource("SettingsMsgUpdateFound").ToString()!,
                    Content = string.Format(Application.Current.FindResource("SettingsMsgAskDownload").ToString()!, latestVersion),
                    PrimaryButtonText = Application.Current.FindResource("SettingsBtnDownloadNow").ToString()!,
                    CloseButtonText = Application.Current.FindResource("SettingsBtnLater").ToString()!
                };

                if (await askDownload.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    return;
                }

                ProgressPanel.Visibility = Visibility.Visible;
                ProgressText.Text = string.Format(Application.Current.FindResource("SettingsMsgUpdateDownloading").ToString()!, 0);
                DownloadProgress.Value = 0;

                var progress = new Progress<int>(percent =>
                {
                    DownloadProgress.Value = percent;
                    ProgressText.Text = string.Format(Application.Current.FindResource("SettingsMsgUpdateDownloading").ToString()!, percent);
                });

                var zipPath = await UpdateService.DownloadUpdateAsync(downloadUrl, progress);
                ProgressText.Text = Application.Current.FindResource("SettingsMsgDownloadComplete").ToString()!;
                DownloadProgress.Value = 100;

                var settings = StorageService.Load();
                if (settings.SilentUpdate)
                {
                    ProgressText.Text = Application.Current.FindResource("SettingsMsgSilentUpdating").ToString()!;
                    await Task.Delay(500);
                    UpdateService.ApplyUpdate(zipPath);
                    return;
                }

                var askApply = new Wpf.Ui.Controls.MessageBox
                {
                    Title = Application.Current.FindResource("SettingsMsgDownloadComplete").ToString()!,
                    Content = Application.Current.FindResource("SettingsMsgAskApply").ToString()!,
                    PrimaryButtonText = Application.Current.FindResource("SettingsBtnApplyNow").ToString()!,
                    CloseButtonText = Application.Current.FindResource("SettingsBtnLater").ToString()!
                };

                if (await askApply.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    ProgressText.Text = Application.Current.FindResource("SettingsMsgSilentUpdating").ToString()!;
                    await Task.Delay(500);
                    UpdateService.ApplyUpdate(zipPath);
                }
                else
                {
                    ProgressPanel.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                VersionBlock.Text = string.Format(Application.Current.FindResource("SettingsMsgUpdateFailed").ToString()! + ": {0}", ex.Message);
                StatusInfo.Message = string.Format(Application.Current.FindResource("SettingsMsgUpdateError").ToString()!, ex.Message);
                StatusInfo.Severity = Wpf.Ui.Controls.InfoBarSeverity.Error;
                StatusInfo.IsOpen = true;
                ProgressPanel.Visibility = Visibility.Collapsed;
            }
            finally
            {
                CheckUpdateButton.IsEnabled = true;
            }
        }

        private void OpenGithubStar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/Metahumanz/JekyllCli",
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private void OnCompositionTargetRendering(object? sender, EventArgs e)
        {
            if (_activeDropdownScrollViewer != null && _targetDropdownScrollOffset >= 0 && _currentDropdownScrollOffset >= 0)
            {
                var diff = _targetDropdownScrollOffset - _currentDropdownScrollOffset;
                if (Math.Abs(diff) < 0.5)
                {
                    _currentDropdownScrollOffset = _targetDropdownScrollOffset;
                    _activeDropdownScrollViewer.ScrollToVerticalOffset(_currentDropdownScrollOffset);
                    _activeDropdownScrollViewer = null;
                }
                else
                {
                    _currentDropdownScrollOffset += diff * 0.2;
                    _activeDropdownScrollViewer.ScrollToVerticalOffset(_currentDropdownScrollOffset);
                }
            }

            if (_activePageScrollViewer != null && _targetPageScrollOffset >= 0 && _currentPageScrollOffset >= 0)
            {
                var diff = _targetPageScrollOffset - _currentPageScrollOffset;
                if (Math.Abs(diff) < 0.5)
                {
                    _currentPageScrollOffset = _targetPageScrollOffset;
                    _activePageScrollViewer.ScrollToVerticalOffset(_currentPageScrollOffset);
                    _targetPageScrollOffset = -1;
                    _activePageScrollViewer = null;
                }
                else
                {
                    _currentPageScrollOffset += diff * 0.18;
                    _activePageScrollViewer.ScrollToVerticalOffset(_currentPageScrollOffset);
                }
            }
        }

        private void PageScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (Helpers.ScrollViewerHelper.SuppressScrollBubble)
            {
                return;
            }

            var rootScrollViewer = FindVisualParent<ScrollViewer>(this);
            if (rootScrollViewer == null)
            {
                return;
            }

            e.Handled = true;
            _activePageScrollViewer = rootScrollViewer;

            if (_targetPageScrollOffset == -1)
            {
                _targetPageScrollOffset = rootScrollViewer.VerticalOffset;
                _currentPageScrollOffset = rootScrollViewer.VerticalOffset;
            }

            _targetPageScrollOffset -= e.Delta * 2.0;
            _targetPageScrollOffset = Math.Max(0, Math.Min(rootScrollViewer.ScrollableHeight, _targetPageScrollOffset));
        }

        private void FontComboBox_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (!FontComboBox.IsDropDownOpen)
            {
                _targetDropdownScrollOffset = -1;
                _currentDropdownScrollOffset = -1;
                _activeDropdownScrollViewer = null;
                return;
            }

            var popup = FindVisualChild<System.Windows.Controls.Primitives.Popup>(FontComboBox);
            if (popup?.Child is not FrameworkElement popupChild)
            {
                return;
            }

            var mousePosition = e.GetPosition(popupChild);
            var isOverPopup = mousePosition.X >= 0 && mousePosition.Y >= 0 &&
                              mousePosition.X <= popupChild.ActualWidth && mousePosition.Y <= popupChild.ActualHeight;

            if (!isOverPopup)
            {
                FontComboBox.IsDropDownOpen = false;
                return;
            }

            e.Handled = true;
            var dropdownScrollViewer = FindVisualChild<ScrollViewer>(popupChild);
            if (dropdownScrollViewer == null)
            {
                return;
            }

            if (_activeDropdownScrollViewer != dropdownScrollViewer)
            {
                _activeDropdownScrollViewer = dropdownScrollViewer;
                _currentDropdownScrollOffset = dropdownScrollViewer.VerticalOffset;
                _targetDropdownScrollOffset = dropdownScrollViewer.VerticalOffset;
            }

            _targetDropdownScrollOffset -= e.Delta * 2.0;
            _targetDropdownScrollOffset = Math.Max(0, Math.Min(dropdownScrollViewer.ScrollableHeight, _targetDropdownScrollOffset));
        }

        private void SaveBoolSetting(Action<AppSettings> update)
        {
            if (_isLoading)
            {
                return;
            }

            var settings = StorageService.Load();
            update(settings);
            StorageService.Save(settings);
        }

        private static string GetStringValue(System.Collections.Generic.Dictionary<string, object> dict, string key) =>
            dict.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            var childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                var nested = FindVisualChild<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null)
            {
                return null;
            }

            if (parent is T typedParent)
            {
                return typedParent;
            }

            return FindVisualParent<T>(parent);
        }
    }
}
