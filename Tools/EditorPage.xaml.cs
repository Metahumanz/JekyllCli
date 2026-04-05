using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BlogTools.Models;
using Markdig;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace BlogTools
{
    public partial class EditorPage : Page
    {
        private MarkdownPipeline _pipeline;
        private bool _isWebViewReady = false;
        private string _currentContent = "";
        private ScrollViewer? _parentSv;

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
            "el.addEventListener('input', () => { window.chrome.webview.postMessage(el.value); });" +
            "window.chrome.webview.addEventListener('message', event => { if (el.value !== event.data) el.value = event.data; });" +
            "</script></body></html>";

            EditorWebView.NavigateToString(editorHtml);
            EditorWebView.WebMessageReceived += EditorWebView_WebMessageReceived;
        }

        private void EditorWebView_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            _currentContent = e.TryGetWebMessageAsString() ?? "";
            UpdateWebViewContent();
            SmartDetectMath();
        }

        private void EditorPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_parentSv != null) _parentSv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
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

                CategoriesBox.Text = string.Join(", ", post.Categories);
                TagsBox.Text = string.Join(", ", post.Tags);

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
            CategoriesBox.Clear();
            TagsBox.Clear();
            AuthorBox.Clear();
            MathSwitch.IsChecked = false;
            TocSwitch.IsChecked = true;
            PinSwitch.IsChecked = false;
            DescriptionBox.Clear();
            ImageBox.Clear();
            
            _currentContent = "";
            if (EditorWebView.CoreWebView2 != null)
                EditorWebView.CoreWebView2.PostWebMessageAsString("");
                
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
            var cats = CategoriesBox.Text.Split(new[] { ',', '\uFF0C', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            var tags = TagsBox.Text.Split(new[] { ',', '\uFF0C', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();

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

        // ─── Sync scrolling ──────────────────────────────────────

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
    }
}
