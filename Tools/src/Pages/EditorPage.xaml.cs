using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BlogTools.Models;
using BlogTools.Services;
using Markdig;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Microsoft.Win32;

namespace BlogTools
{
    public partial class EditorPage : Page
    {
        private enum EditorViewMode
        {
            WriteOnly,
            Split,
            PreviewOnly
        }

        private enum LayoutDragSource
        {
            None,
            SideRail
        }

        private static readonly string[] DefaultRibbonToolIds =
        {
            "insert-image",
            "insert-link",
            "h1",
            "h2",
            "h3",
            "h4",
            "bold",
            "italic",
            "strike",
            "code-inline",
            "code-block",
            "quote",
            "bullet-list",
            "ordered-list",
            "task-list",
            "table",
            "divider"
        };

        private static readonly string[] DefaultSideToolIds =
        {
            "sync-to-preview",
            "sync-to-editor"
        };

        private static readonly Duration ToolboxAnimationDuration = new(TimeSpan.FromMilliseconds(220));
        private static readonly Duration DropCueAnimationDuration = new(TimeSpan.FromMilliseconds(170));
        private static readonly Duration ViewModeAnimationDuration = new(TimeSpan.FromMilliseconds(240));
        private static readonly IEasingFunction PanelEase = new CubicEase { EasingMode = EasingMode.EaseOut };
        private static readonly IEasingFunction ViewModeEase = new QuinticEase { EasingMode = EasingMode.EaseInOut };
        private const double ActiveDropScale = 1.015;
        private const double RibbonInsertionThickness = 4.0;
        private const double SideInsertionThickness = 4.0;
        private const double MinimumInsertionLength = 30.0;
        private const double SideToolsColumnWidth = 88.0;
        private const double DefaultEditorSplitRatio = 0.5;
        private const double MinimumEditorSplitRatio = 0.1;
        private const double MaximumEditorSplitRatio = 0.9;
        private const double SplitAutoSwitchThreshold = 96.0;
        private const double SplitRailDragActivationThreshold = 12.0;

        private readonly record struct ToolDropPlacement(int Index, Point Position, double Length);
        private readonly record struct EditorViewModeWidths(double Editor, double Side, double Preview);

        private MarkdownPipeline _pipeline;
        private bool _isWebViewReady = false;
        private bool _isToolboxCollapsed = false;
        private string _currentContent = "";
        private string _originalState = "";
        private ScrollViewer? _parentSv;
        private readonly ObservableCollection<string> _tagsList = new();
        private readonly ObservableCollection<EditorToolViewItem> _ribbonTools = new();
        private readonly ObservableCollection<EditorToolViewItem> _sideTools = new();
        private readonly Dictionary<string, EditorToolDefinition> _toolDefinitions = new(StringComparer.Ordinal);
        private Point _toolDragStartPoint;
        private EditorToolViewItem? _draggedTool;
        private bool _isRibbonDropCueActive;
        private bool _isSideDropCueActive;
        private EditorViewMode _editorViewMode = EditorViewMode.Split;
        private double _editorSplitRatio = DefaultEditorSplitRatio;
        private bool _isViewModeAnimating;
        private bool _isLayoutDragActive;
        private bool _isLayoutDragArmed;
        private LayoutDragSource _layoutDragSource;
        private FrameworkElement? _layoutDragHost;
        private Point _layoutDragStartPoint;
        private double _layoutDragStartEditorWidth;
        private double _layoutDragAvailableWidth;

        public EditorPage()
        {
            InitializeComponent();

            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .UseMathematics()
                .UseEmojiAndSmiley()
                .Build();

            Loaded += EditorPage_Loaded;
            Unloaded += EditorPage_Unloaded;
            InitializeTimeComboBoxes();
            TagsItemsControl.ItemsSource = _tagsList;
            InitializeEditorTools();
            LoadEditorViewPreferences();
            UpdateToolboxVisualState(animate: false);
            ApplyEditorViewMode(persist: false);
            InitializeWebViewsAsync();
        }

        private void ToolboxToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isToolboxCollapsed = !_isToolboxCollapsed;
            UpdateToolboxVisualState(animate: true);
            SaveEditorToolLayout();
        }

        private void UpdateToolboxVisualState(bool animate)
        {
            if (ToolboxContentPanel == null ||
                ToolboxToggleGlyph == null ||
                ToolboxToggleText == null ||
                ToolboxToggleButton == null)
            {
                return;
            }

            ToolboxContentPanel.Visibility = _isToolboxCollapsed ? Visibility.Collapsed : Visibility.Visible;
            ToolboxToggleGlyph.Text = _isToolboxCollapsed ? "\u25BC" : "\u25B2";
            ToolboxToggleText.Text = Application.Current.FindResource(
                _isToolboxCollapsed ? "EditorToolboxExpand" : "EditorToolboxCollapse").ToString()!;
            ToolboxToggleButton.SetResourceReference(
                FrameworkElement.ToolTipProperty,
                _isToolboxCollapsed ? "EditorToolboxExpandTip" : "EditorToolboxCollapseTip");

            if (animate && IsLoaded)
            {
                AnimateToolboxVisibility(!_isToolboxCollapsed);
            }
            else
            {
                ToolboxContentPanel.BeginAnimation(FrameworkElement.HeightProperty, null);
                ToolboxContentPanel.BeginAnimation(UIElement.OpacityProperty, null);
                ToolboxContentPanel.Visibility = _isToolboxCollapsed ? Visibility.Collapsed : Visibility.Visible;
                ToolboxContentPanel.Height = double.NaN;
                ToolboxContentPanel.Opacity = _isToolboxCollapsed ? 0.0 : 1.0;
            }
        }

        private void AnimateToolboxVisibility(bool expand)
        {
            if (ToolboxContentPanel == null)
            {
                return;
            }

            ToolboxContentPanel.BeginAnimation(FrameworkElement.HeightProperty, null);
            ToolboxContentPanel.BeginAnimation(UIElement.OpacityProperty, null);

            if (expand)
            {
                double targetHeight = MeasureToolboxContentHeight();
                ToolboxContentPanel.Visibility = Visibility.Visible;
                ToolboxContentPanel.Height = 0;
                ToolboxContentPanel.Opacity = 0;

                var heightAnimation = CreateDoubleAnimation(targetHeight);
                heightAnimation.Completed += (_, _) =>
                {
                    ToolboxContentPanel.Height = double.NaN;
                    ToolboxContentPanel.Opacity = 1.0;
                };

                ToolboxContentPanel.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation);
                ToolboxContentPanel.BeginAnimation(UIElement.OpacityProperty, CreateDoubleAnimation(1.0));
            }
            else
            {
                double startHeight = ToolboxContentPanel.ActualHeight;
                if (startHeight <= 0)
                {
                    startHeight = MeasureToolboxContentHeight();
                }

                ToolboxContentPanel.Visibility = Visibility.Visible;
                ToolboxContentPanel.Height = startHeight;
                ToolboxContentPanel.Opacity = 1.0;

                var heightAnimation = CreateDoubleAnimation(0.0);
                heightAnimation.From = startHeight;
                heightAnimation.Completed += (_, _) =>
                {
                    ToolboxContentPanel.Visibility = Visibility.Collapsed;
                    ToolboxContentPanel.Height = double.NaN;
                    ToolboxContentPanel.Opacity = 0.0;
                };

                var opacityAnimation = CreateDoubleAnimation(0.0);
                opacityAnimation.From = 1.0;

                ToolboxContentPanel.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation);
                ToolboxContentPanel.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            }
        }

        private double MeasureToolboxContentHeight()
        {
            if (RibbonToolsItemsControl == null || ToolboxContentPanel == null)
            {
                return 0;
            }

            double availableWidth = (ToolboxContentPanel.Parent as FrameworkElement)?.ActualWidth ?? 0.0;
            if (availableWidth <= 0)
            {
                availableWidth = RootGrid.ActualWidth;
            }

            availableWidth = Math.Max(200.0, availableWidth - ToolboxContentPanel.Padding.Left - ToolboxContentPanel.Padding.Right);
            RibbonToolsItemsControl.Measure(new Size(availableWidth, double.PositiveInfinity));
            return RibbonToolsItemsControl.DesiredSize.Height + ToolboxContentPanel.Padding.Top + ToolboxContentPanel.Padding.Bottom;
        }

        private static DoubleAnimation CreateDoubleAnimation(double to)
        {
            return new DoubleAnimation
            {
                To = to,
                Duration = ToolboxAnimationDuration,
                EasingFunction = PanelEase
            };
        }

        private void InitializeEditorTools()
        {
            RibbonToolsItemsControl.ItemsSource = _ribbonTools;
            SideToolsItemsControl.ItemsSource = _sideTools;

            if (_toolDefinitions.Count == 0)
            {
                RegisterToolDefinitions();
            }

            LoadEditorToolLayout();
        }

        private void LoadEditorViewPreferences()
        {
            var settings = StorageService.Load();
            _editorViewMode = ParseEditorViewMode(settings.EditorViewMode);
            _editorSplitRatio = ClampEditorSplitRatio(settings.EditorSplitRatio);
        }

        private void SaveEditorViewPreferences()
        {
            var settings = StorageService.Load();
            settings.EditorViewMode = _editorViewMode.ToString();
            settings.EditorSplitRatio = _editorSplitRatio;
            StorageService.Save(settings);
        }

        private static EditorViewMode ParseEditorViewMode(string? value)
        {
            return Enum.TryParse(value, ignoreCase: true, out EditorViewMode mode)
                ? mode
                : EditorViewMode.Split;
        }

        private static double ClampEditorSplitRatio(double ratio)
        {
            if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio <= 0)
            {
                return DefaultEditorSplitRatio;
            }

            return Math.Max(MinimumEditorSplitRatio, Math.Min(MaximumEditorSplitRatio, ratio));
        }

        private void ApplyEditorViewMode(bool persist)
        {
            if (EditorColumn == null ||
                SideToolsColumn == null ||
                PreviewColumn == null ||
                EditorPane == null ||
                SideToolsPanel == null ||
                PreviewPane == null)
            {
                return;
            }

            _editorSplitRatio = ClampEditorSplitRatio(_editorSplitRatio);
            ApplyEditorViewModeLayout();

            UpdateViewModeSelector();

            if (persist)
            {
                SaveEditorViewPreferences();
            }
        }

        private void ApplyEditorViewModeLayout()
        {
            switch (_editorViewMode)
            {
                case EditorViewMode.WriteOnly:
                    EditorPane.Visibility = Visibility.Visible;
                    SideToolsPanel.Visibility = Visibility.Collapsed;
                    PreviewPane.Visibility = Visibility.Collapsed;
                    EditorColumn.Width = new GridLength(1, GridUnitType.Star);
                    SideToolsColumn.Width = new GridLength(0);
                    PreviewColumn.Width = new GridLength(0);
                    break;
                case EditorViewMode.PreviewOnly:
                    EditorPane.Visibility = Visibility.Collapsed;
                    SideToolsPanel.Visibility = Visibility.Collapsed;
                    PreviewPane.Visibility = Visibility.Visible;
                    EditorColumn.Width = new GridLength(0);
                    SideToolsColumn.Width = new GridLength(0);
                    PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
                    break;
                default:
                    EditorPane.Visibility = Visibility.Visible;
                    SideToolsPanel.Visibility = Visibility.Visible;
                    PreviewPane.Visibility = Visibility.Visible;
                    EditorColumn.Width = new GridLength(_editorSplitRatio, GridUnitType.Star);
                    SideToolsColumn.Width = new GridLength(SideToolsColumnWidth);
                    PreviewColumn.Width = new GridLength(1.0 - _editorSplitRatio, GridUnitType.Star);
                    break;
            }
        }

        private void UpdateViewModeSelector()
        {
            if (ViewModeButtonText == null)
            {
                return;
            }

            ViewModeButtonText.Text = GetEditorViewModeDisplayText(_editorViewMode);

            if (WriteOnlyMenuItem != null)
            {
                WriteOnlyMenuItem.IsChecked = _editorViewMode == EditorViewMode.WriteOnly;
            }

            if (SplitMenuItem != null)
            {
                SplitMenuItem.IsChecked = _editorViewMode == EditorViewMode.Split;
            }

            if (PreviewOnlyMenuItem != null)
            {
                PreviewOnlyMenuItem.IsChecked = _editorViewMode == EditorViewMode.PreviewOnly;
            }
        }

        private string GetEditorViewModeDisplayText(EditorViewMode mode)
        {
            string resourceKey = mode switch
            {
                EditorViewMode.WriteOnly => "EditorViewModeWriteOnly",
                EditorViewMode.PreviewOnly => "EditorViewModePreviewOnly",
                _ => "EditorViewModeSplit"
            };

            return Application.Current.FindResource(resourceKey).ToString()!;
        }

        private void SetEditorViewMode(EditorViewMode mode, bool persist = true, bool centerSplit = false)
        {
            double targetSplitRatio = centerSplit && mode == EditorViewMode.Split
                ? DefaultEditorSplitRatio
                : _editorSplitRatio;

            targetSplitRatio = ClampEditorSplitRatio(targetSplitRatio);

            if (mode == _editorViewMode && Math.Abs(targetSplitRatio - _editorSplitRatio) < 0.0001)
            {
                return;
            }

            _editorSplitRatio = targetSplitRatio;
            _editorViewMode = mode;
            ApplyEditorViewMode(persist);
        }

        private void ViewModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isViewModeAnimating || ViewModeContextMenu == null)
            {
                return;
            }

            ViewModeContextMenu.PlacementTarget = ViewModeButton;
            ViewModeContextMenu.IsOpen = true;
        }

        private void ViewModeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_isViewModeAnimating)
            {
                return;
            }

            if (sender is not System.Windows.Controls.MenuItem { Tag: string rawValue })
            {
                return;
            }

            var nextMode = ParseEditorViewMode(rawValue);
            bool centerSplit = nextMode == EditorViewMode.Split && _editorViewMode != EditorViewMode.Split;
            double nextSplitRatio = centerSplit
                ? DefaultEditorSplitRatio
                : _editorSplitRatio;

            if (nextMode == _editorViewMode && Math.Abs(nextSplitRatio - _editorSplitRatio) < 0.0001)
            {
                return;
            }

            AnimateEditorViewModeChange(nextMode, nextSplitRatio, persist: true);
        }

        private void AnimateEditorViewModeChange(EditorViewMode targetMode, double targetSplitRatio, bool persist)
        {
            EndLayoutDrag(commitPreference: false);

            if (EditorWorkspaceGrid == null ||
                EditorWorkspaceGrid.ActualWidth <= 0 ||
                EditorColumn == null ||
                SideToolsColumn == null ||
                PreviewColumn == null ||
                EditorPane == null ||
                SideToolsPanel == null ||
                PreviewPane == null)
            {
                _editorSplitRatio = ClampEditorSplitRatio(targetSplitRatio);
                _editorViewMode = targetMode;
                ApplyEditorViewMode(persist);
                return;
            }

            targetSplitRatio = ClampEditorSplitRatio(targetSplitRatio);

            var fromWidths = GetEditorViewModeWidths(_editorViewMode, _editorSplitRatio);
            var toWidths = GetEditorViewModeWidths(targetMode, targetSplitRatio);

            if (Math.Abs(fromWidths.Editor - toWidths.Editor) < 0.5 &&
                Math.Abs(fromWidths.Side - toWidths.Side) < 0.5 &&
                Math.Abs(fromWidths.Preview - toWidths.Preview) < 0.5)
            {
                _editorSplitRatio = targetSplitRatio;
                _editorViewMode = targetMode;
                ApplyEditorViewMode(persist);
                return;
            }

            _isViewModeAnimating = true;
            _editorSplitRatio = targetSplitRatio;
            _editorViewMode = targetMode;
            UpdateViewModeSelector();
            PrepareAnimatedViewModeChange(fromWidths);

            var editorAnimation = CreateColumnWidthAnimation(fromWidths.Editor, toWidths.Editor);
            var sideAnimation = CreateColumnWidthAnimation(fromWidths.Side, toWidths.Side);
            var previewAnimation = CreateColumnWidthAnimation(fromWidths.Preview, toWidths.Preview);
            previewAnimation.Completed += (_, _) =>
            {
                ClearColumnWidthAnimations();
                ApplyEditorViewModeLayout();
                _isViewModeAnimating = false;

                if (persist)
                {
                    SaveEditorViewPreferences();
                }
            };

            EditorColumn.BeginAnimation(ColumnDefinition.WidthProperty, editorAnimation);
            SideToolsColumn.BeginAnimation(ColumnDefinition.WidthProperty, sideAnimation);
            PreviewColumn.BeginAnimation(ColumnDefinition.WidthProperty, previewAnimation);
        }

        private EditorViewModeWidths GetEditorViewModeWidths(EditorViewMode mode, double splitRatio)
        {
            double totalWidth = Math.Max(0.0, EditorWorkspaceGrid.ActualWidth);
            double splitWidth = Math.Max(0.0, totalWidth - SideToolsColumnWidth);
            splitRatio = ClampEditorSplitRatio(splitRatio);

            return mode switch
            {
                EditorViewMode.WriteOnly => new EditorViewModeWidths(totalWidth, 0.0, 0.0),
                EditorViewMode.PreviewOnly => new EditorViewModeWidths(0.0, 0.0, totalWidth),
                _ => new EditorViewModeWidths(splitWidth * splitRatio, SideToolsColumnWidth, splitWidth * (1.0 - splitRatio))
            };
        }

        private void PrepareAnimatedViewModeChange(EditorViewModeWidths widths)
        {
            EditorPane.Visibility = Visibility.Visible;
            SideToolsPanel.Visibility = Visibility.Visible;
            PreviewPane.Visibility = Visibility.Visible;
            EditorColumn.Width = new GridLength(widths.Editor, GridUnitType.Pixel);
            SideToolsColumn.Width = new GridLength(widths.Side, GridUnitType.Pixel);
            PreviewColumn.Width = new GridLength(widths.Preview, GridUnitType.Pixel);
        }

        private GridLengthAnimation CreateColumnWidthAnimation(double from, double to)
        {
            return new GridLengthAnimation
            {
                From = new GridLength(from, GridUnitType.Pixel),
                To = new GridLength(to, GridUnitType.Pixel),
                Duration = ViewModeAnimationDuration,
                EasingFunction = ViewModeEase
            };
        }

        private void ClearColumnWidthAnimations()
        {
            EditorColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
            SideToolsColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
            PreviewColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
        }

        private void RegisterToolDefinitions()
        {
            AddToolDefinition(new EditorToolDefinition
            {
                Id = "insert-image",
                Command = "insert-image",
                Symbol = SymbolRegular.ImageAdd24,
                ToolTipResourceKey = "EditorTipInsertImage"
            });
            AddToolDefinition(new EditorToolDefinition
            {
                Id = "insert-link",
                Command = "insert-link",
                Symbol = SymbolRegular.Link24,
                ToolTipResourceKey = "EditorTipInsertLink"
            });
            AddToolDefinition(new EditorToolDefinition { Id = "h1", Command = "h1", Symbol = SymbolRegular.TextHeader124, ToolTipResourceKey = "EditorToolHeading1Tip" });
            AddToolDefinition(new EditorToolDefinition { Id = "h2", Command = "h2", Symbol = SymbolRegular.TextHeader224, ToolTipResourceKey = "EditorToolHeading2Tip" });
            AddToolDefinition(new EditorToolDefinition { Id = "h3", Command = "h3", Symbol = SymbolRegular.TextHeader324, ToolTipResourceKey = "EditorToolHeading3Tip" });
            AddToolDefinition(new EditorToolDefinition { Id = "h4", Command = "h4", Symbol = SymbolRegular.TextHeader424, ToolTipResourceKey = "EditorToolHeading4Tip" });
            AddToolDefinition(new EditorToolDefinition
            {
                Id = "bold",
                Command = "bold",
                Symbol = SymbolRegular.TextBold24,
                ToolTipResourceKey = "EditorToolBoldTip"
            });
            AddToolDefinition(new EditorToolDefinition
            {
                Id = "italic",
                Command = "italic",
                Symbol = SymbolRegular.TextItalic24,
                ToolTipResourceKey = "EditorToolItalicTip"
            });
            AddToolDefinition(new EditorToolDefinition
            {
                Id = "strike",
                Command = "strike",
                Symbol = SymbolRegular.TextStrikethrough24,
                ToolTipResourceKey = "EditorToolStrikeTip"
            });
            AddToolDefinition(new EditorToolDefinition
            {
                Id = "code-inline",
                Command = "code-inline",
                Symbol = SymbolRegular.CodeText20,
                ToolTipResourceKey = "EditorToolInlineCodeTip",
                SymbolFontSize = 18
            });
            AddToolDefinition(new EditorToolDefinition
            {
                Id = "code-block",
                Command = "code-block",
                Symbol = SymbolRegular.CodeBlock24,
                ToolTipResourceKey = "EditorToolCodeBlockTip"
            });
            AddToolDefinition(new EditorToolDefinition { Id = "quote", Command = "quote", Symbol = SymbolRegular.TextQuote24, ToolTipResourceKey = "EditorToolQuoteTip" });
            AddToolDefinition(new EditorToolDefinition { Id = "bullet-list", Command = "bullet-list", Symbol = SymbolRegular.TextBulletListLtr24, ToolTipResourceKey = "EditorToolBulletListTip" });
            AddToolDefinition(new EditorToolDefinition { Id = "ordered-list", Command = "ordered-list", Symbol = SymbolRegular.TextNumberListLtr24, ToolTipResourceKey = "EditorToolOrderedListTip" });
            AddToolDefinition(new EditorToolDefinition
            {
                Id = "task-list",
                Command = "task-list",
                Symbol = SymbolRegular.TaskListLtr24,
                ToolTipResourceKey = "EditorToolTaskListTip"
            });
            AddToolDefinition(new EditorToolDefinition { Id = "table", Command = "table", Symbol = SymbolRegular.TableAdd20, ToolTipResourceKey = "EditorToolTableTip", SymbolFontSize = 18 });
            AddToolDefinition(new EditorToolDefinition
            {
                Id = "divider",
                Command = "divider",
                Symbol = SymbolRegular.LineHorizontal324,
                ToolTipResourceKey = "EditorToolDividerTip"
            });
            AddToolDefinition(new EditorToolDefinition
            {
                Id = "sync-to-preview",
                Command = "sync-to-preview",
                Symbol = SymbolRegular.ArrowCircleRight24,
                ToolTipResourceKey = "EditorTipSyncToPreview",
                IsSideOnly = true
            });
            AddToolDefinition(new EditorToolDefinition
            {
                Id = "sync-to-editor",
                Command = "sync-to-editor",
                Symbol = SymbolRegular.ArrowCircleLeft24,
                ToolTipResourceKey = "EditorTipSyncToEditor",
                IsSideOnly = true
            });
        }

        private void AddToolDefinition(EditorToolDefinition definition)
        {
            _toolDefinitions[definition.Id] = definition;
        }

        private void LoadEditorToolLayout()
        {
            var settings = StorageService.Load();
            _isToolboxCollapsed = settings.EditorToolboxCollapsed;

            var ribbonIds = BuildOrderedToolIds(
                settings.EditorRibbonToolOrder,
                DefaultRibbonToolIds,
                static (_, definition) => !definition.IsSideOnly);

            var sideIds = BuildOrderedToolIds(
                settings.EditorSideToolOrder,
                DefaultSideToolIds,
                static (_, _) => true);

            if (!settings.KeepToolboxToolWhenPinned)
            {
                var pinnedSideToolIds = sideIds.ToHashSet(StringComparer.Ordinal);
                ribbonIds = ribbonIds
                    .Where(id => !pinnedSideToolIds.Contains(id))
                    .ToList();
            }

            _ribbonTools.Clear();
            foreach (var id in ribbonIds)
            {
                _ribbonTools.Add(CreateToolViewItem(id, EditorToolHost.Ribbon));
            }

            _sideTools.Clear();
            foreach (var id in sideIds)
            {
                _sideTools.Add(CreateToolViewItem(id, EditorToolHost.Side));
            }
        }

        private List<string> BuildOrderedToolIds(IEnumerable<string>? savedIds, IEnumerable<string> defaultIds, Func<string, EditorToolDefinition, bool> predicate)
        {
            var orderedIds = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (savedIds != null)
            {
                foreach (var id in savedIds)
                {
                    if (TryIncludeTool(id, predicate, seen, orderedIds))
                    {
                        continue;
                    }
                }
            }

            foreach (var id in defaultIds)
            {
                TryIncludeTool(id, predicate, seen, orderedIds);
            }

            return orderedIds;
        }

        private bool TryIncludeTool(string? id, Func<string, EditorToolDefinition, bool> predicate, HashSet<string> seen, List<string> orderedIds)
        {
            if (string.IsNullOrWhiteSpace(id) ||
                !seen.Add(id) ||
                !_toolDefinitions.TryGetValue(id, out var definition) ||
                !predicate(id, definition))
            {
                return false;
            }

            orderedIds.Add(id);
            return true;
        }

        private EditorToolViewItem CreateToolViewItem(string id, EditorToolHost host)
        {
            var definition = _toolDefinitions[id];
            var toolTipText = Application.Current.FindResource(definition.ToolTipResourceKey).ToString()!;
            return new EditorToolViewItem(definition, host, toolTipText);
        }

        private void SaveEditorToolLayout()
        {
            var settings = StorageService.Load();
            settings.EditorToolboxCollapsed = _isToolboxCollapsed;
            settings.EditorRibbonToolOrder = _ribbonTools.Select(item => item.Id).ToList();
            settings.EditorSideToolOrder = _sideTools.Select(item => item.Id).ToList();
            StorageService.Save(settings);
        }

        private static bool KeepToolboxToolWhenPinned()
        {
            return StorageService.Load().KeepToolboxToolWhenPinned;
        }

        private void SideToolsPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isViewModeAnimating ||
                _editorViewMode != EditorViewMode.Split ||
                sender is not FrameworkElement host ||
                !CanStartLayoutDrag(e.OriginalSource as DependencyObject))
            {
                return;
            }

            BeginLayoutDrag(host, LayoutDragSource.SideRail, e.GetPosition(EditorWorkspaceGrid));
            e.Handled = true;
        }

        private void SideToolsPanel_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isLayoutDragActive)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndLayoutDrag(commitPreference: true);
                return;
            }

            var currentPosition = e.GetPosition(EditorWorkspaceGrid);
            UpdateLayoutDrag(currentPosition);
            e.Handled = true;
        }

        private void SideToolsPanel_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isLayoutDragActive)
            {
                return;
            }

            EndLayoutDrag(commitPreference: true);
            e.Handled = true;
        }

        private void SideToolsPanel_LostMouseCapture(object sender, MouseEventArgs e)
        {
            EndLayoutDrag(commitPreference: true);
        }

        private bool CanStartLayoutDrag(DependencyObject? source)
        {
            return source != null &&
                   FindToolElement(source) == null &&
                   FindVisualParent<ButtonBase>(source) == null;
        }

        private void BeginLayoutDrag(FrameworkElement host, LayoutDragSource source, Point startPoint)
        {
            _isLayoutDragActive = true;
            _isLayoutDragArmed = false;
            _layoutDragSource = source;
            _layoutDragHost = host;
            _layoutDragStartPoint = startPoint;
            _layoutDragStartEditorWidth = EditorColumn.ActualWidth;
            _layoutDragAvailableWidth = EditorColumn.ActualWidth + PreviewColumn.ActualWidth;

            host.CaptureMouse();
        }

        private void UpdateLayoutDrag(Point currentPosition)
        {
            if (!_isLayoutDragArmed)
            {
                if (Math.Abs(currentPosition.X - _layoutDragStartPoint.X) < SplitRailDragActivationThreshold)
                {
                    return;
                }

                _isLayoutDragArmed = true;
            }

            UpdateSplitRailDrag(currentPosition);
        }

        private void UpdateSplitRailDrag(Point currentPosition)
        {
            if (_layoutDragAvailableWidth <= 0)
            {
                _layoutDragAvailableWidth = EditorColumn.ActualWidth + PreviewColumn.ActualWidth;
                if (_layoutDragAvailableWidth <= 0)
                {
                    return;
                }
            }

            double deltaX = currentPosition.X - _layoutDragStartPoint.X;
            double nextEditorWidth = _layoutDragStartEditorWidth + deltaX;
            double nextPreviewWidth = _layoutDragAvailableWidth - nextEditorWidth;

            if (nextEditorWidth <= SplitAutoSwitchThreshold)
            {
                EndLayoutDrag(commitPreference: false);
                SetEditorViewMode(EditorViewMode.PreviewOnly);
                return;
            }

            if (nextPreviewWidth <= SplitAutoSwitchThreshold)
            {
                EndLayoutDrag(commitPreference: false);
                SetEditorViewMode(EditorViewMode.WriteOnly);
                return;
            }

            _editorSplitRatio = ClampEditorSplitRatio(nextEditorWidth / _layoutDragAvailableWidth);
            ApplyEditorViewMode(persist: false);
        }

        private void EndLayoutDrag(bool commitPreference)
        {
            if (!_isLayoutDragActive)
            {
                return;
            }

            _isLayoutDragActive = false;
            _isLayoutDragArmed = false;
            _layoutDragSource = LayoutDragSource.None;

            if (_layoutDragHost?.IsMouseCaptured == true)
            {
                _layoutDragHost.ReleaseMouseCapture();
            }

            _layoutDragHost = null;

            if (commitPreference)
            {
                SaveEditorViewPreferences();
            }
        }

        private void ToolButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: EditorToolViewItem item })
            {
                _draggedTool = item;
                _toolDragStartPoint = e.GetPosition(this);
            }
        }

        private void ToolButton_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_draggedTool == null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var currentPosition = e.GetPosition(this);
            if (Math.Abs(currentPosition.X - _toolDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPosition.Y - _toolDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(EditorToolViewItem), _draggedTool), DragDropEffects.Move);
            ClearDropCues();
            _draggedTool = null;
            e.Handled = true;
        }

        private void ToolButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _draggedTool = null;
        }

        private void RibbonToolsHost_DragOver(object sender, DragEventArgs e)
        {
            bool canDrop = TryGetDraggedTool(e.Data, out var item) && item != null && !item.IsSideOnly;
            SetRibbonDropCueActive(canDrop);
            SetSideDropCueActive(false);
            HideInsertionIndicator(EditorToolHost.Side);

            if (canDrop)
            {
                ShowInsertionIndicator(
                    EditorToolHost.Ribbon,
                    GetDropPlacement(e, _ribbonTools, Orientation.Horizontal, RibbonToolsItemsControl, RibbonToolsHostGrid));
            }
            else
            {
                HideInsertionIndicator(EditorToolHost.Ribbon);
            }

            e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void SideToolsHost_DragOver(object sender, DragEventArgs e)
        {
            bool canDrop = TryGetDraggedTool(e.Data, out var item) && item != null;
            SetSideDropCueActive(canDrop);
            SetRibbonDropCueActive(false);
            HideInsertionIndicator(EditorToolHost.Ribbon);

            if (canDrop)
            {
                ShowInsertionIndicator(
                    EditorToolHost.Side,
                    GetDropPlacement(e, _sideTools, Orientation.Vertical, SideToolsItemsControl, SideToolsHostGrid));
            }
            else
            {
                HideInsertionIndicator(EditorToolHost.Side);
            }

            e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void RibbonToolsHost_DragLeave(object sender, DragEventArgs e)
        {
            SetRibbonDropCueActive(false);
            HideInsertionIndicator(EditorToolHost.Ribbon);
        }

        private void SideToolsHost_DragLeave(object sender, DragEventArgs e)
        {
            SetSideDropCueActive(false);
            HideInsertionIndicator(EditorToolHost.Side);
        }

        private void RibbonToolsHost_Drop(object sender, DragEventArgs e)
        {
            ClearDropCues();

            if (!TryGetDraggedTool(e.Data, out var draggedItem) || draggedItem == null || draggedItem.IsSideOnly)
            {
                return;
            }

            var placement = GetDropPlacement(e, _ribbonTools, Orientation.Horizontal, RibbonToolsItemsControl, RibbonToolsHostGrid);
            EnsureRibbonToolAt(placement.Index, draggedItem.Id);

            if (draggedItem.Host == EditorToolHost.Side)
            {
                RemovePinnedSideTool(draggedItem.Id);
            }

            SaveEditorToolLayout();
            AnimateDropCommit(EditorToolHost.Ribbon);
            e.Handled = true;
        }

        private void SideToolsHost_Drop(object sender, DragEventArgs e)
        {
            ClearDropCues();

            if (!TryGetDraggedTool(e.Data, out var draggedItem) || draggedItem == null)
            {
                return;
            }

            var placement = GetDropPlacement(e, _sideTools, Orientation.Vertical, SideToolsItemsControl, SideToolsHostGrid);

            if (draggedItem.Host == EditorToolHost.Side)
            {
                MoveCollectionItem(_sideTools, draggedItem, placement.Index);
            }
            else
            {
                PinToolToSide(placement.Index, draggedItem.Id);

                if (!KeepToolboxToolWhenPinned())
                {
                    RemoveRibbonTool(draggedItem.Id);
                }
            }

            SaveEditorToolLayout();
            AnimateDropCommit(EditorToolHost.Side);
            e.Handled = true;
        }

        private static bool TryGetDraggedTool(IDataObject dataObject, out EditorToolViewItem? item)
        {
            item = dataObject.GetDataPresent(typeof(EditorToolViewItem))
                ? dataObject.GetData(typeof(EditorToolViewItem)) as EditorToolViewItem
                : null;
            return item != null;
        }

        private ToolDropPlacement GetDropPlacement(
            DragEventArgs e,
            ObservableCollection<EditorToolViewItem> collection,
            Orientation orientation,
            ItemsControl? itemsControl,
            FrameworkElement? host)
        {
            if (host == null || itemsControl == null)
            {
                return CreateEmptyDropPlacement(host, orientation);
            }

            if (collection.Count == 0)
            {
                return CreateEmptyDropPlacement(host, orientation);
            }

            if (TryGetTargetContainer(e, itemsControl, out var targetContainer, out var targetItem))
            {
                int targetIndex = collection.IndexOf(targetItem!);
                if (targetIndex >= 0)
                {
                    var position = e.GetPosition(targetContainer);
                    bool insertAfter = orientation == Orientation.Horizontal
                        ? position.X > targetContainer.ActualWidth / 2
                        : position.Y > targetContainer.ActualHeight / 2;

                    return CreateDropPlacement(targetContainer, host, orientation, targetIndex, insertAfter);
                }
            }

            return GetNearestDropPlacement(e.GetPosition(host), collection, orientation, itemsControl, host);
        }

        private static bool TryGetTargetContainer(
            DragEventArgs e,
            ItemsControl itemsControl,
            out FrameworkElement targetContainer,
            out EditorToolViewItem targetItem)
        {
            targetContainer = null!;
            targetItem = null!;

            if (FindToolElement(e.OriginalSource as DependencyObject)?.DataContext is not EditorToolViewItem hoveredItem)
            {
                return false;
            }

            if (itemsControl.ItemContainerGenerator.ContainerFromItem(hoveredItem) is not FrameworkElement container)
            {
                return false;
            }

            targetContainer = container;
            targetItem = hoveredItem;
            return true;
        }

        private ToolDropPlacement GetNearestDropPlacement(
            Point pointerPosition,
            ObservableCollection<EditorToolViewItem> collection,
            Orientation orientation,
            ItemsControl itemsControl,
            FrameworkElement host)
        {
            ToolDropPlacement? nearestPlacement = null;
            double bestDistance = double.MaxValue;

            for (int index = 0; index < collection.Count; index++)
            {
                if (itemsControl.ItemContainerGenerator.ContainerFromItem(collection[index]) is not FrameworkElement container ||
                    container.ActualWidth <= 0 ||
                    container.ActualHeight <= 0)
                {
                    continue;
                }

                var beforePlacement = CreateDropPlacement(container, host, orientation, index, insertAfter: false);
                UpdateNearestPlacement(pointerPosition, orientation, beforePlacement, ref nearestPlacement, ref bestDistance);

                var afterPlacement = CreateDropPlacement(container, host, orientation, index, insertAfter: true);
                UpdateNearestPlacement(pointerPosition, orientation, afterPlacement, ref nearestPlacement, ref bestDistance);
            }

            return nearestPlacement ?? CreateEmptyDropPlacement(host, orientation);
        }

        private static void UpdateNearestPlacement(
            Point pointerPosition,
            Orientation orientation,
            ToolDropPlacement candidate,
            ref ToolDropPlacement? nearestPlacement,
            ref double bestDistance)
        {
            var center = GetPlacementCenter(candidate, orientation);
            double dx = pointerPosition.X - center.X;
            double dy = pointerPosition.Y - center.Y;
            double distance = (dx * dx) + (dy * dy);
            if (distance >= bestDistance)
            {
                return;
            }

            bestDistance = distance;
            nearestPlacement = candidate;
        }

        private ToolDropPlacement CreateDropPlacement(
            FrameworkElement container,
            FrameworkElement host,
            Orientation orientation,
            int targetIndex,
            bool insertAfter)
        {
            var bounds = GetElementBounds(container, host);

            if (orientation == Orientation.Horizontal)
            {
                double length = Math.Max(MinimumInsertionLength, bounds.Height - 8.0);
                double x = insertAfter ? bounds.Right : bounds.Left;
                double y = bounds.Top + Math.Max(0.0, (bounds.Height - length) / 2.0);
                return new ToolDropPlacement(
                    insertAfter ? targetIndex + 1 : targetIndex,
                    new Point(x - (RibbonInsertionThickness / 2.0), y),
                    length);
            }

            double hostWidth = host.ActualWidth > 0 ? host.ActualWidth : bounds.Width;
            double lineLength = Math.Max(MinimumInsertionLength, hostWidth - 12.0);
            double lineX = Math.Max(6.0, (hostWidth - lineLength) / 2.0);
            double lineY = insertAfter ? bounds.Bottom : bounds.Top;
            return new ToolDropPlacement(
                insertAfter ? targetIndex + 1 : targetIndex,
                new Point(lineX, lineY - (SideInsertionThickness / 2.0)),
                lineLength);
        }

        private ToolDropPlacement CreateEmptyDropPlacement(FrameworkElement? host, Orientation orientation)
        {
            double hostWidth = host?.ActualWidth ?? 0.0;
            double hostHeight = host?.ActualHeight ?? 0.0;

            if (orientation == Orientation.Horizontal)
            {
                double y = Math.Max(8.0, (hostHeight - MinimumInsertionLength) / 2.0);
                return new ToolDropPlacement(0, new Point(14.0, y), MinimumInsertionLength + 2.0);
            }

            double length = Math.Max(46.0, hostWidth - 12.0);
            double x = Math.Max(6.0, (hostWidth - length) / 2.0);
            return new ToolDropPlacement(0, new Point(x, 18.0), length);
        }

        private static Rect GetElementBounds(FrameworkElement element, FrameworkElement host)
        {
            var topLeft = element.TranslatePoint(new Point(0, 0), host);
            return new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
        }

        private static Point GetPlacementCenter(ToolDropPlacement placement, Orientation orientation)
        {
            return orientation == Orientation.Horizontal
                ? new Point(placement.Position.X + (RibbonInsertionThickness / 2.0), placement.Position.Y + (placement.Length / 2.0))
                : new Point(placement.Position.X + (placement.Length / 2.0), placement.Position.Y + (SideInsertionThickness / 2.0));
        }

        private static FrameworkElement? FindToolElement(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is FrameworkElement element && element.DataContext is EditorToolViewItem)
                {
                    return element;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        private void EnsureRibbonToolAt(int targetIndex, string toolId)
        {
            var ribbonItem = _ribbonTools.FirstOrDefault(item => item.Id == toolId);
            if (ribbonItem != null)
            {
                MoveCollectionItem(_ribbonTools, ribbonItem, targetIndex);
                return;
            }

            targetIndex = Math.Max(0, Math.Min(targetIndex, _ribbonTools.Count));
            _ribbonTools.Insert(targetIndex, CreateToolViewItem(toolId, EditorToolHost.Ribbon));
        }

        private void PinToolToSide(int targetIndex, string toolId)
        {
            var existingSideItem = _sideTools.FirstOrDefault(item => item.Id == toolId);
            if (existingSideItem != null)
            {
                MoveCollectionItem(_sideTools, existingSideItem, targetIndex);
                return;
            }

            targetIndex = Math.Max(0, Math.Min(targetIndex, _sideTools.Count));
            _sideTools.Insert(targetIndex, CreateToolViewItem(toolId, EditorToolHost.Side));
        }

        private void RemoveRibbonTool(string toolId)
        {
            var ribbonItem = _ribbonTools.FirstOrDefault(item => item.Id == toolId);
            if (ribbonItem != null)
            {
                _ribbonTools.Remove(ribbonItem);
            }
        }

        private void ClearDropCues()
        {
            SetRibbonDropCueActive(false);
            SetSideDropCueActive(false);
            HideInsertionIndicator(EditorToolHost.Ribbon);
            HideInsertionIndicator(EditorToolHost.Side);
        }

        private void SetRibbonDropCueActive(bool isActive)
        {
            if (_isRibbonDropCueActive == isActive)
            {
                return;
            }

            _isRibbonDropCueActive = isActive;
            AnimateDropCue(RibbonDropCue, RibbonToolsScaleTransform, isActive);
        }

        private void SetSideDropCueActive(bool isActive)
        {
            if (_isSideDropCueActive == isActive)
            {
                return;
            }

            _isSideDropCueActive = isActive;
            AnimateDropCue(SideDropCue, SideToolsScaleTransform, isActive);
        }

        private static void AnimateDropCue(Border? cueBorder, ScaleTransform? scaleTransform, bool isActive)
        {
            if (cueBorder == null || scaleTransform == null)
            {
                return;
            }

            cueBorder.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
            {
                To = isActive ? 1.0 : 0.0,
                Duration = DropCueAnimationDuration,
                EasingFunction = PanelEase
            });

            double targetScale = isActive ? ActiveDropScale : 1.0;
            var scaleAnimation = new DoubleAnimation
            {
                To = targetScale,
                Duration = DropCueAnimationDuration,
                EasingFunction = PanelEase
            };
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation.Clone());
        }

        private void ShowInsertionIndicator(EditorToolHost host, ToolDropPlacement placement)
        {
            var indicator = host == EditorToolHost.Ribbon ? RibbonInsertionIndicator : SideInsertionIndicator;
            var transform = host == EditorToolHost.Ribbon ? RibbonInsertionTransform : SideInsertionTransform;
            if (indicator == null || transform == null)
            {
                return;
            }

            if (host == EditorToolHost.Ribbon)
            {
                indicator.Width = RibbonInsertionThickness;
                indicator.Height = placement.Length;
            }
            else
            {
                indicator.Width = placement.Length;
                indicator.Height = SideInsertionThickness;
            }

            AnimateIndicatorOpacity(indicator, 1.0);
            AnimateIndicatorTranslation(transform, placement.Position);
        }

        private void HideInsertionIndicator(EditorToolHost host)
        {
            var indicator = host == EditorToolHost.Ribbon ? RibbonInsertionIndicator : SideInsertionIndicator;
            AnimateIndicatorOpacity(indicator, 0.0);
        }

        private static void AnimateIndicatorOpacity(UIElement? indicator, double opacity)
        {
            if (indicator == null)
            {
                return;
            }

            indicator.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
            {
                To = opacity,
                Duration = DropCueAnimationDuration,
                EasingFunction = PanelEase
            });
        }

        private static void AnimateIndicatorTranslation(TranslateTransform? transform, Point position)
        {
            if (transform == null)
            {
                return;
            }

            transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
            {
                To = position.X,
                Duration = DropCueAnimationDuration,
                EasingFunction = PanelEase
            });

            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
            {
                To = position.Y,
                Duration = DropCueAnimationDuration,
                EasingFunction = PanelEase
            });
        }

        private void AnimateDropCommit(EditorToolHost host)
        {
            var targetTransform = host == EditorToolHost.Ribbon ? RibbonToolsScaleTransform : SideToolsScaleTransform;
            if (targetTransform == null)
            {
                return;
            }

            var pulse = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(210)
            };
            pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0.0)));
            pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.022, KeyTime.FromPercent(0.45)) { EasingFunction = PanelEase });
            pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0)) { EasingFunction = PanelEase });

            targetTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            targetTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulse.Clone());
        }

        private void RemovePinnedSideTool(string toolId)
        {
            var sideItem = _sideTools.FirstOrDefault(item => item.Id == toolId && !item.IsSideOnly);
            if (sideItem != null)
            {
                _sideTools.Remove(sideItem);
            }
        }

        private static void MoveCollectionItem(ObservableCollection<EditorToolViewItem> collection, EditorToolViewItem item, int targetIndex)
        {
            int currentIndex = collection.IndexOf(item);
            if (currentIndex < 0)
            {
                return;
            }

            targetIndex = Math.Max(0, Math.Min(targetIndex, collection.Count));
            if (targetIndex > currentIndex)
            {
                targetIndex--;
            }

            if (targetIndex == currentIndex)
            {
                return;
            }

            collection.Move(currentIndex, targetIndex);
        }

        private async void ToolbarTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: EditorToolViewItem item })
            {
                return;
            }

            await ExecuteToolbarCommandAsync(item.Command);
        }

        private void InitializeTimeComboBoxes()
        {
            var hours = Enumerable.Range(0, 24).Select(i => i.ToString("D2")).ToList();
            var minutes = Enumerable.Range(0, 60).Select(i => i.ToString("D2")).ToList();

            PublishHourBox.ItemsSource = hours;
            PublishMinuteBox.ItemsSource = minutes;
            ModifyHourBox.ItemsSource = hours;
            ModifyMinuteBox.ItemsSource = minutes;
        }

        private async void InitializeWebViewsAsync()
        {
            var webViewDataDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlogTools",
                "WebView2");
            System.IO.Directory.CreateDirectory(webViewDataDir);

            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, webViewDataDir);
            
            await PreviewWebView.EnsureCoreWebView2Async(env);
            await EditorWebView.EnsureCoreWebView2Async(env);

            // 从嵌入式资源提取 KaTeX 到临时目录
            var katexFolder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlogTools", "katex");
            ExtractKatexResources(katexFolder);

            PreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "localassets", katexFolder,
                Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

            if (!string.IsNullOrEmpty(App.JekyllContext.BlogPath))
            {
                PreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "bloglocal", App.JekyllContext.BlogPath,
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
            }

            var isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
            var darkCss = isDark ? "body { background-color: #1e1e1e; color: #d4d4d4; }" : "body { background-color: #ffffff; color: #000000; }";

            var katexCss = "";
            var katexJs = "";
            try
            {
                katexCss = System.IO.File.ReadAllText(System.IO.Path.Combine(katexFolder, "katex.min.css"));
                katexJs = System.IO.File.ReadAllText(System.IO.Path.Combine(katexFolder, "katex.min.js"));
                katexCss = katexCss.Replace("fonts/", "https://localassets/fonts/");
            }
            catch { }

            var renderScript = @"
      function renderMathInElement(el) {
        if (!window.katex) return;
        var mathEls = el.querySelectorAll('.math');
        mathEls.forEach(function(m) {
          try {
            var tex = m.textContent || '';
            var isDisplay = m.tagName === 'DIV';
            if (tex.startsWith('\\(') && tex.endsWith('\\)')) {
              tex = tex.slice(2, -2);
            } else if (tex.startsWith('\\[') && tex.endsWith('\\]')) {
              tex = tex.slice(2, -2);
              isDisplay = true;
            }
            katex.render(tex.trim(), m, { throwOnError: false, displayMode: isDisplay });
          } catch(e) {
            console.log('KaTeX render error:', e.message);
          }
        });
      }
";
            string trackColor = "transparent";
            string thumbColor = isDark ? "#666" : "#aaa";
            string thumbHover = isDark ? "#888" : "#888";
            string bga = isDark ? "#1e1e1e" : "#fafafa";
            string scrollbarCss = $@"
                ::-webkit-scrollbar {{ width: 14px; height: 14px; }}
                ::-webkit-scrollbar-track {{ background: {trackColor}; }}
                ::-webkit-scrollbar-thumb {{ background: {thumbColor}; border-radius: 7px; border: 3px solid {bga}; }}
                ::-webkit-scrollbar-thumb:hover {{ background: {thumbHover}; }}
                ::-webkit-scrollbar-corner {{ background: transparent; }}
            ";

            var initialHtml = "<!DOCTYPE html><html><head><meta charset='utf-8' />"
                + "<base href='https://bloglocal/' />"
                + "<style>" + katexCss + "</style>"
                + "<script>" + katexJs + "</script>"
                + "<style>"
                + darkCss
                + scrollbarCss
                +  $@"
        html, body {{ margin: 0; padding: 0; overflow: hidden; height: 100%; width: 100%; box-sizing: border-box; }}
        #content {{ font-family: -apple-system, 'Microsoft YaHei UI', Helvetica, Arial, sans-serif; padding: 20px; line-height: 1.6; word-wrap: break-word; box-sizing: border-box; height: 100%; overflow-y: auto; overflow-x: hidden; }}
        img {{ max-width: 100%; height: auto; border-radius: 6px; }}
        pre {{ background: {(isDark ? "#2d2d2d" : "#f6f8fa")}; padding: 12px; border-radius: 6px; overflow-x: auto; }}
        code {{ font-family: Consolas, monospace; background: {(isDark ? "#333" : "#eee")}; padding: 2px 4px; border-radius: 4px; }}
        pre code {{ background: none; padding: 0; }}
        blockquote {{ border-left: 4px solid #0078D4; padding-left: 10px; color: {(isDark ? "#aaa" : "#555")}; margin-left: 0; }}
        table {{ border-collapse: collapse; width: 100%; }}
        th, td {{ border: 1px solid {(isDark ? "#444" : "#ddd")}; padding: 8px; text-align: left; }}
        .katex {{ font-size: 1.1em; }}
        .katex-display {{ overflow-x: auto; overflow-y: hidden; padding: 4px 0; }}
"
                + "</style>"
                + "<script>" + renderScript + "</script>"
                + "</head><body><div id='content'></div></body></html>";

            PreviewWebView.NavigateToString(initialHtml);

            PreviewWebView.NavigationCompleted += (s, e) =>
            {
                _isWebViewReady = true;
                UpdateWebViewContent();
            };

            var placeholder = Application.Current.FindResource("EditorPlaceholder").ToString();
            var editorScript = """
                const el = document.getElementById('editor');
                const notifyContent = () => {
                    window.chrome.webview.postMessage('CONTENT:' + el.value);
                };

                const replaceRange = (start, end, replacement, selectionStart, selectionEnd) => {
                    const current = el.value || '';
                    el.focus();
                    el.value = current.slice(0, start) + replacement + current.slice(end);
                    el.selectionStart = selectionStart;
                    el.selectionEnd = selectionEnd;
                    const inputEvent = new Event('input', { bubbles: true });
                    el.dispatchEvent(inputEvent);
                };

                const getLineRange = (start, end) => {
                    const value = el.value || '';
                    const lineStart = value.lastIndexOf('\n', Math.max(0, start - 1)) + 1;
                    let lineEnd = value.indexOf('\n', end);
                    if (lineEnd === -1) {
                        lineEnd = value.length;
                    }

                    return { lineStart, lineEnd };
                };

                window.editorTools = {
                    wrapSelection(prefix, suffix, placeholder) {
                        const start = typeof el.selectionStart === 'number' ? el.selectionStart : el.value.length;
                        const end = typeof el.selectionEnd === 'number' ? el.selectionEnd : el.value.length;
                        const selected = (el.value || '').slice(start, end);
                        const content = selected.length > 0 ? selected : placeholder;
                        const replacement = prefix + content + suffix;
                        const selectionOffset = start + prefix.length;
                        replaceRange(start, end, replacement, selectionOffset, selectionOffset + content.length);
                    },

                    prefixLines(prefix, placeholder) {
                        const selectionStart = typeof el.selectionStart === 'number' ? el.selectionStart : el.value.length;
                        const selectionEnd = typeof el.selectionEnd === 'number' ? el.selectionEnd : el.value.length;
                        const hasSelection = selectionStart !== selectionEnd;
                        const value = el.value || '';
                        const { lineStart, lineEnd } = getLineRange(selectionStart, selectionEnd);
                        const block = value.slice(lineStart, lineEnd);
                        const lines = block.length > 0 ? block.split('\n') : [''];
                        const replacement = lines
                            .map(line => prefix + ((line.length === 0 && !hasSelection) ? placeholder : line))
                            .join('\n');

                        if (!hasSelection && lines.length === 1 && lines[0].length === 0) {
                            const targetStart = lineStart + prefix.length;
                            replaceRange(lineStart, lineEnd, replacement, targetStart, targetStart + placeholder.length);
                            return;
                        }

                        replaceRange(lineStart, lineEnd, replacement, lineStart, lineStart + replacement.length);
                    },

                    numberLines(placeholder) {
                        const selectionStart = typeof el.selectionStart === 'number' ? el.selectionStart : el.value.length;
                        const selectionEnd = typeof el.selectionEnd === 'number' ? el.selectionEnd : el.value.length;
                        const hasSelection = selectionStart !== selectionEnd;
                        const value = el.value || '';
                        const { lineStart, lineEnd } = getLineRange(selectionStart, selectionEnd);
                        const block = value.slice(lineStart, lineEnd);
                        const lines = block.length > 0 ? block.split('\n') : [''];
                        const replacement = lines
                            .map((line, index) => `${index + 1}. ${line.length === 0 && !hasSelection ? placeholder : line}`)
                            .join('\n');

                        if (!hasSelection && lines.length === 1 && lines[0].length === 0) {
                            const targetStart = lineStart + 3;
                            replaceRange(lineStart, lineEnd, replacement, targetStart, targetStart + placeholder.length);
                            return;
                        }

                        replaceRange(lineStart, lineEnd, replacement, lineStart, lineStart + replacement.length);
                    },

                    insertBlock(before, after, placeholder) {
                        const start = typeof el.selectionStart === 'number' ? el.selectionStart : el.value.length;
                        const end = typeof el.selectionEnd === 'number' ? el.selectionEnd : el.value.length;
                        const value = el.value || '';
                        const selected = value.slice(start, end);
                        const content = selected.length > 0 ? selected : placeholder;
                        let replacement = before + content + after;
                        let offset = 0;

                        if (start > 0 && value[start - 1] !== '\n') {
                            replacement = '\n' + replacement;
                            offset = 1;
                        }

                        if (end < value.length && value[end] !== '\n') {
                            replacement += '\n';
                        }

                        const selectionOffset = start + offset + before.length;
                        replaceRange(start, end, replacement, selectionOffset, selectionOffset + content.length);
                    },

                    insertText(text) {
                        const start = typeof el.selectionStart === 'number' ? el.selectionStart : el.value.length;
                        const end = typeof el.selectionEnd === 'number' ? el.selectionEnd : el.value.length;
                        replaceRange(start, end, text, start + text.length, start + text.length);
                    },

                    insertLine(text) {
                        const start = typeof el.selectionStart === 'number' ? el.selectionStart : el.value.length;
                        const end = typeof el.selectionEnd === 'number' ? el.selectionEnd : el.value.length;
                        const value = el.value || '';
                        let replacement = text;

                        if (start > 0 && value[start - 1] !== '\n') {
                            replacement = '\n' + replacement;
                        }

                        if (end < value.length && value[end] !== '\n') {
                            replacement += '\n';
                        }

                        replaceRange(start, end, replacement, start + replacement.length, start + replacement.length);
                    }
                };

                el.addEventListener('input', notifyContent);
                window.chrome.webview.addEventListener('message', event => {
                    if (el.value !== event.data) {
                        el.value = event.data;
                    }
                });
                el.addEventListener('paste', function(e) {
                    var items = (e.clipboardData || e.originalEvent.clipboardData).items;
                    for (var index in items) {
                        var item = items[index];
                        if (item.kind === 'file' && item.type.indexOf('image/') !== -1) {
                            e.preventDefault();
                            window.chrome.webview.postMessage('ACTION:pasteImage');
                            break;
                        }
                    }
                });
                """;
            var editorHtml = $"<!DOCTYPE html><html><head><meta charset='utf-8' /><style>{darkCss} {scrollbarCss} " +
            "html, body { margin: 0; padding: 0; overflow: hidden; height: 100%; width: 100%; box-sizing: border-box; } " +
            "textarea { width: 100%; height: 100%; box-sizing: border-box; padding: 20px; border: none; outline: none; resize: none; " +
            "font-family: Consolas, monospace; font-size: 15px; background-color: transparent; color: inherit; line-height: 1.6; } " +
            "</style></head><body>" +
            $"<textarea id='editor' spellcheck='false' placeholder='{placeholder}'></textarea>" +
            "<script>" + editorScript + "</script></body></html>";

            EditorWebView.NavigateToString(editorHtml);
            EditorWebView.WebMessageReceived += EditorWebView_WebMessageReceived;
        }

        private void EditorWebView_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            var msg = e.TryGetWebMessageAsString() ?? "";
            if (msg.StartsWith("CONTENT:"))
            {
                _currentContent = msg.Substring(8);
                UpdateWebViewContent();
                SmartDetectMath();
            }
            else if (msg == "ACTION:pasteImage")
            {
                _ = HandlePastedImageAsync();
            }
        }

        private async System.Threading.Tasks.Task HandlePastedImageAsync()
        {
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                var msg = new Wpf.Ui.Controls.MessageBox 
                { 
                    Title = Application.Current.FindResource("CommonPrompt").ToString()!, 
                    Content = Application.Current.FindResource("EditorMsgTitleRequired").ToString()!, 
                    CloseButtonText = Application.Current.FindResource("CommonConfirm").ToString()! 
                };
                await msg.ShowDialogAsync();
                return;
            }

            try
            {
                string[] pastedFiles = Array.Empty<string>();
                System.Windows.Media.Imaging.BitmapSource? pastedImage = null;

                if (System.Windows.Clipboard.ContainsFileDropList())
                {
                    var dropList = System.Windows.Clipboard.GetFileDropList();
                    pastedFiles = dropList.Cast<string>().Where(f => 
                        f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || 
                        f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                        f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || 
                        f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || 
                        f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)).ToArray();
                }
                else if (System.Windows.Clipboard.ContainsImage())
                {
                    pastedImage = System.Windows.Clipboard.GetImage();
                }

                if (pastedFiles.Length == 0 && pastedImage == null) return;

                var safeDirName = System.Text.RegularExpressions.Regex.Replace(TitleBox.Text, @"[\\/:*?""<>|]+", "-").Trim('-', ' ');
                safeDirName = System.Text.RegularExpressions.Regex.Replace(safeDirName, @"\s+", "-").ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(safeDirName)) safeDirName = "untitled";

                var relativeDir = $"assets/img/inposts/{safeDirName}";
                var absPathDir = System.IO.Path.Combine(App.JekyllContext.BlogPath, relativeDir.Replace("/", "\\"));
                
                if (!System.IO.Directory.Exists(absPathDir))
                {
                    System.IO.Directory.CreateDirectory(absPathDir);
                }

                string injectedMd = "";

                foreach (var file in pastedFiles)
                {
                    var fileName = System.IO.Path.GetFileName(file);
                    var destFile = System.IO.Path.Combine(absPathDir, fileName);
                    int counter = 1;
                    while (System.IO.File.Exists(destFile))
                    {
                        destFile = System.IO.Path.Combine(absPathDir, $"{System.IO.Path.GetFileNameWithoutExtension(fileName)}-{counter}{System.IO.Path.GetExtension(fileName)}");
                        fileName = System.IO.Path.GetFileName(destFile);
                        counter++;
                    }
                    System.IO.File.Copy(file, destFile);
                    injectedMd += $"![{System.IO.Path.GetFileNameWithoutExtension(fileName)}](/{relativeDir}/{fileName})\n";
                }

                if (pastedImage != null)
                {
                    var fileName = $"image-{DateTime.Now:yyyyMMddHHmmss}.png";
                    var destFile = System.IO.Path.Combine(absPathDir, fileName);
                    using (var fileStream = new System.IO.FileStream(destFile, System.IO.FileMode.Create))
                    {
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(pastedImage));
                        encoder.Save(fileStream);
                    }
                    injectedMd += $"![{System.IO.Path.GetFileNameWithoutExtension(fileName)}](/{relativeDir}/{fileName})\n";
                }

                injectedMd = injectedMd.TrimEnd('\n');

                await InsertTextIntoEditorAsync(injectedMd);
            }
            catch (Exception ex)
            {
                var msg = new Wpf.Ui.Controls.MessageBox 
                { 
                    Title = Application.Current.FindResource("CommonError").ToString()!, 
                    Content = string.Format(Application.Current.FindResource("EditorMsgErrorPasteImage").ToString()!, ex.Message), 
                    CloseButtonText = Application.Current.FindResource("CommonConfirm").ToString()! 
                };
                await msg.ShowDialogAsync();
            }
        }

        private bool _allowNav = false;
        private async void Nav_Navigating(Wpf.Ui.Controls.NavigationView sender, Wpf.Ui.Controls.NavigatingCancelEventArgs e)
        {
            if (_allowNav) return;
            if (CheckIsDirty())
            {
                e.Cancel = true;
                var msg = new Wpf.Ui.Controls.MessageBox 
                { 
                    Title = Application.Current.FindResource("EditorMsgConfirmLeave").ToString()!.Split('，')[0], // Extract "确认离开" roughly
                    Content = Application.Current.FindResource("EditorMsgConfirmLeave").ToString()!, 
                    PrimaryButtonText = Application.Current.FindResource("CommonConfirmLeave").ToString()!, 
                    CloseButtonText = Application.Current.FindResource("CommonCancel").ToString()! 
                };
                var res = await msg.ShowDialogAsync();
                if (res == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    _allowNav = true;
                    // Let's just navigate to Dashboard if we can't figure out the target page
                    sender.Navigate(typeof(DashboardPage));
                }
            }
        }

        private bool _allowClose = false;
        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_allowClose) return;
            if (CheckIsDirty())
            {
                e.Cancel = true;
                var msg = new Wpf.Ui.Controls.MessageBox 
                { 
                    Title = Application.Current.FindResource("CommonConfirmExitApp").ToString()!, 
                    Content = Application.Current.FindResource("EditorMsgConfirmExit").ToString()!, 
                    PrimaryButtonText = Application.Current.FindResource("CommonConfirmExit").ToString()!, 
                    CloseButtonText = Application.Current.FindResource("CommonCancel").ToString()! 
                };
                var res = await msg.ShowDialogAsync();
                if (res == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    _allowClose = true;
                    Application.Current.MainWindow.Close();
                }
            }
        }

        private void UpdateOriginalState()
        {
            try
            {
                var post = GeneratePostObject();
                _originalState = System.Text.Json.JsonSerializer.Serialize(post);
            }
            catch { }
        }

        private bool CheckIsDirty()
        {
            try
            {
                var post = GeneratePostObject();
                return System.Text.Json.JsonSerializer.Serialize(post) != _originalState;
            }
            catch { return false; }
        }

        private void EditorPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_parentSv != null) _parentSv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            EndLayoutDrag(commitPreference: true);
            ClearDropCues();
            
            var nav = (Application.Current.MainWindow as MainWindow)?.RootNavigation;
            if (nav != null) nav.Navigating -= Nav_Navigating;
            Application.Current.MainWindow.Closing -= MainWindow_Closing;
        }

        private void MetadataExpander_Expanded(object sender, RoutedEventArgs e)
        {
            var settings = BlogTools.Services.StorageService.Load();
            if (settings.RememberMetadataExpanded && !settings.IsMetadataExpanded)
            {
                settings.IsMetadataExpanded = true;
                BlogTools.Services.StorageService.Save(settings);
            }
        }

        private void MetadataExpander_Collapsed(object sender, RoutedEventArgs e)
        {
            var settings = BlogTools.Services.StorageService.Load();
            if (settings.RememberMetadataExpanded && settings.IsMetadataExpanded)
            {
                settings.IsMetadataExpanded = false;
                BlogTools.Services.StorageService.Save(settings);
            }
        }

        private void EditorPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Disable global parent scroll to ensure page fits viewport perfectly
            _parentSv = FindVisualParent<ScrollViewer>(this);
            if (_parentSv != null) _parentSv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

            var settings = BlogTools.Services.StorageService.Load();
            if (settings.RememberMetadataExpanded)
            {
                MetadataExpander.IsExpanded = settings.IsMetadataExpanded;
            }

            var nav = (Application.Current.MainWindow as MainWindow)?.RootNavigation;
            if (nav != null) nav.Navigating += Nav_Navigating;
            Application.Current.MainWindow.Closing += MainWindow_Closing;

            var allPosts = App.JekyllContext.GetAllPosts();
            var primaryCats = new HashSet<string>();
            var secondaryCats = new HashSet<string>();
            foreach (var p in allPosts)
            {
                if (p.Categories.Count > 0) primaryCats.Add(p.Categories[0]);
                if (p.Categories.Count > 1) secondaryCats.Add(p.Categories[1]);
            }
            PrimaryCategoryBox.ItemsSource = primaryCats.OrderBy(c => c).ToList();
            SecondaryCategoryBox.ItemsSource = secondaryCats.OrderBy(c => c).ToList();

            if (App.CurrentEditPost != null)
            {
                var post = App.CurrentEditPost;
                TitleBox.Text = post.Title;

                PublishDatePicker.SelectedDate = post.Date;
                PublishHourBox.SelectedItem = post.Date.Hour.ToString("D2");
                PublishMinuteBox.SelectedItem = post.Date.Minute.ToString("D2");

                if (post.LastModifiedAt.HasValue)
                {
                    ModifyDatePicker.SelectedDate = post.LastModifiedAt;
                    ModifyHourBox.SelectedItem = post.LastModifiedAt.Value.Hour.ToString("D2");
                    ModifyMinuteBox.SelectedItem = post.LastModifiedAt.Value.Minute.ToString("D2");
                }

                if (post.Categories.Count > 0) PrimaryCategoryBox.Text = post.Categories[0];
                if (post.Categories.Count > 1) SecondaryCategoryBox.Text = post.Categories[1];
                
                _tagsList.Clear();
                if (post.Tags != null)
                {
                    foreach (var t in post.Tags)
                        if (!string.IsNullOrWhiteSpace(t)) _tagsList.Add(t.Trim());
                }

                AuthorBox.Text = post.Author;
                MathSwitch.IsChecked = post.Math;
                TocSwitch.IsChecked = post.Toc;
                PinSwitch.IsChecked = post.Pin;
                DescriptionBox.Text = post.Description;
                ImageBox.Text = post.Image;

                _currentContent = post.Content ?? "";
            }
            else
            {
                SetPublishNow_Click(null, null);
                TocSwitch.IsChecked = true;
                _currentContent = "";
            }
            
            if (EditorWebView.CoreWebView2 != null)
            {
                EditorWebView.CoreWebView2.PostWebMessageAsString(_currentContent);
            }
            else
            {
                EditorWebView.NavigationCompleted += (s, ev) => 
                {
                    if (EditorWebView.CoreWebView2 != null)
                    {
                        EditorWebView.CoreWebView2.PostWebMessageAsString(_currentContent);
                    }
                };
            }
            
            UpdateOriginalState();
        }

        private void SmartDetectMath()
        {
            if (string.IsNullOrEmpty(_currentContent)) return;

            if (_currentContent.Contains("$"))
            {
                bool hasMath = System.Text.RegularExpressions.Regex.IsMatch(_currentContent, @"(\$\$.*?\$\$)|(\$.*?\$)", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (hasMath && MathSwitch.IsChecked == false)
                {
                    MathSwitch.IsChecked = true;
                }
            }
        }

        private async void UpdateWebViewContent()
        {
            if (!_isWebViewReady || PreviewWebView.CoreWebView2 == null) return;

            var htmlContent = Markdown.ToHtml(_currentContent, _pipeline);
            var base64Html = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(htmlContent));

            var script = $@"
                try {{
                    const b64 = '{base64Html}';
                    const bin = window.atob(b64);
                    const bytes = new Uint8Array(bin.length);
                    for (let i = 0; i < bin.length; i++) {{
                        bytes[i] = bin.charCodeAt(i);
                    }}
                    const decoded = new TextDecoder('utf-8').decode(bytes);
                    const el = document.getElementById('content');
                    el.innerHTML = decoded;
                    renderMathInElement(el);
                }} catch(e) {{
                    console.error('Render error:', e);
                }}
            ";

            await PreviewWebView.ExecuteScriptAsync(script);
        }

        private void NewPost_Click(object sender, RoutedEventArgs e)
        {
            App.CurrentEditPost = null;
            TitleBox.Clear();
            SetPublishNow_Click(null, null);
            ModifyDatePicker.SelectedDate = null;
            ModifyHourBox.SelectedIndex = -1;
            ModifyMinuteBox.SelectedIndex = -1;
            PrimaryCategoryBox.Text = "";
            SecondaryCategoryBox.Text = "";
            _tagsList.Clear();
            TagInputBox.Clear();
            AuthorBox.Clear();
            MathSwitch.IsChecked = false;
            TocSwitch.IsChecked = true;
            PinSwitch.IsChecked = false;
            DescriptionBox.Clear();
            ImageBox.Clear();
            
            _currentContent = "";
            if (EditorWebView.CoreWebView2 != null)
                EditorWebView.CoreWebView2.PostWebMessageAsString("");
                
            UpdateOriginalState();
            ShowInfo(Application.Current.FindResource("EditorMsgReset").ToString()!, InfoBarSeverity.Informational);
        }

        private void SetPublishNow_Click(object? sender, RoutedEventArgs? e)
        {
            PublishDatePicker.SelectedDate = DateTime.Now;
            PublishHourBox.SelectedItem = DateTime.Now.Hour.ToString("D2");
            PublishMinuteBox.SelectedItem = DateTime.Now.Minute.ToString("D2");
        }

        private void SetModifyNow_Click(object sender, RoutedEventArgs e)
        {
            ModifyDatePicker.SelectedDate = DateTime.Now;
            ModifyHourBox.SelectedItem = DateTime.Now.Hour.ToString("D2");
            ModifyMinuteBox.SelectedItem = DateTime.Now.Minute.ToString("D2");
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            await SavePostAsync();
        }

        private async System.Threading.Tasks.Task<bool> SavePostAsync()
        {
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                ShowInfo(Application.Current.FindResource("EditorMsgTitleEmpty").ToString()!, InfoBarSeverity.Error);
                return false;
            }

            var post = GeneratePostObject();

            if (App.CurrentEditPost != null && CheckIsDirty())
            {
                bool timeManuallyChanged = post.LastModifiedAt != App.CurrentEditPost.LastModifiedAt;
                if (!timeManuallyChanged)
                {
                    var settings = BlogTools.Services.StorageService.Load();
                    if (settings.AutoUpdateModifiedTime)
                    {
                        SetModifyNow_Click(null!, new RoutedEventArgs());
                        post = GeneratePostObject();
                    }
                    else
                    {
                        var askModify = new Wpf.Ui.Controls.MessageBox
                        {
                            Title = Application.Current.FindResource("EditorMsgTimeUpdateTitle").ToString()!,
                            Content = Application.Current.FindResource("EditorMsgTimeUpdateContent").ToString()!,
                            PrimaryButtonText = Application.Current.FindResource("CommonUpdateTime").ToString()!,
                            CloseButtonText = Application.Current.FindResource("CommonNoUpdate").ToString()!
                        };
                        var result = await askModify.ShowDialogAsync();
                        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                        {
                            SetModifyNow_Click(null!, new RoutedEventArgs());
                            post = GeneratePostObject();
                        }
                    }
                }
            }

            App.JekyllContext.SavePost(post);
            App.CurrentEditPost = post;

            UpdateOriginalState();
            ShowInfo(string.Format(Application.Current.FindResource("EditorMsgSavedLocal").ToString()!, post.FileName), InfoBarSeverity.Success);
            return true;
        }

        private async void PublishButton_Click(object sender, RoutedEventArgs e)
        {
            bool saved = await SavePostAsync();
            if (!saved || StatusInfo.Severity == InfoBarSeverity.Error)
                return;

            ShowInfo(Application.Current.FindResource("EditorMsgPublishing").ToString()!, InfoBarSeverity.Informational);
            try
            {
                var pullResult = await App.GitContext.PullAsync();
                if (pullResult.Contains("CONFLICT") || pullResult.Contains("Automatic merge failed"))
                {
                    ShowInfo(Application.Current.FindResource("EditorMsgConflict").ToString()!, InfoBarSeverity.Error);
                    return;
                }

                await App.GitContext.CommitAndPushAsync($"Update post: {TitleBox.Text}");
                ShowInfo(Application.Current.FindResource("EditorMsgPublishSuccess").ToString()!, InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowInfo(string.Format(Application.Current.FindResource("EditorMsgPublishError").ToString()!, ex.Message), InfoBarSeverity.Error);
            }
        }

        private BlogPost GeneratePostObject()
        {
            var cats = new List<string>();
            var prim = PrimaryCategoryBox.Text?.Trim();
            var sec = SecondaryCategoryBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(prim)) cats.Add(prim);
            if (!string.IsNullOrWhiteSpace(sec)) cats.Add(sec);
            
            ParseTagsInput(); // Ensure pending input is parsed
            var tags = _tagsList.ToList();

            var publishDate = PublishDatePicker.SelectedDate ?? DateTime.Now;
            int h = int.TryParse(PublishHourBox.SelectedItem as string, out int hVal) ? hVal : DateTime.Now.Hour;
            int m = int.TryParse(PublishMinuteBox.SelectedItem as string, out int mVal) ? mVal : DateTime.Now.Minute;
            publishDate = new DateTime(publishDate.Year, publishDate.Month, publishDate.Day, h, m, 0);

            DateTime? modifyDate = null;
            if (ModifyDatePicker.SelectedDate.HasValue)
            {
                var md = ModifyDatePicker.SelectedDate.Value;
                int mh = int.TryParse(ModifyHourBox.SelectedItem as string, out int mhVal) ? mhVal : DateTime.Now.Hour;
                int mm = int.TryParse(ModifyMinuteBox.SelectedItem as string, out int mmVal) ? mmVal : DateTime.Now.Minute;
                modifyDate = new DateTime(md.Year, md.Month, md.Day, mh, mm, 0);
            }

            return new BlogPost
            {
                Title = TitleBox.Text,
                Date = publishDate,
                LastModifiedAt = modifyDate,
                Categories = cats,
                Tags = tags,
                Author = AuthorBox.Text,
                Math = MathSwitch.IsChecked == true,
                Toc = TocSwitch.IsChecked == true,
                Pin = PinSwitch.IsChecked == true,
                Description = DescriptionBox.Text,
                Image = ImageBox.Text,
                Content = _currentContent,
                FileName = App.CurrentEditPost?.FileName ?? string.Empty
            };
        }

        private void ShowInfo(string message, InfoBarSeverity severity)
        {
            StatusInfo.Message = message;
            StatusInfo.Severity = severity;
            StatusInfo.IsOpen = true;
        }

        // ─── Sync scrolling & Tools ─────────────────────────────────
        
        private async System.Threading.Tasks.Task InsertTextIntoEditorAsync(string textToInsert)
        {
            if (EditorWebView.CoreWebView2 == null)
            {
                return;
            }

            var script = $@"
                (function() {{
                    var el = document.getElementById('editor');
                    if (!el) {{
                        return;
                    }}

                    el.focus();

                    var text = {System.Text.Json.JsonSerializer.Serialize(textToInsert)};
                    var start = typeof el.selectionStart === 'number' ? el.selectionStart : el.value.length;
                    var end = typeof el.selectionEnd === 'number' ? el.selectionEnd : el.value.length;
                    var current = el.value || '';

                    el.value = current.slice(0, start) + text + current.slice(end);

                    var caret = start + text.length;
                    el.selectionStart = caret;
                    el.selectionEnd = caret;

                    var inputEvent = new Event('input', {{ bubbles: true }});
                    el.dispatchEvent(inputEvent);
                }})();
            ";

            await EditorWebView.ExecuteScriptAsync(script);
        }

        private static string GetEditorResourceText(string resourceKey)
        {
            return Application.Current.FindResource(resourceKey).ToString()!;
        }

        private static string JsString(string value)
        {
            return System.Text.Json.JsonSerializer.Serialize(value);
        }

        private async System.Threading.Tasks.Task ExecuteEditorToolAsync(string invocationScript)
        {
            if (EditorWebView.CoreWebView2 == null)
            {
                return;
            }

            var script = $@"
                (function() {{
                    if (!window.editorTools) {{
                        return;
                    }}

                    {invocationScript}
                }})();
            ";

            await EditorWebView.ExecuteScriptAsync(script);
        }

        private async System.Threading.Tasks.Task WrapEditorSelectionAsync(string prefix, string suffix, string placeholderResourceKey)
        {
            await ExecuteEditorToolAsync(
                $"window.editorTools.wrapSelection({JsString(prefix)}, {JsString(suffix)}, {JsString(GetEditorResourceText(placeholderResourceKey))});");
        }

        private async System.Threading.Tasks.Task PrefixEditorLinesAsync(string prefix, string placeholderResourceKey)
        {
            await ExecuteEditorToolAsync(
                $"window.editorTools.prefixLines({JsString(prefix)}, {JsString(GetEditorResourceText(placeholderResourceKey))});");
        }

        private async System.Threading.Tasks.Task NumberEditorLinesAsync(string placeholderResourceKey)
        {
            await ExecuteEditorToolAsync(
                $"window.editorTools.numberLines({JsString(GetEditorResourceText(placeholderResourceKey))});");
        }

        private async System.Threading.Tasks.Task InsertEditorBlockAsync(string before, string after, string placeholderResourceKey)
        {
            await ExecuteEditorToolAsync(
                $"window.editorTools.insertBlock({JsString(before)}, {JsString(after)}, {JsString(GetEditorResourceText(placeholderResourceKey))});");
        }

        private async System.Threading.Tasks.Task InsertEditorLineAsync(string text)
        {
            await ExecuteEditorToolAsync($"window.editorTools.insertLine({JsString(text)});");
        }

        private string BuildMarkdownTableTemplate()
        {
            return
                $"| {GetEditorResourceText("EditorFormatTableColumn1")} | {GetEditorResourceText("EditorFormatTableColumn2")} |\n" +
                $"| --- | --- |\n" +
                $"| {GetEditorResourceText("EditorFormatTableValue1")} | {GetEditorResourceText("EditorFormatTableValue2")} |";
        }

        private async System.Threading.Tasks.Task ExecuteToolbarCommandAsync(string command)
        {
            switch (command)
            {
                case "h1":
                    await PrefixEditorLinesAsync("# ", "EditorFormatHeading1Placeholder");
                    break;
                case "h2":
                    await PrefixEditorLinesAsync("## ", "EditorFormatHeading2Placeholder");
                    break;
                case "h3":
                    await PrefixEditorLinesAsync("### ", "EditorFormatHeading3Placeholder");
                    break;
                case "h4":
                    await PrefixEditorLinesAsync("#### ", "EditorFormatHeading4Placeholder");
                    break;
                case "bold":
                    await WrapEditorSelectionAsync("**", "**", "EditorFormatTextPlaceholder");
                    break;
                case "italic":
                    await WrapEditorSelectionAsync("*", "*", "EditorFormatTextPlaceholder");
                    break;
                case "strike":
                    await WrapEditorSelectionAsync("~~", "~~", "EditorFormatTextPlaceholder");
                    break;
                case "code-inline":
                    await WrapEditorSelectionAsync("`", "`", "EditorFormatCodePlaceholder");
                    break;
                case "code-block":
                    await InsertEditorBlockAsync("```\n", "\n```", "EditorFormatCodePlaceholder");
                    break;
                case "quote":
                    await PrefixEditorLinesAsync("> ", "EditorFormatQuotePlaceholder");
                    break;
                case "bullet-list":
                    await PrefixEditorLinesAsync("- ", "EditorFormatListPlaceholder");
                    break;
                case "ordered-list":
                    await NumberEditorLinesAsync("EditorFormatListPlaceholder");
                    break;
                case "task-list":
                    await PrefixEditorLinesAsync("- [ ] ", "EditorFormatTaskPlaceholder");
                    break;
                case "link":
                case "insert-link":
                    await InsertLinkAsync();
                    break;
                case "insert-image":
                    await InsertImageAsync();
                    break;
                case "table":
                    await InsertEditorLineAsync(BuildMarkdownTableTemplate());
                    break;
                case "divider":
                    await InsertEditorLineAsync("---");
                    break;
                case "sync-to-preview":
                    await SyncEditorToPreviewAsync();
                    break;
                case "sync-to-editor":
                    await SyncPreviewToEditorAsync();
                    break;
            }
        }

        private async void MarkdownTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string command })
            {
                return;
            }

            await ExecuteToolbarCommandAsync(command);
        }

        private static string BuildLinkMarkup(string linkText, string linkUrl, bool openInNewTab)
        {
            var normalizedText = linkText.Trim();
            var normalizedUrl = EscapeMarkdownLinkUrl(linkUrl.Trim());

            if (openInNewTab)
            {
                return $"<a href=\"{WebUtility.HtmlEncode(normalizedUrl)}\" target=\"_blank\" rel=\"noopener noreferrer\">{WebUtility.HtmlEncode(normalizedText)}</a>";
            }

            return $"[{EscapeMarkdownLinkText(normalizedText)}](<{normalizedUrl}>)";
        }

        private static string EscapeMarkdownLinkText(string value)
        {
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("[", "\\[", StringComparison.Ordinal)
                .Replace("]", "\\]", StringComparison.Ordinal);
        }

        private static string EscapeMarkdownLinkUrl(string value)
        {
            return value.Replace(">", "%3E", StringComparison.Ordinal);
        }

        private async System.Threading.Tasks.Task InsertLinkAsync()
        {
            var dialog = new InsertLinkDialog();
            if (Window.GetWindow(this) is Window owner)
            {
                dialog.Owner = owner;
            }

            if (dialog.ShowDialog() == true)
            {
                var markup = BuildLinkMarkup(dialog.LinkText, dialog.LinkUrl, dialog.OpenInNewTab);
                await InsertTextIntoEditorAsync(markup);
            }
        }

        private async void InsertLink_Click(object sender, RoutedEventArgs e)
        {
            await InsertLinkAsync();
        }

        private async System.Threading.Tasks.Task InsertImageAsync()
        {
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                var msg = new Wpf.Ui.Controls.MessageBox { Title = "提示", Content = "请先填写文章标题，以便确定图片存放目录！", CloseButtonText = "确定" };
                await msg.ShowDialogAsync();
                return;
            }

            var filter = $"{Application.Current.FindResource("CommonFilterImages").ToString()!}|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.svg|{Application.Current.FindResource("CommonFilterAllFiles").ToString()!}|*.*";
            var dialog = new OpenFileDialog
            {
                Title = Application.Current.FindResource("EditorMsgImageSelect").ToString()!,
                Filter = filter
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // 1. Determine safe directory name based on article title or filename
                    var safeDirName = System.Text.RegularExpressions.Regex.Replace(TitleBox.Text, @"[\\/:*?""<>|]+", "-").Trim('-', ' ');
                    safeDirName = System.Text.RegularExpressions.Regex.Replace(safeDirName, @"\s+", "-").ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(safeDirName)) safeDirName = "untitled";

                    var relativeDir = $"assets/img/inposts/{safeDirName}";
                    var absPathDir = System.IO.Path.Combine(App.JekyllContext.BlogPath, relativeDir.Replace("/", "\\"));
                    
                    if (!System.IO.Directory.Exists(absPathDir))
                    {
                        System.IO.Directory.CreateDirectory(absPathDir);
                    }

                    // 2. Copy file
                    var fileName = System.IO.Path.GetFileName(dialog.FileName);
                    var destFile = System.IO.Path.Combine(absPathDir, fileName);
                    
                    // Add suffix if file exists to prevent overwrite
                    int counter = 1;
                    while (System.IO.File.Exists(destFile))
                    {
                        var nameOnly = System.IO.Path.GetFileNameWithoutExtension(fileName);
                        var ext = System.IO.Path.GetExtension(fileName);
                        destFile = System.IO.Path.Combine(absPathDir, $"{nameOnly}-{counter}{ext}");
                        fileName = System.IO.Path.GetFileName(destFile);
                        counter++;
                    }
                    
                    System.IO.File.Copy(dialog.FileName, destFile);

                    // 3. Inject MD into Editor at cursor
                    var mdSyntax = $"![{System.IO.Path.GetFileNameWithoutExtension(fileName)}](/{relativeDir}/{fileName})";
                    await InsertTextIntoEditorAsync(mdSyntax);
                }
                catch (Exception ex)
                {
                    var msg = new Wpf.Ui.Controls.MessageBox 
                    { 
                        Title = Application.Current.FindResource("CommonError").ToString()!, 
                        Content = string.Format(Application.Current.FindResource("EditorMsgInsertImageError").ToString()!, ex.Message), 
                        CloseButtonText = Application.Current.FindResource("CommonConfirm").ToString()! 
                    };
                    await msg.ShowDialogAsync();
                }
            }
        }

        private async void InsertImage_Click(object sender, RoutedEventArgs e)
        {
            await InsertImageAsync();
        }

        private async System.Threading.Tasks.Task SyncEditorToPreviewAsync()
        {
            if (!_isWebViewReady || PreviewWebView.CoreWebView2 == null || EditorWebView.CoreWebView2 == null) return;

            var getRatioScript = @"
                (function() {
                    var el = document.getElementById('editor');
                    if (!el) return 0;
                    var maxScroll = el.scrollHeight - el.clientHeight;
                    if (maxScroll <= 0) return 0;
                    return el.scrollTop / maxScroll;
                })();
            ";
            var result = await EditorWebView.ExecuteScriptAsync(getRatioScript);
            
            if (double.TryParse(result, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double ratio))
            {
                ratio = Math.Clamp(ratio, 0, 1);
                var script = $@"
                    (function() {{
                        var el = document.getElementById('content');
                        if (!el) return;
                        var maxScroll = el.scrollHeight - el.clientHeight;
                        if (maxScroll > 0) {{
                            el.scrollTop = maxScroll * {ratio.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                        }}
                    }})();
                ";
                await PreviewWebView.ExecuteScriptAsync(script);
            }
        }

        private async void SyncEditorToPreview_Click(object sender, RoutedEventArgs e)
        {
            await SyncEditorToPreviewAsync();
        }

        private async System.Threading.Tasks.Task SyncPreviewToEditorAsync()
        {
            if (!_isWebViewReady || PreviewWebView.CoreWebView2 == null || EditorWebView.CoreWebView2 == null) return;

            var result = await PreviewWebView.ExecuteScriptAsync(@"
                (function() {
                    var el = document.getElementById('content');
                    if (!el) return 0;
                    var maxScroll = el.scrollHeight - el.clientHeight;
                    if (maxScroll <= 0) return 0;
                    return el.scrollTop / maxScroll;
                })();
            ");

            if (double.TryParse(result, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double ratio))
            {
                ratio = Math.Clamp(ratio, 0, 1);
                var script = $@"
                    (function() {{
                        var el = document.getElementById('editor');
                        if (!el) return;
                        var maxScroll = el.scrollHeight - el.clientHeight;
                        if (maxScroll > 0) {{
                            el.scrollTop = maxScroll * {ratio.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                        }}
                    }})();
                ";
                await EditorWebView.ExecuteScriptAsync(script);
            }
        }

        private async void SyncPreviewToEditor_Click(object sender, RoutedEventArgs e)
        {
            await SyncPreviewToEditorAsync();
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindVisualParent<T>(parentObject);
        }
        private void ParseTagsInput()
        {
            var text = TagInputBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            var delimiters = new[] { ',', '，', '.', '。', ';', '；', '、' };
            var tokens = text.Split(delimiters, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0);
            foreach (var token in tokens)
            {
                if (!_tagsList.Contains(token))
                {
                    _tagsList.Add(token);
                }
            }
            TagInputBox.Text = "";
        }

        private void TagInputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                ParseTagsInput();
                DismissTagInput();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                TagInputBox.Clear();
                DismissTagInput();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Back && string.IsNullOrEmpty(TagInputBox.Text) && _tagsList.Count > 0)
            {
                _tagsList.RemoveAt(_tagsList.Count - 1);
            }
        }

        private void DismissTagInput()
        {
            if (RootGrid == null)
            {
                Keyboard.ClearFocus();
                return;
            }

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    FocusManager.SetFocusedElement(FocusManager.GetFocusScope(RootGrid), RootGrid);
                    Keyboard.Focus(RootGrid);
                }),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        private void TagInputBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ParseTagsInput();
        }

        private void TagsBorder_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            TagInputBox.Focus();
        }

        private void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tag)
            {
                _tagsList.Remove(tag);
            }
        }

        /// <summary>
        /// 从 EmbeddedResource 中提取 KaTeX 文件到指定目录。
        /// 使用版本标记文件避免重复解压。
        /// </summary>
        private static void ExtractKatexResources(string targetDir)
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0";
            var stampFile = System.IO.Path.Combine(targetDir, ".version");

            // 如果版本号匹配，跳过提取
            if (System.IO.File.Exists(stampFile) && System.IO.File.ReadAllText(stampFile).Trim() == version)
                return;

            System.IO.Directory.CreateDirectory(targetDir);

            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var prefix = "katex/";
            foreach (var resName in assembly.GetManifestResourceNames())
            {
                if (!resName.StartsWith(prefix)) continue;

                // resName 格式: "katex/fonts/xxx.woff2" 或 "katex/katex.min.css"
                var relativePath = resName.Substring(prefix.Length);
                var destPath = System.IO.Path.Combine(targetDir, relativePath.Replace("/", "\\"));
                var destDir = System.IO.Path.GetDirectoryName(destPath);
                if (destDir != null) System.IO.Directory.CreateDirectory(destDir);

                using var stream = assembly.GetManifestResourceStream(resName);
                if (stream == null) continue;
                using var fs = System.IO.File.Create(destPath);
                stream.CopyTo(fs);
            }

            System.IO.File.WriteAllText(stampFile, version);
        }

        private sealed class GridLengthAnimation : AnimationTimeline
        {
            public override Type TargetPropertyType => typeof(GridLength);

            public GridLength? From
            {
                get => (GridLength?)GetValue(FromProperty);
                set => SetValue(FromProperty, value);
            }

            public static readonly DependencyProperty FromProperty =
                DependencyProperty.Register(nameof(From), typeof(GridLength?), typeof(GridLengthAnimation));

            public GridLength? To
            {
                get => (GridLength?)GetValue(ToProperty);
                set => SetValue(ToProperty, value);
            }

            public static readonly DependencyProperty ToProperty =
                DependencyProperty.Register(nameof(To), typeof(GridLength?), typeof(GridLengthAnimation));

            public IEasingFunction? EasingFunction
            {
                get => (IEasingFunction?)GetValue(EasingFunctionProperty);
                set => SetValue(EasingFunctionProperty, value);
            }

            public static readonly DependencyProperty EasingFunctionProperty =
                DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(GridLengthAnimation));

            public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
            {
                double from = (From ?? (GridLength)defaultOriginValue).Value;
                double to = (To ?? (GridLength)defaultDestinationValue).Value;
                double progress = animationClock.CurrentProgress ?? 0.0;

                if (EasingFunction != null)
                {
                    progress = EasingFunction.Ease(progress);
                }

                double current = from + ((to - from) * progress);
                return new GridLength(current, GridUnitType.Pixel);
            }

            protected override Freezable CreateInstanceCore()
            {
                return new GridLengthAnimation();
            }
        }
    }
}
