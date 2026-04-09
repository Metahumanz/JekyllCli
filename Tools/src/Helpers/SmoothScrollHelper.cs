using System;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BlogTools.Helpers
{
    /// <summary>
    /// 为 ScrollViewer 提供基于物理物理弹簧算法（指数衰减）的平滑滚动支持。
    /// </summary>
    public static class SmoothScrollHelper
    {
        public static readonly DependencyProperty SmoothingEnabledProperty =
            DependencyProperty.RegisterAttached("SmoothingEnabled", typeof(bool), typeof(SmoothScrollHelper), new PropertyMetadata(false, OnSmoothingEnabledChanged));

        public static bool GetSmoothingEnabled(DependencyObject obj) => (bool)obj.GetValue(SmoothingEnabledProperty);
        public static void SetSmoothingEnabled(DependencyObject obj, bool value) => obj.SetValue(SmoothingEnabledProperty, value);

        public static readonly DependencyProperty SmoothingFactorProperty =
            DependencyProperty.RegisterAttached("SmoothingFactor", typeof(double), typeof(SmoothScrollHelper), new PropertyMetadata(0.18));

        public static double GetSmoothingFactor(DependencyObject obj) => (double)obj.GetValue(SmoothingFactorProperty);
        public static void SetSmoothingFactor(DependencyObject obj, double value) => obj.SetValue(SmoothingFactorProperty, value);

        private static readonly DependencyProperty TargetOffsetProperty =
            DependencyProperty.RegisterAttached("TargetOffset", typeof(double), typeof(SmoothScrollHelper), new PropertyMetadata(-1.0));

        private static readonly DependencyProperty CurrentOffsetProperty =
            DependencyProperty.RegisterAttached("CurrentOffset", typeof(double), typeof(SmoothScrollHelper), new PropertyMetadata(-1.0));

        private static readonly DependencyProperty InnerScrollViewerProperty =
            DependencyProperty.RegisterAttached("InnerScrollViewer", typeof(ScrollViewer), typeof(SmoothScrollHelper), new PropertyMetadata(null));

        private static readonly ConcurrentDictionary<ScrollViewer, bool> _activeScrollViewers = new();

        private static void OnSmoothingEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
            {
                if ((bool)e.NewValue)
                {
                    sv.PreviewMouseWheel += ScrollViewer_PreviewMouseWheel;
                }
                else
                {
                    sv.PreviewMouseWheel -= ScrollViewer_PreviewMouseWheel;
                    _activeScrollViewers.TryRemove(sv, out _);
                }
            }
            else if (d is DataGrid dg)
            {
                // Для DataGrid, мы пытаемся найти внутренний ScrollViewer при загрузке
                if ((bool)e.NewValue)
                {
                    dg.Loaded += DataGrid_Loaded;
                    dg.Unloaded += DataGrid_Unloaded;
                    dg.AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(DataGrid_PreviewMouseWheel), true);
                }
                else
                {
                    dg.Loaded -= DataGrid_Loaded;
                    dg.Unloaded -= DataGrid_Unloaded;
                    dg.RemoveHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(DataGrid_PreviewMouseWheel));
                    dg.ClearValue(InnerScrollViewerProperty);
                }
            }
            else if (d is ListView lv)
            {
                if ((bool)e.NewValue)
                {
                    lv.Loaded += ListView_Loaded;
                    lv.Unloaded += ListView_Unloaded;
                    lv.AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(ListView_PreviewMouseWheel), true);
                }
                else
                {
                    lv.Loaded -= ListView_Loaded;
                    lv.Unloaded -= ListView_Unloaded;
                    lv.RemoveHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(ListView_PreviewMouseWheel));
                    lv.ClearValue(InnerScrollViewerProperty);
                }
            }
            else if (d is ComboBox cb)
            {
                if ((bool)e.NewValue)
                {
                    cb.DropDownOpened += (s, ev) => 
                    {
                        var popup = FindVisualChild<System.Windows.Controls.Primitives.Popup>(cb);
                        if (popup?.Child is FrameworkElement popupChild)
                        {
                            var innerSv = FindVisualChild<ScrollViewer>(popupChild);
                            if (innerSv != null) SetSmoothingEnabled(innerSv, true);
                        }
                    };
                }
            }
        }

        private static void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                HandleScroll(sv, e);
            }
        }

        private static void HandleScroll(ScrollViewer sv, MouseWheelEventArgs e)
        {
            if (ScrollViewerHelper.SuppressScrollBubble) return;
            if (sv.ScrollableHeight <= 0) return;

            e.Handled = true;

            double target = (double)sv.GetValue(TargetOffsetProperty);
            if (target < 0)
            {
                target = sv.VerticalOffset;
                sv.SetValue(CurrentOffsetProperty, sv.VerticalOffset);
            }

            // 放大滚动增量，模拟更快速的手感
            target -= e.Delta * 2.0; 
            target = Math.Max(0, Math.Min(sv.ScrollableHeight, target));
            sv.SetValue(TargetOffsetProperty, target);

            if (!_activeScrollViewers.ContainsKey(sv))
            {
                _activeScrollViewers[sv] = true;
                if (_activeScrollViewers.Count == 1)
                {
                    CompositionTarget.Rendering += OnRendering;
                }
            }
        }

        private static void OnRendering(object? sender, EventArgs e)
        {
            var keys = _activeScrollViewers.Keys;
            foreach (var sv in keys)
            {
                double target = (double)sv.GetValue(TargetOffsetProperty);
                double current = (double)sv.GetValue(CurrentOffsetProperty);
                double factor = GetSmoothingFactor(sv);

                double diff = target - current;
                if (Math.Abs(diff) < 0.5)
                {
                    sv.ScrollToVerticalOffset(target);
                    sv.SetValue(TargetOffsetProperty, -1.0);
                    sv.SetValue(CurrentOffsetProperty, -1.0);
                    _activeScrollViewers.TryRemove(sv, out _);
                }
                else
                {
                    current += diff * factor;
                    sv.ScrollToVerticalOffset(current);
                    sv.SetValue(CurrentOffsetProperty, current);
                }
            }

            if (_activeScrollViewers.IsEmpty)
            {
                CompositionTarget.Rendering -= OnRendering;
            }
        }

        private static void DataGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is DataGrid dg)
            {
                CacheInnerScrollViewer(dg);
            }
        }

        private static void DataGrid_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is DataGrid dg)
            {
                dg.ClearValue(InnerScrollViewerProperty);
            }
        }

        private static void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not DataGrid dg) return;

            var innerSv = (ScrollViewer?)dg.GetValue(InnerScrollViewerProperty);
            if (innerSv == null || !innerSv.IsLoaded)
            {
                innerSv = CacheInnerScrollViewer(dg);
            }

            if (innerSv != null)
            {
                HandleScroll(innerSv, e);
            }
        }

        private static ScrollViewer? CacheInnerScrollViewer(DataGrid dg)
        {
            dg.ApplyTemplate();
            dg.UpdateLayout();

            var innerSv = dg.Template?.FindName("DG_ScrollViewer", dg) as ScrollViewer;
            innerSv ??= FindBestScrollViewer(dg, "DG_ScrollViewer");

            if (innerSv != null)
            {
                dg.SetValue(InnerScrollViewerProperty, innerSv);
            }

            return innerSv;
        }

        private static void ListView_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ListView lv)
            {
                CacheInnerScrollViewer(lv);
            }
        }

        private static void ListView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is ListView lv)
            {
                lv.ClearValue(InnerScrollViewerProperty);
            }
        }

        private static void ListView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ListView lv) return;

            var innerSv = (ScrollViewer?)lv.GetValue(InnerScrollViewerProperty);
            if (innerSv == null || !innerSv.IsLoaded)
            {
                innerSv = CacheInnerScrollViewer(lv);
            }

            if (innerSv != null)
            {
                HandleScroll(innerSv, e);
            }
        }

        private static ScrollViewer? CacheInnerScrollViewer(ListView lv)
        {
            var innerSv = FindVisualChild<ScrollViewer>(lv);
            if (innerSv != null)
            {
                lv.SetValue(InnerScrollViewerProperty, innerSv);
            }

            return innerSv;
        }

        private static ScrollViewer? FindBestScrollViewer(DependencyObject root, string preferredName)
        {
            ScrollViewer? fallback = null;

            foreach (var scrollViewer in FindVisualChildren<ScrollViewer>(root))
            {
                if (string.Equals(scrollViewer.Name, preferredName, StringComparison.Ordinal))
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

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result) return result;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;

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
    }
}
