using System;
using System.Windows.Markup;

namespace BlogTools.Icons
{
    [MarkupExtensionReturnType(typeof(AssetIcon))]
    public sealed class AssetIconExtension : MarkupExtension
    {
        public AssetIconExtension(AssetIconKind kind)
        {
            Kind = kind;
        }

        [ConstructorArgument("kind")]
        public AssetIconKind Kind { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return new AssetIcon { Kind = Kind };
        }
    }
}
