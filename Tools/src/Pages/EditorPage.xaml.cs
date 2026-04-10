using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BlogTools.Models;
using BlogTools.Services;
using Markdig;
using Microsoft.Web.WebView2.Core;
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
        private static readonly Duration FindReplaceAnimationDuration = new(TimeSpan.FromMilliseconds(190));
        private static readonly Duration PreviewChromeAnimationDuration = new(TimeSpan.FromMilliseconds(180));
        private static readonly IEasingFunction PanelEase = new CubicEase { EasingMode = EasingMode.EaseOut };
        private static readonly IEasingFunction ViewModeEase = new QuinticEase { EasingMode = EasingMode.EaseInOut };
        private static readonly FieldInfo? WebView2BaseField =
            typeof(Microsoft.Web.WebView2.Wpf.WebView2).GetField("m_webview2Base", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo? CoreWebView2ControllerProperty =
            WebView2BaseField?.FieldType.GetProperty("CoreWebView2Controller", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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
        private const double FindReplaceHiddenScale = 0.97;
        private const double FindReplaceHiddenOffset = -18.0;
        private const double PreviewChromeHiddenOffset = -10.0;

        private readonly record struct ToolDropPlacement(int Index, Point Position, double Length);
        private readonly record struct EditorViewModeWidths(double Editor, double Side, double Preview);
        private readonly record struct EditorFindResult(bool Found, int Count, int Index, int Length, int ReplacedCount);
        private readonly record struct EditorSelectionState(bool HasSelection, bool HasText, int Start, int End);

        private MarkdownPipeline _pipeline;
        private bool _isWebViewReady = false;
        private bool _isToolboxCollapsed = false;
        private string _currentContent = "";
        private string _originalState = "";
        private string _currentImageFolderSlug = "";
        private ScrollViewer? _parentSv;
        private readonly ObservableCollection<string> _tagsList = new();
        private readonly ObservableCollection<EditorToolViewItem> _ribbonTools = new();
        private readonly ObservableCollection<EditorToolViewItem> _sideTools = new();
        private readonly ObservableCollection<EditorToolViewItem> _writeOnlyRibbonTools = new();
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
        private bool _isFindReplaceOpen;
        private bool _suppressFindQueryRefresh;
        private bool _isPageActive;
        private bool _isWebViewInitializing;
        private bool _isRecoveringWebViews;
        private bool _hasWebViewFaulted;
        private bool _pendingEditorContentSync;
        private bool _pendingPreviewRefresh;
        private bool _isPreviewBrowsing;
        private bool _previewChromeAnimatedIn;
        private string _previewShellHtml = string.Empty;
        private CoreWebView2Controller? _editorWebViewController;

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
            _ = InitializeWebViewsAsync();
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

        private static string BuildImageFolderSlug(string? title)
        {
            var safeDirName = System.Text.RegularExpressions.Regex.Replace(title ?? string.Empty, @"[\\/:*?""<>|]+", "-").Trim('-', ' ');
            safeDirName = System.Text.RegularExpressions.Regex.Replace(safeDirName, @"\s+", "-").ToLowerInvariant();
            return string.IsNullOrWhiteSpace(safeDirName) ? "untitled" : safeDirName;
        }

        private static string BuildImageRelativeDirectory(string slug)
        {
            return $"assets/img/inposts/{slug}";
        }

        private static string BuildImageReferencePrefix(string slug, bool withLeadingSlash)
        {
            return $"{(withLeadingSlash ? "/" : string.Empty)}assets/img/inposts/{slug}/";
        }

        private static string? DetectImageFolderSlug(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var normalized = text.Replace('\\', '/');
            const string marker = "assets/img/inposts/";
            var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return null;
            }

            var slugStart = markerIndex + marker.Length;
            var slugEnd = normalized.IndexOf('/', slugStart);
            if (slugEnd <= slugStart)
            {
                return null;
            }

            return normalized.Substring(slugStart, slugEnd - slugStart);
        }

        private string? ResolveTrackedImageFolderSlug()
        {
            if (!string.IsNullOrWhiteSpace(_currentImageFolderSlug))
            {
                return _currentImageFolderSlug;
            }

            var detected = DetectImageFolderSlug(ImageBox?.Text) ?? DetectImageFolderSlug(_currentContent);
            if (!string.IsNullOrWhiteSpace(detected))
            {
                _currentImageFolderSlug = detected;
                return detected;
            }

            if (App.CurrentEditPost != null)
            {
                var fallback = BuildImageFolderSlug(App.CurrentEditPost.Title);
                var fallbackDirectory = GetImageDirectoryPath(fallback);
                if (System.IO.Directory.Exists(fallbackDirectory))
                {
                    _currentImageFolderSlug = fallback;
                    return fallback;
                }
            }

            return null;
        }

        private string GetImageDirectoryPath(string slug)
        {
            var relativeDirectory = BuildImageRelativeDirectory(slug);
            return System.IO.Path.Combine(App.JekyllContext.BlogPath, relativeDirectory.Replace("/", "\\"));
        }

        private static string GetUniqueFilePath(string targetPath)
        {
            var directory = System.IO.Path.GetDirectoryName(targetPath) ?? string.Empty;
            var name = System.IO.Path.GetFileNameWithoutExtension(targetPath);
            var extension = System.IO.Path.GetExtension(targetPath);
            var counter = 1;
            var candidate = targetPath;

            while (System.IO.File.Exists(candidate))
            {
                candidate = System.IO.Path.Combine(directory, $"{name}-{counter}{extension}");
                counter++;
            }

            return candidate;
        }

        private static void DeleteEmptyDirectoryTree(string directoryPath)
        {
            if (!System.IO.Directory.Exists(directoryPath))
            {
                return;
            }

            foreach (var subDirectory in System.IO.Directory.GetDirectories(directoryPath))
            {
                DeleteEmptyDirectoryTree(subDirectory);
            }

            if (!System.IO.Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                System.IO.Directory.Delete(directoryPath, false);
            }
        }

        private static string ApplyOrderedReplacements(string input, IEnumerable<(string OldValue, string NewValue)> replacements)
        {
            var result = input ?? string.Empty;

            foreach (var replacement in replacements.OrderByDescending(item => item.OldValue.Length))
            {
                if (string.IsNullOrEmpty(replacement.OldValue) || replacement.OldValue == replacement.NewValue)
                {
                    continue;
                }

                result = result.Replace(replacement.OldValue, replacement.NewValue, StringComparison.OrdinalIgnoreCase);
            }

            return result;
        }

        private void SyncEditorAndPreviewContent()
        {
            TryPostEditorContent(_currentContent);
            _ = UpdateWebViewContentAsync();
        }

        private void AttachWebViewHandlers()
        {
            PreviewWebView.NavigationStarting -= PreviewWebView_NavigationStarting;
            PreviewWebView.NavigationStarting += PreviewWebView_NavigationStarting;
            PreviewWebView.NavigationCompleted -= PreviewWebView_NavigationCompleted;
            PreviewWebView.NavigationCompleted += PreviewWebView_NavigationCompleted;

            EditorWebView.PreviewKeyDown -= EditorWebView_PreviewKeyDown;
            AttachEditorAcceleratorKeyHandler();
            if (_editorWebViewController == null)
            {
                EditorWebView.PreviewKeyDown += EditorWebView_PreviewKeyDown;
            }
            EditorWebView.NavigationCompleted -= EditorWebView_NavigationCompleted;
            EditorWebView.NavigationCompleted += EditorWebView_NavigationCompleted;

            EditorWebView.WebMessageReceived -= EditorWebView_WebMessageReceived;
            EditorWebView.WebMessageReceived += EditorWebView_WebMessageReceived;

            if (PreviewWebView.CoreWebView2 != null)
            {
                PreviewWebView.CoreWebView2.ProcessFailed -= WebView_ProcessFailed;
                PreviewWebView.CoreWebView2.ProcessFailed += WebView_ProcessFailed;
                PreviewWebView.CoreWebView2.HistoryChanged -= PreviewWebView_HistoryChanged;
                PreviewWebView.CoreWebView2.HistoryChanged += PreviewWebView_HistoryChanged;
                PreviewWebView.CoreWebView2.NewWindowRequested -= PreviewWebView_NewWindowRequested;
                PreviewWebView.CoreWebView2.NewWindowRequested += PreviewWebView_NewWindowRequested;
            }

            if (EditorWebView.CoreWebView2 != null)
            {
                EditorWebView.CoreWebView2.ProcessFailed -= WebView_ProcessFailed;
                EditorWebView.CoreWebView2.ProcessFailed += WebView_ProcessFailed;
            }
        }

        private void DetachWebViewHandlers()
        {
            PreviewWebView.NavigationStarting -= PreviewWebView_NavigationStarting;
            PreviewWebView.NavigationCompleted -= PreviewWebView_NavigationCompleted;
            EditorWebView.PreviewKeyDown -= EditorWebView_PreviewKeyDown;
            DetachEditorAcceleratorKeyHandler();
            EditorWebView.NavigationCompleted -= EditorWebView_NavigationCompleted;
            EditorWebView.WebMessageReceived -= EditorWebView_WebMessageReceived;

            if (PreviewWebView.CoreWebView2 != null)
            {
                PreviewWebView.CoreWebView2.ProcessFailed -= WebView_ProcessFailed;
                PreviewWebView.CoreWebView2.HistoryChanged -= PreviewWebView_HistoryChanged;
                PreviewWebView.CoreWebView2.NewWindowRequested -= PreviewWebView_NewWindowRequested;
            }

            if (EditorWebView.CoreWebView2 != null)
            {
                EditorWebView.CoreWebView2.ProcessFailed -= WebView_ProcessFailed;
            }
        }

        private void PreviewWebView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Uri) &&
                !string.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                _isPreviewBrowsing = true;
                UpdatePreviewNavigationUi();
            }
        }

        private async void PreviewWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                HandleRecoverableWebViewException(
                    new InvalidOperationException($"Preview navigation failed: {e.WebErrorStatus}."),
                    "preview navigation");
                return;
            }

            _isWebViewReady = PreviewWebView.CoreWebView2 != null;
            _hasWebViewFaulted = false;
            await UpdatePreviewNavigationStateAsync();

            if (!_isPreviewBrowsing && (_pendingPreviewRefresh || !string.IsNullOrEmpty(_currentContent)))
            {
                _ = UpdateWebViewContentAsync();
            }
        }

        private void EditorWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                HandleRecoverableWebViewException(
                    new InvalidOperationException($"Editor navigation failed: {e.WebErrorStatus}."),
                    "editor navigation");
                return;
            }

            _hasWebViewFaulted = false;

            if (_pendingEditorContentSync || !string.IsNullOrEmpty(_currentContent))
            {
                TryPostEditorContent(_currentContent);
            }
        }

        private void AttachEditorAcceleratorKeyHandler()
        {
            DetachEditorAcceleratorKeyHandler();

            var controller = GetWebViewController(EditorWebView);
            if (controller == null)
            {
                return;
            }

            controller.AcceleratorKeyPressed -= EditorWebView_AcceleratorKeyPressed;
            controller.AcceleratorKeyPressed += EditorWebView_AcceleratorKeyPressed;
            _editorWebViewController = controller;
        }

        private void DetachEditorAcceleratorKeyHandler()
        {
            if (_editorWebViewController == null)
            {
                return;
            }

            _editorWebViewController.AcceleratorKeyPressed -= EditorWebView_AcceleratorKeyPressed;
            _editorWebViewController = null;
        }

        private static CoreWebView2Controller? GetWebViewController(Microsoft.Web.WebView2.Wpf.WebView2? webView)
        {
            if (webView == null || WebView2BaseField == null || CoreWebView2ControllerProperty == null)
            {
                return null;
            }

            var webViewBase = WebView2BaseField.GetValue(webView);
            if (webViewBase == null)
            {
                return null;
            }

            return CoreWebView2ControllerProperty.GetValue(webViewBase) as CoreWebView2Controller;
        }

        private void EditorWebView_AcceleratorKeyPressed(object? sender, CoreWebView2AcceleratorKeyPressedEventArgs e)
        {
            if (_isFindReplaceOpen ||
                (e.KeyEventKind != CoreWebView2KeyEventKind.KeyDown && e.KeyEventKind != CoreWebView2KeyEventKind.SystemKeyDown))
            {
                return;
            }

            var modifiers = GetCurrentEditorModifierKeys();
            if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
            {
                return;
            }

            int? horizontalDelta = e.VirtualKey switch
            {
                0x25 => -1,
                0x27 => 1,
                _ => null
            };

            int? verticalDelta = e.VirtualKey switch
            {
                0x26 => -1,
                0x28 => 1,
                _ => null
            };

            if (horizontalDelta is null && verticalDelta is null)
            {
                return;
            }

            e.Handled = true;
            ScheduleEditorDirectionalMove(horizontalDelta, verticalDelta, (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
        }

        private void EditorWebView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not Microsoft.Web.WebView2.Wpf.WebView2 || _isFindReplaceOpen)
            {
                return;
            }

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var modifiers = Keyboard.Modifiers;

            if ((modifiers & ModifierKeys.Control) != 0 ||
                (modifiers & ModifierKeys.Alt) != 0 ||
                (modifiers & ModifierKeys.Windows) != 0)
            {
                return;
            }

            int? horizontalDelta = key switch
            {
                Key.Left => -1,
                Key.Right => 1,
                _ => null
            };

            int? verticalDelta = key switch
            {
                Key.Up => -1,
                Key.Down => 1,
                _ => null
            };

            if (horizontalDelta is null && verticalDelta is null)
            {
                return;
            }

            bool extendSelection = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            e.Handled = true;
            ScheduleEditorDirectionalMove(horizontalDelta, verticalDelta, extendSelection);
        }

        private void ScheduleEditorDirectionalMove(int? horizontalDelta, int? verticalDelta, bool extendSelection)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (horizontalDelta is int dx)
                    {
                        _ = MoveEditorCaretHorizontalAsync(dx, extendSelection);
                        return;
                    }

                    if (verticalDelta is int dy)
                    {
                        _ = MoveEditorCaretVerticalAsync(dy, extendSelection);
                    }
                }),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        private static ModifierKeys GetCurrentEditorModifierKeys()
        {
            ModifierKeys modifiers = ModifierKeys.None;

            if (IsVirtualKeyPressed(0x10))
            {
                modifiers |= ModifierKeys.Shift;
            }

            if (IsVirtualKeyPressed(0x11))
            {
                modifiers |= ModifierKeys.Control;
            }

            if (IsVirtualKeyPressed(0x12))
            {
                modifiers |= ModifierKeys.Alt;
            }

            if (IsVirtualKeyPressed(0x5B) || IsVirtualKeyPressed(0x5C))
            {
                modifiers |= ModifierKeys.Windows;
            }

            return modifiers;
        }

        private static bool IsVirtualKeyPressed(int virtualKey)
        {
            return (GetKeyState(virtualKey) & 0x8000) != 0;
        }

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int virtualKey);

        private void WebView_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        {
            HandleRecoverableWebViewException(
                new InvalidOperationException($"WebView2 process failed: {e.ProcessFailedKind}."),
                "webview process");
        }

        private void PreviewWebView_HistoryChanged(object? sender, object e)
        {
            UpdatePreviewNavigationUi();
        }

        private void PreviewWebView_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            if (PreviewWebView.CoreWebView2 != null && !string.IsNullOrWhiteSpace(e.Uri))
            {
                _isPreviewBrowsing = true;
                PreviewWebView.CoreWebView2.Navigate(e.Uri);
                UpdatePreviewNavigationUi();
            }
        }

        private void HandleRecoverableWebViewException(Exception ex, string operation)
        {
            _isWebViewReady = false;
            _pendingEditorContentSync = true;
            _pendingPreviewRefresh = true;
            System.Diagnostics.Debug.WriteLine($"[EditorPage] {operation} failed: {ex}");

            if (!_hasWebViewFaulted && IsLoaded)
            {
                _hasWebViewFaulted = true;
                ShowInfo(GetEditorResourceText("EditorMsgWebViewRecovering"), InfoBarSeverity.Warning);
            }

            if (_isPageActive && !_isRecoveringWebViews)
            {
                _ = RecoverWebViewsAsync();
            }
        }

        private async System.Threading.Tasks.Task RecoverWebViewsAsync()
        {
            if (_isRecoveringWebViews)
            {
                return;
            }

            _isRecoveringWebViews = true;

            try
            {
                await System.Threading.Tasks.Task.Delay(250);
                if (_isPageActive)
                {
                    await InitializeWebViewsAsync();
                }
            }
            finally
            {
                _isRecoveringWebViews = false;
            }
        }

        private bool TryPostEditorContent(string content)
        {
            _pendingEditorContentSync = true;

            if (EditorWebView?.CoreWebView2 == null)
            {
                return false;
            }

            try
            {
                EditorWebView.CoreWebView2.PostWebMessageAsString(content);
                _pendingEditorContentSync = false;
                _hasWebViewFaulted = false;
                return true;
            }
            catch (Exception ex)
            {
                HandleRecoverableWebViewException(ex, "editor content sync");
                return false;
            }
        }

        private async System.Threading.Tasks.Task<bool> TryExecuteEditorScriptAsync(string script, string operation)
        {
            if (EditorWebView.CoreWebView2 == null)
            {
                _pendingEditorContentSync = true;
                return false;
            }

            try
            {
                await EditorWebView.ExecuteScriptAsync(script);
                _hasWebViewFaulted = false;
                return true;
            }
            catch (Exception ex)
            {
                HandleRecoverableWebViewException(ex, operation);
                return false;
            }
        }

        private async System.Threading.Tasks.Task<string> TryExecuteEditorScriptWithResultAsync(string script, string operation)
        {
            if (EditorWebView.CoreWebView2 == null)
            {
                _pendingEditorContentSync = true;
                return "null";
            }

            try
            {
                var result = await EditorWebView.ExecuteScriptAsync(script);
                _hasWebViewFaulted = false;
                return result;
            }
            catch (Exception ex)
            {
                HandleRecoverableWebViewException(ex, operation);
                return "null";
            }
        }

        private async System.Threading.Tasks.Task<bool> TryExecutePreviewScriptAsync(string script, string operation)
        {
            _pendingPreviewRefresh = true;

            if (!_isWebViewReady || PreviewWebView.CoreWebView2 == null)
            {
                return false;
            }

            try
            {
                await PreviewWebView.ExecuteScriptAsync(script);
                _pendingPreviewRefresh = false;
                _hasWebViewFaulted = false;
                return true;
            }
            catch (Exception ex)
            {
                HandleRecoverableWebViewException(ex, operation);
                return false;
            }
        }

        private async System.Threading.Tasks.Task<string> TryExecutePreviewScriptWithResultAsync(string script, string operation)
        {
            _pendingPreviewRefresh = true;

            if (!_isWebViewReady || PreviewWebView.CoreWebView2 == null)
            {
                return "null";
            }

            try
            {
                var result = await PreviewWebView.ExecuteScriptAsync(script);
                _pendingPreviewRefresh = false;
                _hasWebViewFaulted = false;
                return result;
            }
            catch (Exception ex)
            {
                HandleRecoverableWebViewException(ex, operation);
                return "null";
            }
        }

        private async System.Threading.Tasks.Task SyncArticleImagePathsWithCurrentTitleAsync()
        {
            if (string.IsNullOrWhiteSpace(TitleBox.Text) || string.IsNullOrWhiteSpace(App.JekyllContext.BlogPath))
            {
                return;
            }

            var trackedSlug = ResolveTrackedImageFolderSlug();
            if (string.IsNullOrWhiteSpace(trackedSlug))
            {
                return;
            }

            var desiredSlug = BuildImageFolderSlug(TitleBox.Text);
            if (string.Equals(trackedSlug, desiredSlug, StringComparison.OrdinalIgnoreCase))
            {
                _currentImageFolderSlug = desiredSlug;
                return;
            }

            var oldDirectory = GetImageDirectoryPath(trackedSlug);
            var newDirectory = GetImageDirectoryPath(desiredSlug);
            var replacements = new List<(string OldValue, string NewValue)>();
            var oldPrefix = BuildImageReferencePrefix(trackedSlug, withLeadingSlash: true);
            var newPrefix = BuildImageReferencePrefix(desiredSlug, withLeadingSlash: true);
            var oldPrefixWithoutSlash = BuildImageReferencePrefix(trackedSlug, withLeadingSlash: false);
            var newPrefixWithoutSlash = BuildImageReferencePrefix(desiredSlug, withLeadingSlash: false);

            if (System.IO.Directory.Exists(oldDirectory))
            {
                var newParent = System.IO.Path.GetDirectoryName(newDirectory);
                if (!string.IsNullOrWhiteSpace(newParent))
                {
                    System.IO.Directory.CreateDirectory(newParent);
                }

                if (!System.IO.Directory.Exists(newDirectory))
                {
                    System.IO.Directory.Move(oldDirectory, newDirectory);
                }
                else
                {
                    foreach (var sourceFile in System.IO.Directory.GetFiles(oldDirectory, "*", System.IO.SearchOption.AllDirectories))
                    {
                        var oldRelativePath = System.IO.Path.GetRelativePath(oldDirectory, sourceFile).Replace("\\", "/");
                        var targetFilePath = System.IO.Path.Combine(newDirectory, oldRelativePath.Replace("/", "\\"));
                        var targetDirectory = System.IO.Path.GetDirectoryName(targetFilePath);
                        if (!string.IsNullOrWhiteSpace(targetDirectory))
                        {
                            System.IO.Directory.CreateDirectory(targetDirectory);
                        }

                        if (System.IO.File.Exists(targetFilePath))
                        {
                            targetFilePath = GetUniqueFilePath(targetFilePath);
                        }

                        System.IO.File.Move(sourceFile, targetFilePath);

                        var targetRelativePath = System.IO.Path.GetRelativePath(newDirectory, targetFilePath).Replace("\\", "/");
                        replacements.Add(($"{oldPrefix}{oldRelativePath}", $"{newPrefix}{targetRelativePath}"));
                        replacements.Add(($"{oldPrefixWithoutSlash}{oldRelativePath}", $"{newPrefixWithoutSlash}{targetRelativePath}"));
                    }

                    DeleteEmptyDirectoryTree(oldDirectory);
                }
            }

            replacements.Add((oldPrefix, newPrefix));
            replacements.Add((oldPrefixWithoutSlash, newPrefixWithoutSlash));

            var updatedContent = ApplyOrderedReplacements(_currentContent, replacements);
            var updatedImage = ApplyOrderedReplacements(ImageBox.Text ?? string.Empty, replacements);
            var contentChanged = !string.Equals(updatedContent, _currentContent, StringComparison.Ordinal);
            var imageChanged = !string.Equals(updatedImage, ImageBox.Text ?? string.Empty, StringComparison.Ordinal);

            if (contentChanged)
            {
                _currentContent = updatedContent;
                SyncEditorAndPreviewContent();
            }

            if (imageChanged)
            {
                ImageBox.Text = updatedImage;
            }

            _currentImageFolderSlug = desiredSlug;

            if (_isFindReplaceOpen)
            {
                await RefreshFindReplaceStatusAsync();
            }
        }

        private void SetFindReplaceStatus(string resourceKey, params object[] args)
        {
            if (FindStatusText == null)
            {
                return;
            }

            var template = Application.Current.FindResource(resourceKey).ToString() ?? string.Empty;
            FindStatusText.Text = args.Length > 0 ? string.Format(template, args) : template;
        }

        private async System.Threading.Tasks.Task UpdatePreviewNavigationStateAsync()
        {
            if (!_isWebViewReady || PreviewWebView.CoreWebView2 == null)
            {
                return;
            }

            var result = await TryExecutePreviewScriptWithResultAsync(
                "(function(){ return document.documentElement && document.documentElement.getAttribute('data-blogtools-preview') === 'true'; })();",
                "detect preview shell");

            _isPreviewBrowsing = !string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
            UpdatePreviewNavigationUi();
        }

        private void UpdatePreviewNavigationUi()
        {
            if (PreviewBackButton != null)
            {
                PreviewBackButton.IsEnabled = PreviewWebView?.CoreWebView2?.CanGoBack == true;
            }

            if (PreviewForwardButton != null)
            {
                PreviewForwardButton.IsEnabled = PreviewWebView?.CoreWebView2?.CanGoForward == true;
            }

            if (PreviewHomeButton != null)
            {
                PreviewHomeButton.IsEnabled = _isPreviewBrowsing || _pendingPreviewRefresh;
            }

            AnimatePreviewNavigationChrome(show: true);
        }

        private void AnimatePreviewNavigationChrome(bool show)
        {
            if (PreviewNavigationChrome == null || PreviewNavigationTranslateTransform == null)
            {
                return;
            }

            PreviewNavigationChrome.BeginAnimation(UIElement.OpacityProperty, null);
            PreviewNavigationTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);

            if (show)
            {
                if (!_previewChromeAnimatedIn)
                {
                    PreviewNavigationChrome.Opacity = 0.0;
                    PreviewNavigationTranslateTransform.Y = PreviewChromeHiddenOffset;
                    PreviewNavigationChrome.BeginAnimation(
                        UIElement.OpacityProperty,
                        new DoubleAnimation(1.0, PreviewChromeAnimationDuration) { EasingFunction = PanelEase });
                    PreviewNavigationTranslateTransform.BeginAnimation(
                        TranslateTransform.YProperty,
                        new DoubleAnimation(0.0, PreviewChromeAnimationDuration) { EasingFunction = PanelEase });
                    _previewChromeAnimatedIn = true;
                    return;
                }

                PreviewNavigationChrome.Opacity = 1.0;
                PreviewNavigationTranslateTransform.Y = 0.0;
                return;
            }

            _previewChromeAnimatedIn = false;
            PreviewNavigationChrome.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0.0, PreviewChromeAnimationDuration) { EasingFunction = PanelEase });
            PreviewNavigationTranslateTransform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(PreviewChromeHiddenOffset, PreviewChromeAnimationDuration) { EasingFunction = PanelEase });
        }

        private async System.Threading.Tasks.Task ShowEditorContextMenuAsync(double x, double y)
        {
            if (EditorContextMenu == null || EditorWebView == null)
            {
                return;
            }

            var selectionState = await GetEditorSelectionStateAsync();
            UpdateEditorContextMenuState(selectionState);

            EditorContextMenu.IsOpen = false;
            EditorContextMenu.PlacementTarget = EditorWebView;
            EditorContextMenu.Placement = PlacementMode.RelativePoint;
            EditorContextMenu.HorizontalOffset = Math.Max(0.0, x);
            EditorContextMenu.VerticalOffset = Math.Max(0.0, y);
            EditorContextMenu.IsOpen = true;
        }

        private void UpdateEditorContextMenuState(EditorSelectionState selectionState)
        {
            bool hasClipboardContent =
                System.Windows.Clipboard.ContainsText() ||
                System.Windows.Clipboard.ContainsImage() ||
                System.Windows.Clipboard.ContainsFileDropList();

            if (EditorUndoMenuItem != null)
            {
                EditorUndoMenuItem.IsEnabled = true;
            }

            if (EditorRedoMenuItem != null)
            {
                EditorRedoMenuItem.IsEnabled = true;
            }

            if (EditorCutMenuItem != null)
            {
                EditorCutMenuItem.IsEnabled = selectionState.HasSelection;
            }

            if (EditorCopyMenuItem != null)
            {
                EditorCopyMenuItem.IsEnabled = selectionState.HasSelection;
            }

            if (EditorPasteMenuItem != null)
            {
                EditorPasteMenuItem.IsEnabled = hasClipboardContent;
            }

            if (EditorDeleteMenuItem != null)
            {
                EditorDeleteMenuItem.IsEnabled = selectionState.HasSelection;
            }

            if (EditorSelectAllMenuItem != null)
            {
                EditorSelectAllMenuItem.IsEnabled = selectionState.HasText;
            }
        }

        private async void EditorContextMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem { Tag: string command })
            {
                return;
            }

            bool returnFocusToEditor = true;

            switch (command)
            {
                case "undo":
                    await UndoEditorAsync();
                    break;
                case "redo":
                    await RedoEditorAsync();
                    break;
                case "cut":
                    await CutSelectedEditorTextAsync();
                    break;
                case "copy":
                    await CopySelectedEditorTextAsync();
                    break;
                case "paste":
                    await PasteIntoEditorAsync();
                    break;
                case "delete":
                    await DeleteEditorSelectionAsync();
                    break;
                case "select-all":
                    await SelectAllEditorAsync();
                    break;
                case "bold":
                case "italic":
                    await ExecuteToolbarCommandAsync(command);
                    break;
                case "insert-link":
                    returnFocusToEditor = false;
                    await InsertLinkAsync();
                    break;
                case "insert-image":
                    returnFocusToEditor = false;
                    await InsertImageAsync();
                    break;
                case "find-replace":
                    returnFocusToEditor = false;
                    await OpenFindReplacePopupAsync(populateFromSelection: true);
                    break;
                default:
                    return;
            }

            if (returnFocusToEditor)
            {
                await FocusEditorAsync();
            }
        }

        private async System.Threading.Tasks.Task RestoreRenderedPreviewAsync()
        {
            if (string.IsNullOrWhiteSpace(_previewShellHtml))
            {
                return;
            }

            _isPreviewBrowsing = false;
            _pendingPreviewRefresh = true;
            PreviewWebView.NavigateToString(_previewShellHtml);
            UpdatePreviewNavigationUi();
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private void PreviewBackButton_Click(object sender, RoutedEventArgs e)
        {
            if (PreviewWebView.CoreWebView2?.CanGoBack == true)
            {
                PreviewWebView.CoreWebView2.GoBack();
            }
        }

        private void PreviewForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (PreviewWebView.CoreWebView2?.CanGoForward == true)
            {
                PreviewWebView.CoreWebView2.GoForward();
            }
        }

        private async void PreviewHomeButton_Click(object sender, RoutedEventArgs e)
        {
            await RestoreRenderedPreviewAsync();
        }

        private void AnimateFindReplacePopup(bool show)
        {
            if (FindReplacePopupHost == null || FindReplacePopup == null || FindReplaceScaleTransform == null || FindReplaceTranslateTransform == null)
            {
                return;
            }

            FindReplacePopup.BeginAnimation(UIElement.OpacityProperty, null);
            FindReplaceScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            FindReplaceScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            FindReplaceTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);

            if (show)
            {
                UpdateFindReplacePopupPosition();
                FindReplacePopupHost.IsOpen = true;
                FindReplacePopup.Opacity = 0.0;
                FindReplaceScaleTransform.ScaleX = FindReplaceHiddenScale;
                FindReplaceScaleTransform.ScaleY = FindReplaceHiddenScale;
                FindReplaceTranslateTransform.Y = FindReplaceHiddenOffset;

                FindReplacePopup.BeginAnimation(
                    UIElement.OpacityProperty,
                    new DoubleAnimation(1.0, FindReplaceAnimationDuration) { EasingFunction = PanelEase });

                FindReplaceScaleTransform.BeginAnimation(
                    ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(1.0, FindReplaceAnimationDuration) { EasingFunction = PanelEase });

                FindReplaceScaleTransform.BeginAnimation(
                    ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(1.0, FindReplaceAnimationDuration) { EasingFunction = PanelEase });

                FindReplaceTranslateTransform.BeginAnimation(
                    TranslateTransform.YProperty,
                    new DoubleAnimation(0.0, FindReplaceAnimationDuration) { EasingFunction = PanelEase });
            }
            else
            {
                var fadeOut = new DoubleAnimation(0.0, FindReplaceAnimationDuration) { EasingFunction = PanelEase };
                fadeOut.Completed += (_, _) =>
                {
                    if (!_isFindReplaceOpen && FindReplacePopupHost != null)
                    {
                        FindReplacePopupHost.IsOpen = false;
                    }
                };

                FindReplacePopup.BeginAnimation(UIElement.OpacityProperty, fadeOut);

                FindReplaceScaleTransform.BeginAnimation(
                    ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(FindReplaceHiddenScale, FindReplaceAnimationDuration) { EasingFunction = PanelEase });

                FindReplaceScaleTransform.BeginAnimation(
                    ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(FindReplaceHiddenScale, FindReplaceAnimationDuration) { EasingFunction = PanelEase });

                FindReplaceTranslateTransform.BeginAnimation(
                    TranslateTransform.YProperty,
                    new DoubleAnimation(FindReplaceHiddenOffset, FindReplaceAnimationDuration) { EasingFunction = PanelEase });
            }
        }

        private void UpdateFindReplacePopupPosition()
        {
            if (FindReplacePopupHost == null || EditorWorkspaceGrid == null)
            {
                return;
            }

            var popupWidth = FindReplacePopup?.ActualWidth > 0
                ? FindReplacePopup.ActualWidth
                : FindReplacePopup?.Width > 0
                    ? FindReplacePopup.Width
                    : 404.0;

            var availableWidth = Math.Max(EditorWorkspaceGrid.ActualWidth, popupWidth + 32.0);
            FindReplacePopupHost.HorizontalOffset = Math.Max(16.0, availableWidth - popupWidth - 16.0);
            FindReplacePopupHost.VerticalOffset = 16.0;
        }

        private async System.Threading.Tasks.Task OpenFindReplacePopupAsync(bool populateFromSelection = true)
        {
            if (!_isFindReplaceOpen)
            {
                _isFindReplaceOpen = true;
                AnimateFindReplacePopup(show: true);
            }

            if (populateFromSelection)
            {
                var selectedText = await GetEditorSelectedTextAsync();
                if (!string.IsNullOrWhiteSpace(selectedText) &&
                    !selectedText.Contains('\r') &&
                    !selectedText.Contains('\n'))
                {
                    _suppressFindQueryRefresh = true;
                    FindQueryBox.Text = selectedText;
                    _suppressFindQueryRefresh = false;
                }
            }

            SetFindReplaceStatus("EditorFindEnterQuery");
            await RefreshFindReplaceStatusAsync();

            _ = Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    FindQueryBox.Focus();
                    FindQueryBox.SelectAll();
                }),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        private void CloseFindReplacePopup(bool returnFocusToEditor)
        {
            if (!_isFindReplaceOpen && FindReplacePopupHost?.IsOpen != true)
            {
                return;
            }

            _isFindReplaceOpen = false;
            AnimateFindReplacePopup(show: false);

            if (returnFocusToEditor)
            {
                _ = FocusEditorAsync();
            }
        }

        private void InitializeEditorTools()
        {
            if (_toolDefinitions.Count == 0)
            {
                RegisterToolDefinitions();
            }

            LoadEditorToolLayout();
            UpdateVisibleToolCollections();
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
            UpdateVisibleToolCollections();
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
            if (IsUsingWriteOnlyMergedRibbonTools())
            {
                return;
            }

            if (sender is FrameworkElement { DataContext: EditorToolViewItem item })
            {
                _draggedTool = item;
                _toolDragStartPoint = e.GetPosition(this);
            }
        }

        private void ToolButton_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (IsUsingWriteOnlyMergedRibbonTools())
            {
                return;
            }

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

        private void UpdateVisibleToolCollections()
        {
            if (RibbonToolsItemsControl == null || SideToolsItemsControl == null)
            {
                return;
            }

            SideToolsItemsControl.ItemsSource = _sideTools;

            if (!IsUsingWriteOnlyMergedRibbonTools())
            {
                RibbonToolsItemsControl.ItemsSource = _ribbonTools;
                RibbonToolsItemsControl.InvalidateMeasure();
                return;
            }

            _writeOnlyRibbonTools.Clear();

            foreach (var item in _ribbonTools)
            {
                _writeOnlyRibbonTools.Add(item);
            }

            var seenIds = _writeOnlyRibbonTools
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var item in _sideTools)
            {
                if (!seenIds.Add(item.Id))
                {
                    continue;
                }

                _writeOnlyRibbonTools.Add(CreateToolViewItem(item.Id, EditorToolHost.Ribbon));
            }

            RibbonToolsItemsControl.ItemsSource = _writeOnlyRibbonTools;
            RibbonToolsItemsControl.InvalidateMeasure();
        }

        private bool IsUsingWriteOnlyMergedRibbonTools()
        {
            return _editorViewMode == EditorViewMode.WriteOnly && _sideTools.Count > 0;
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

        private async System.Threading.Tasks.Task InitializeWebViewsAsync()
        {
            if (_isWebViewInitializing)
            {
                return;
            }

            _isWebViewInitializing = true;
            _pendingEditorContentSync = true;
            _pendingPreviewRefresh = true;
            _isWebViewReady = false;

            try
            {
                var webViewDataDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BlogTools",
                    "WebView2");
                System.IO.Directory.CreateDirectory(webViewDataDir);

                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, webViewDataDir);

                await PreviewWebView.EnsureCoreWebView2Async(env);
                await EditorWebView.EnsureCoreWebView2Async(env);

                PreviewWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                PreviewWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                EditorWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                EditorWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                DetachWebViewHandlers();
                AttachWebViewHandlers();

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
                catch
                {
                }

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

                var initialHtml = "<!DOCTYPE html><html data-blogtools-preview='true'><head><meta charset='utf-8' />"
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
                _previewShellHtml = initialHtml;

                var placeholder = Application.Current.FindResource("EditorPlaceholder").ToString();
                var editorScript = """
                const el = document.getElementById('editor');
                const notifyContent = () => {
                    window.chrome.webview.postMessage('CONTENT:' + el.value);
                };

                const normalizeSearchValue = (value, caseSensitive) =>
                    caseSensitive ? value : value.toLocaleLowerCase();

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

                const getSearchMatches = (query, caseSensitive) => {
                    if (!query) {
                        return [];
                    }

                    const value = el.value || '';
                    const source = normalizeSearchValue(value, caseSensitive);
                    const needle = normalizeSearchValue(query, caseSensitive);
                    const matches = [];
                    let fromIndex = 0;

                    while (fromIndex <= source.length - needle.length) {
                        const matchIndex = source.indexOf(needle, fromIndex);
                        if (matchIndex < 0) {
                            break;
                        }

                        matches.push(matchIndex);
                        fromIndex = matchIndex + Math.max(needle.length, 1);
                    }

                    return matches;
                };

                const isSelectionMatch = (query, caseSensitive) => {
                    if (!query) {
                        return false;
                    }

                    const selectionStart = typeof el.selectionStart === 'number' ? el.selectionStart : 0;
                    const selectionEnd = typeof el.selectionEnd === 'number' ? el.selectionEnd : 0;
                    if (selectionStart === selectionEnd) {
                        return false;
                    }

                    const selected = (el.value || '').slice(selectionStart, selectionEnd);
                    return normalizeSearchValue(selected, caseSensitive) === normalizeSearchValue(query, caseSensitive);
                };

                const revealSelection = (start, length) => {
                    el.focus();
                    el.selectionStart = start;
                    el.selectionEnd = start + length;

                    const lineHeight = parseFloat(window.getComputedStyle(el).lineHeight || '24') || 24;
                    const prefix = (el.value || '').slice(0, start);
                    const lineIndex = prefix.split('\n').length - 1;
                    const targetTop = Math.max(0, (lineIndex * lineHeight) - (el.clientHeight / 3));
                    el.scrollTop = targetTop;
                };

                const buildSearchResult = (matches, foundIndex, length, replacedCount) => ({
                    found: foundIndex >= 0,
                    count: matches.length,
                    index: foundIndex,
                    length,
                    replacedCount
                });

                const getSelectionSnapshot = () => {
                    const start = typeof el.selectionStart === 'number' ? el.selectionStart : 0;
                    const end = typeof el.selectionEnd === 'number' ? el.selectionEnd : 0;
                    const direction = el.selectionDirection === 'backward'
                        ? 'backward'
                        : start === end
                            ? 'none'
                            : 'forward';
                    const anchor = direction === 'backward' ? end : start;
                    const focus = direction === 'backward' ? start : end;
                    return { start, end, anchor, focus, direction };
                };

                const getLineInfo = (value, index) => {
                    const safeIndex = Math.max(0, Math.min((value || '').length, index));
                    const start = (value || '').lastIndexOf('\n', Math.max(0, safeIndex - 1)) + 1;
                    let end = (value || '').indexOf('\n', safeIndex);
                    if (end === -1) {
                        end = (value || '').length;
                    }

                    return { start, end };
                };

                const getPreviousLineInfo = (value, currentLineStart) => {
                    if (currentLineStart <= 0) {
                        return null;
                    }

                    const previousLineEnd = currentLineStart - 1;
                    const previousLineStart = value.lastIndexOf('\n', Math.max(0, previousLineEnd - 1)) + 1;
                    return { start: previousLineStart, end: previousLineEnd };
                };

                const getNextLineInfo = (value, currentLineEnd) => {
                    if (currentLineEnd >= value.length) {
                        return null;
                    }

                    const nextLineStart = currentLineEnd + 1;
                    let nextLineEnd = value.indexOf('\n', nextLineStart);
                    if (nextLineEnd === -1) {
                        nextLineEnd = value.length;
                    }

                    return { start: nextLineStart, end: nextLineEnd };
                };

                const scrollCaretIntoView = index => {
                    const lineHeight = parseFloat(window.getComputedStyle(el).lineHeight || '24') || 24;
                    const prefix = (el.value || '').slice(0, index);
                    const lineIndex = prefix.split('\n').length - 1;
                    const targetTop = Math.max(0, (lineIndex * lineHeight) - (el.clientHeight / 3));
                    el.scrollTop = targetTop;
                };

                const setSelectionFromAnchorFocus = (anchor, focus) => {
                    const value = el.value || '';
                    const boundedAnchor = Math.max(0, Math.min(value.length, anchor));
                    const boundedFocus = Math.max(0, Math.min(value.length, focus));

                    el.focus();

                    if (boundedAnchor === boundedFocus) {
                        el.setSelectionRange(boundedFocus, boundedFocus, 'none');
                        scrollCaretIntoView(boundedFocus);
                        return;
                    }

                    if (boundedFocus < boundedAnchor) {
                        el.setSelectionRange(boundedFocus, boundedAnchor, 'backward');
                    } else {
                        el.setSelectionRange(boundedAnchor, boundedFocus, 'forward');
                    }

                    scrollCaretIntoView(boundedFocus);
                };

                const resetVerticalNavigation = () => {
                    window.editorNavigationDesiredColumn = null;
                    window.editorNavigationLastVerticalDirection = 0;
                };

                const moveCaretHorizontal = (delta, extendSelection) => {
                    const value = el.value || '';
                    const { start, end, anchor, focus } = getSelectionSnapshot();

                    if (!extendSelection && start !== end) {
                        const collapsed = delta < 0 ? start : end;
                        setSelectionFromAnchorFocus(collapsed, collapsed);
                        resetVerticalNavigation();
                        return;
                    }

                    const nextFocus = Math.max(0, Math.min(value.length, focus + delta));
                    const nextAnchor = extendSelection ? anchor : nextFocus;
                    setSelectionFromAnchorFocus(nextAnchor, nextFocus);
                    resetVerticalNavigation();
                };

                const moveCaretVertical = (delta, extendSelection) => {
                    const value = el.value || '';
                    const { anchor, focus } = getSelectionSnapshot();
                    const currentLine = getLineInfo(value, focus);

                    if (typeof window.editorNavigationDesiredColumn !== 'number' ||
                        window.editorNavigationLastVerticalDirection !== delta) {
                        window.editorNavigationDesiredColumn = focus - currentLine.start;
                    }

                    const desiredColumn = window.editorNavigationDesiredColumn;
                    const targetLine = delta < 0
                        ? getPreviousLineInfo(value, currentLine.start)
                        : getNextLineInfo(value, currentLine.end);

                    if (!targetLine) {
                        setSelectionFromAnchorFocus(extendSelection ? anchor : focus, focus);
                        window.editorNavigationLastVerticalDirection = delta;
                        return;
                    }

                    const nextFocus = Math.min(targetLine.end, targetLine.start + desiredColumn);
                    const nextAnchor = extendSelection ? anchor : nextFocus;
                    setSelectionFromAnchorFocus(nextAnchor, nextFocus);
                    window.editorNavigationLastVerticalDirection = delta;
                };

                window.editorTools = {
                    moveCaretHorizontal(delta, extendSelection) {
                        moveCaretHorizontal(delta, !!extendSelection);
                    },

                    moveCaretVertical(delta, extendSelection) {
                        moveCaretVertical(delta, !!extendSelection);
                    },

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
                    },

                    getSelectedText() {
                        const start = typeof el.selectionStart === 'number' ? el.selectionStart : 0;
                        const end = typeof el.selectionEnd === 'number' ? el.selectionEnd : 0;
                        return (el.value || '').slice(start, end);
                    },

                    getSelectionState() {
                        const value = el.value || '';
                        const start = typeof el.selectionStart === 'number' ? el.selectionStart : 0;
                        const end = typeof el.selectionEnd === 'number' ? el.selectionEnd : 0;
                        return {
                            hasSelection: start !== end,
                            hasText: value.length > 0,
                            start,
                            end
                        };
                    },

                    focus() {
                        el.focus();
                    },

                    deleteSelection() {
                        const start = typeof el.selectionStart === 'number' ? el.selectionStart : 0;
                        const end = typeof el.selectionEnd === 'number' ? el.selectionEnd : 0;
                        if (start === end) {
                            return;
                        }

                        replaceRange(start, end, '', start, start);
                    },

                    selectAll() {
                        const value = el.value || '';
                        el.focus();
                        el.setSelectionRange(0, value.length, value.length > 0 ? 'forward' : 'none');
                        scrollCaretIntoView(0);
                    },

                    undo() {
                        el.focus();
                        document.execCommand('undo');
                    },

                    redo() {
                        el.focus();
                        document.execCommand('redo');
                    },

                    countMatches(query, caseSensitive) {
                        const matches = getSearchMatches(query, caseSensitive);
                        return buildSearchResult(matches, -1, query ? query.length : 0, 0);
                    },

                    find(query, caseSensitive, forward, restart) {
                        const matches = getSearchMatches(query, caseSensitive);
                        if (matches.length === 0 || !query) {
                            return buildSearchResult(matches, -1, query ? query.length : 0, 0);
                        }

                        const selectionStart = typeof el.selectionStart === 'number' ? el.selectionStart : 0;
                        const selectionEnd = typeof el.selectionEnd === 'number' ? el.selectionEnd : 0;
                        const selectionMatches = isSelectionMatch(query, caseSensitive);
                        let targetIndex = -1;

                        if (forward) {
                            if (restart) {
                                targetIndex = matches[0];
                            } else {
                                const anchor = selectionMatches ? selectionEnd : selectionStart;
                                targetIndex = matches.find(match => match >= anchor);
                                if (typeof targetIndex === 'undefined') {
                                    targetIndex = matches[0];
                                }
                            }
                        } else {
                            if (restart) {
                                targetIndex = matches[matches.length - 1];
                            } else {
                                const anchor = selectionMatches ? selectionStart - 1 : selectionStart - 1;
                                for (let i = matches.length - 1; i >= 0; i--) {
                                    if (matches[i] <= anchor) {
                                        targetIndex = matches[i];
                                        break;
                                    }
                                }

                                if (targetIndex < 0) {
                                    targetIndex = matches[matches.length - 1];
                                }
                            }
                        }

                        revealSelection(targetIndex, query.length);
                        return buildSearchResult(matches, targetIndex, query.length, 0);
                    },

                    replaceCurrent(query, replacement, caseSensitive) {
                        if (!query) {
                            return buildSearchResult([], -1, 0, 0);
                        }

                        if (!isSelectionMatch(query, caseSensitive)) {
                            const searched = this.find(query, caseSensitive, true, false);
                            if (!searched.found) {
                                return searched;
                            }
                        }

                        const selectionStart = typeof el.selectionStart === 'number' ? el.selectionStart : 0;
                        const selectionEnd = typeof el.selectionEnd === 'number' ? el.selectionEnd : 0;
                        replaceRange(
                            selectionStart,
                            selectionEnd,
                            replacement,
                            selectionStart + replacement.length,
                            selectionStart + replacement.length);

                        const matches = getSearchMatches(query, caseSensitive);
                        if (matches.length > 0) {
                            const caret = selectionStart + replacement.length;
                            let nextIndex = matches.find(match => match >= caret);
                            if (typeof nextIndex === 'undefined') {
                                nextIndex = matches[0];
                            }

                            revealSelection(nextIndex, query.length);
                            return buildSearchResult(matches, nextIndex, query.length, 1);
                        }

                        return buildSearchResult(matches, -1, query.length, 1);
                    },

                    replaceAll(query, replacement, caseSensitive) {
                        if (!query) {
                            return buildSearchResult([], -1, 0, 0);
                        }

                        const current = el.value || '';
                        const matches = getSearchMatches(query, caseSensitive);
                        if (matches.length === 0) {
                            return buildSearchResult(matches, -1, query.length, 0);
                        }

                        let rebuilt = '';
                        let cursor = 0;

                        matches.forEach(matchIndex => {
                            rebuilt += current.slice(cursor, matchIndex) + replacement;
                            cursor = matchIndex + query.length;
                        });

                        rebuilt += current.slice(cursor);
                        el.focus();
                        el.value = rebuilt;
                        el.selectionStart = rebuilt.length;
                        el.selectionEnd = rebuilt.length;
                        const inputEvent = new Event('input', { bubbles: true });
                        el.dispatchEvent(inputEvent);

                        const remainingMatches = getSearchMatches(query, caseSensitive);
                        if (remainingMatches.length > 0) {
                            revealSelection(remainingMatches[0], query.length);
                            return buildSearchResult(remainingMatches, remainingMatches[0], query.length, matches.length);
                        }

                        return buildSearchResult(remainingMatches, -1, query.length, matches.length);
                    }
                };

                const handleEditorKeyDown = e => {
                    if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && e.key.toLowerCase() === 'f') {
                        e.preventDefault();
                        window.chrome.webview.postMessage('ACTION:openFindReplace');
                        return;
                    }

                    if (!e.altKey && !e.ctrlKey && !e.metaKey) {
                        switch (e.key || e.code) {
                            case 'ArrowLeft':
                            case 'Left':
                                e.preventDefault();
                                moveCaretHorizontal(-1, e.shiftKey);
                                return;
                            case 'ArrowRight':
                            case 'Right':
                                e.preventDefault();
                                moveCaretHorizontal(1, e.shiftKey);
                                return;
                            case 'ArrowUp':
                            case 'Up':
                                e.preventDefault();
                                moveCaretVertical(-1, e.shiftKey);
                                return;
                            case 'ArrowDown':
                            case 'Down':
                                e.preventDefault();
                                moveCaretVertical(1, e.shiftKey);
                                return;
                        }
                    }

                    if (e.key === 'Escape') {
                        window.chrome.webview.postMessage('ACTION:editorEscape');
                    }

                    if (e.key !== 'Shift') {
                        resetVerticalNavigation();
                    }
                };

                const handleEditorContextMenu = e => {
                    e.preventDefault();
                    el.focus();
                    resetVerticalNavigation();
                    window.chrome.webview.postMessage(`ACTION:editorContextMenu:${e.clientX}:${e.clientY}`);
                };

                el.addEventListener('input', notifyContent);
                document.addEventListener('keydown', handleEditorKeyDown, true);
                document.addEventListener('contextmenu', handleEditorContextMenu, true);
                el.addEventListener('mousedown', function() {
                    resetVerticalNavigation();
                });
                el.addEventListener('mouseup', function() {
                    resetVerticalNavigation();
                });
                el.addEventListener('click', function() {
                    el.focus();
                });
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

                PreviewWebView.NavigateToString(initialHtml);
                EditorWebView.NavigateToString(editorHtml);
            }
            catch (Exception ex)
            {
                HandleRecoverableWebViewException(ex, "webview initialization");
            }
            finally
            {
                _isWebViewInitializing = false;
            }
        }

        private void EditorWebView_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            var msg = e.TryGetWebMessageAsString() ?? "";
            if (msg.StartsWith("CONTENT:"))
            {
                _currentContent = msg.Substring(8);
                _ = UpdateWebViewContentAsync();
                SmartDetectMath();
            }
            else if (msg == "ACTION:pasteImage")
            {
                _ = HandlePastedImageAsync();
            }
            else if (msg == "ACTION:openFindReplace")
            {
                _ = OpenFindReplacePopupAsync();
            }
            else if (msg.StartsWith("ACTION:editorContextMenu:", StringComparison.Ordinal))
            {
                var payload = msg["ACTION:editorContextMenu:".Length..];
                var parts = payload.Split(':');
                if (parts.Length == 2 &&
                    double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) &&
                    double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y))
                {
                    _ = ShowEditorContextMenuAsync(x, y);
                }
            }
            else if (msg == "ACTION:editorEscape" && _isFindReplaceOpen)
            {
                CloseFindReplacePopup(returnFocusToEditor: true);
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

                await SyncArticleImagePathsWithCurrentTitleAsync();

                var safeDirName = BuildImageFolderSlug(TitleBox.Text);

                var relativeDir = BuildImageRelativeDirectory(safeDirName);
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

                _currentImageFolderSlug = safeDirName;
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
            _isPageActive = false;
            if (_parentSv != null) _parentSv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            EndLayoutDrag(commitPreference: true);
            ClearDropCues();
            DetachWebViewHandlers();
            _isWebViewReady = false;
            
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
            _isPageActive = true;

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
                _currentImageFolderSlug = DetectImageFolderSlug(post.Image) ?? DetectImageFolderSlug(post.Content) ?? BuildImageFolderSlug(post.Title);
            }
            else
            {
                SetPublishNow_Click(null, null);
                TocSwitch.IsChecked = true;
                _currentContent = "";
                _currentImageFolderSlug = "";
            }
            
            if (!TryPostEditorContent(_currentContent) && !_isWebViewInitializing)
            {
                _ = InitializeWebViewsAsync();
            }

            _ = UpdateWebViewContentAsync();
            
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

        private async System.Threading.Tasks.Task UpdateWebViewContentAsync()
        {
            _pendingPreviewRefresh = true;

            if (!_isWebViewReady || PreviewWebView.CoreWebView2 == null)
            {
                UpdatePreviewNavigationUi();
                return;
            }

            if (_isPreviewBrowsing)
            {
                UpdatePreviewNavigationUi();
                return;
            }

            try
            {
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

                await TryExecutePreviewScriptAsync(script, "preview render");
            }
            catch (Exception ex)
            {
                HandleRecoverableWebViewException(ex, "preview content update");
            }
            finally
            {
                UpdatePreviewNavigationUi();
            }
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
            _currentImageFolderSlug = "";
            TryPostEditorContent(string.Empty);

            CloseFindReplacePopup(returnFocusToEditor: false);
                 
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

        private async void TitleBox_LostFocus(object sender, RoutedEventArgs e)
        {
            await SyncArticleImagePathsWithCurrentTitleAsync();
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

            await SyncArticleImagePathsWithCurrentTitleAsync();
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
            _currentImageFolderSlug = DetectImageFolderSlug(post.Image) ?? DetectImageFolderSlug(post.Content) ?? BuildImageFolderSlug(post.Title);

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

            await TryExecuteEditorScriptAsync(script, "insert text into editor");
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
            var script = $@"
                (function() {{
                    if (!window.editorTools) {{
                        return;
                    }}

                    {invocationScript}
                }})();
            ";

            await TryExecuteEditorScriptAsync(script, "execute editor tool");
        }

        private async System.Threading.Tasks.Task<string> ExecuteEditorScriptWithResultAsync(string invocationScript)
        {
            var script = $@"
                (function() {{
                    if (!window.editorTools) {{
                        return null;
                    }}

                    return {invocationScript};
                }})();
            ";

            return await TryExecuteEditorScriptWithResultAsync(script, "execute editor query");
        }

        private static string ParseEditorStringResult(string rawResult)
        {
            if (string.IsNullOrWhiteSpace(rawResult) || rawResult == "null")
            {
                return string.Empty;
            }

            return System.Text.Json.JsonSerializer.Deserialize<string>(rawResult) ?? string.Empty;
        }

        private static EditorSelectionState ParseEditorSelectionState(string rawResult)
        {
            if (string.IsNullOrWhiteSpace(rawResult) || rawResult == "null")
            {
                return default;
            }

            using var document = System.Text.Json.JsonDocument.Parse(rawResult);
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return default;
            }

            return new EditorSelectionState(
                root.TryGetProperty("hasSelection", out var hasSelection) && hasSelection.GetBoolean(),
                root.TryGetProperty("hasText", out var hasText) && hasText.GetBoolean(),
                root.TryGetProperty("start", out var start) ? start.GetInt32() : 0,
                root.TryGetProperty("end", out var end) ? end.GetInt32() : 0);
        }

        private static EditorFindResult ParseEditorFindResult(string rawResult)
        {
            if (string.IsNullOrWhiteSpace(rawResult) || rawResult == "null")
            {
                return default;
            }

            using var document = System.Text.Json.JsonDocument.Parse(rawResult);
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return default;
            }

            return new EditorFindResult(
                root.TryGetProperty("found", out var found) && found.GetBoolean(),
                root.TryGetProperty("count", out var count) ? count.GetInt32() : 0,
                root.TryGetProperty("index", out var index) ? index.GetInt32() : -1,
                root.TryGetProperty("length", out var length) ? length.GetInt32() : 0,
                root.TryGetProperty("replacedCount", out var replacedCount) ? replacedCount.GetInt32() : 0);
        }

        private async System.Threading.Tasks.Task<string> GetEditorSelectedTextAsync()
        {
            var result = await ExecuteEditorScriptWithResultAsync("window.editorTools.getSelectedText()");
            return ParseEditorStringResult(result);
        }

        private async System.Threading.Tasks.Task<EditorSelectionState> GetEditorSelectionStateAsync()
        {
            var result = await ExecuteEditorScriptWithResultAsync("window.editorTools.getSelectionState()");
            return ParseEditorSelectionState(result);
        }

        private async System.Threading.Tasks.Task FocusEditorAsync()
        {
            await ExecuteEditorToolAsync("window.editorTools.focus();");
        }

        private async System.Threading.Tasks.Task MoveEditorCaretHorizontalAsync(int delta, bool extendSelection)
        {
            await ExecuteEditorToolAsync(
                $"window.editorTools.moveCaretHorizontal({delta}, {(extendSelection ? "true" : "false")});");
        }

        private async System.Threading.Tasks.Task MoveEditorCaretVerticalAsync(int delta, bool extendSelection)
        {
            await ExecuteEditorToolAsync(
                $"window.editorTools.moveCaretVertical({delta}, {(extendSelection ? "true" : "false")});");
        }

        private async System.Threading.Tasks.Task UndoEditorAsync()
        {
            await ExecuteEditorToolAsync("window.editorTools.undo();");
        }

        private async System.Threading.Tasks.Task RedoEditorAsync()
        {
            await ExecuteEditorToolAsync("window.editorTools.redo();");
        }

        private async System.Threading.Tasks.Task DeleteEditorSelectionAsync()
        {
            await ExecuteEditorToolAsync("window.editorTools.deleteSelection();");
        }

        private async System.Threading.Tasks.Task SelectAllEditorAsync()
        {
            await ExecuteEditorToolAsync("window.editorTools.selectAll();");
        }

        private async System.Threading.Tasks.Task CopySelectedEditorTextAsync()
        {
            var text = await GetEditorSelectedTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                System.Windows.Clipboard.SetText(text);
            }
        }

        private async System.Threading.Tasks.Task CutSelectedEditorTextAsync()
        {
            var selectionState = await GetEditorSelectionStateAsync();
            if (!selectionState.HasSelection)
            {
                return;
            }

            await CopySelectedEditorTextAsync();
            await DeleteEditorSelectionAsync();
        }

        private async System.Threading.Tasks.Task PasteIntoEditorAsync()
        {
            if (System.Windows.Clipboard.ContainsFileDropList() || System.Windows.Clipboard.ContainsImage())
            {
                await HandlePastedImageAsync();
                return;
            }

            if (System.Windows.Clipboard.ContainsText())
            {
                await InsertTextIntoEditorAsync(System.Windows.Clipboard.GetText());
            }
        }

        private async System.Threading.Tasks.Task<EditorFindResult> CountEditorMatchesAsync(string query, bool matchCase)
        {
            var result = await ExecuteEditorScriptWithResultAsync(
                $"window.editorTools.countMatches({JsString(query)}, {(matchCase ? "true" : "false")})");

            return ParseEditorFindResult(result);
        }

        private async System.Threading.Tasks.Task<EditorFindResult> FindInEditorAsync(string query, bool matchCase, bool forward, bool restart)
        {
            var result = await ExecuteEditorScriptWithResultAsync(
                $"window.editorTools.find({JsString(query)}, {(matchCase ? "true" : "false")}, {(forward ? "true" : "false")}, {(restart ? "true" : "false")})");

            return ParseEditorFindResult(result);
        }

        private async System.Threading.Tasks.Task<EditorFindResult> ReplaceCurrentInEditorAsync(string query, string replacement, bool matchCase)
        {
            var result = await ExecuteEditorScriptWithResultAsync(
                $"window.editorTools.replaceCurrent({JsString(query)}, {JsString(replacement)}, {(matchCase ? "true" : "false")})");

            return ParseEditorFindResult(result);
        }

        private async System.Threading.Tasks.Task<EditorFindResult> ReplaceAllInEditorAsync(string query, string replacement, bool matchCase)
        {
            var result = await ExecuteEditorScriptWithResultAsync(
                $"window.editorTools.replaceAll({JsString(query)}, {JsString(replacement)}, {(matchCase ? "true" : "false")})");

            return ParseEditorFindResult(result);
        }

        private async System.Threading.Tasks.Task RefreshFindReplaceStatusAsync()
        {
            if (!_isFindReplaceOpen)
            {
                return;
            }

            var query = FindQueryBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                SetFindReplaceStatus("EditorFindEnterQuery");
                return;
            }

            var result = await CountEditorMatchesAsync(query, FindMatchCaseCheckBox.IsChecked == true);
            if (result.Count <= 0)
            {
                SetFindReplaceStatus("EditorFindNoMatch");
            }
            else
            {
                SetFindReplaceStatus("EditorFindMatches", result.Count);
            }
        }

        private async System.Threading.Tasks.Task FindNextMatchAsync(bool forward, bool restart = false)
        {
            var query = FindQueryBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                SetFindReplaceStatus("EditorFindEnterQuery");
                return;
            }

            var result = await FindInEditorAsync(query, FindMatchCaseCheckBox.IsChecked == true, forward, restart);
            if (result.Count <= 0 || !result.Found)
            {
                SetFindReplaceStatus("EditorFindNoMatch");
                return;
            }

            SetFindReplaceStatus("EditorFindMatches", result.Count);
        }

        private async System.Threading.Tasks.Task ReplaceCurrentMatchAsync()
        {
            var query = FindQueryBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                SetFindReplaceStatus("EditorFindEnterQuery");
                return;
            }

            var result = await ReplaceCurrentInEditorAsync(query, ReplaceQueryBox.Text ?? string.Empty, FindMatchCaseCheckBox.IsChecked == true);
            if (result.ReplacedCount <= 0 && result.Count <= 0)
            {
                SetFindReplaceStatus("EditorFindNoMatch");
                return;
            }

            if (result.Count <= 0)
            {
                SetFindReplaceStatus("EditorFindNoMatch");
            }
            else
            {
                SetFindReplaceStatus("EditorFindMatches", result.Count);
            }
        }

        private async System.Threading.Tasks.Task ReplaceAllMatchesAsync()
        {
            var query = FindQueryBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                SetFindReplaceStatus("EditorFindEnterQuery");
                return;
            }

            var result = await ReplaceAllInEditorAsync(query, ReplaceQueryBox.Text ?? string.Empty, FindMatchCaseCheckBox.IsChecked == true);
            if (result.ReplacedCount <= 0)
            {
                SetFindReplaceStatus("EditorFindNoMatch");
                return;
            }

            if (result.Count <= 0)
            {
                SetFindReplaceStatus("EditorFindNoMatch");
            }
            else
            {
                SetFindReplaceStatus("EditorFindMatches", result.Count);
            }
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
                    await SyncArticleImagePathsWithCurrentTitleAsync();

                    // 1. Determine safe directory name based on article title or filename
                    var safeDirName = BuildImageFolderSlug(TitleBox.Text);

                    var relativeDir = BuildImageRelativeDirectory(safeDirName);
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
                    _currentImageFolderSlug = safeDirName;
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

        private async void OpenFindReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenFindReplacePopupAsync();
        }

        private void CloseFindReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            CloseFindReplacePopup(returnFocusToEditor: true);
        }

        private async void FindQueryBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressFindQueryRefresh)
            {
                return;
            }

            await RefreshFindReplaceStatusAsync();
        }

        private async void FindMatchCaseCheckBox_Click(object sender, RoutedEventArgs e)
        {
            await RefreshFindReplaceStatusAsync();
        }

        private async void FindPreviousButton_Click(object sender, RoutedEventArgs e)
        {
            await FindNextMatchAsync(forward: false);
        }

        private async void FindNextButton_Click(object sender, RoutedEventArgs e)
        {
            await FindNextMatchAsync(forward: true);
        }

        private async void ReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            await ReplaceCurrentMatchAsync();
        }

        private async void ReplaceAllButton_Click(object sender, RoutedEventArgs e)
        {
            await ReplaceAllMatchesAsync();
        }

        private async void FindQueryBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await FindNextMatchAsync(forward: (Keyboard.Modifiers & ModifierKeys.Shift) == 0);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                CloseFindReplacePopup(returnFocusToEditor: true);
                e.Handled = true;
            }
        }

        private async void ReplaceQueryBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    await ReplaceAllMatchesAsync();
                }
                else
                {
                    await ReplaceCurrentMatchAsync();
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                CloseFindReplacePopup(returnFocusToEditor: true);
                e.Handled = true;
            }
        }

        private async void RootGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                (Keyboard.Modifiers & ModifierKeys.Alt) == 0 &&
                e.Key == Key.F)
            {
                await OpenFindReplacePopupAsync(populateFromSelection: true);
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Escape)
            {
                return;
            }

            if (_isFindReplaceOpen)
            {
                CloseFindReplacePopup(returnFocusToEditor: true);
                e.Handled = true;
                return;
            }

            if (ReferenceEquals(e.OriginalSource, TagInputBox) || IsDescendantOf(TagInputBox, e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (IsMetadataInputElement(e.OriginalSource as DependencyObject))
            {
                DismissMetadataInputFocus(e.OriginalSource as DependencyObject);
                e.Handled = true;
            }
        }

        private void EditorWorkspaceGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isFindReplaceOpen || FindReplacePopupHost?.IsOpen == true)
            {
                UpdateFindReplacePopupPosition();
            }
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
            var result = await TryExecuteEditorScriptWithResultAsync(getRatioScript, "read editor scroll ratio");
            
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
                await TryExecutePreviewScriptAsync(script, "sync editor scroll to preview");
            }
        }

        private async void SyncEditorToPreview_Click(object sender, RoutedEventArgs e)
        {
            await SyncEditorToPreviewAsync();
        }

        private async System.Threading.Tasks.Task SyncPreviewToEditorAsync()
        {
            if (!_isWebViewReady || PreviewWebView.CoreWebView2 == null || EditorWebView.CoreWebView2 == null) return;

            var result = await TryExecutePreviewScriptWithResultAsync(@"
                (function() {
                    var el = document.getElementById('content');
                    if (!el) return 0;
                    var maxScroll = el.scrollHeight - el.clientHeight;
                    if (maxScroll <= 0) return 0;
                    return el.scrollTop / maxScroll;
                })();
            ", "read preview scroll ratio");

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
                await TryExecuteEditorScriptAsync(script, "sync preview scroll to editor");
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

        private static T? FindSelfOrVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            if (child is T self)
            {
                return self;
            }

            return child == null ? null : FindVisualParent<T>(child);
        }

        private static bool IsDescendantOf(DependencyObject? ancestor, DependencyObject? child)
        {
            if (ancestor == null || child == null)
            {
                return false;
            }

            var current = child;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }

                try
                {
                    current = System.Windows.Media.VisualTreeHelper.GetParent(current);
                }
                catch
                {
                    current = null;
                }
            }

            return false;
        }

        private bool IsMetadataInputElement(DependencyObject? source)
        {
            if (MetadataContentGrid == null || source == null || !IsDescendantOf(MetadataContentGrid, source))
            {
                return false;
            }

            return FindSelfOrVisualParent<System.Windows.Controls.TextBox>(source) != null ||
                   FindSelfOrVisualParent<ComboBox>(source) != null ||
                   FindSelfOrVisualParent<DatePicker>(source) != null;
        }

        private void DismissMetadataInputFocus(DependencyObject? source)
        {
            var comboBox = FindSelfOrVisualParent<ComboBox>(source);
            if (comboBox != null)
            {
                comboBox.IsDropDownOpen = false;
            }

            var datePicker = FindSelfOrVisualParent<DatePicker>(source);
            if (datePicker != null)
            {
                datePicker.IsDropDownOpen = false;
            }

            DismissFocusToRoot();
        }

        private void DismissFocusToRoot()
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
            DismissFocusToRoot();
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
