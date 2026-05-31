using System.Collections.Generic;
using System.Windows.Media;

namespace BlogTools.Icons
{
    internal static class AssetIconCatalog
    {
        private static readonly Dictionary<AssetIconKind, Geometry> GeometryCache = [];

        public static Geometry GetGeometry(AssetIconKind kind)
        {
            if (kind == AssetIconKind.None)
            {
                return Geometry.Empty;
            }

            lock (GeometryCache)
            {
                if (GeometryCache.TryGetValue(kind, out var geometry))
                {
                    return geometry;
                }

                geometry = Geometry.Parse(AssetIconPathData.Paths[kind]);
                geometry.Freeze();
                GeometryCache[kind] = geometry;
                return geometry;
            }
        }
    }
}
