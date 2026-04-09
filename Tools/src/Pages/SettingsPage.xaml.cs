using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace BlogTools
{
    public partial class SettingsPage : Page
    {
        private Dictionary<string, object>? _config;
        private double _targetPageScrollOffset = -1;
        private double _currentPageScrollOffset = -1;
        private ScrollViewer? _activePageScrollViewer;

        public SettingsPage()
        {
            InitializeComponent();
            Loaded += SettingsPage_Loaded;
            Unloaded += SettingsPage_Unloaded;
            App.BlogFilesChanged += OnBlogFilesChanged;
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
            var parentSv = FindVisualParent<ScrollViewer>(this);
            parentSv?.ScrollToTop();

            _config = App.JekyllContext.LoadConfig();

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
            else
            {
                AuthorNameBox.Text = string.Empty;
                EmailBox.Text = string.Empty;
            }

            if (_config.TryGetValue("github", out var githubObj) && githubObj is Dictionary<object, object> githubDict)
            {
                GithubBox.Text = GetStringValue(githubDict, "username");
            }
            else
            {
                GithubBox.Text = string.Empty;
            }

            var contacts = App.JekyllContext.LoadData<System.Collections.Generic.List<System.Collections.Generic.Dictionary<object, object>>>("_data/contact.yml");
            if (contacts != null)
            {
                var bEntry = contacts.FirstOrDefault(c => c.TryGetValue("type", out var t) && t?.ToString() == "bilibili");
                if (bEntry != null && bEntry.TryGetValue("url", out var u))
                {
                    BilibiliBox.Text = u?.ToString();
                }
                else
                {
                    BilibiliBox.Text = string.Empty;
                }
            }
            else
            {
                BilibiliBox.Text = string.Empty;
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

        private void OnCompositionTargetRendering(object? sender, EventArgs e)
        {
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

        private void SaveConfigToMemory()
        {
            _config ??= App.JekyllContext.LoadConfig();
            
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

            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RefreshShellFromCurrentBlogConfig();
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
                await App.GitContext.CommitAndPushAsync("Update blog configuration settings");
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
