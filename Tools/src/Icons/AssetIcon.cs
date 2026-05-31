using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Wpf.Ui.Controls;
using IconPath = System.Windows.Shapes.Path;

namespace BlogTools.Icons
{
    public sealed class AssetIcon : IconElement
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind),
            typeof(AssetIconKind),
            typeof(AssetIcon),
            new PropertyMetadata(AssetIconKind.None, OnKindChanged));

        private IconPath? _path;

        public AssetIcon()
        {
            Width = 20;
            Height = 20;
        }

        public AssetIconKind Kind
        {
            get => (AssetIconKind)GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }

        protected override UIElement InitializeChildren()
        {
            _path = new IconPath
            {
                Data = AssetIconCatalog.GetGeometry(Kind),
                Stretch = Stretch.Uniform
            };

            BindingOperations.SetBinding(
                _path,
                Shape.FillProperty,
                new Binding(nameof(Foreground)) { Source = this });

            return new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = _path
            };
        }

        private static void OnKindChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _)
        {
            if (dependencyObject is AssetIcon { _path: not null } icon)
            {
                icon._path.Data = AssetIconCatalog.GetGeometry(icon.Kind);
            }
        }
    }
}
