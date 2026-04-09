using Wpf.Ui.Controls;

namespace BlogTools
{
    public enum EditorToolHost
    {
        Ribbon,
        Side
    }

    public sealed class EditorToolDefinition
    {
        public required string Id { get; init; }
        public required string Command { get; init; }
        public required SymbolRegular Symbol { get; init; }
        public required string ToolTipResourceKey { get; init; }
        public double SymbolFontSize { get; init; } = 18;
        public bool IsSideOnly { get; init; }
    }

    public sealed class EditorToolViewItem
    {
        public EditorToolViewItem(EditorToolDefinition definition, EditorToolHost host, string toolTipText)
        {
            Definition = definition;
            Host = host;
            ToolTipText = toolTipText;
        }

        public EditorToolDefinition Definition { get; }
        public EditorToolHost Host { get; }
        public string ToolTipText { get; }
        public string Id => Definition.Id;
        public string Command => Definition.Command;
        public SymbolRegular Symbol => Definition.Symbol;
        public double SymbolFontSize => Definition.SymbolFontSize;
        public bool IsSideOnly => Definition.IsSideOnly;
    }
}
