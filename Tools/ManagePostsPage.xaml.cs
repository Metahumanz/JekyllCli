using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BlogTools.Models;
using Wpf.Ui.Controls;

namespace BlogTools
{
    public partial class ManagePostsPage : Page
    {
        private List<BlogPost> _allPosts = new();

        public ManagePostsPage()
        {
            InitializeComponent();
            Loaded += ManagePostsPage_Loaded;
            Unloaded += ManagePostsPage_Unloaded;
            App.BlogFilesChanged += OnBlogFilesChanged;
        }

        private void ManagePostsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            App.BlogFilesChanged -= OnBlogFilesChanged;
        }

        private void OnBlogFilesChanged()
        {
            Dispatcher.InvokeAsync(() => LoadPosts());
        }

        private void ManagePostsPage_Loaded(object sender, RoutedEventArgs e)
        {
            var parentSv = FindVisualParent<ScrollViewer>(this);
            parentSv?.ScrollToTop();

            LoadPosts();
        }

        private void LoadPosts()
        {
            _allPosts = App.JekyllContext.GetAllPosts();
            FilterList();
        }

        private void FilterList()
        {
            var query = SearchBox.Text.ToLower();
            if (string.IsNullOrWhiteSpace(query))
            {
                PostsGrid.ItemsSource = _allPosts;
            }
            else
            {
                PostsGrid.ItemsSource = _allPosts.Where(p => p.Title.ToLower().Contains(query) || p.FileName.ToLower().Contains(query)).ToList();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterList();
        }

        private void PostsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (PostsGrid.SelectedItem is BlogPost post)
            {
                EditPost(post);
            }
        }

        private void PostsGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Find the DataGrid's built-in ScrollViewer and scroll it manually
            var dataGrid = (System.Windows.Controls.DataGrid)sender;
            var scrollViewer = FindVisualChild<ScrollViewer>(dataGrid);
            if (scrollViewer != null && scrollViewer.ScrollableHeight > 0)
            {
                double lineHeight = 48;
                double offset = scrollViewer.VerticalOffset - (e.Delta / 120.0) * lineHeight;
                if (offset < 0) offset = 0;
                if (offset > scrollViewer.ScrollableHeight) offset = scrollViewer.ScrollableHeight;
                scrollViewer.ScrollToVerticalOffset(offset);
                e.Handled = true;
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result) return result;
                var descendant = FindVisualChild<T>(child);
                if (descendant != null) return descendant;
            }
            return null;
        }

        private void EditPost_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is BlogPost post)
            {
                EditPost(post);
            }
        }

        private void DeletePost_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is BlogPost post)
            {
                var result = System.Windows.MessageBox.Show($"您确定要彻底删除这篇文章 '{post.Title}' 吗？", "删除确认", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    App.JekyllContext.DeletePost(post.FullPath);
                    LoadPosts(); // Refresh list
                }
            }
        }

        private void EditPost(BlogPost post)
        {
            App.CurrentEditPost = post;
            var w = Application.Current.MainWindow as MainWindow;
            w?.RootNavigation.Navigate(typeof(EditorPage));
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
