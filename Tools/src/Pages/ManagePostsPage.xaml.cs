using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BlogTools.Helpers;
using BlogTools.Models;

namespace BlogTools
{
    public partial class ManagePostsPage : Page
    {
        private List<BlogPost> _allPosts = new();
        private string _sortProperty = nameof(BlogPost.Date);
        private ListSortDirection _sortDirection = ListSortDirection.Descending;
        private Dictionary<string, string>? _headerLabels;
        private readonly Dictionary<DataGridColumn, string> _columnSortMap = new();
        private ScrollViewer? _activeGridScrollViewer;
        private double _targetGridScrollOffset = -1;
        private double _currentGridScrollOffset = -1;

        public ManagePostsPage()
        {
            InitializeComponent();
            Loaded += ManagePostsPage_Loaded;
            Unloaded += ManagePostsPage_Unloaded;
            App.BlogFilesChanged += OnBlogFilesChanged;
            CompositionTarget.Rendering += OnCompositionTargetRendering;
            AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(ManagePostsPage_PreviewMouseWheel), true);
            AddHandler(UIElement.ManipulationStartingEvent, new EventHandler<ManipulationStartingEventArgs>(ManagePostsPage_ManipulationStarting), true);
            AddHandler(UIElement.ManipulationDeltaEvent, new EventHandler<ManipulationDeltaEventArgs>(ManagePostsPage_ManipulationDelta), true);
            AddHandler(UIElement.ManipulationInertiaStartingEvent, new EventHandler<ManipulationInertiaStartingEventArgs>(ManagePostsPage_ManipulationInertiaStarting), true);
            _columnSortMap[DateColumn] = nameof(BlogPost.Date);
            _columnSortMap[ModifiedDateColumn] = nameof(BlogPost.LastModifiedAt);
            _columnSortMap[TitleColumn] = nameof(BlogPost.Title);
            _columnSortMap[FileNameColumn] = nameof(BlogPost.FileName);
        }

        private void ManagePostsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            App.BlogFilesChanged -= OnBlogFilesChanged;
            CompositionTarget.Rendering -= OnCompositionTargetRendering;
            RemoveHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(ManagePostsPage_PreviewMouseWheel));
            RemoveHandler(UIElement.ManipulationStartingEvent, new EventHandler<ManipulationStartingEventArgs>(ManagePostsPage_ManipulationStarting));
            RemoveHandler(UIElement.ManipulationDeltaEvent, new EventHandler<ManipulationDeltaEventArgs>(ManagePostsPage_ManipulationDelta));
            RemoveHandler(UIElement.ManipulationInertiaStartingEvent, new EventHandler<ManipulationInertiaStartingEventArgs>(ManagePostsPage_ManipulationInertiaStarting));
            _activeGridScrollViewer = null;
            ResetGridScrollAnimation();
        }

        private void OnBlogFilesChanged()
        {
            Dispatcher.InvokeAsync(() =>
            {
                LoadPosts();
                UpdateSortHeaders();
            });
        }

        private void ManagePostsPage_Loaded(object sender, RoutedEventArgs e)
        {
            var parentSv = FindVisualParent<ScrollViewer>(this);
            parentSv?.ScrollToTop();

            EnsureHeaderLabels();
            LoadPosts();
            UpdateSortHeaders();
            _activeGridScrollViewer = EnsureGridScrollViewer();
            ScrollGridToTop();
        }

        private void LoadPosts()
        {
            _allPosts = App.JekyllContext.GetAllPosts();
            ApplyFilterAndSort();
        }

        private void ApplyFilterAndSort()
        {
            var selectedFullPath = (PostsDataGrid.SelectedItem as BlogPost)?.FullPath;
            var searchText = SearchBox.Text?.Trim();
            IEnumerable<BlogPost> posts = _allPosts;

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                posts = posts.Where(post =>
                    post.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    post.FileName.Contains(searchText, StringComparison.OrdinalIgnoreCase));
            }

            posts = (_sortProperty, _sortDirection) switch
            {
                (nameof(BlogPost.Title), ListSortDirection.Ascending) => posts.OrderBy(post => post.Title, StringComparer.CurrentCultureIgnoreCase),
                (nameof(BlogPost.Title), ListSortDirection.Descending) => posts.OrderByDescending(post => post.Title, StringComparer.CurrentCultureIgnoreCase),
                (nameof(BlogPost.FileName), ListSortDirection.Ascending) => posts.OrderBy(post => post.FileName, StringComparer.CurrentCultureIgnoreCase),
                (nameof(BlogPost.FileName), ListSortDirection.Descending) => posts.OrderByDescending(post => post.FileName, StringComparer.CurrentCultureIgnoreCase),
                (nameof(BlogPost.LastModifiedAt), ListSortDirection.Ascending) => posts
                    .OrderBy(post => post.LastModifiedAt.HasValue ? 0 : 1)
                    .ThenBy(post => post.LastModifiedAt),
                (nameof(BlogPost.LastModifiedAt), ListSortDirection.Descending) => posts
                    .OrderBy(post => post.LastModifiedAt.HasValue ? 0 : 1)
                    .ThenByDescending(post => post.LastModifiedAt),
                (nameof(BlogPost.Date), ListSortDirection.Ascending) => posts.OrderBy(post => post.Date),
                _ => posts.OrderByDescending(post => post.Date)
            };

            var visiblePosts = posts.ToList();
            PostsDataGrid.ItemsSource = visiblePosts;

            if (!string.IsNullOrWhiteSpace(selectedFullPath))
            {
                PostsDataGrid.SelectedItem = visiblePosts.FirstOrDefault(post => post.FullPath == selectedFullPath);
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilterAndSort();
            UpdateSortHeaders();
            ScrollGridToTop();
        }

        private void PostsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                if (FindVisualParent<ButtonBase>(source) != null ||
                    FindVisualParent<DataGridColumnHeader>(source) != null ||
                    FindVisualParent<ScrollBar>(source) != null ||
                    FindVisualParent<Thumb>(source) != null)
                {
                    return;
                }
            }

            if (PostsDataGrid.SelectedItem is BlogPost post)
            {
                EditPost(post);
            }
        }

        private void PostsDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;

            if (!_columnSortMap.TryGetValue(e.Column, out var sortProperty))
            {
                e.Column.SortDirection = null;
                return;
            }

            if (_sortProperty == sortProperty)
            {
                _sortDirection = _sortDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            }
            else
            {
                _sortProperty = sortProperty;
                _sortDirection = sortProperty == nameof(BlogPost.Date) || sortProperty == nameof(BlogPost.LastModifiedAt)
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            }

            ApplyFilterAndSort();
            UpdateSortHeaders();
            ScrollGridToTop();
        }

        private void ManagePostsPage_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (ScrollViewerHelper.SuppressScrollBubble)
            {
                return;
            }

            if (e.OriginalSource is not DependencyObject source)
            {
                return;
            }

            if (!IsWithinPostsDataGrid(source))
            {
                return;
            }

            if (FindVisualParent<ScrollBar>(source) != null)
            {
                return;
            }

            var gridScrollViewer = EnsureGridScrollViewer();
            if (gridScrollViewer == null)
            {
                TryForwardWheelToParentScrollViewer(e);
                return;
            }

            bool canScrollUp = gridScrollViewer.VerticalOffset > 0;
            bool canScrollDown = gridScrollViewer.VerticalOffset < gridScrollViewer.ScrollableHeight;
            bool shouldScrollGrid = gridScrollViewer.ScrollableHeight > 0 &&
                ((e.Delta > 0 && canScrollUp) || (e.Delta < 0 && canScrollDown));

            if (!shouldScrollGrid)
            {
                TryForwardWheelToParentScrollViewer(e);
                return;
            }

            e.Handled = true;
            _activeGridScrollViewer = gridScrollViewer;

            if (_targetGridScrollOffset < 0 || _currentGridScrollOffset < 0)
            {
                _targetGridScrollOffset = gridScrollViewer.VerticalOffset;
                _currentGridScrollOffset = gridScrollViewer.VerticalOffset;
            }

            _targetGridScrollOffset -= e.Delta * 2.0;
            _targetGridScrollOffset = Math.Max(0, Math.Min(gridScrollViewer.ScrollableHeight, _targetGridScrollOffset));
        }

        private void OnCompositionTargetRendering(object? sender, EventArgs e)
        {
            if (_activeGridScrollViewer == null || _targetGridScrollOffset < 0 || _currentGridScrollOffset < 0)
            {
                return;
            }

            double diff = _targetGridScrollOffset - _currentGridScrollOffset;
            if (Math.Abs(diff) < 0.5)
            {
                _currentGridScrollOffset = _targetGridScrollOffset;
                _activeGridScrollViewer.ScrollToVerticalOffset(_currentGridScrollOffset);
                ResetGridScrollAnimation();
            }
            else
            {
                _currentGridScrollOffset += diff * 0.18;
                _activeGridScrollViewer.ScrollToVerticalOffset(_currentGridScrollOffset);
            }
        }

        private void ManagePostsPage_ManipulationStarting(object? sender, ManipulationStartingEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source ||
                !IsWithinPostsDataGrid(source) ||
                FindVisualParent<ScrollBar>(source) != null)
            {
                return;
            }

            ResetGridScrollAnimation();
            _activeGridScrollViewer = EnsureGridScrollViewer();
            e.ManipulationContainer = this;
            e.Mode = ManipulationModes.TranslateY;
        }

        private void ManagePostsPage_ManipulationDelta(object? sender, ManipulationDeltaEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source ||
                !IsWithinPostsDataGrid(source) ||
                FindVisualParent<ScrollBar>(source) != null)
            {
                return;
            }

            ResetGridScrollAnimation();

            double deltaY = e.DeltaManipulation.Translation.Y;
            bool scrolledGrid = false;

            var gridScrollViewer = EnsureGridScrollViewer();
            if (gridScrollViewer != null)
            {
                _activeGridScrollViewer = gridScrollViewer;
                scrolledGrid = TryScrollViewerByVerticalDelta(gridScrollViewer, deltaY);
            }

            bool scrolledParent = !scrolledGrid && TryScrollParentByVerticalDelta(deltaY);
            if (!scrolledGrid && !scrolledParent && e.IsInertial)
            {
                e.Complete();
            }

            if (scrolledGrid || scrolledParent)
            {
                e.Handled = true;
            }
        }

        private void ManagePostsPage_ManipulationInertiaStarting(object? sender, ManipulationInertiaStartingEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source ||
                !IsWithinPostsDataGrid(source) ||
                FindVisualParent<ScrollBar>(source) != null)
            {
                return;
            }

            // Match a phone-like deceleration so a quick swipe keeps gliding briefly.
            e.TranslationBehavior.DesiredDeceleration = 10.0 * 96.0 / (1000.0 * 1000.0);
            e.Handled = true;
        }

        private void EditPost_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is BlogPost post)
            {
                EditPost(post);
            }
        }

        private async void DeletePost_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is BlogPost post)
            {
                var msg = new Wpf.Ui.Controls.MessageBox
                {
                    Title = Application.Current.FindResource("ManagePostsMsgDeleteTitle").ToString()!,
                    Content = string.Format(Application.Current.FindResource("ManagePostsMsgDeleteConfirm").ToString()!, post.Title),
                    PrimaryButtonText = Application.Current.FindResource("CommonConfirm").ToString()!,
                    CloseButtonText = Application.Current.FindResource("CommonCancel").ToString()!
                };
                var result = await msg.ShowDialogAsync();
                if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    App.JekyllContext.DeletePost(post.FullPath);
                    LoadPosts();
                    UpdateSortHeaders();
                    ScrollGridToTop();
                }
            }
        }

        private void EditPost(BlogPost post)
        {
            App.CurrentEditPost = post;
            var w = Application.Current.MainWindow as MainWindow;
            w?.RootNavigation.Navigate(typeof(EditorPage));
        }

        private void ScrollGridToTop()
        {
            ResetGridScrollAnimation();
            EnsureGridScrollViewer()?.ScrollToTop();
        }

        private ScrollViewer? EnsureGridScrollViewer()
        {
            if (_activeGridScrollViewer != null && _activeGridScrollViewer.IsLoaded)
            {
                return _activeGridScrollViewer;
            }

            PostsDataGrid.ApplyTemplate();
            PostsDataGrid.UpdateLayout();
            if (PostsDataGrid.Template?.FindName("DG_ScrollViewer", PostsDataGrid) is ScrollViewer templateScrollViewer)
            {
                _activeGridScrollViewer = templateScrollViewer;
                return _activeGridScrollViewer;
            }

            _activeGridScrollViewer = FindBestGridScrollViewer();
            return _activeGridScrollViewer;
        }

        private void ResetGridScrollAnimation()
        {
            _targetGridScrollOffset = -1;
            _currentGridScrollOffset = -1;
        }

        private ScrollViewer? FindBestGridScrollViewer()
        {
            ScrollViewer? fallback = null;

            foreach (var scrollViewer in FindVisualChildren<ScrollViewer>(PostsDataGrid))
            {
                if (string.Equals(scrollViewer.Name, "DG_ScrollViewer", StringComparison.Ordinal))
                {
                    return scrollViewer;
                }

                if (ReferenceEquals(scrollViewer.TemplatedParent, PostsDataGrid))
                {
                    return scrollViewer;
                }

                if (fallback == null ||
                    scrollViewer.ScrollableHeight > fallback.ScrollableHeight ||
                    scrollViewer.ViewportHeight > fallback.ViewportHeight)
                {
                    fallback = scrollViewer;
                }
            }

            return fallback;
        }

        private bool IsWithinPostsDataGrid(DependencyObject source)
        {
            return ReferenceEquals(source, PostsDataGrid) ||
                   ReferenceEquals(FindVisualParent<DataGrid>(source), PostsDataGrid);
        }

        private void TryForwardWheelToParentScrollViewer(MouseWheelEventArgs e)
        {
            var parentScrollViewer = FindVisualParent<ScrollViewer>(this);
            if (parentScrollViewer == null || ReferenceEquals(parentScrollViewer, _activeGridScrollViewer))
            {
                return;
            }

            e.Handled = true;
            var forwardedEvent = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = PostsDataGrid
            };
            parentScrollViewer.RaiseEvent(forwardedEvent);
        }

        private static bool TryScrollViewerByVerticalDelta(ScrollViewer scrollViewer, double deltaY)
        {
            if (scrollViewer.ScrollableHeight <= 0)
            {
                return false;
            }

            double nextOffset = scrollViewer.VerticalOffset - deltaY;
            nextOffset = Math.Max(0, Math.Min(scrollViewer.ScrollableHeight, nextOffset));

            if (Math.Abs(nextOffset - scrollViewer.VerticalOffset) < 0.01)
            {
                return false;
            }

            scrollViewer.ScrollToVerticalOffset(nextOffset);
            return true;
        }

        private bool TryScrollParentByVerticalDelta(double deltaY)
        {
            var parentScrollViewer = FindVisualParent<ScrollViewer>(this);
            if (parentScrollViewer == null || ReferenceEquals(parentScrollViewer, _activeGridScrollViewer))
            {
                return false;
            }

            return TryScrollViewerByVerticalDelta(parentScrollViewer, deltaY);
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject? current = child;
            while (current != null)
            {
                current = VisualTreeHelper.GetParent(current);
                if (current is T matched)
                {
                    return matched;
                }
            }

            return null;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result) return result;

                var nested = FindVisualChild<T>(child);
                if (nested != null) return nested;
            }

            return null;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                {
                    yield return result;
                }

                foreach (var nested in FindVisualChildren<T>(child))
                {
                    yield return nested;
                }
            }
        }

        private void EnsureHeaderLabels()
        {
            if (_headerLabels != null)
            {
                return;
            }

            _headerLabels = new Dictionary<string, string>
            {
                [nameof(BlogPost.Date)] = Application.Current.FindResource("ManagePostsColDate").ToString() ?? "Date",
                [nameof(BlogPost.LastModifiedAt)] = Application.Current.FindResource("ManagePostsColModifiedDate").ToString() ?? "Modified",
                [nameof(BlogPost.Title)] = Application.Current.FindResource("ManagePostsColTitle").ToString() ?? "Title",
                [nameof(BlogPost.FileName)] = Application.Current.FindResource("ManagePostsColFileName").ToString() ?? "File Name",
                ["Actions"] = Application.Current.FindResource("ManagePostsColActions").ToString() ?? "Actions"
            };
        }

        private void UpdateSortHeaders()
        {
            EnsureHeaderLabels();
            if (_headerLabels == null)
            {
                return;
            }

            foreach (var column in PostsDataGrid.Columns)
            {
                if (_columnSortMap.TryGetValue(column, out var sortProperty))
                {
                    if (_headerLabels.TryGetValue(sortProperty, out var label))
                    {
                        column.Header = label;
                    }

                    column.SortDirection = sortProperty == _sortProperty
                        ? _sortDirection
                        : null;
                }
            }

            if (_headerLabels.TryGetValue("Actions", out var actionsLabel))
            {
                ActionsColumn.Header = actionsLabel;
                ActionsColumn.SortDirection = null;
            }
        }

    }
}
