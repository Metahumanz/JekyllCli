using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace BlogTools.Helpers
{
    public static class HoverLiftHelper
    {
        private static readonly DependencyProperty IsAttachedProperty =
            DependencyProperty.RegisterAttached("IsAttached", typeof(bool), typeof(HoverLiftHelper), new PropertyMetadata(false));

        private static readonly DependencyProperty TranslateTransformProperty =
            DependencyProperty.RegisterAttached("TranslateTransform", typeof(TranslateTransform), typeof(HoverLiftHelper), new PropertyMetadata(null));

        private static readonly DependencyProperty ShadowEffectProperty =
            DependencyProperty.RegisterAttached("ShadowEffect", typeof(DropShadowEffect), typeof(HoverLiftHelper), new PropertyMetadata(null));

        private const double HoverOffsetY = -2.0;
        private const double HoverShadowOpacity = 0.12;
        private static readonly Duration AnimationDuration = new(TimeSpan.FromMilliseconds(220));
        private static readonly IEasingFunction HoverEase = new SineEase { EasingMode = EasingMode.EaseOut };
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            EventManager.RegisterClassHandler(typeof(Control), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnElementLoaded), true);
        }

        private static void OnElementLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || GetIsAttached(element) || !ShouldApply(element))
            {
                return;
            }

            SetIsAttached(element, true);
            EnsureTranslateTransform(element);
            EnsureShadowEffect(element);

            element.MouseEnter += Element_MouseEnter;
            element.MouseLeave += Element_MouseLeave;
            element.IsEnabledChanged += Element_IsEnabledChanged;
            element.Unloaded += Element_Unloaded;
        }

        private static void Element_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is FrameworkElement element && element.IsEnabled)
            {
                AnimateElement(element, HoverOffsetY, HoverShadowOpacity);
            }
        }

        private static void Element_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                AnimateElement(element, 0.0, 0.0);
            }
        }

        private static void Element_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is FrameworkElement element && e.NewValue is bool isEnabled && !isEnabled)
            {
                AnimateElement(element, 0.0, 0.0);
            }
        }

        private static void Element_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            element.MouseEnter -= Element_MouseEnter;
            element.MouseLeave -= Element_MouseLeave;
            element.IsEnabledChanged -= Element_IsEnabledChanged;
            element.Unloaded -= Element_Unloaded;
            SetIsAttached(element, false);
        }

        private static void AnimateElement(FrameworkElement element, double targetY, double targetShadowOpacity)
        {
            if (GetTranslateTransform(element) is TranslateTransform translate)
            {
                translate.BeginAnimation(TranslateTransform.YProperty, CreateAnimation(targetY));
            }

            if (GetShadowEffect(element) is DropShadowEffect shadow)
            {
                shadow.BeginAnimation(DropShadowEffect.OpacityProperty, CreateAnimation(targetShadowOpacity));
            }
        }

        private static DoubleAnimation CreateAnimation(double to)
        {
            return new DoubleAnimation
            {
                To = to,
                Duration = AnimationDuration,
                EasingFunction = HoverEase
            };
        }

        private static bool ShouldApply(FrameworkElement element)
        {
            if (!element.IsVisible ||
                element.Name.StartsWith("PART_", StringComparison.Ordinal) ||
                element is ScrollBar ||
                element is Slider ||
                element is ProgressBar ||
                element is Separator ||
                element is ScrollViewer ||
                element is Border ||
                element is ComboBoxItem ||
                element is ListBoxItem ||
                element is ListViewItem ||
                element is TreeViewItem ||
                element is DataGridCell ||
                element is DataGridRow ||
                element is DataGridRowHeader ||
                element is DataGridColumnHeader ||
                element is RepeatButton ||
                element is Thumb ||
                element is Expander ||
                element is Wpf.Ui.Controls.CardControl ||
                element is Wpf.Ui.Controls.CardExpander ||
                element is Wpf.Ui.Controls.NavigationViewItem ||
                element is Wpf.Ui.Controls.NavigationView ||
                element is Wpf.Ui.Controls.TitleBar)
            {
                return false;
            }

            return element is ButtonBase ||
                   element is ComboBox ||
                   element is DatePicker ||
                   element is TextBox ||
                   element is PasswordBox ||
                   element is Wpf.Ui.Controls.CardAction;
        }

        private static void EnsureTranslateTransform(FrameworkElement element)
        {
            if (GetTranslateTransform(element) != null)
            {
                return;
            }

            TranslateTransform translate;

            if (element.RenderTransform is TransformGroup existingGroup)
            {
                translate = GetOrAddTranslateTransform(existingGroup);
            }
            else if (element.RenderTransform is TranslateTransform existingTranslate)
            {
                translate = existingTranslate;
            }
            else if (element.RenderTransform == null || element.RenderTransform == Transform.Identity)
            {
                translate = new TranslateTransform();
                element.RenderTransform = translate;
            }
            else
            {
                translate = new TranslateTransform();
                var group = new TransformGroup();
                group.Children.Add(element.RenderTransform);
                group.Children.Add(translate);
                element.RenderTransform = group;
            }

            SetTranslateTransform(element, translate);
        }

        private static TranslateTransform GetOrAddTranslateTransform(TransformGroup group)
        {
            foreach (var child in group.Children)
            {
                if (child is TranslateTransform translate)
                {
                    return translate;
                }
            }

            var created = new TranslateTransform();
            group.Children.Add(created);
            return created;
        }

        private static void EnsureShadowEffect(FrameworkElement element)
        {
            if (element.Effect is DropShadowEffect existingShadow)
            {
                SetShadowEffect(element, existingShadow);
                return;
            }

            if (element.Effect != null)
            {
                return;
            }

            var shadow = new DropShadowEffect
            {
                BlurRadius = 18,
                Direction = 270,
                ShadowDepth = 5,
                Color = Colors.Black,
                Opacity = 0
            };

            element.Effect = shadow;
            SetShadowEffect(element, shadow);
        }

        private static bool GetIsAttached(DependencyObject obj) => (bool)obj.GetValue(IsAttachedProperty);

        private static void SetIsAttached(DependencyObject obj, bool value) => obj.SetValue(IsAttachedProperty, value);

        private static TranslateTransform? GetTranslateTransform(DependencyObject obj) =>
            obj.GetValue(TranslateTransformProperty) as TranslateTransform;

        private static void SetTranslateTransform(DependencyObject obj, TranslateTransform value) =>
            obj.SetValue(TranslateTransformProperty, value);

        private static DropShadowEffect? GetShadowEffect(DependencyObject obj) =>
            obj.GetValue(ShadowEffectProperty) as DropShadowEffect;

        private static void SetShadowEffect(DependencyObject obj, DropShadowEffect value) =>
            obj.SetValue(ShadowEffectProperty, value);
    }
}
