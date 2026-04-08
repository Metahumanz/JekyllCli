using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BlogTools.Models;
using Markdig;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Microsoft.Win32;

namespace BlogTools
{
    public partial class EditorPage : Page
    {
        private MarkdownPipeline _pipeline;
        private bool _isWebViewReady = false;
        private string _currentContent = "";
        private string _originalState = "";
        private ScrollViewer? _parentSv;
        private ObservableCollection<string> _tagsList = new ObservableCollection<string>();

        public EditorPage()
        {
            InitializeComponent();

            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .UseMathematics()
                .UseEmojiAndSmiley()
                .Build();

            Loaded += EditorPage_Loaded;
            Unloaded += EditorPage_Unloaded;
            InitializeTimeComboBoxes();
            InitializeWebViewsAsync();
            TagsItemsControl.ItemsSource = _tagsList;
        }

        private void InitializeTimeComboBoxes()
        {
            var hours = Enumerable.Range(0, 24).Select(i => i.ToString("D2")).ToList();
            var minutes = Enumerable.Range(0, 60).Select(i => i.ToString("D2")).ToList();

            PublishHourBox.ItemsSource = hours;
            PublishMinuteBox.ItemsSource = minutes;
            ModifyHourBox.ItemsSource = hours;
            ModifyMinuteBox.ItemsSource = minutes;
        }

        private async void InitializeWebViewsAsync()
        {
            var webViewDataDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlogTools",
                "WebView2");
            System.IO.Directory.CreateDirectory(webViewDataDir);

            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, webViewDataDir);
            
            await PreviewWebView.EnsureCoreWebView2Async(env);
            await EditorWebView.EnsureCoreWebView2Async(env);

            var katexFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "katex");
            PreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "localassets", katexFolder,
                Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

            if (!string.IsNullOrEmpty(App.JekyllContext.BlogPath))
            {
                PreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "bloglocal", App.JekyllContext.BlogPath,
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
            }

            var isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
            var darkCss = isDark ? "body { background-color: #1e1e1e; color: #d4d4d4; }" : "body { background-color: #ffffff; color: #000000; }";

            var katexCss = "";
            var katexJs = "";
            try
            {
                katexCss = System.IO.File.ReadAllText(System.IO.Path.Combine(katexFolder, "katex.min.css"));
                katexJs = System.IO.File.ReadAllText(System.IO.Path.Combine(katexFolder, "katex.min.js"));
                katexCss = katexCss.Replace("fonts/", "https://localassets/fonts/");
            }
            catch { }

            var renderScript = @"
      function renderMathInElement(el) {
        if (!window.katex) return;
        var mathEls = el.querySelectorAll('.math');
        mathEls.forEach(function(m) {
          try {
            var tex = m.textContent || '';
            var isDisplay = m.tagName === 'DIV';
            if (tex.startsWith('\\(') && tex.endsWith('\\)')) {
              tex = tex.slice(2, -2);
            } else if (tex.startsWith('\\[') && tex.endsWith('\\]')) {
              tex = tex.slice(2, -2);
              isDisplay = true;
            }
            katex.render(tex.trim(), m, { throwOnError: false, displayMode: isDisplay });
          } catch(e) {
            console.log('KaTeX render error:', e.message);
          }
        });
      }
";
            string trackColor = "transparent";
            string thumbColor = isDark ? "#666" : "#aaa";
            string thumbHover = isDark ? "#888" : "#888";
            string bga = isDark ? "#1e1e1e" : "#fafafa";
            string scrollbarCss = $@"
                ::-webkit-scrollbar {{ width: 14px; height: 14px; }}
                ::-webkit-scrollbar-track {{ background: {trackColor}; }}
                ::-webkit-scrollbar-thumb {{ background: {thumbColor}; border-radius: 7px; border: 3px solid {bga}; }}
                ::-webkit-scrollbar-thumb:hover {{ background: {thumbHover}; }}
                ::-webkit-scrollbar-corner {{ background: transparent; }}
            ";

            var initialHtml = "<!DOCTYPE html><html><head><meta charset='utf-8' />"
                + "<base href='https://bloglocal/' />"
                + "<style>" + katexCss + "</style>"
                + "<script>" + katexJs + "</script>"
                + "<style>"
                + darkCss
                + scrollbarCss
                +  $@"
        html, body {{ margin: 0; padding: 0; overflow: hidden; height: 100%; width: 100%; box-sizing: border-box; }}
        #content {{ font-family: -apple-system, 'Microsoft YaHei UI', Helvetica, Arial, sans-serif; padding: 20px; line-height: 1.6; word-wrap: break-word; box-sizing: border-box; height: 100%; overflow-y: auto; overflow-x: hidden; }}
        img {{ max-width: 100%; height: auto; border-radius: 6px; }}
        pre {{ background: {(isDark ? "#2d2d2d" : "#f6f8fa")}; padding: 12px; border-radius: 6px; overflow-x: auto; }}
        code {{ font-family: Consolas, monospace; background: {(isDark ? "#333" : "#eee")}; padding: 2px 4px; border-radius: 4px; }}
        pre code {{ background: none; padding: 0; }}
        blockquote {{ border-left: 4px solid #0078D4; padding-left: 10px; color: {(isDark ? "#aaa" : "#555")}; margin-left: 0; }}
        table {{ border-collapse: collapse; width: 100%; }}
        th, td {{ border: 1px solid {(isDark ? "#444" : "#ddd")}; padding: 8px; text-align: left; }}
        .katex {{ font-size: 1.1em; }}
        .katex-display {{ overflow-x: auto; overflow-y: hidden; padding: 4px 0; }}
"
                + "</style>"
                + "<script>" + renderScript + "</script>"
                + "</head><body><div id='content'></div></body></html>";

            PreviewWebView.NavigateToString(initialHtml);

            PreviewWebView.NavigationCompleted += (s, e) =>
            {
                _isWebViewReady = true;
                UpdateWebViewContent();
            };

            var editorHtml = $"<!DOCTYPE html><html><head><meta charset='utf-8' /><style>{darkCss} {scrollbarCss} " +
            "html, body { margin: 0; padding: 0; overflow: hidden; height: 100%; width: 100%; box-sizing: border-box; } " +
            "textarea { width: 100%; height: 100%; box-sizing: border-box; padding: 20px; border: none; outline: none; resize: none; " +
            "font-family: Consolas, monospace; font-size: 15px; background-color: transparent; color: inherit; line-height: 1.6; } " +
            "</style></head><body>" +
            "<textarea id='editor' spellcheck='false' placeholder='在此撰写 Markdown 内容...'></textarea>" +
            "<script>" +
            "const el = document.getElementById('editor');" +
            "el.addEventListener('input', () => { window.chrome.webview.postMessage('CONTENT:' + el.value); });" +
            "window.chrome.webview.addEventListener('message', event => { if (el.value !== event.data) el.value = event.data; });" +
            "el.addEventListener('paste', function(e) {" +
            "    var items = (e.clipboardData || e.originalEvent.clipboardData).items;" +
            "    for (var index in items) {" +
            "        var item = items[index];" +
            "        if (item.kind === 'file' && item.type.indexOf('image/') !== -1) {" +
            "            e.preventDefault();" +
            "            window.chrome.webview.postMessage('ACTION:pasteImage');" +
            "            break;" +
            "        }" +
            "    }" +
            "});" +
            "</script></body></html>";

            EditorWebView.NavigateToString(editorHtml);
            EditorWebView.WebMessageReceived += EditorWebView_WebMessageReceived;
        }

        private void EditorWebView_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            var msg = e.TryGetWebMessageAsString() ?? "";
            if (msg.StartsWith("CONTENT:"))
            {
                _currentContent = msg.Substring(8);
                UpdateWebViewContent();
                SmartDetectMath();
            }
            else if (msg == "ACTION:pasteImage")
            {
                _ = HandlePastedImageAsync();
            }
        }

        private async System.Threading.Tasks.Task HandlePastedImageAsync()
        {
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                var msg = new Wpf.Ui.Controls.MessageBox { Title = "提示", Content = "请先填写文章标题，以便确定图片存放目录！", CloseButtonText = "确定" };
                await msg.ShowDialogAsync();
                return;
            }

            try
            {
                string[] pastedFiles = Array.Empty<string>();
                System.Windows.Media.Imaging.BitmapSource? pastedImage = null;

                if (System.Windows.Clipboard.ContainsFileDropList())
                {
                    var dropList = System.Windows.Clipboard.GetFileDropList();
                    pastedFiles = dropList.Cast<string>().Where(f => 
                        f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || 
                        f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                        f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || 
                        f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || 
                        f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)).ToArray();
                }
                else if (System.Windows.Clipboard.ContainsImage())
                {
                    pastedImage = System.Windows.Clipboard.GetImage();
                }

                if (pastedFiles.Length == 0 && pastedImage == null) return;

                var safeDirName = System.Text.RegularExpressions.Regex.Replace(TitleBox.Text, @"[\\/:*?""<>|]+", "-").Trim('-', ' ');
                safeDirName = System.Text.RegularExpressions.Regex.Replace(safeDirName, @"\s+", "-").ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(safeDirName)) safeDirName = "untitled";

                var relativeDir = $"assets/img/inposts/{safeDirName}";
                var absPathDir = System.IO.Path.Combine(App.JekyllContext.BlogPath, relativeDir.Replace("/", "\\"));
                
                if (!System.IO.Directory.Exists(absPathDir))
                {
                    System.IO.Directory.CreateDirectory(absPathDir);
                }

                string injectedMd = "";

                foreach (var file in pastedFiles)
                {
                    var fileName = System.IO.Path.GetFileName(file);
                    var destFile = System.IO.Path.Combine(absPathDir, fileName);
                    int counter = 1;
                    while (System.IO.File.Exists(destFile))
                    {
                        destFile = System.IO.Path.Combine(absPathDir, $"{System.IO.Path.GetFileNameWithoutExtension(fileName)}-{counter}{System.IO.Path.GetExtension(fileName)}");
                        fileName = System.IO.Path.GetFileName(destFile);
                        counter++;
                    }
                    System.IO.File.Copy(file, destFile);
                    injectedMd += $"![{System.IO.Path.GetFileNameWithoutExtension(fileName)}](/{relativeDir}/{fileName})\n";
                }

                if (pastedImage != null)
                {
                    var fileName = $"image-{DateTime.Now:yyyyMMddHHmmss}.png";
                    var destFile = System.IO.Path.Combine(absPathDir, fileName);
                    using (var fileStream = new System.IO.FileStream(destFile, System.IO.FileMode.Create))
                    {
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(pastedImage));
                        encoder.Save(fileStream);
                    }
                    injectedMd += $"![{System.IO.Path.GetFileNameWithoutExtension(fileName)}](/{relativeDir}/{fileName})\n";
                }

                injectedMd = injectedMd.TrimEnd('\n');

                var script = $@"
                    (function() {{
                        var el = document.getElementById('editor');
                        if (el) {{
                            el.focus();
                            var textToInsert = {System.Text.Json.JsonSerializer.Serialize(injectedMd)};
                            document.execCommand('insertText', false, textToInsert);
                            var event = new Event('input', {{ bubbles: true }});
                            el.dispatchEvent(event);
                        }}
                    }})();
                ";
                await EditorWebView.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                var msg = new Wpf.Ui.Controls.MessageBox { Title = "出错", Content = $"粘贴图片处理失败:\n{ex.Message}", CloseButtonText = "确定" };
                await msg.ShowDialogAsync();
            }
        }

        private bool _allowNav = false;
        private async void Nav_Navigating(Wpf.Ui.Controls.NavigationView sender, Wpf.Ui.Controls.NavigatingCancelEventArgs e)
        {
            if (_allowNav) return;
            if (CheckIsDirty())
            {
                e.Cancel = true;
                var msg = new Wpf.Ui.Controls.MessageBox { Title = "确认离开", Content = "您有未保存的草稿，确定要离开吗？未保存的内容将会丢失。", PrimaryButtonText = "确定离开", CloseButtonText = "取消" };
                var res = await msg.ShowDialogAsync();
                if (res == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    _allowNav = true;
                    // Let's just navigate to Dashboard if we can't figure out the target page
                    sender.Navigate(typeof(DashboardPage));
                }
            }
        }

        private bool _allowClose = false;
        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_allowClose) return;
            if (CheckIsDirty())
            {
                e.Cancel = true;
                var msg = new Wpf.Ui.Controls.MessageBox { Title = "确认退出", Content = "您有未保存的草稿，确定要退出程序吗？未保存的内容将会丢失。", PrimaryButtonText = "确定退出", CloseButtonText = "取消" };
                var res = await msg.ShowDialogAsync();
                if (res == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    _allowClose = true;
                    Application.Current.MainWindow.Close();
                }
            }
        }

        private void UpdateOriginalState()
        {
            try
            {
                var post = GeneratePostObject();
                _originalState = System.Text.Json.JsonSerializer.Serialize(post);
            }
            catch { }
        }

        private bool CheckIsDirty()
        {
            try
            {
                var post = GeneratePostObject();
                return System.Text.Json.JsonSerializer.Serialize(post) != _originalState;
            }
            catch { return false; }
        }

        private void EditorPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_parentSv != null) _parentSv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            
            var nav = (Application.Current.MainWindow as MainWindow)?.RootNavigation;
            if (nav != null) nav.Navigating -= Nav_Navigating;
            Application.Current.MainWindow.Closing -= MainWindow_Closing;
        }

        private void MetadataExpander_Expanded(object sender, RoutedEventArgs e)
        {
            var settings = BlogTools.Services.StorageService.Load();
            if (settings.RememberMetadataExpanded && !settings.IsMetadataExpanded)
            {
                settings.IsMetadataExpanded = true;
                BlogTools.Services.StorageService.Save(settings);
            }
        }

        private void MetadataExpander_Collapsed(object sender, RoutedEventArgs e)
        {
            var settings = BlogTools.Services.StorageService.Load();
            if (settings.RememberMetadataExpanded && settings.IsMetadataExpanded)
            {
                settings.IsMetadataExpanded = false;
                BlogTools.Services.StorageService.Save(settings);
            }
        }

        private void EditorPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Disable global parent scroll to ensure page fits viewport perfectly
            _parentSv = FindVisualParent<ScrollViewer>(this);
            if (_parentSv != null) _parentSv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

            var settings = BlogTools.Services.StorageService.Load();
            if (settings.RememberMetadataExpanded)
            {
                MetadataExpander.IsExpanded = settings.IsMetadataExpanded;
            }

            var nav = (Application.Current.MainWindow as MainWindow)?.RootNavigation;
            if (nav != null) nav.Navigating += Nav_Navigating;
            Application.Current.MainWindow.Closing += MainWindow_Closing;

            var allPosts = App.JekyllContext.GetAllPosts();
            var primaryCats = new HashSet<string>();
            var secondaryCats = new HashSet<string>();
            foreach (var p in allPosts)
            {
                if (p.Categories.Count > 0) primaryCats.Add(p.Categories[0]);
                if (p.Categories.Count > 1) secondaryCats.Add(p.Categories[1]);
            }
            PrimaryCategoryBox.ItemsSource = primaryCats.OrderBy(c => c).ToList();
            SecondaryCategoryBox.ItemsSource = secondaryCats.OrderBy(c => c).ToList();

            if (App.CurrentEditPost != null)
            {
                var post = App.CurrentEditPost;
                TitleBox.Text = post.Title;

                PublishDatePicker.SelectedDate = post.Date;
                PublishHourBox.SelectedItem = post.Date.Hour.ToString("D2");
                PublishMinuteBox.SelectedItem = post.Date.Minute.ToString("D2");

                if (post.LastModifiedAt.HasValue)
                {
                    ModifyDatePicker.SelectedDate = post.LastModifiedAt;
                    ModifyHourBox.SelectedItem = post.LastModifiedAt.Value.Hour.ToString("D2");
                    ModifyMinuteBox.SelectedItem = post.LastModifiedAt.Value.Minute.ToString("D2");
                }

                if (post.Categories.Count > 0) PrimaryCategoryBox.Text = post.Categories[0];
                if (post.Categories.Count > 1) SecondaryCategoryBox.Text = post.Categories[1];
                
                _tagsList.Clear();
                if (post.Tags != null)
                {
                    foreach (var t in post.Tags)
                        if (!string.IsNullOrWhiteSpace(t)) _tagsList.Add(t.Trim());
                }

                AuthorBox.Text = post.Author;
                MathSwitch.IsChecked = post.Math;
                TocSwitch.IsChecked = post.Toc;
                PinSwitch.IsChecked = post.Pin;
                DescriptionBox.Text = post.Description;
                ImageBox.Text = post.Image;

                _currentContent = post.Content ?? "";
            }
            else
            {
                SetPublishNow_Click(null, null);
                TocSwitch.IsChecked = true;
                _currentContent = "";
            }
            
            if (EditorWebView.CoreWebView2 != null)
            {
                EditorWebView.CoreWebView2.PostWebMessageAsString(_currentContent);
            }
            else
            {
                EditorWebView.NavigationCompleted += (s, ev) => 
                {
                    EditorWebView.CoreWebView2.PostWebMessageAsString(_currentContent);
                };
            }
            
            UpdateOriginalState();
        }

        private void SmartDetectMath()
        {
            if (string.IsNullOrEmpty(_currentContent)) return;

            if (_currentContent.Contains("$"))
            {
                bool hasMath = System.Text.RegularExpressions.Regex.IsMatch(_currentContent, @"(\$\$.*?\$\$)|(\$.*?\$)", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (hasMath && MathSwitch.IsChecked == false)
                {
                    MathSwitch.IsChecked = true;
                }
            }
        }

        private async void UpdateWebViewContent()
        {
            if (!_isWebViewReady || PreviewWebView.CoreWebView2 == null) return;

            var htmlContent = Markdown.ToHtml(_currentContent, _pipeline);
            var base64Html = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(htmlContent));

            var script = $@"
                try {{
                    const b64 = '{base64Html}';
                    const bin = window.atob(b64);
                    const bytes = new Uint8Array(bin.length);
                    for (let i = 0; i < bin.length; i++) {{
                        bytes[i] = bin.charCodeAt(i);
                    }}
                    const decoded = new TextDecoder('utf-8').decode(bytes);
                    const el = document.getElementById('content');
                    el.innerHTML = decoded;
                    renderMathInElement(el);
                }} catch(e) {{
                    console.error('Render error:', e);
                }}
            ";

            await PreviewWebView.ExecuteScriptAsync(script);
        }

        private void NewPost_Click(object sender, RoutedEventArgs e)
        {
            App.CurrentEditPost = null;
            TitleBox.Clear();
            SetPublishNow_Click(null, null);
            ModifyDatePicker.SelectedDate = null;
            ModifyHourBox.SelectedIndex = -1;
            ModifyMinuteBox.SelectedIndex = -1;
            PrimaryCategoryBox.Text = "";
            SecondaryCategoryBox.Text = "";
            _tagsList.Clear();
            TagInputBox.Clear();
            AuthorBox.Clear();
            MathSwitch.IsChecked = false;
            TocSwitch.IsChecked = true;
            PinSwitch.IsChecked = false;
            DescriptionBox.Clear();
            ImageBox.Clear();
            
            _currentContent = "";
            if (EditorWebView.CoreWebView2 != null)
                EditorWebView.CoreWebView2.PostWebMessageAsString("");
                
            UpdateOriginalState();
            ShowInfo("已重置，准备开始新创作。", InfoBarSeverity.Informational);
        }

        private void SetPublishNow_Click(object? sender, RoutedEventArgs? e)
        {
            PublishDatePicker.SelectedDate = DateTime.Now;
            PublishHourBox.SelectedItem = DateTime.Now.Hour.ToString("D2");
            PublishMinuteBox.SelectedItem = DateTime.Now.Minute.ToString("D2");
        }

        private void SetModifyNow_Click(object sender, RoutedEventArgs e)
        {
            ModifyDatePicker.SelectedDate = DateTime.Now;
            ModifyHourBox.SelectedItem = DateTime.Now.Hour.ToString("D2");
            ModifyMinuteBox.SelectedItem = DateTime.Now.Minute.ToString("D2");
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                ShowInfo("文章必须有一个标题！", InfoBarSeverity.Error);
                return;
            }

            var post = GeneratePostObject();
            App.JekyllContext.SavePost(post);
            App.CurrentEditPost = post;

            UpdateOriginalState();
            ShowInfo($"已存放到本地: {post.FileName}", InfoBarSeverity.Success);
        }

        private async void PublishButton_Click(object sender, RoutedEventArgs e)
        {
            SaveButton_Click(sender, e);
            if (!StatusInfo.IsOpen || StatusInfo.Severity == InfoBarSeverity.Error)
                return;

            ShowInfo("正在拉取并推送至远端...", InfoBarSeverity.Informational);
            try
            {
                var pullResult = await App.GitContext.PullAsync();
                if (pullResult.Contains("CONFLICT") || pullResult.Contains("Automatic merge failed"))
                {
                    ShowInfo("拉取冲突，请手动解决后重试。", InfoBarSeverity.Error);
                    return;
                }

                await App.GitContext.CommitAndPushAsync($"Update post: {TitleBox.Text}");
                ShowInfo("发布成功！", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowInfo($"推送失败: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private BlogPost GeneratePostObject()
        {
            var cats = new List<string>();
            var prim = PrimaryCategoryBox.Text?.Trim();
            var sec = SecondaryCategoryBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(prim)) cats.Add(prim);
            if (!string.IsNullOrWhiteSpace(sec)) cats.Add(sec);
            
            ParseTagsInput(); // Ensure pending input is parsed
            var tags = _tagsList.ToList();

            var publishDate = PublishDatePicker.SelectedDate ?? DateTime.Now;
            int h = int.TryParse(PublishHourBox.SelectedItem as string, out int hVal) ? hVal : DateTime.Now.Hour;
            int m = int.TryParse(PublishMinuteBox.SelectedItem as string, out int mVal) ? mVal : DateTime.Now.Minute;
            publishDate = new DateTime(publishDate.Year, publishDate.Month, publishDate.Day, h, m, 0);

            DateTime? modifyDate = null;
            if (ModifyDatePicker.SelectedDate.HasValue)
            {
                var md = ModifyDatePicker.SelectedDate.Value;
                int mh = int.TryParse(ModifyHourBox.SelectedItem as string, out int mhVal) ? mhVal : DateTime.Now.Hour;
                int mm = int.TryParse(ModifyMinuteBox.SelectedItem as string, out int mmVal) ? mmVal : DateTime.Now.Minute;
                modifyDate = new DateTime(md.Year, md.Month, md.Day, mh, mm, 0);
            }

            return new BlogPost
            {
                Title = TitleBox.Text,
                Date = publishDate,
                LastModifiedAt = modifyDate,
                Categories = cats,
                Tags = tags,
                Author = AuthorBox.Text,
                Math = MathSwitch.IsChecked == true,
                Toc = TocSwitch.IsChecked == true,
                Pin = PinSwitch.IsChecked == true,
                Description = DescriptionBox.Text,
                Image = ImageBox.Text,
                Content = _currentContent,
                FileName = App.CurrentEditPost?.FileName ?? string.Empty
            };
        }

        private void ShowInfo(string message, InfoBarSeverity severity)
        {
            StatusInfo.Message = message;
            StatusInfo.Severity = severity;
            StatusInfo.IsOpen = true;
        }

        // ─── Sync scrolling & Tools ─────────────────────────────────
        
        private async void InsertImage_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                var msg = new Wpf.Ui.Controls.MessageBox { Title = "提示", Content = "请先填写文章标题，以便确定图片存放目录！", CloseButtonText = "确定" };
                await msg.ShowDialogAsync();
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "插入本地图片",
                Filter = "图像文件|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.svg|所有文件|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // 1. Determine safe directory name based on article title or filename
                    var safeDirName = System.Text.RegularExpressions.Regex.Replace(TitleBox.Text, @"[\\/:*?""<>|]+", "-").Trim('-', ' ');
                    safeDirName = System.Text.RegularExpressions.Regex.Replace(safeDirName, @"\s+", "-").ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(safeDirName)) safeDirName = "untitled";

                    var relativeDir = $"assets/img/inposts/{safeDirName}";
                    var absPathDir = System.IO.Path.Combine(App.JekyllContext.BlogPath, relativeDir.Replace("/", "\\"));
                    
                    if (!System.IO.Directory.Exists(absPathDir))
                    {
                        System.IO.Directory.CreateDirectory(absPathDir);
                    }

                    // 2. Copy file
                    var fileName = System.IO.Path.GetFileName(dialog.FileName);
                    var destFile = System.IO.Path.Combine(absPathDir, fileName);
                    
                    // Add suffix if file exists to prevent overwrite
                    int counter = 1;
                    while (System.IO.File.Exists(destFile))
                    {
                        var nameOnly = System.IO.Path.GetFileNameWithoutExtension(fileName);
                        var ext = System.IO.Path.GetExtension(fileName);
                        destFile = System.IO.Path.Combine(absPathDir, $"{nameOnly}-{counter}{ext}");
                        fileName = System.IO.Path.GetFileName(destFile);
                        counter++;
                    }
                    
                    System.IO.File.Copy(dialog.FileName, destFile);

                    // 3. Inject MD into Editor at cursor
                    var mdSyntax = $"![{System.IO.Path.GetFileNameWithoutExtension(fileName)}](/{relativeDir}/{fileName})";
                    
                    var script = $@"
                        (function() {{
                            var el = document.getElementById('editor');
                            if (el) {{
                                el.focus();
                                var textToInsert = {System.Text.Json.JsonSerializer.Serialize(mdSyntax)};
                                document.execCommand('insertText', false, textToInsert);
                                var event = new Event('input', {{ bubbles: true }});
                                el.dispatchEvent(event);
                            }}
                        }})();
                    ";
                    
                    await EditorWebView.ExecuteScriptAsync(script);
                }
                catch (Exception ex)
                {
                    var msg = new Wpf.Ui.Controls.MessageBox { Title = "出错", Content = $"处理图片时出错:\n{ex.Message}", CloseButtonText = "确定" };
                    await msg.ShowDialogAsync();
                }
            }
        }

        private async void SyncEditorToPreview_Click(object sender, RoutedEventArgs e)
        {
            if (!_isWebViewReady || PreviewWebView.CoreWebView2 == null || EditorWebView.CoreWebView2 == null) return;

            var getRatioScript = @"
                (function() {
                    var el = document.getElementById('editor');
                    if (!el) return 0;
                    var maxScroll = el.scrollHeight - el.clientHeight;
                    if (maxScroll <= 0) return 0;
                    return el.scrollTop / maxScroll;
                })();
            ";
            var result = await EditorWebView.ExecuteScriptAsync(getRatioScript);
            
            if (double.TryParse(result, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double ratio))
            {
                ratio = Math.Clamp(ratio, 0, 1);
                var script = $@"
                    (function() {{
                        var el = document.getElementById('content');
                        if (!el) return;
                        var maxScroll = el.scrollHeight - el.clientHeight;
                        if (maxScroll > 0) {{
                            el.scrollTop = maxScroll * {ratio.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                        }}
                    }})();
                ";
                await PreviewWebView.ExecuteScriptAsync(script);
            }
        }

        private async void SyncPreviewToEditor_Click(object sender, RoutedEventArgs e)
        {
            if (!_isWebViewReady || PreviewWebView.CoreWebView2 == null || EditorWebView.CoreWebView2 == null) return;

            var result = await PreviewWebView.ExecuteScriptAsync(@"
                (function() {
                    var el = document.getElementById('content');
                    if (!el) return 0;
                    var maxScroll = el.scrollHeight - el.clientHeight;
                    if (maxScroll <= 0) return 0;
                    return el.scrollTop / maxScroll;
                })();
            ");

            if (double.TryParse(result, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double ratio))
            {
                ratio = Math.Clamp(ratio, 0, 1);
                var script = $@"
                    (function() {{
                        var el = document.getElementById('editor');
                        if (!el) return;
                        var maxScroll = el.scrollHeight - el.clientHeight;
                        if (maxScroll > 0) {{
                            el.scrollTop = maxScroll * {ratio.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                        }}
                    }})();
                ";
                await EditorWebView.ExecuteScriptAsync(script);
            }
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindVisualParent<T>(parentObject);
        }
        private void ParseTagsInput()
        {
            var text = TagInputBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            var delimiters = new[] { ',', '，', '.', '。', ';', '；', '、' };
            var tokens = text.Split(delimiters, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0);
            foreach (var token in tokens)
            {
                if (!_tagsList.Contains(token))
                {
                    _tagsList.Add(token);
                }
            }
            TagInputBox.Text = "";
        }

        private void TagInputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                ParseTagsInput();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Back && string.IsNullOrEmpty(TagInputBox.Text) && _tagsList.Count > 0)
            {
                _tagsList.RemoveAt(_tagsList.Count - 1);
            }
        }

        private void TagInputBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ParseTagsInput();
        }

        private void TagsBorder_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            TagInputBox.Focus();
        }

        private void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tag)
            {
                _tagsList.Remove(tag);
            }
        }
    }
}
