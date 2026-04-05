using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BlogTools.Helpers
{
    public static class ScrollViewerHelper
    {
        public static readonly DependencyProperty BubbleScrollEventsProperty =
            DependencyProperty.RegisterAttached(
                "BubbleScrollEvents",
                typeof(bool),
                typeof(ScrollViewerHelper),
                new PropertyMetadata(false, OnBubbleScrollEventsChanged));

        public static bool GetBubbleScrollEvents(DependencyObject obj) =>
            (bool)obj.GetValue(BubbleScrollEventsProperty);

        public static void SetBubbleScrollEvents(DependencyObject obj, bool value) =>
            obj.SetValue(BubbleScrollEventsProperty, value);

        private static void OnBubbleScrollEventsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
            {
                if ((bool)e.NewValue)
                    sv.PreviewMouseWheel += HandleMouseWheel;
                else
                    sv.PreviewMouseWheel -= HandleMouseWheel;
            }
        }

        private static void HandleMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = sender as ScrollViewer;
            if (sv == null) return;

            e.Handled = true;

            var parent = sv.Parent as UIElement;
            while (parent != null)
            {
                if (parent is ScrollViewer parentSv)
                {
                    var ev = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                    {
                        RoutedEvent = UIElement.MouseWheelEvent,
                        Source = sv
                    };
                    parentSv.RaiseEvent(ev);
                    return;
                }
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent) as UIElement;
            }
        }
    }
}
