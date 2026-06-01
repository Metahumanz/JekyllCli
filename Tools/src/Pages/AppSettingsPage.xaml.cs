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
        private bool _isSyncingAiKey;

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
            PopulateAiProviderCombo();
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
            try
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
                    AiProfileComboBox.Items.Add(p.Name ?? GetResourceString("AiCommitDefaultUnnamedProfile", "Unnamed"));
                AiProfileComboBox.SelectedIndex = _aiActiveIndex;

                // Populate provider combo
                PopulateAiProviderCombo();

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

                // Populate behavior combo
                foreach (ComboBoxItem item in AiBehaviorComboBox.Items)
                {
                    if (item.Tag?.ToString() == settings.AiCommitBehavior.ToString())
                    {
                        AiBehaviorComboBox.SelectedItem = item;
                        break;
                    }
                }
                if (AiBehaviorComboBox.SelectedItem == null)
                    AiBehaviorComboBox.SelectedIndex = 0;

                AiCommitPostsToggle.IsChecked = settings.AiCommitEnabledPosts;
                AiCommitSettingsToggle.IsChecked = settings.AiCommitEnabledSettings;

                LoadActiveProfileIntoUi();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadAiSettings failed: {ex}");
            }
            finally
            {
                _isLoadingAi = false;
            }
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

        private void PopulateAiProviderCombo()
        {
            var selectedProvider = (AiProviderComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(selectedProvider) &&
                _aiActiveIndex >= 0 && _aiActiveIndex < _aiProfiles.Count)
            {
                selectedProvider = _aiProfiles[_aiActiveIndex].Provider;
            }

            var wasLoading = _isLoadingAi;
            _isLoadingAi = true;
            try
            {
                AiProviderComboBox.Items.Clear();
                foreach (var kv in AiProviderPresets.Presets)
                {
                    AiProviderComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = GetProviderDisplayName(kv.Key, kv.Value.Name),
                        Tag = kv.Key
                    });
                }

                foreach (ComboBoxItem item in AiProviderComboBox.Items)
                {
                    if (item.Tag?.ToString() == selectedProvider)
                    {
                        AiProviderComboBox.SelectedItem = item;
                        return;
                    }
                }

                AiProviderComboBox.SelectedIndex = 0;
            }
            finally
            {
                _isLoadingAi = wasLoading;
            }
        }

        private static string GetProviderDisplayName(string provider, string fallback) =>
            provider switch
            {
                AiProviderPresets.PresetOpenAI => GetResourceString("AiCommitProviderOpenAI", fallback),
                AiProviderPresets.PresetDeepSeek => GetResourceString("AiCommitProviderDeepSeek", fallback),
                AiProviderPresets.PresetAliyun => GetResourceString("AiCommitProviderAliyun", fallback),
                AiProviderPresets.PresetMoonshot => GetResourceString("AiCommitProviderMoonshot", fallback),
                AiProviderPresets.PresetZhipu => GetResourceString("AiCommitProviderZhipu", fallback),
                AiProviderPresets.PresetOpenRouter => GetResourceString("AiCommitProviderOpenRouter", fallback),
                AiProviderPresets.PresetSiliconFlow => GetResourceString("AiCommitProviderSiliconFlow", fallback),
                AiProviderPresets.PresetOllama => GetResourceString("AiCommitProviderOllama", fallback),
                AiProviderPresets.PresetLmStudio => GetResourceString("AiCommitProviderLmStudio", fallback),
                AiProviderPresets.PresetCustom => GetResourceString("AiCommitProviderCustom", fallback),
                _ => fallback
            };

        private static string GetResourceString(string key, string fallback) =>
            Application.Current.TryFindResource(key) as string ?? fallback;

        private static string FormatResourceString(string key, string fallback, params object[] args) =>
            string.Format(GetResourceString(key, fallback), args);

        private void LoadActiveProfileIntoUi()
        {
            if (_aiActiveIndex < 0 || _aiActiveIndex >= _aiProfiles.Count)
                return;

            var wasLoading = _isLoadingAi;
            _isLoadingAi = true;
            try
            {
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
                AiModelsUrlBox.Text = profile.ModelsUrl;
                SetApiKeyInUi(DpapiEncryption.Decrypt(profile.EncryptedKey));

                // Show suggested models for this provider
                UpdateSuggestedModels(profile.Provider);

                // Check deprecation
                CheckDeprecationWarning(profile.Provider, profile.Model);
            }
            finally
            {
                _isLoadingAi = wasLoading;
            }
        }

        private void UpdateSuggestedModels(string provider)
        {
            try
            {
                UpdateModelChoices(provider);
                AiSuggestedModelsPanel.Children.Clear();
                AiSuggestedModelsPanel.Visibility = Visibility.Collapsed;

                var labelText = Application.Current.TryFindResource("AiCommitLabelSuggestions") as string ?? "Suggestions:";
                var fgBrush = TryFindResource("TextFillColorSecondaryBrush") as Brush;
                var btnStyle = TryFindResource("LiftedUiButtonStyle") as Style;

                if (_aiFetchedModels.Count > 0)
                {
                    AiSuggestedModelsPanel.Visibility = Visibility.Visible;
                    var label = new Swc.TextBlock
                    {
                        Text = labelText + " ",
                        FontSize = 12,
                        Foreground = fgBrush,
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
                            Margin = new Sw.Thickness(0, 0, 4, 4)
                        };
                        if (btnStyle != null) btn.Style = btnStyle;
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
                            Text = labelText + " ",
                            FontSize = 12,
                            Foreground = fgBrush,
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
                                Margin = new Sw.Thickness(0, 0, 4, 4)
                            };
                            if (btnStyle != null) btn.Style = btnStyle;
                            btn.Click += (_, _) => AiModelBox.Text = model;
                            AiSuggestedModelsPanel.Children.Add(btn);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateSuggestedModels failed: {ex}");
            }
        }

        private void UpdateModelChoices(string provider)
        {
            var currentModel = AiModelBox.Text;
            IEnumerable<string> models = _aiFetchedModels;

            if (_aiFetchedModels.Count == 0 &&
                AiProviderPresets.Presets.TryGetValue(provider, out var preset))
            {
                models = new[] { preset.DefaultModel }
                    .Concat(preset.SuggestedModels);
            }

            AiModelBox.ItemsSource = models
                .Where(model => !string.IsNullOrWhiteSpace(model))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            AiModelBox.Text = currentModel;
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

        private bool TrySaveCurrentProfileFromUi(out string error)
        {
            error = string.Empty;
            if (_aiActiveIndex < 0 || _aiActiveIndex >= _aiProfiles.Count)
                return false;

            try
            {
                var profile = _aiProfiles[_aiActiveIndex];
                var providerTag = (AiProviderComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;

                profile.Provider = providerTag;
                profile.BaseUrl = AiBaseUrlBox.Text?.Trim() ?? string.Empty;
                profile.Model = AiModelBox.Text?.Trim() ?? string.Empty;
                profile.ModelsUrl = AiModelsUrlBox.Text?.Trim() ?? string.Empty;

                var keyText = GetApiKeyFromUi();
                var encryptedKey = string.IsNullOrWhiteSpace(keyText)
                    ? string.Empty
                    : DpapiEncryption.Encrypt(keyText);
                if (!string.IsNullOrWhiteSpace(keyText) && string.IsNullOrWhiteSpace(encryptedKey))
                {
                    error = GetResourceString("AiCommitMsgKeyEncryptFailed", "Failed to protect the API Key.");
                    return false;
                }

                profile.EncryptedKey = encryptedKey;

                // Check deprecation after model update
                CheckDeprecationWarning(profile.Provider, profile.Model);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveCurrentProfileFromUi failed: {ex}");
                error = ex.Message;
                return false;
            }
        }

        private void SaveAiSettingsToDisk()
        {
            try
            {
                var settings = StorageService.Load();
                settings.AiCommitProfiles = _aiProfiles;
                settings.AiCommitActiveProfileIndex = _aiActiveIndex;
                StorageService.Save(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveAiSettingsToDisk failed: {ex}");
            }
        }

        private void SetApiKeyInUi(string apiKey)
        {
            _isSyncingAiKey = true;
            try
            {
                AiApiKeyPasswordBox.Password = apiKey;
                AiApiKeyVisibleBox.Text = apiKey;
            }
            finally
            {
                _isSyncingAiKey = false;
            }
        }

        private string GetApiKeyFromUi() =>
            AiShowApiKeyCheckBox.IsChecked == true
                ? AiApiKeyVisibleBox.Text ?? string.Empty
                : AiApiKeyPasswordBox.Password ?? string.Empty;

        private static bool TryValidateProfile(AiCommitProfile profile, bool requireModel, out string error)
        {
            if (!TryValidateHttpUrl(profile.BaseUrl))
            {
                error = GetResourceString("AiCommitMsgInvalidBaseUrl", "Please enter a valid HTTP or HTTPS Base URL.");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(profile.ModelsUrl) && !TryValidateHttpUrl(profile.ModelsUrl))
            {
                error = GetResourceString("AiCommitMsgInvalidModelsUrl", "Please enter a valid HTTP or HTTPS Models URL.");
                return false;
            }

            if (requireModel && string.IsNullOrWhiteSpace(profile.Model))
            {
                error = GetResourceString("AiCommitMsgNeedModel", "Please fill in or select a model.");
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidateHttpUrl(string? value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        private bool TryGetValidActiveProfile(out string error)
        {
            if (_aiActiveIndex < 0 || _aiActiveIndex >= _aiProfiles.Count)
            {
                error = GetResourceString("AiCommitMsgNeedProfile", "Please configure an AI profile first.");
                return false;
            }

            return TryValidateProfile(_aiProfiles[_aiActiveIndex], requireModel: true, out error);
        }

        private void ShowAiError(string error)
        {
            AiStatusText.Text = FormatResourceString("AiCommitMsgError", "Error: {0}", error);
        }

        // ── Event handlers ──────────────────────────────────────

        private void AiProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingAi) return;

            var newIndex = AiProfileComboBox.SelectedIndex;
            if (newIndex < 0 || newIndex >= _aiProfiles.Count)
            {
                // Editable ComboBox temporarily clears selection while the user types a new name.
                if (AiProfileComboBox.IsKeyboardFocusWithin)
                    return;

                _isLoadingAi = true;
                AiProfileComboBox.SelectedIndex = _aiActiveIndex;
                _isLoadingAi = false;
                return;
            }

            if (!TrySaveCurrentProfileFromUi(out var error))
            {
                ShowAiError(error);
                _isLoadingAi = true;
                AiProfileComboBox.SelectedIndex = _aiActiveIndex;
                _isLoadingAi = false;
                return;
            }

            _aiActiveIndex = newIndex;
            if (_aiActiveIndex >= 0 && _aiActiveIndex < _aiProfiles.Count)
            {
                _aiFetchedModels.Clear();
                LoadActiveProfileIntoUi();
            }

            SaveAiSettingsToDisk();
        }

        private void AiProfileComboBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && CommitInlineProfileName())
                e.Handled = true;
        }

        private void AiProfileComboBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (_isLoadingAi || AiProfileComboBox.IsKeyboardFocusWithin)
                return;

            CommitInlineProfileName();
        }

        private bool CommitInlineProfileName()
        {
            if (_aiActiveIndex < 0 || _aiActiveIndex >= _aiProfiles.Count)
                return false;

            var profile = _aiProfiles[_aiActiveIndex];
            var newName = AiProfileComboBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(newName))
            {
                UpdateProfileComboItemName(profile);
                AiStatusText.Text = GetResourceString("AiCommitMsgNeedProfileName", "Please enter a profile name.");
                return true;
            }

            if (newName == profile.Name)
                return false;

            profile.Name = newName;
            UpdateProfileComboItemName(profile);
            SaveAiSettingsToDisk();
            AiStatusText.Text = GetResourceString("AiCommitMsgProfileRenamed", "Profile renamed.");
            return true;
        }

        private void AiAddProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TrySaveCurrentProfileFromUi(out var error))
                {
                    ShowAiError(error);
                    return;
                }

                var newProfile = CreateDefaultProfile();
                newProfile.Name = FormatResourceString("AiCommitDefaultProfileName", "Profile {0}", _aiProfiles.Count + 1);
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AiAddProfile_Click failed: {ex}");
                ShowAiError(ex.Message);
            }
        }

        private async void AiRenameProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_aiActiveIndex < 0 || _aiActiveIndex >= _aiProfiles.Count)
                    return;

                if (!TrySaveCurrentProfileFromUi(out var error))
                {
                    ShowAiError(error);
                    return;
                }

                var profile = _aiProfiles[_aiActiveIndex];
                var newName = await ShowTextInputDialogAsync(
                    Application.Current.TryFindResource("AiCommitDialogRenameTitle") as string ?? "Rename Profile",
                    Application.Current.TryFindResource("AiCommitDialogRenameMsg") as string ?? "Enter a new profile name:",
                    profile.Name);

                if (!string.IsNullOrWhiteSpace(newName) && newName != profile.Name)
                {
                    profile.Name = newName;
                    UpdateProfileComboItemName(profile);
                    SaveAiSettingsToDisk();
                    AiStatusText.Text = GetResourceString("AiCommitMsgProfileRenamed", "Profile renamed.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AiRenameProfile_Click failed: {ex}");
                ShowAiError(ex.Message);
            }
        }

        private async void AiDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_aiProfiles.Count <= 1)
                {
                    AiStatusText.Text = Application.Current.TryFindResource("AiCommitMsgCantDeleteLast") as string ?? "Cannot delete the last profile.";
                    return;
                }

                if (_aiActiveIndex < 0 || _aiActiveIndex >= _aiProfiles.Count)
                    return;

                var profile = _aiProfiles[_aiActiveIndex];
                var msg = new WpfUiControls.MessageBox
                {
                    Title = Application.Current.TryFindResource("AiCommitDialogDeleteTitle") as string ?? "Delete Profile",
                    Content = string.Format(
                        Application.Current.TryFindResource("AiCommitDialogDeleteMsg") as string ?? "Delete profile \"{0}\"?",
                        profile.Name),
                    PrimaryButtonText = Application.Current.TryFindResource("CommonConfirm") as string ?? "Confirm",
                    CloseButtonText = Application.Current.TryFindResource("CommonCancel") as string ?? "Cancel"
                };

                if (await msg.ShowDialogAsync() != WpfUiControls.MessageBoxResult.Primary)
                    return;

                _isLoadingAi = true;
                _aiProfiles.RemoveAt(_aiActiveIndex);
                AiProfileComboBox.Items.RemoveAt(_aiActiveIndex);

                if (_aiActiveIndex >= _aiProfiles.Count)
                    _aiActiveIndex = _aiProfiles.Count - 1;
                if (_aiActiveIndex < 0) _aiActiveIndex = 0;

                AiProfileComboBox.SelectedIndex = _aiActiveIndex;
                _isLoadingAi = false;

                _aiFetchedModels.Clear();
                LoadActiveProfileIntoUi();
                SaveAiSettingsToDisk();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AiDeleteProfile_Click failed: {ex}");
            }
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
                AiModelsUrlBox.Text = preset.ModelsUrl;
                if (!string.IsNullOrWhiteSpace(preset.DefaultModel))
                    AiModelBox.Text = preset.DefaultModel;
                else if (AiProviderPresets.NoDefaultModelProviders.Contains(tag))
                    AiModelBox.Text = string.Empty;
            }

            _aiFetchedModels.Clear();
            UpdateSuggestedModels(tag);
            if (!TrySaveCurrentProfileFromUi(out var error))
                ShowAiError(error);
        }

        private async void AiRefreshModels_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TrySaveCurrentProfileFromUi(out var error))
                {
                    ShowAiError(error);
                    return;
                }

                if (_aiActiveIndex < 0 || _aiActiveIndex >= _aiProfiles.Count) return;
                var profile = _aiProfiles[_aiActiveIndex];
                if (!TryValidateProfile(profile, requireModel: false, out error))
                {
                    ShowAiError(error);
                    return;
                }

                AiRefreshModelsBtn.IsEnabled = false;
                AiStatusText.Text = Application.Current.TryFindResource("AiCommitMsgRefreshingModels") as string ?? "Refreshing...";

                var decryptedKey = DpapiEncryption.Decrypt(profile.EncryptedKey);
                var models = await AiCommitMessageService.FetchModelsAsync(profile, decryptedKey);

                if (models != null && models.Count > 0)
                {
                    _aiFetchedModels = models;
                    AiStatusText.Text = string.Format(
                        (Application.Current.TryFindResource("AiCommitMsgModelsFetched") as string) ?? "Fetched {0} models.", models.Count);
                    UpdateSuggestedModels(profile.Provider);
                }
                else
                {
                    AiStatusText.Text = Application.Current.TryFindResource("AiCommitMsgModelsFetchFailed") as string ?? "Failed to fetch models.";
                }
            }
            catch (Exception ex)
            {
                ShowAiError(ex.Message);
            }
            finally
            {
                AiRefreshModelsBtn.IsEnabled = true;
            }
        }

        private async void AiTestGenerate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TrySaveCurrentProfileFromUi(out var error))
                {
                    ShowAiError(error);
                    return;
                }

                SaveAiSettingsToDisk();

                if (_aiActiveIndex < 0 || _aiActiveIndex >= _aiProfiles.Count) return;
                var profile = _aiProfiles[_aiActiveIndex];
                if (!TryValidateProfile(profile, requireModel: true, out error))
                {
                    ShowAiError(error);
                    return;
                }

                AiTestGenerateBtn.IsEnabled = false;
                AiStatusText.Text = Application.Current.TryFindResource("AiCommitMsgTesting") as string ?? "Testing...";

                var decryptedKey = DpapiEncryption.Decrypt(profile.EncryptedKey);
                var (success, text, generationError) = await AiCommitMessageService.TestGenerateAsync(profile, decryptedKey);

                if (success)
                {
                    AiStatusText.Text = string.Format(
                        (Application.Current.TryFindResource("AiCommitMsgTestSuccess") as string) ?? "Success: {0}", text);
                }
                else
                {
                    AiStatusText.Text = string.Format(
                        (Application.Current.TryFindResource("AiCommitMsgTestFailed") as string) ?? "Failed: {0}", generationError);
                }
            }
            catch (Exception ex)
            {
                ShowAiError(ex.Message);
            }
            finally
            {
                AiTestGenerateBtn.IsEnabled = true;
            }
        }

        private void AiSaveProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TrySaveCurrentProfileFromUi(out var error))
                {
                    ShowAiError(error);
                    return;
                }

                SaveAiSettingsToDisk();
                var settings = StorageService.Load();
                var profile = _aiProfiles[_aiActiveIndex];
                var isValid = TryValidateProfile(profile, requireModel: true, out _);

                // Only auto-enable after the user chose "Configure" in the onboarding dialog.
                if (isValid && settings.AiCommitEnablePostsAfterProfileSave)
                {
                    settings.AiCommitEnabledPosts = true;
                    settings.AiCommitEnablePostsAfterProfileSave = false;
                    StorageService.Save(settings);
                    _isLoadingAi = true;
                    AiCommitPostsToggle.IsChecked = true;
                    _isLoadingAi = false;
                    AiStatusText.Text = GetResourceString("AiCommitMsgProfileSavedPostsEnabled", "Profile saved. AI commit for post publishing is now enabled.");
                    return;
                }

                AiStatusText.Text = isValid
                    ? GetResourceString("AiCommitMsgProfileSaved", "Profile saved.")
                    : GetResourceString("AiCommitMsgProfileDraftSaved", "Draft profile saved. Fill in a valid Base URL and model before enabling AI commit.");
            }
            catch (Exception ex)
            {
                ShowAiError(ex.Message);
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

        private void AiBehavior_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingAi) return;
            var tag = (AiBehaviorComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (Enum.TryParse<AiCommitBehavior>(tag, out var behavior))
            {
                SaveAiSetting(s =>
                {
                    s.AiCommitBehavior = behavior;
                    s.AiCommitBehaviorConfigured = true;
                });
            }
        }

        private void AiCommitPosts_Checked(object sender, RoutedEventArgs e)
        {
            if (!TryEnableAiCommit())
                return;
            SaveAiSetting(s => s.AiCommitEnabledPosts = true);
        }

        private void AiCommitPosts_Unchecked(object sender, RoutedEventArgs e) => SaveAiSetting(s => s.AiCommitEnabledPosts = false);
        private void AiCommitSettings_Checked(object sender, RoutedEventArgs e)
        {
            if (!TryEnableAiCommit())
                return;
            SaveAiSetting(s => s.AiCommitEnabledSettings = true);
        }

        private void AiCommitSettings_Unchecked(object sender, RoutedEventArgs e) => SaveAiSetting(s => s.AiCommitEnabledSettings = false);

        private bool TryEnableAiCommit()
        {
            if (_isLoadingAi)
                return false;

            if (TrySaveCurrentProfileFromUi(out var error) && TryGetValidActiveProfile(out error))
            {
                SaveAiSettingsToDisk();
                return true;
            }

            ShowAiError(error);
            _isLoadingAi = true;
            AiCommitPostsToggle.IsChecked = StorageService.Load().AiCommitEnabledPosts;
            AiCommitSettingsToggle.IsChecked = StorageService.Load().AiCommitEnabledSettings;
            _isLoadingAi = false;
            return false;
        }

        private void AiShowApiKey_Changed(object sender, RoutedEventArgs e)
        {
            var showApiKey = AiShowApiKeyCheckBox.IsChecked == true;
            AiApiKeyPasswordBox.Visibility = showApiKey ? Visibility.Collapsed : Visibility.Visible;
            AiApiKeyVisibleBox.Visibility = showApiKey ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AiApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isSyncingAiKey) return;
            _isSyncingAiKey = true;
            AiApiKeyVisibleBox.Text = AiApiKeyPasswordBox.Password;
            _isSyncingAiKey = false;
        }

        private void UpdateProfileComboItemName(AiCommitProfile profile)
        {
            var profileIndex = _aiProfiles.IndexOf(profile);
            if (profileIndex < 0)
                return;

            var wasLoading = _isLoadingAi;
            _isLoadingAi = true;
            try
            {
                if (AiProfileComboBox.Items.Count != _aiProfiles.Count)
                {
                    AiProfileComboBox.Items.Clear();
                    foreach (var item in _aiProfiles)
                    {
                        AiProfileComboBox.Items.Add(
                            item.Name ?? GetResourceString("AiCommitDefaultUnnamedProfile", "Unnamed"));
                    }
                }
                else
                {
                    AiProfileComboBox.Items[profileIndex] =
                        profile.Name ?? GetResourceString("AiCommitDefaultUnnamedProfile", "Unnamed");
                }

                _aiActiveIndex = profileIndex;
                AiProfileComboBox.SelectedIndex = profileIndex;
            }
            finally
            {
                _isLoadingAi = wasLoading;
            }
        }

        private void AiApiKeyVisibleBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSyncingAiKey) return;
            _isSyncingAiKey = true;
            AiApiKeyPasswordBox.Password = AiApiKeyVisibleBox.Text;
            _isSyncingAiKey = false;
        }

        private void SaveAiSetting(Action<AppSettings> update)
        {
            if (_isLoadingAi) return;
            var settings = StorageService.Load();
            update(settings);
            StorageService.Save(settings);
        }

        private async Task<string> ShowTextInputDialogAsync(string title, string message, string defaultText)
        {
            try
            {
                var textBox = new System.Windows.Controls.TextBox
                {
                    Text = defaultText,
                    Margin = new Sw.Thickness(0, 8, 0, 0),
                    MinWidth = 300
                };

                var stackPanel = new Swc.StackPanel();
                stackPanel.Children.Add(new Swc.TextBlock { Text = message, TextWrapping = Sw.TextWrapping.Wrap });
                stackPanel.Children.Add(textBox);

                var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
                var dialogHost = owner == null
                    ? null
                    : WpfUiControls.ContentDialogHost.GetForWindow(owner);
                if (dialogHost == null)
                {
                    ShowAiError(GetResourceString("AiCommitDialogHostUnavailable", "The dialog host is not ready. Please reopen App Settings and try again."));
                    return string.Empty;
                }

                var dialog = new WpfUiControls.ContentDialog(dialogHost)
                {
                    Title = title,
                    Content = stackPanel,
                    PrimaryButtonText = Application.Current.TryFindResource("CommonConfirm") as string ?? "Confirm",
                    CloseButtonText = Application.Current.TryFindResource("CommonCancel") as string ?? "Cancel",
                    DefaultButton = WpfUiControls.ContentDialogButton.Primary
                };
                dialog.Opened += (_, _) =>
                {
                    textBox.Focus();
                    textBox.SelectAll();
                };

                var result = await dialog.ShowAsync();
                return result == WpfUiControls.ContentDialogResult.Primary ? textBox.Text.Trim() : string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ShowTextInputDialogAsync failed: {ex}");
                return string.Empty;
            }
        }
    }
}
