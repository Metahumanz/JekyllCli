using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Appearance;
using Microsoft.Win32;
using System.IO;
using BlogTools.Services;

using System.Threading.Tasks;

namespace BlogTools
{
    public partial class SettingsPage : Page
    {
        private Dictionary<string, object>? _config;
        private bool _isLoading = false;

        public SettingsPage()
        {
            InitializeComponent();
            Loaded += SettingsPage_Loaded;
            Unloaded += SettingsPage_Unloaded;
            App.BlogFilesChanged += OnBlogFilesChanged;
            
            ThemeToggle.IsChecked = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

            // 下拉框打开/关闭时控制 ScrollViewerHelper 的事件转发
            FontComboBox.DropDownOpened += (s, args) => Helpers.ScrollViewerHelper.SuppressScrollBubble = true;
            FontComboBox.DropDownClosed += (s, args) => Helpers.ScrollViewerHelper.SuppressScrollBubble = false;

            // handledEventsToo: true 让 ComboBox 即使在 ScrollViewerHelper 拦截后仍能收到滚轮事件
            FontComboBox.AddHandler(UIElement.PreviewMouseWheelEvent,
                new System.Windows.Input.MouseWheelEventHandler(FontComboBox_PreviewMouseWheel), true);

            CompositionTarget.Rendering += OnCompositionTargetRendering;
        }

        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            App.BlogFilesChanged -= OnBlogFilesChanged;
            CompositionTarget.Rendering -= OnCompositionTargetRendering;
        }

        private void OnBlogFilesChanged()
        {
            Dispatcher.InvokeAsync(() => SettingsPage_Loaded(this, new RoutedEventArgs()));
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoading = true;
            var parentSv = FindVisualParent<ScrollViewer>(this);
            parentSv?.ScrollToTop();

            CurrentPathBlock.Text = App.JekyllContext.BlogPath;
            var settings = StorageService.Load();
            RememberMetadataToggle.IsChecked = settings.RememberMetadataExpanded;
            AutoUpdateModifiedTimeToggle.IsChecked = settings.AutoUpdateModifiedTime;
            SilentUpdateToggle.IsChecked = settings.SilentUpdate;
            ThemeToggle.IsChecked = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

            // Load Language setting
            foreach (ComboBoxItem item in LanguageComboBox.Items)
            {
                if (item.Tag?.ToString() == settings.AppLanguage)
                {
                    LanguageComboBox.SelectedItem = item;
                    break;
                }
            }
            if (LanguageComboBox.SelectedItem == null) LanguageComboBox.SelectedIndex = 0; // Default to Auto
            _config = App.JekyllContext.LoadConfig();

            FontComboBox.Items.Clear();
            foreach (var family in System.Windows.Media.Fonts.SystemFontFamilies.OrderBy(f => f.Source))
            {
                FontComboBox.Items.Add(new ComboBoxItem { Content = family.Source, FontFamily = family });
            }

            var font = settings.AppFontFamily;
            if (string.IsNullOrWhiteSpace(font)) font = GetStringValue(_config, "blogtools_font");
            if (string.IsNullOrWhiteSpace(font)) font = "Microsoft YaHei UI";

            foreach (ComboBoxItem item in FontComboBox.Items)
            {
                if (item.Content.ToString() == font)
                {
                    FontComboBox.SelectedItem = item;
                    break;
                }
            }
            _isLoading = false;

            TitleBox.Text = GetStringValue(_config, "title");
            TaglineBox.Text = GetStringValue(_config, "tagline");
            DescriptionBox.Text = GetStringValue(_config, "description");
            AvatarBox.Text = GetStringValue(_config, "avatar");
            UrlBox.Text = GetStringValue(_config, "url");

            if (_config.TryGetValue("social", out var socialObj) && socialObj is Dictionary<object, object> socialDict)
            {
                AuthorNameBox.Text = GetStringValue(socialDict, "name");
                EmailBox.Text = GetStringValue(socialDict, "email");
            }

            if (_config.TryGetValue("github", out var githubObj) && githubObj is Dictionary<object, object> githubDict)
            {
                GithubBox.Text = GetStringValue(githubDict, "username");
            }

            var contacts = App.JekyllContext.LoadData<System.Collections.Generic.List<System.Collections.Generic.Dictionary<object, object>>>("_data/contact.yml");
            if (contacts != null)
            {
                var bEntry = contacts.FirstOrDefault(c => c.TryGetValue("type", out var t) && t?.ToString() == "bilibili");
                if (bEntry != null && bEntry.TryGetValue("url", out var u))
                {
                    BilibiliBox.Text = u?.ToString();
                }
            }

            try
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                var versionStr = $"{version?.Major}.{version?.Minor}.{version?.Build}";
                VersionBlock.Text = string.Format(Application.Current.FindResource("CommonVersionCurrent").ToString()!, versionStr);
            }
            catch
            {
                VersionBlock.Text = Application.Current.FindResource("CommonVersionDev").ToString();
            }

            // Load Tabs
            LoadTab("_tabs/about.md", AboutTitleBox, AboutIconBox, AboutOrderBox, AboutContentBox);
            LoadTab("_tabs/archives.md", ArchivesTitleBox, null, ArchivesOrderBox, null);
            LoadTab("_tabs/categories.md", CategoriesTitleBox, null, CategoriesOrderBox, null);
            LoadTab("_tabs/tags.md", TagsTitleBox, null, TagsOrderBox, null);
        }

        private void LoadTab(string path, Wpf.Ui.Controls.TextBox titleBox, Wpf.Ui.Controls.TextBox? iconBox, Wpf.Ui.Controls.TextBox orderBox, Wpf.Ui.Controls.TextBox? contentBox)
        {
            var parsed = App.JekyllContext.ParseMarkdownWithFrontMatter(path);
            if (parsed != null)
            {
                if (parsed.Value.FrontMatter.TryGetValue("title", out var title)) titleBox.Text = title?.ToString() ?? "";
                if (iconBox != null && parsed.Value.FrontMatter.TryGetValue("icon", out var icon)) iconBox.Text = icon?.ToString() ?? "";
                if (parsed.Value.FrontMatter.TryGetValue("order", out var order)) orderBox.Text = order?.ToString() ?? "";
                if (contentBox != null) contentBox.Text = parsed.Value.Content;
            }
        }

        private async void ChangeBlogPath_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog
            {
                Title = Application.Current.FindResource("SettingsBtnChangePath").ToString()!
            };

            if (dialog.ShowDialog() == true)
            {
                string newPath = dialog.FolderName;
                if (!File.Exists(Path.Combine(newPath, "_config.yml")))
                {
                    var msg = new Wpf.Ui.Controls.MessageBox 
                    { 
                        Title = Application.Current.FindResource("CommonError").ToString()!, 
                        Content = Application.Current.FindResource("SettingsMsgInvalidRoot").ToString()!, 
                        CloseButtonText = Application.Current.FindResource("CommonConfirm").ToString()! 
                    };
                    await msg.ShowDialogAsync();
                    return;
                }

                // 1. Update settings
                var settings = StorageService.Load();
                settings.BlogPath = newPath;
                StorageService.Save(settings);

                // 2. Re-init services
                App.JekyllContext = new JekyllService(newPath);
                App.GitContext = new GitService(newPath);

                // 3. Restart file watcher for new path
                App.StartFileWatcher(newPath);

                // 4. Refresh UI
                SettingsPage_Loaded(sender, e);
                
                StatusInfo.Message = Application.Current.FindResource("SettingsMsgPathChanged").ToString()!;
                StatusInfo.Severity = Wpf.Ui.Controls.InfoBarSeverity.Success;
                StatusInfo.IsOpen = true;
            }
        }

        private void RememberMetadata_Checked(object sender, RoutedEventArgs e)
        {
            var settings = StorageService.Load();
            settings.RememberMetadataExpanded = true;
            StorageService.Save(settings);
        }

        private void RememberMetadata_Unchecked(object sender, RoutedEventArgs e)
        {
            var settings = StorageService.Load();
            settings.RememberMetadataExpanded = false;
            StorageService.Save(settings);
        }

        private void AutoUpdateModifiedTime_Checked(object sender, RoutedEventArgs e)
        {
            var settings = StorageService.Load();
            settings.AutoUpdateModifiedTime = true;
            StorageService.Save(settings);
        }

        private void AutoUpdateModifiedTime_Unchecked(object sender, RoutedEventArgs e)
        {
            var settings = StorageService.Load();
            settings.AutoUpdateModifiedTime = false;
            StorageService.Save(settings);
        }

        private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        }

        private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Light);
        }

        private void FontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var selectedFont = (FontComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrWhiteSpace(selectedFont)) return;

            // 立即保存到 settings.json
            var settings = StorageService.Load();
            settings.AppFontFamily = selectedFont;
            StorageService.Save(settings);

            // 立即应用到主窗口
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null)
            {
                mainWindow.FontFamily = new System.Windows.Media.FontFamily(selectedFont);
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var selectedTag = (LanguageComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(selectedTag)) return;

            // 1. Save to settings
            var settings = StorageService.Load();
            settings.AppLanguage = selectedTag;
            StorageService.Save(settings);

            // 2. Apply language immediately
            App.ApplyLanguage(selectedTag);
        }

        private double _targetDropdownScrollOffset = -1;
        private double _currentDropdownScrollOffset = -1;
        private ScrollViewer? _activeDropdownScrollViewer = null;

        private double _targetPageScrollOffset = -1;
        private double _currentPageScrollOffset = -1;
        private ScrollViewer? _activePageScrollViewer = null;

        private void OnCompositionTargetRendering(object? sender, EventArgs e)
        {
            if (_activeDropdownScrollViewer != null && _targetDropdownScrollOffset >= 0 && _currentDropdownScrollOffset >= 0)
            {
                double diff = _targetDropdownScrollOffset - _currentDropdownScrollOffset;
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
                double diffPage = _targetPageScrollOffset - _currentPageScrollOffset;
                if (Math.Abs(diffPage) < 0.5)
                {
                    _currentPageScrollOffset = _targetPageScrollOffset;
                    _activePageScrollViewer.ScrollToVerticalOffset(_currentPageScrollOffset);
                    _targetPageScrollOffset = -1;
                    _activePageScrollViewer = null;
                }
                else
                {
                    _currentPageScrollOffset += diffPage * 0.18;
                    _activePageScrollViewer.ScrollToVerticalOffset(_currentPageScrollOffset);
                }
            }
        }

        private void PageScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (Helpers.ScrollViewerHelper.SuppressScrollBubble) return;

            var rootSv = FindVisualParent<ScrollViewer>(this);
            if (rootSv == null) return;

            e.Handled = true;
            _activePageScrollViewer = rootSv;

            if (_targetPageScrollOffset == -1)
            {
                _targetPageScrollOffset = rootSv.VerticalOffset;
                _currentPageScrollOffset = rootSv.VerticalOffset;
            }

            _targetPageScrollOffset -= e.Delta * 2.0;
            _targetPageScrollOffset = Math.Max(0, Math.Min(rootSv.ScrollableHeight, _targetPageScrollOffset));
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
            if (popup?.Child is FrameworkElement popupChild)
            {
                var mousePos = e.GetPosition(popupChild);
                bool isOverPopup = mousePos.X >= 0 && mousePos.Y >= 0
                    && mousePos.X <= popupChild.ActualWidth
                    && mousePos.Y <= popupChild.ActualHeight;

                if (isOverPopup)
                {
                    e.Handled = true;
                    var sv = FindVisualChild<ScrollViewer>(popupChild);
                    if (sv != null)
                    {
                        if (_activeDropdownScrollViewer != sv)
                        {
                            _activeDropdownScrollViewer = sv;
                            _currentDropdownScrollOffset = sv.VerticalOffset;
                            _targetDropdownScrollOffset = sv.VerticalOffset;
                        }

                        _targetDropdownScrollOffset -= e.Delta * 2.0;
                        _targetDropdownScrollOffset = Math.Max(0, Math.Min(sv.ScrollableHeight, _targetDropdownScrollOffset));
                    }
                }
                else
                {
                    FontComboBox.IsDropDownOpen = false;
                }
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result) return result;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private void SaveConfigToMemory()
        {
            _config ??= App.JekyllContext.LoadConfig();

            var selectedFont = (FontComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            _config["blogtools_font"] = string.IsNullOrWhiteSpace(selectedFont) ? "Microsoft YaHei UI" : selectedFont;
            
            var settings = StorageService.Load();
            settings.AppFontFamily = _config["blogtools_font"]?.ToString() ?? string.Empty;
            StorageService.Save(settings);
            
            _config["title"] = TitleBox.Text;
            _config["tagline"] = TaglineBox.Text;
            _config["description"] = DescriptionBox.Text;
            _config["avatar"] = AvatarBox.Text;
            _config["url"] = UrlBox.Text;

            UpdateNestedDict(_config, "social", "name", AuthorNameBox.Text);
            UpdateNestedDict(_config, "social", "email", EmailBox.Text);
            UpdateNestedDict(_config, "github", "username", GithubBox.Text);
            
            if (!string.IsNullOrWhiteSpace(BilibiliBox.Text))
            {
                var contactsList = App.JekyllContext.LoadData<System.Collections.Generic.List<System.Collections.Generic.Dictionary<object, object>>>("_data/contact.yml") ?? new System.Collections.Generic.List<System.Collections.Generic.Dictionary<object, object>>();
                var bEntry = contactsList.FirstOrDefault(c => c.TryGetValue("type", out var t) && t?.ToString() == "bilibili");
                if (bEntry != null)
                {
                    if (bEntry.ContainsKey("noblank")) bEntry.Remove("noblank");
                    bEntry["url"] = BilibiliBox.Text;
                }
                else
                {
                    contactsList.Add(new System.Collections.Generic.Dictionary<object, object> {
                        { "type", "bilibili" },
                        { "icon", "fa-brands fa-bilibili" },
                        { "url", BilibiliBox.Text }
                    });
                }
                App.JekyllContext.SaveData("_data/contact.yml", contactsList);
            }
            
            // Clean up old social links if they exist
            if (_config.TryGetValue("social", out var sObj) && sObj is System.Collections.Generic.Dictionary<object, object> sDict)
            {
                if (sDict.TryGetValue("links", out var lObj) && lObj is System.Collections.Generic.List<object> lList)
                {
                    lList.RemoveAll(l => l?.ToString()?.Contains("bilibili.com") == true);
                }
            }

            App.JekyllContext.SaveConfig(_config);
            
            // Save Tabs
            SaveTab("_tabs/about.md", AboutTitleBox, AboutIconBox, AboutOrderBox, AboutContentBox);
            SaveTab("_tabs/archives.md", ArchivesTitleBox, null, ArchivesOrderBox, null);
            SaveTab("_tabs/categories.md", CategoriesTitleBox, null, CategoriesOrderBox, null);
            SaveTab("_tabs/tags.md", TagsTitleBox, null, TagsOrderBox, null);
            
            // Apply font dynamically after save!
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null && _config.TryGetValue("blogtools_font", out var fontObj))
            {
                var fontName = fontObj?.ToString();
                if (!string.IsNullOrWhiteSpace(fontName))
                {
                    mainWindow.FontFamily = new System.Windows.Media.FontFamily(fontName);
                }
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveConfigToMemory();
            StatusInfo.Message = Application.Current.FindResource("SettingsMsgSaveSuccess").ToString()!;
            StatusInfo.Severity = Wpf.Ui.Controls.InfoBarSeverity.Success;
            StatusInfo.IsOpen = true;
        }

        private async void PublishButton_Click(object sender, RoutedEventArgs e)
        {
            SaveConfigToMemory();
            StatusInfo.Message = Application.Current.FindResource("SettingsMsgPublishing").ToString()!;
            StatusInfo.Severity = Wpf.Ui.Controls.InfoBarSeverity.Informational;
            StatusInfo.IsOpen = true;
            
            try
            {
                var result = await App.GitContext.CommitAndPushAsync("Update blog configuration settings");
                StatusInfo.Message = Application.Current.FindResource("SettingsMsgPublishSuccess").ToString()!;
                StatusInfo.Severity = Wpf.Ui.Controls.InfoBarSeverity.Success;
            }
            catch (Exception ex)
            {
                StatusInfo.Message = string.Format(Application.Current.FindResource("SettingsMsgPublishError").ToString()!, ex.Message);
                StatusInfo.Severity = Wpf.Ui.Controls.InfoBarSeverity.Error;
            }
        }

        private string GetStringValue(Dictionary<string, object> dict, string key) => dict.TryGetValue(key, out var val) ? val?.ToString() ?? string.Empty : string.Empty;
        private string GetStringValue(Dictionary<object, object> dict, string key) => dict.TryGetValue(key, out var val) ? val?.ToString() ?? string.Empty : string.Empty;

        private void UpdateNestedDict(Dictionary<string, object> root, string outerKey, string innerKey, string value)
        {
            if (!root.TryGetValue(outerKey, out var outerObj) || !(outerObj is Dictionary<object, object> outerDict))
            {
                outerDict = new Dictionary<object, object>();
                root[outerKey] = outerDict;
            }
            outerDict[innerKey] = value;
        }

        private void SaveTab(string path, Wpf.Ui.Controls.TextBox titleBox, Wpf.Ui.Controls.TextBox? iconBox, Wpf.Ui.Controls.TextBox orderBox, Wpf.Ui.Controls.TextBox? contentBox)
        {
            var parsed = App.JekyllContext.ParseMarkdownWithFrontMatter(path);
            var fm = parsed?.FrontMatter ?? new Dictionary<string, object>();
            if (string.IsNullOrWhiteSpace(fm.ContainsKey("layout") ? fm["layout"]?.ToString() : ""))
                fm["layout"] = path.Contains("about") ? "page" : path.Replace("_tabs/", "").Replace(".md", "");

            if (!string.IsNullOrWhiteSpace(titleBox.Text)) fm["title"] = titleBox.Text; else fm.Remove("title");
            if (iconBox != null)
            {
                 if (!string.IsNullOrWhiteSpace(iconBox.Text)) fm["icon"] = iconBox.Text; else fm.Remove("icon");
            }
            else if (!fm.ContainsKey("icon"))
            {
                 if (path.Contains("archives")) fm["icon"] = "fas fa-archive";
                 if (path.Contains("categories")) fm["icon"] = "fas fa-stream";
                 if (path.Contains("tags")) fm["icon"] = "fas fa-tags";
            }
            if (int.TryParse(orderBox.Text, out var order)) fm["order"] = order; else fm.Remove("order");
            
            var content = contentBox != null ? contentBox.Text : (parsed?.Content ?? "");
            App.JekyllContext.SaveMarkdownWithFrontMatter(path, fm, content);
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindVisualParent<T>(parentObject);
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
            catch { }
        }

        private void OpenFaviconGenerator_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://realfavicongenerator.net/",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            await PerformUpdateCheckAsync(isManual: true);
        }

        /// <summary>
        /// 公开方法，供 App.xaml.cs 启动时调用进行自动检查。
        /// </summary>
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
                    var currentVersionText = $"v{current.Major}.{current.Minor}.{current.Build}";
                    VersionBlock.Text = string.Format(Application.Current.FindResource("SettingsMsgUpdateLatest").ToString()!, currentVersionText);
                    if (isManual)
                    {
                        StatusInfo.Message = Application.Current.FindResource("SettingsMsgUpdateLatest").ToString()!; 
                        // Better: just use the whole string or a dedicated key. Let's keep it simple for now.
                        StatusInfo.IsOpen = true;
                    }
                    return;
                }

                var currentStr = $"v{UpdateService.GetCurrentVersion().Major}.{UpdateService.GetCurrentVersion().Minor}.{UpdateService.GetCurrentVersion().Build}";
                VersionBlock.Text = string.Format(Application.Current.FindResource("CommonVersionCurrent").ToString()!, currentStr) + $"  ➜  {Application.Current.FindResource("SettingsMsgUpdateFound").ToString()!}: {latestVersion}";

                // 弹窗询问是否下载
                var askDownload = new Wpf.Ui.Controls.MessageBox
                {
                    Title = Application.Current.FindResource("SettingsMsgUpdateFound").ToString()!,
                    Content = string.Format(Application.Current.FindResource("SettingsMsgAskDownload").ToString()!, latestVersion),
                    PrimaryButtonText = Application.Current.FindResource("SettingsBtnDownloadNow").ToString()!,
                    CloseButtonText = Application.Current.FindResource("SettingsBtnLater").ToString()!
                };
                var result = await askDownload.ShowDialogAsync();
                if (result != Wpf.Ui.Controls.MessageBoxResult.Primary)
                    return;

                // 开始下载
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

                // 检查是否静默更新
                var settings = StorageService.Load();
                if (settings.SilentUpdate)
                {
                    ProgressText.Text = Application.Current.FindResource("SettingsMsgSilentUpdating").ToString()!;
                    await Task.Delay(500);
                    UpdateService.ApplyUpdate(zipPath);
                    return;
                }

                // 弹窗询问是否立即更新
                var askApply = new Wpf.Ui.Controls.MessageBox
                {
                    Title = Application.Current.FindResource("SettingsMsgDownloadComplete").ToString()!,
                    Content = Application.Current.FindResource("SettingsMsgAskApply").ToString()!,
                    PrimaryButtonText = Application.Current.FindResource("SettingsBtnApplyNow").ToString()!,
                    CloseButtonText = Application.Current.FindResource("SettingsBtnLater").ToString()!
                };
                var applyResult = await askApply.ShowDialogAsync();
                if (applyResult == Wpf.Ui.Controls.MessageBoxResult.Primary)
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

        private void SilentUpdate_Checked(object sender, RoutedEventArgs e)
        {
            var settings = StorageService.Load();
            settings.SilentUpdate = true;
            StorageService.Save(settings);
        }

        private void SilentUpdate_Unchecked(object sender, RoutedEventArgs e)
        {
            var settings = StorageService.Load();
            settings.SilentUpdate = false;
            StorageService.Save(settings);
        }

        private async void UploadFavicons_Click(object sender, RoutedEventArgs e)
        {
            var filter = $"{Application.Current.FindResource("CommonFilterImages").ToString()!}|*.png;*.ico;*.jpg;*.jpeg;*.svg;*.xml;*.webmanifest|{Application.Current.FindResource("CommonFilterAllFiles").ToString()!}|*.*";
            var dialog = new OpenFileDialog
            {
                Title = Application.Current.FindResource("SettingsMsgFaviconSelect").ToString()!,
                Filter = filter,
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                var targetDir = System.IO.Path.Combine(App.JekyllContext.BlogPath, "assets", "img", "favicons");
                if (!System.IO.Directory.Exists(targetDir))
                    System.IO.Directory.CreateDirectory(targetDir);

                int count = 0;
                int skipped = 0;
                foreach (var file in dialog.FileNames)
                {
                    var fileName = System.IO.Path.GetFileName(file);

                    // Chirpy v7.4.0+ 规范：自动过滤 site.webmanifest
                    if (fileName.Equals("site.webmanifest", StringComparison.OrdinalIgnoreCase))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        var destFile = System.IO.Path.Combine(targetDir, fileName);
                        System.IO.File.Copy(file, destFile, true);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        var msg = new Wpf.Ui.Controls.MessageBox 
                        { 
                            Title = Application.Current.FindResource("CommonError").ToString()!, 
                            Content = string.Format(Application.Current.FindResource("SettingsMsgCopyError").ToString()!, fileName, ex.Message), 
                            CloseButtonText = Application.Current.FindResource("CommonConfirm").ToString()! 
                        };
                        await msg.ShowDialogAsync();
                    }
                }

                var resultMsg = string.Format(Application.Current.FindResource("SettingsMsgUploadFaviconsSuccess").ToString()!, count);
                if (skipped > 0)
                    resultMsg += string.Format(Application.Current.FindResource("SettingsMsgUploadFaviconsSkipped").ToString()!, skipped);
                StatusInfo.Message = resultMsg;
                StatusInfo.Severity = Wpf.Ui.Controls.InfoBarSeverity.Success;
                StatusInfo.IsOpen = true;
            }
        }
    }
}
