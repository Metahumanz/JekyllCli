using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

        public SettingsPage()
        {
            InitializeComponent();
            Loaded += SettingsPage_Loaded;
            Unloaded += SettingsPage_Unloaded;
            App.BlogFilesChanged += OnBlogFilesChanged;
            
            ThemeToggle.IsChecked = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        }

        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            App.BlogFilesChanged -= OnBlogFilesChanged;
        }

        private void OnBlogFilesChanged()
        {
            Dispatcher.InvokeAsync(() => SettingsPage_Loaded(this, new RoutedEventArgs()));
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            var parentSv = FindVisualParent<ScrollViewer>(this);
            parentSv?.ScrollToTop();

            CurrentPathBlock.Text = App.JekyllContext.BlogPath;
            var settings = StorageService.Load();
            RememberMetadataToggle.IsChecked = settings.RememberMetadataExpanded;
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
                VersionBlock.Text = $"当前版本: v{version?.Major}.{version?.Minor}.{version?.Build}";
            }
            catch
            {
                VersionBlock.Text = "当前版本: 开发版";
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
                Title = "更换 Jekyll 博客本地根目录"
            };

            if (dialog.ShowDialog() == true)
            {
                string newPath = dialog.FolderName;
                if (!File.Exists(Path.Combine(newPath, "_config.yml")))
                {
                    var msg = new Wpf.Ui.Controls.MessageBox { Title = "错误", Content = "该目录不是有效的 Jekyll 根目录（未找到 _config.yml 文件）。", CloseButtonText = "确定" };
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
                
                StatusInfo.Message = "博客目录已更换并重新加载配置！";
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

        private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        }

        private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Light);
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
            StatusInfo.Message = "配置已成功保存！";
            StatusInfo.Severity = Wpf.Ui.Controls.InfoBarSeverity.Success;
            StatusInfo.IsOpen = true;
        }

        private async void PublishButton_Click(object sender, RoutedEventArgs e)
        {
            SaveConfigToMemory();
            StatusInfo.Message = "发布中... 正在推送到远端服务器。";
            StatusInfo.Severity = Wpf.Ui.Controls.InfoBarSeverity.Informational;
            StatusInfo.IsOpen = true;
            
            try
            {
                var result = await App.GitContext.CommitAndPushAsync("Update blog configuration settings");
                StatusInfo.Message = "发布成功！推送至 GitHub 并触发构建。";
                StatusInfo.Severity = Wpf.Ui.Controls.InfoBarSeverity.Success;
            }
            catch (Exception ex)
            {
                StatusInfo.Message = $"推送失败: {ex.Message}";
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

        private void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                VersionBlock.Text = "即将打开浏览器检查更新...";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/Metahumanz/JekyllCli/releases/latest",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                VersionBlock.Text = $"获取更新失败: {ex.Message}";
            }
        }
        private async void UploadFavicons_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择网站图标文件 (支持多选)",
                Filter = "图像文件|*.png;*.ico;*.jpg;*.jpeg;*.svg;*.xml;*.webmanifest|所有文件|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                var targetDir = System.IO.Path.Combine(App.JekyllContext.BlogPath, "assets", "img", "favicons");
                if (!System.IO.Directory.Exists(targetDir))
                    System.IO.Directory.CreateDirectory(targetDir);

                int count = 0;
                foreach (var file in dialog.FileNames)
                {
                    try
                    {
                        var destFile = System.IO.Path.Combine(targetDir, System.IO.Path.GetFileName(file));
                        System.IO.File.Copy(file, destFile, true);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        var msg = new Wpf.Ui.Controls.MessageBox { Title = "错误", Content = $"复制文件 {System.IO.Path.GetFileName(file)} 失败:\n{ex.Message}", CloseButtonText = "确定" };
                        await msg.ShowDialogAsync();
                    }
                }
                
                StatusInfo.Message = $"成功导入 {count} 个网站图标！";
                StatusInfo.Severity = Wpf.Ui.Controls.InfoBarSeverity.Success;
                StatusInfo.IsOpen = true;
            }
        }
    }
}