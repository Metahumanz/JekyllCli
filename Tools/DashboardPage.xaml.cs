using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BlogTools
{
    /// <summary>
    /// ViewModel wrapper for timeline display
    /// </summary>
    public class TimelinePostItem
    {
        public string Title { get; set; } = "";
        public System.DateTime Date { get; set; }
        public string CategoryDisplay { get; set; } = "";
        public Models.BlogPost Source { get; set; } = null!;
    }

    public class DetailGroupItem : System.ComponentModel.INotifyPropertyChanged
    {
        public string Header { get; set; } = string.Empty;
        public List<string> Posts { get; set; } = new();
        public List<DetailGroupItem> Children { get; set; } = new();
        public bool HasChildren => Children.Count > 0;

        private bool _isExpanded;
        public bool IsExpanded 
        { 
            get => _isExpanded; 
            set { _isExpanded = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsExpanded))); } 
        }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class DashboardPage : Page
    {
        private List<Models.BlogPost> _allPosts = new();
        private HashSet<string> _allCategories = new();
        private HashSet<string> _allTags = new();
        // Track category hierarchy: primary -> sub-categories
        private Dictionary<string, HashSet<string>> _categoryTree = new();

        public DashboardPage()
        {
            InitializeComponent();
            Loaded += DashboardPage_Loaded;
            Unloaded += DashboardPage_Unloaded;
            App.BlogFilesChanged += OnBlogFilesChanged;
        }

        private void DashboardPage_Unloaded(object sender, RoutedEventArgs e)
        {
            App.BlogFilesChanged -= OnBlogFilesChanged;
        }

        private void OnBlogFilesChanged()
        {
            Dispatcher.InvokeAsync(() => DashboardPage_Loaded(this, new RoutedEventArgs()));
        }

        private async void DashboardPage_Loaded(object sender, RoutedEventArgs e)
        {
            var parentSv = FindVisualParent<ScrollViewer>(this);
            parentSv?.ScrollToTop();

            // Dynamic welcome message
            var config = App.JekyllContext.LoadConfig();
            if (config.TryGetValue("title", out var titleObj) && titleObj is string siteTitle && !string.IsNullOrWhiteSpace(siteTitle))
            {
                WelcomeTitleText.Text = siteTitle + " 管理后台";
                WelcomeSubText.Text = "轻松管理您的「" + siteTitle + "」站点！";
            }

            _allPosts = App.JekyllContext.GetAllPosts();
            
            // Build stats
            _allCategories.Clear();
            _allTags.Clear();
            _categoryTree.Clear();

            foreach (var post in _allPosts)
            {
                if (post.Categories != null && post.Categories.Count > 0)
                {
                    var primary = post.Categories[0]?.Trim();
                    if (!string.IsNullOrWhiteSpace(primary))
                    {
                        _allCategories.Add(primary);
                        if (!_categoryTree.ContainsKey(primary))
                            _categoryTree[primary] = new HashSet<string>();

                        if (post.Categories.Count > 1)
                        {
                            var sub = post.Categories[1]?.Trim();
                            if (!string.IsNullOrWhiteSpace(sub))
                            {
                                _allCategories.Add(sub);
                                _categoryTree[primary].Add(sub);
                            }
                        }
                    }
                }
                if (post.Tags != null)
                {
                    foreach (var t in post.Tags)
                        if (!string.IsNullOrWhiteSpace(t)) _allTags.Add(t.Trim());
                }
            }

            PostCountText.Text = _allPosts.Count.ToString();
            CategoryCountText.Text = _allCategories.Count.ToString();
            TagCountText.Text = _allTags.Count.ToString();

            // Build timeline - ALL posts sorted by date descending
            var timelineItems = _allPosts
                .OrderByDescending(p => p.Date)
                .Select(p => new TimelinePostItem
                {
                    Title = p.Title,
                    Date = p.Date,
                    CategoryDisplay = BuildCategoryDisplay(p),
                    Source = p
                })
                .ToList();
            TimelineList.ItemsSource = timelineItems;

            // Check Git sync status
            await CheckGitSyncStatusAsync();
        }

        private string BuildCategoryDisplay(Models.BlogPost post)
        {
            if (post.Categories == null || post.Categories.Count == 0)
                return "-";
            var parts = post.Categories.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            return parts.Count switch
            {
                0 => "-",
                1 => parts[0].Trim(),
                _ => parts[0].Trim() + " / " + parts[1].Trim()
            };
        }

        private async System.Threading.Tasks.Task CheckGitSyncStatusAsync()
        {
            try
            {
                var (behind, ahead, _) = await App.GitContext.CheckSyncStatusAsync();
                if (behind > 0)
                {
                    SyncInfoBar.Message = $"⚠ 您的本地已落后云端 {behind} 个版本，请及时同步！";
                    SyncInfoBar.Severity = Wpf.Ui.Controls.InfoBarSeverity.Warning;
                    SyncInfoBar.IsOpen = true;
                }
                else if (ahead > 0)
                {
                    SyncInfoBar.Message = $"ℹ 您的本地领先云端 {ahead} 个版本，记得推送。";
                    SyncInfoBar.Severity = Wpf.Ui.Controls.InfoBarSeverity.Informational;
                    SyncInfoBar.IsOpen = true;
                }
                else
                {
                    SyncInfoBar.IsOpen = false;
                }
            }
            catch
            {
                // Silently ignore network errors on load
            }
        }

        // ─── Card clicks ──────────────────────────────────────────

        private void PostCountCard_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to ManagePostsPage (full article list)
            var w = Application.Current.MainWindow as MainWindow;
            w?.RootNavigation.Navigate(typeof(ManagePostsPage));
        }

        private string _currentDetailView = "";

        private void CategoryCountCard_Click(object sender, RoutedEventArgs e)
        {
            if (DetailPanel.Visibility == Visibility.Visible && _currentDetailView == "Categories")
            {
                DetailPanel.Visibility = Visibility.Collapsed;
                _currentDetailView = "";
                return;
            }

            _currentDetailView = "Categories";
            var groups = new List<DetailGroupItem>();
            foreach (var kv in _categoryTree.OrderBy(k => k.Key))
            {
                var primaryGroup = new DetailGroupItem { Header = $"📂 {kv.Key}" };

                // Build sub-category children
                foreach (var sub in kv.Value.OrderBy(s => s))
                {
                    var subGroup = new DetailGroupItem { Header = $"📁 {sub}" };
                    var postsInSub = _allPosts
                        .Where(p => p.Categories != null && p.Categories.Count > 1
                                    && p.Categories[0]?.Trim() == kv.Key
                                    && p.Categories[1]?.Trim() == sub)
                        .OrderByDescending(p => p.Date)
                        .ToList();
                    foreach (var p in postsInSub)
                    {
                        subGroup.Posts.Add($"📄 {p.Title}");
                    }
                    if (subGroup.Posts.Count > 0) primaryGroup.Children.Add(subGroup);
                }

                // Posts that only have primary category (no sub-category)
                var postsOnlyPrimary = _allPosts
                    .Where(p => p.Categories != null && p.Categories.Count == 1
                                && p.Categories[0]?.Trim() == kv.Key)
                    .OrderByDescending(p => p.Date)
                    .ToList();
                foreach (var p in postsOnlyPrimary)
                {
                    primaryGroup.Posts.Add($"📄 {p.Title}");
                }

                if (primaryGroup.Children.Count > 0 || primaryGroup.Posts.Count > 0)
                    groups.Add(primaryGroup);
            }

            DetailPanelTitle.Text = $"分类一览（{_allCategories.Count} 个）";
            DetailItemsControl.ItemsSource = groups;
            DetailPanel.Visibility = Visibility.Visible;
        }

        private void TagCountCard_Click(object sender, RoutedEventArgs e)
        {
            if (DetailPanel.Visibility == Visibility.Visible && _currentDetailView == "Tags")
            {
                DetailPanel.Visibility = Visibility.Collapsed;
                _currentDetailView = "";
                return;
            }

            _currentDetailView = "Tags";
            var groups = new List<DetailGroupItem>();
            foreach (var tag in _allTags.OrderBy(t => t))
            {
                var group = new DetailGroupItem { Header = $"🏷 {tag}" };
                var postsWithTag = _allPosts.Where(p => p.Tags.Contains(tag)).OrderByDescending(p => p.Date).ToList();
                foreach (var p in postsWithTag)
                {
                    group.Posts.Add($"📄 {p.Title}");
                }
                if (group.Posts.Count > 0) groups.Add(group);
            }

            DetailPanelTitle.Text = $"标签一览（{_allTags.Count} 个）";
            DetailItemsControl.ItemsSource = groups;
            DetailPanel.Visibility = Visibility.Visible;
        }

        private void ExpandAllDetail_Click(object sender, RoutedEventArgs e)
        {
            if (DetailItemsControl.ItemsSource is List<DetailGroupItem> items)
            {
                SetExpandedRecursive(items, true);
            }
        }

        private void CollapseAllDetail_Click(object sender, RoutedEventArgs e)
        {
            if (DetailItemsControl.ItemsSource is List<DetailGroupItem> items)
            {
                SetExpandedRecursive(items, false);
            }
        }

        private void SetExpandedRecursive(List<DetailGroupItem> items, bool expanded)
        {
            foreach (var item in items)
            {
                item.IsExpanded = expanded;
                if (item.Children.Count > 0)
                    SetExpandedRecursive(item.Children, expanded);
            }
        }

        private void CloseDetailPanel_Click(object sender, RoutedEventArgs e)
        {
            DetailPanel.Visibility = Visibility.Collapsed;
            _currentDetailView = "";
        }

        // ─── Quick actions ────────────────────────────────────────

        private async void SyncButton_Click(object sender, RoutedEventArgs e)
        {
            SyncButton.IsEnabled = false;
            SyncInfoBar.Message = "正在拉取云端仓库最新改动...";
            SyncInfoBar.Severity = Wpf.Ui.Controls.InfoBarSeverity.Informational;
            SyncInfoBar.IsOpen = true;

            try
            {
                await App.GitContext.PullAsync();
                SyncInfoBar.Message = "✅ 同步完成！本地已是最新版本。";
                SyncInfoBar.Severity = Wpf.Ui.Controls.InfoBarSeverity.Success;

                // Refresh
                DashboardPage_Loaded(this, new RoutedEventArgs());
            }
            catch (System.Exception ex)
            {
                SyncInfoBar.Message = $"同步失败: {ex.Message}";
                SyncInfoBar.Severity = Wpf.Ui.Controls.InfoBarSeverity.Error;
            }
            finally
            {
                SyncButton.IsEnabled = true;
            }
        }

        private void WriteNewPost_Click(object sender, RoutedEventArgs e)
        {
            App.CurrentEditPost = null;
            var w = Application.Current.MainWindow as MainWindow;
            w?.RootNavigation.Navigate(typeof(EditorPage));
        }

        private async void ViewLiveSite_Click(object sender, RoutedEventArgs e)
        {
            var config = App.JekyllContext.LoadConfig();
            if (config.TryGetValue("url", out var urlObj) && urlObj is string url && !string.IsNullOrWhiteSpace(url))
            {
                if (!url.StartsWith("http")) url = "https://" + url;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            else
            {
                var msg = new Wpf.Ui.Controls.MessageBox { Title = "Error", Content = "No valid URL found in _config.yml", CloseButtonText = "OK" };
                await msg.ShowDialogAsync();
            }
        }

        // ─── Timeline item click ─────────────────────────────────

        private void TimelineItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TimelinePostItem item)
            {
                App.CurrentEditPost = item.Source;
                var w = Application.Current.MainWindow as MainWindow;
                w?.RootNavigation.Navigate(typeof(EditorPage));
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
