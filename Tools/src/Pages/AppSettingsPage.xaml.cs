using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BlogTools.Models;
using BlogTools.Services;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using WpfUiControls = Wpf.Ui.Controls;
using Swc = System.Windows.Controls;
using Sw = System.Windows;

namespace BlogTools
{
    public partial class AppSettingsPage : Page
    {
        private bool _isLoading;
        private bool _isLoadingAi;
        private double _targetDropdownScrollOffset = -1;
        private double _currentDropdownScrollOffset = -1;
        private ScrollViewer? _activeDropdownScrollViewer;
        private double _targetPageScrollOffset = -1;
        private double _currentPageScrollOffset = -1;
        private ScrollViewer? _activePageScrollViewer;

        // AI Commit state
        private List<AiCommitProfile> _aiProfiles = new();
        private int _aiActiveIndex = -1;
        private List<string> _aiFetchedModels = new();

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

            foreach (ComboBoxItem item in ThemeModeComboBox.Items)
            {
                if (item.Tag?.ToString() == App.NormalizeThemeMode(settings.ThemeMode))
                {
                    ThemeModeComboBox.SelectedItem = item;
                    break;
                }
            }

            if (ThemeModeComboBox.SelectedItem == null)
            {
                ThemeModeComboBox.SelectedIndex = 0;
            }

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

            // Load AI commit settings
            LoadAiSettings();
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

        private void ThemeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading)
            {
                return;
            }

            var selectedTag = (ThemeModeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(selectedTag))
            {
                return;
            }

            var settings = StorageService.Load();
            settings.ThemeMode = App.NormalizeThemeMode(selectedTag);
            StorageService.Save(settings);
            App.ApplyThemeMode(settings.ThemeMode);
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

                if (await askDownload.ShowDialogAsync() != WpfUiControls.MessageBoxResult.Primary)
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

                if (await askApply.ShowDialogAsync() == WpfUiControls.MessageBoxResult.Primary)
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

        // ── AI Commit handlers ──────────────────────────────────

        private void LoadAiSettings()
        {
            _isLoadingAi = true;

            var settings = StorageService.Load();
            _aiProfiles = settings.AiCommitProfiles ?? new List<AiCommitProfile>();
            _aiActiveIndex = settings.AiCommitActiveProfileIndex;

            // Ensure at least one default profile exists
            if (_aiProfiles.Count == 0)
            {
                _aiProfiles.Add(CreateDefaultProfile());
                _aiActiveIndex = 0;
                SaveAiSettingsToDisk();
            }

            if (_aiActiveIndex < 0 || _aiActiveIndex >= _aiProfiles.Count)
                _aiActiveIndex = 0;

            // Populate profile combo
            AiProfileComboBox.Items.Clear();
            foreach (var p in _aiProfiles)
                AiProfileComboBox.Items.Add(p.Name);
            AiProfileComboBox.SelectedIndex = _aiActiveIndex;

            // Populate provider combo
            AiProviderComboBox.Items.Clear();
            foreach (var kv in AiProviderPresets.Presets)
                AiProviderComboBox.Items.Add(new ComboBoxItem { Content = kv.Value.Name, Tag = kv.Key });
            AiProviderComboBox.SelectedIndex = 0;

            // Populate style combo
            foreach (ComboBoxItem item in AiCommitStyleComboBox.Items)
            {
                if (item.Tag?.ToString() == settings.AiCommitStyle.ToString())
                {
                    AiCommitStyleComboBox.SelectedItem = item;
                    break;
                }
            }
            if (AiCommitStyleComboBox.SelectedItem == null)
                AiCommitStyleComboBox.SelectedIndex = 0;

            // Populate language combo
            foreach (ComboBoxItem item in AiLanguageComboBox.Items)
            {
                if (item.Tag?.ToString() == settings.AiCommitLanguage.ToString())
                {
                    AiLanguageComboBox.SelectedItem = item;
                    break;
                }
            }
            if (AiLanguageComboBox.SelectedItem == null)
                AiLanguageComboBox.SelectedIndex = 0;

            AiCommitPostsToggle.IsChecked = settings.AiCommitEnabledPosts;
            AiCommitSettingsToggle.IsChecked = settings.AiCommitEnabledSettings;

            LoadActiveProfileIntoUi();

            _isLoadingAi = false;
        }

        private static AiCommitProfile CreateDefaultProfile()
        {
            var (name, baseUrl, defaultModel, _, _) = AiProviderPresets.Presets[AiProviderPresets.PresetOpenAI];
            return new AiCommitProfile
            {
                Name = name,
                Provider = AiProviderPresets.PresetOpenAI,
                BaseUrl = baseUrl,
                Model = defaultModel
            };
        }

        private void LoadActiveProfileIntoUi()
        {
            if (_aiActiveIndex < 0 || _aiActiveIndex >= _aiProfiles.Count)
                return;

            var profile = _aiProfiles[_aiActiveIndex];

            // Select provider
            foreach (ComboBoxItem item in AiProviderComboBox.Items)
            {
                if (item.Tag?.ToString() == profile.Provider)
                {
                    AiProviderComboBox.SelectedItem = item;
                    break;
                }
            }

            AiBaseUrlBox.Text = profile.BaseUrl;
            AiModelBox.Text = profile.Model;
            AiApiKeyBox.Text = DpapiEncryption.Decrypt(profile.EncryptedKey);

            // Show suggested models for this provider
            UpdateSuggestedModels(profile.Provider);

            // Check deprecation
            CheckDeprecationWarning(profile.Provider, profile.Model);
        }

        private void UpdateSuggestedModels(string provider)
        {
            AiSuggestedModelsPanel.Children.Clear();
            AiSuggestedModelsPanel.Visibility = Visibility.Collapsed;

            if (_aiFetchedModels.Count > 0)
            {
                AiSuggestedModelsPanel.Visibility = Visibility.Visible;
                var label = new Swc.TextBlock
                {
                    Text = Application.Current.FindResource("AiCommitLabelSuggestions").ToString()! + " ",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Sw.Thickness(0, 0, 4, 0)
                };
                AiSuggestedModelsPanel.Children.Add(label);

                foreach (var model in _aiFetchedModels.Take(15))
                {
                    var btn = new Swc.Button
                    {
                        Content = model,
                        FontSize = 11,
                        Padding = new Sw.Thickness(6, 2, 6, 2),
                        Margin = new Sw.Thickness(0, 0, 4, 4),
                        Style = (Style)FindResource("LiftedUiButtonStyle")
                    };
                    btn.Click += (_, _) => AiModelBox.Text = model;
                    AiSuggestedModelsPanel.Children.Add(btn);
                }
            }
            else if (AiProviderPresets.Presets.TryGetValue(provider, out var preset))
            {
                var suggestions = preset.SuggestedModels;
                if (suggestions.Count > 0)
                {
                    AiSuggestedModelsPanel.Visibility = Visibility.Visible;
                    var label = new Swc.TextBlock
                    {
                        Text = Application.Current.FindResource("AiCommitLabelSuggestions").ToString()! + " ",
                        FontSize = 12,
                        Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Sw.Thickness(0, 0, 4, 0)
                    };
                    AiSuggestedModelsPanel.Children.Add(label);

                    foreach (var model in suggestions)
                    {
                        var btn = new Swc.Button
                        {
                            Content = model,
                            FontSize = 11,
                            Padding = new Sw.Thickness(6, 2, 6, 2),
                            Margin = new Sw.Thickness(0, 0, 4, 4),
                            Style = (Style)FindResource("LiftedUiButtonStyle")
                        };
                        btn.Click += (_, _) => AiModelBox.Text = model;
                        AiSuggestedModelsPanel.Children.Add(btn);
                    }
                }
            }
        }

        private void CheckDeprecationWarning(string provider, string model)
        {
            AiDeprecationInfo.IsOpen = false;
            if (provider == AiProviderPresets.PresetDeepSeek &&
                !string.IsNullOrWhiteSpace(model) &&
                AiProviderPresets.DeepSeekDeprecatedModels.Contains(model.Trim().ToLowerInvariant()))
            {
                AiDeprecationInfo.IsOpen = true;
            }
        }

        private void SaveCurrentProfileFromUi()
        {
            if (_aiActiveIndex < 0 || _aiActiveIndex >= _aiProfiles.Count)
                return;

            var profile = _aiProfiles[_aiActiveIndex];
            var providerTag = (AiProviderComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;

            profile.Provider = providerTag;
            profile.BaseUrl = AiBaseUrlBox.Text?.Trim() ?? string.Empty;
            profile.Model = AiModelBox.Text?.Trim() ?? string.Empty;

            var keyText = AiApiKeyBox.Text?.Trim() ?? string.Empty;
            profile.EncryptedKey = string.IsNullOrWhiteSpace(keyText)
                ? string.Empty
                : DpapiEncryption.Encrypt(keyText);

            // Also update the profile name in the combo box
            if (AiProfileComboBox.SelectedIndex >= 0 && AiProfileComboBox.SelectedIndex < AiProfileComboBox.Items.Count)
                AiProfileComboBox.Items[AiProfileComboBox.SelectedIndex] = profile.Name;

            // Check deprecation after model update
            CheckDeprecationWarning(profile.Provider, profile.Model);
        }

        private void SaveAiSettingsToDisk()
        {
            var settings = StorageService.Load();
            settings.AiCommitProfiles = _aiProfiles;
            settings.AiCommitActiveProfileIndex = _aiActiveIndex;
            StorageService.Save(settings);

            // Also persist the in-memory profile fields
            SaveCurrentProfileFromUi();
            StorageService.Save(settings);
        }

        // ── Event handlers ──────────────────────────────────────

        private void AiProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingAi) return;

            SaveCurrentProfileFromUi();
            SaveAiSettingsToDisk();

            _aiActiveIndex = AiProfileComboBox.SelectedIndex;
            if (_aiActiveIndex >= 0 && _aiActiveIndex < _aiProfiles.Count)
            {
                _aiFetchedModels.Clear();
                LoadActiveProfileIntoUi();
            }
        }

        private void AiAddProfile_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentProfileFromUi();

            var newProfile = CreateDefaultProfile();
            newProfile.Name = $"Profile {_aiProfiles.Count + 1}";
            _aiProfiles.Add(newProfile);
            _aiActiveIndex = _aiProfiles.Count - 1;

            _isLoadingAi = true;
            AiProfileComboBox.Items.Add(newProfile.Name);
            AiProfileComboBox.SelectedIndex = _aiActiveIndex;
            _isLoadingAi = false;

            _aiFetchedModels.Clear();
            LoadActiveProfileIntoUi();
            SaveAiSettingsToDisk();
        }

        private async void AiRenameProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_aiActiveIndex < 0 || _aiActiveIndex >= _aiProfiles.Count)
                return;

            var profile = _aiProfiles[_aiActiveIndex];
            var newName = await ShowTextInputDialogAsync(
                Application.Current.FindResource("AiCommitDialogRenameTitle").ToString()!,
                Application.Current.FindResource("AiCommitDialogRenameMsg").ToString()!,
                profile.Name);

            if (!string.IsNullOrWhiteSpace(newName) && newName != profile.Name)
            {
                profile.Name = newName;
                AiProfileComboBox.Items[_aiActiveIndex] = newName;
                SaveAiSettingsToDisk();
            }
        }

        private async void AiDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_aiProfiles.Count <= 1)
            {
                AiStatusText.Text = Application.Current.FindResource("AiCommitMsgCantDeleteLast").ToString()!;
                return;
            }

            if (_aiActiveIndex < 0 || _aiActiveIndex >= _aiProfiles.Count)
                return;

            var profile = _aiProfiles[_aiActiveIndex];
            var msg = new WpfUiControls.MessageBox
            {
                Title = Application.Current.FindResource("AiCommitDialogDeleteTitle").ToString()!,
                Content = string.Format(Application.Current.FindResource("AiCommitDialogDeleteMsg").ToString()!, profile.Name),
                PrimaryButtonText = Application.Current.FindResource("CommonConfirm").ToString()!,
                CloseButtonText = Application.Current.FindResource("CommonCancel").ToString()!
            };

            if (await msg.ShowDialogAsync() != WpfUiControls.MessageBoxResult.Primary)
                return;

            _aiProfiles.RemoveAt(_aiActiveIndex);
            AiProfileComboBox.Items.RemoveAt(_aiActiveIndex);

            if (_aiActiveIndex >= _aiProfiles.Count)
                _aiActiveIndex = _aiProfiles.Count - 1;
            if (_aiActiveIndex < 0) _aiActiveIndex = 0;

            _isLoadingAi = true;
            AiProfileComboBox.SelectedIndex = _aiActiveIndex;
            _isLoadingAi = false;

            _aiFetchedModels.Clear();
            LoadActiveProfileIntoUi();
            SaveAiSettingsToDisk();
        }

        private void AiProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingAi) return;

            var tag = (AiProviderComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(tag)) return;

            // Auto-fill defaults from preset
            if (AiProviderPresets.Presets.TryGetValue(tag, out var preset))
            {
                AiBaseUrlBox.Text = preset.BaseUrl;
                if (!string.IsNullOrWhiteSpace(preset.DefaultModel))
                    AiModelBox.Text = preset.DefaultModel;
                else if (AiProviderPresets.NoDefaultModelProviders.Contains(tag))
                    AiModelBox.Text = string.Empty;
            }

            _aiFetchedModels.Clear();
            UpdateSuggestedModels(tag);
            SaveCurrentProfileFromUi();
        }

        private async void AiRefreshModels_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentProfileFromUi();

            var profile = _aiProfiles[_aiActiveIndex];
            AiRefreshModelsBtn.IsEnabled = false;
            AiStatusText.Text = Application.Current.FindResource("AiCommitMsgRefreshingModels").ToString()!;

            try
            {
                var decryptedKey = DpapiEncryption.Decrypt(profile.EncryptedKey);
                var models = await AiCommitMessageService.FetchModelsAsync(profile, decryptedKey);

                if (models != null && models.Count > 0)
                {
                    _aiFetchedModels = models;
                    AiStatusText.Text = string.Format(
                        Application.Current.FindResource("AiCommitMsgModelsFetched").ToString()!, models.Count);
                    UpdateSuggestedModels(profile.Provider);
                }
                else
                {
                    AiStatusText.Text = Application.Current.FindResource("AiCommitMsgModelsFetchFailed").ToString()!;
                }
            }
            catch (Exception ex)
            {
                AiStatusText.Text = string.Format(
                    Application.Current.FindResource("AiCommitMsgError").ToString()!, ex.Message);
            }
            finally
            {
                AiRefreshModelsBtn.IsEnabled = true;
            }
        }

        private async void AiTestGenerate_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentProfileFromUi();
            SaveAiSettingsToDisk();

            var profile = _aiProfiles[_aiActiveIndex];
            if (string.IsNullOrWhiteSpace(profile.BaseUrl))
            {
                AiStatusText.Text = Application.Current.FindResource("AiCommitMsgNeedBaseUrl").ToString()!;
                return;
            }

            if (string.IsNullOrWhiteSpace(profile.Model))
            {
                AiStatusText.Text = Application.Current.FindResource("AiCommitMsgNeedModel").ToString()!;
                return;
            }

            AiTestGenerateBtn.IsEnabled = false;
            AiStatusText.Text = Application.Current.FindResource("AiCommitMsgTesting").ToString()!;

            try
            {
                var decryptedKey = DpapiEncryption.Decrypt(profile.EncryptedKey);
                var (success, text, error) = await AiCommitMessageService.TestGenerateAsync(profile, decryptedKey);

                if (success)
                {
                    AiStatusText.Text = string.Format(
                        Application.Current.FindResource("AiCommitMsgTestSuccess").ToString()!, text);
                }
                else
                {
                    AiStatusText.Text = string.Format(
                        Application.Current.FindResource("AiCommitMsgTestFailed").ToString()!, error);
                }
            }
            catch (Exception ex)
            {
                AiStatusText.Text = string.Format(
                    Application.Current.FindResource("AiCommitMsgError").ToString()!, ex.Message);
            }
            finally
            {
                AiTestGenerateBtn.IsEnabled = true;
            }
        }

        private void AiSaveProfile_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentProfileFromUi();
            SaveAiSettingsToDisk();
            AiStatusText.Text = Application.Current.FindResource("AiCommitMsgProfileSaved").ToString()!;

            // Auto-enable article AI commit on first valid profile save
            var settings = StorageService.Load();
            if (!settings.AiCommitEnabledPosts && !settings.AiCommitEnabledSettings)
            {
                settings.AiCommitEnabledPosts = true;
                StorageService.Save(settings);
                AiCommitPostsToggle.IsChecked = true;
            }
        }

        private void AiCommitStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingAi) return;
            var tag = (AiCommitStyleComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (Enum.TryParse<AiCommitStyle>(tag, out var style))
                SaveAiSetting(s => s.AiCommitStyle = style);
        }

        private void AiLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingAi) return;
            var tag = (AiLanguageComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (Enum.TryParse<AiCommitLanguage>(tag, out var lang))
                SaveAiSetting(s => s.AiCommitLanguage = lang);
        }

        private void AiCommitPosts_Checked(object sender, RoutedEventArgs e) => SaveAiSetting(s => s.AiCommitEnabledPosts = true);
        private void AiCommitPosts_Unchecked(object sender, RoutedEventArgs e) => SaveAiSetting(s => s.AiCommitEnabledPosts = false);
        private void AiCommitSettings_Checked(object sender, RoutedEventArgs e) => SaveAiSetting(s => s.AiCommitEnabledSettings = true);
        private void AiCommitSettings_Unchecked(object sender, RoutedEventArgs e) => SaveAiSetting(s => s.AiCommitEnabledSettings = false);

        private void SaveAiSetting(Action<AppSettings> update)
        {
            if (_isLoadingAi) return;
            var settings = StorageService.Load();
            update(settings);
            StorageService.Save(settings);
        }

        private async Task<string> ShowTextInputDialogAsync(string title, string message, string defaultText)
        {
            var textBox = new System.Windows.Controls.TextBox
            {
                Text = defaultText,
                Margin = new Sw.Thickness(0, 8, 0, 0),
                MinWidth = 300
            };

            var stackPanel = new StackPanel();
            stackPanel.Children.Add(new Swc.TextBlock { Text = message, TextWrapping = Sw.TextWrapping.Wrap });
            stackPanel.Children.Add(textBox);

            var dialog = new Wpf.Ui.Controls.ContentDialog
            {
                Title = title,
                Content = stackPanel,
                PrimaryButtonText = Application.Current.FindResource("CommonConfirm").ToString()!,
                CloseButtonText = Application.Current.FindResource("CommonCancel").ToString()!,
                DefaultButton = Wpf.Ui.Controls.ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            return result == Wpf.Ui.Controls.ContentDialogResult.Primary ? textBox.Text.Trim() : string.Empty;
        }
    }
}
