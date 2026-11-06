// ==== WinUI3 类型桩: 仅用于语法/类型检查, 不随产品分发 ====
using System;
namespace Microsoft.UI.Xaml
{
    public class Window
    {
        public object? Content { get; set; }
        public bool ExtendsContentIntoTitleBar { get; set; }
        public Microsoft.UI.Windowing.AppWindow AppWindow { get; } = new();
        public void Activate() { }
        protected void InitializeComponent() { }
        public void Close() { }
        public event global::System.EventHandler<object> Closed { add { } remove { } }
        public event global::System.EventHandler<WindowActivatedEventArgs> Activated { add { } remove { } }
        // 真实 WinUI: Window.DispatcherQueue 为 Microsoft.UI.Dispatching.DispatcherQueue
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
    }
    public class RoutedEventArgs { }
    public class UIElement : DependencyObject
    {
        public Microsoft.UI.Xaml.VerticalAlignment VerticalAlignment { get; set; }
        public Microsoft.UI.Xaml.HorizontalAlignment HorizontalAlignment { get; set; }
        public string Name { get; set; } = "";
        public Visibility Visibility { get; set; } = Visibility.Visible;
        public bool IsEnabled { get; set; } = true;
        public event global::System.EventHandler<object> DoubleTapped { add { } remove { } }
    }
    public enum Visibility { Visible, Collapsed, Hidden }
    public class DependencyObject { }
    public class WindowActivatedEventArgs { }

    public class FrameworkElement : UIElement
    {
        public Microsoft.UI.Xaml.XamlRoot XamlRoot { get; set; } = new();
        public ElementTheme RequestedTheme { get; set; } = ElementTheme.Default;
        public ElementTheme ActualTheme => ElementTheme.Light;
        public double ActualWidth => 0;
        public double ActualHeight => 0;
        public double MinWidth { get; set; }
        public double MinHeight { get; set; }
        public object? Tag { get; set; }
    }
    public class ResourceDictionary
    {
        public object? this[string key] { get => null; set { } }
        public System.Uri? Source { get; set; }
        public System.Collections.Generic.IList<ResourceDictionary> MergedDictionaries { get; } =
            new System.Collections.Generic.List<ResourceDictionary>();
    }
    public class Application
    {
        public static Application Current { get; } = new();
        public ResourceDictionary Resources { get; } = new();
        public void Exit() { }
        protected virtual void OnLaunched(LaunchActivatedEventArgs args) { }
        public event global::System.EventHandler<UnhandledExceptionEventArgs> UnhandledException { add { } remove { } }
    }
    public class LaunchActivatedEventArgs { }
    public class UnhandledExceptionEventArgs { public string Message { get; } = ""; public global::System.Exception? Exception { get; } }
    public class XamlRoot
    {
        // v85 小窗按屏幕缩放换算尺寸用
        public double RasterizationScale => 1.0;
    }
    public enum ElementTheme { Light, Dark, Default }
    public class DragEventArgs
    {
        public Windows.ApplicationModel.DataTransfer.DataPackageView DataView { get; } = new();
        public Windows.ApplicationModel.DataTransfer.DataPackageOperation AcceptedOperation { get; set; }
    }
    public interface IWinRTObject { }
}

namespace Microsoft.UI
{
    public static class ColorHelper
    {
        public static Windows.UI.Color FromArgb(byte a, byte r, byte g, byte b) => default;
    }
}

namespace Microsoft.UI.Windowing
{
    public class AppWindowTitleBar
    {
        public Windows.UI.Color BackgroundColor { get; set; }
        public Windows.UI.Color ForegroundColor { get; set; }
        public Windows.UI.Color ButtonBackgroundColor { get; set; }
        public Windows.UI.Color ButtonForegroundColor { get; set; }
        public Windows.UI.Color ButtonHoverBackgroundColor { get; set; }
        public Windows.UI.Color ButtonHoverForegroundColor { get; set; }
        public Windows.UI.Color ButtonPressedBackgroundColor { get; set; }
        public Windows.UI.Color ButtonPressedForegroundColor { get; set; }
        public Windows.UI.Color ButtonInactiveBackgroundColor { get; set; }
        public Windows.UI.Color ButtonInactiveForegroundColor { get; set; }
    }
    public class AppWindow
    {
        public string Title { get; set; } = "";
        public Microsoft.UI.Windowing.AppWindowTitleBar TitleBar { get; } = new();
        public void Resize(Windows.Graphics.SizeInt32 size) { }
        // v82 小窗居中走 Move (Position 只读)
        public void Move(Windows.Graphics.PointInt32 point) { }
        public void SetIcon(string iconPath) { }
        public void Show() { }
        public void Hide() { }
    }
    // v82 屏幕工作区取数
    public class DisplayArea
    {
        public static DisplayArea Primary => new();
        public Windows.Graphics.RectInt32 WorkArea => default;
    }
}

namespace Microsoft.UI.Text
{
    public struct FontWeights { public static readonly FontWeights SemiBold = default; }
}

namespace Microsoft.UI.Xaml
{
    public struct Thickness { public Thickness(double l, double t, double r, double b) { } }
    public enum TextWrapping { NoWrap, Wrap }
    public enum TextTrimming { None, CharacterEllipsis, WordEllipsis }
    public enum VerticalAlignment { Top, Center, Bottom, Stretch }
    public enum HorizontalAlignment { Left, Center, Right, Stretch }
    public enum GridUnitType { Auto, Pixel, Star }
    public struct GridLength
    {
        public GridLength(double value, GridUnitType type) { }
        public static GridLength Auto => default;
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public class Selector : UIElement
    {
        public int SelectedIndex { get; set; }
        public object? SelectedItem { get; set; }
        public event global::System.EventHandler<SelectionChangedEventArgs> SelectionChanged { add { } remove { } }
    }
    public class SelectionChangedEventArgs : global::System.EventArgs { }
    public class ComboBox : Selector { }
    public class ListView : Selector
    {
        public global::System.Collections.Generic.IList<object> Items { get; } = new global::System.Collections.Generic.List<object>();
        public new event global::System.EventHandler<global::System.EventArgs> DoubleTapped { add { } remove { } }
    }
    public class TextBlock : UIElement
    {
        public string Text { get; set; } = "";
        public Microsoft.UI.Text.FontWeights FontWeight { get; set; }
        public double FontSize { get; set; }
        public Microsoft.UI.Xaml.TextWrapping TextWrapping { get; set; }
        public Microsoft.UI.Xaml.TextTrimming TextTrimming { get; set; }
        public int MaxLines { get; set; }
        public Microsoft.UI.Xaml.Media.Brush? Foreground { get; set; }
    }
    // 真实 WinUI: Panel 派生自 FrameworkElement (XamlRoot 等经此可得)
    public class Panel : FrameworkElement
    {
        public System.Collections.Generic.IList<UIElement> Children { get; } =
            new System.Collections.Generic.List<UIElement>();
        public new double ActualWidth => 0;
        public new double ActualHeight => 0;
    }
    public enum Orientation { Horizontal, Vertical }
    public class StackPanel : Panel
    {
        public Orientation Orientation { get; set; }
        public double Spacing { get; set; }
    }
    public class ColumnDefinition { public Microsoft.UI.Xaml.GridLength Width { get; set; } }
    public class RowDefinition { public Microsoft.UI.Xaml.GridLength Height { get; set; } }
    public class Grid : Panel
    {
        public double ColumnSpacing { get; set; }
        public double RowSpacing { get; set; }
        public System.Collections.Generic.IList<ColumnDefinition> ColumnDefinitions { get; } =
            new System.Collections.Generic.List<ColumnDefinition>();
        public System.Collections.Generic.IList<RowDefinition> RowDefinitions { get; } =
            new System.Collections.Generic.List<RowDefinition>();
        public static void SetColumn(UIElement o, int index) { }
        public static void SetRow(UIElement o, int index) { }
    }
    public class Image : UIElement { }
    public class ProgressBar : UIElement
    {
        public Microsoft.UI.Xaml.Media.Brush? Foreground { get; set; }
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double Value { get; set; }
    }
    public class CheckBox : UIElement
    {
        public object? Content { get; set; }
        public bool? IsChecked { get; set; }
    }
    public class ContentDialog
    {
        public object? Title { get; set; }
        public object? Content { get; set; }
        public string PrimaryButtonText { get; set; } = "";
        public string CloseButtonText { get; set; } = "";
        public Microsoft.UI.Xaml.XamlRoot XamlRoot { get; set; } = new();
        public global::System.Threading.Tasks.Task<ContentDialogResult> ShowAsync()
            => global::System.Threading.Tasks.Task.FromResult(ContentDialogResult.Primary);
        public void Hide() { }
    }
    public enum ContentDialogResult { None, Primary, Secondary }
}

namespace Microsoft.UI.Xaml.Media
{
    public static class VisualTreeHelper
    {
        public static System.Collections.Generic.IEnumerable<DependencyObject> GetChildObjects(DependencyObject o)
            => System.Linq.Enumerable.Empty<DependencyObject>();
        public static DependencyObject? GetChild(DependencyObject o, int i) => null;
        public static int GetChildrenCount(DependencyObject o) => 0;
    }
    public class Brush { }
    public class SolidColorBrush : Brush
    {
        public Windows.UI.Color Color { get; set; }
        public SolidColorBrush(Windows.UI.Color c) { }
    }
}

namespace Microsoft.UI
{
    public static class Colors
    {
        public static readonly Windows.UI.Color IndianRed = default;
    }
}

namespace Windows.UI
{
    public struct Color
    {
        public byte A, R, G, B;
        public static Color FromArgb(byte a, byte r, byte g, byte b) => new Color { A = a, R = r, G = g, B = b };
    }
}

namespace Windows.System
{
    public class DispatcherQueue
    {
        public bool TryEnqueue(Action callback) { callback(); return true; }
        public static DispatcherQueue GetForCurrentThread() => new();
        public DispatcherQueueTimer CreateTimer() => new DispatcherQueueTimer();
    }
    public class DispatcherQueueTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsRepeating { get; set; }
        public event global::System.EventHandler<object>? Tick { add { } remove { } }
        public void Start() { }
        public void Stop() { }
    }
}

namespace Microsoft.UI.Dispatching
{
    // v84 起 ThemeService 快切并档定时器声明为 Microsoft.UI.Dispatching.DispatcherQueueTimer,
    // 此桩缺失曾令体检静默报错 (防线带伤), v87 补齐; DispatcherQueue 同源补齐
    public class DispatcherQueue
    {
        public bool TryEnqueue(System.Action callback) { callback(); return true; }
        public DispatcherQueueTimer CreateTimer() => new();
    }
    public class DispatcherQueueTimer
    {
        public System.TimeSpan Interval { get; set; }
        public bool IsRepeating { get; set; }
        public event System.EventHandler<object>? Tick { add { } remove { } }
        public void Start() { }
        public void Stop() { }
    }
}

namespace Microsoft.UI.Xaml
{
    public class DispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public event global::System.EventHandler<object>? Tick { add { } remove { } }
        public void Start() { }
        public void Stop() { }
    }
}

namespace Windows.UI.Xaml
{
    public struct Thickness { public Thickness(double l, double t, double r, double b) { } }
}

namespace Windows.Graphics
{
    public struct SizeInt32 { public int Width; public int Height; }
    public struct PointInt32 { public int X; public int Y; public PointInt32(int x, int y) { X = x; Y = y; } }
    public struct RectInt32 { public int X; public int Y; public int Width; public int Height; }
}

namespace Windows.ApplicationModel.DataTransfer
{
    public enum DataPackageOperation { Copy }
    public class DataPackageView
    {
        public global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<Windows.Storage.StorageItem>> GetStorageItemsAsync()
            => global::System.Threading.Tasks.Task.FromResult<global::System.Collections.Generic.IReadOnlyList<Windows.Storage.StorageItem>>(new global::System.Collections.Generic.List<Windows.Storage.StorageItem>());
    }
    public class DataPackage { public DataPackageView GetView() => new(); }
}

namespace Windows.Storage
{
    public class StorageItem { public string Path { get; } = ""; }
    public class StorageFile : StorageItem { }
    public class StorageFolder : StorageItem { }
}

namespace Windows.Storage.Pickers
{
    public class FileOpenPicker
    {
        public global::System.Collections.Generic.IList<string> FileTypeFilter { get; } = new global::System.Collections.Generic.List<string>();
        public global::System.Threading.Tasks.Task<StorageFile?> PickSingleFileAsync() => global::System.Threading.Tasks.Task.FromResult<StorageFile?>(new StorageFile());
    }
    public class FolderPicker
    {
        public global::System.Collections.Generic.IList<string> FileTypeFilter { get; } = new global::System.Collections.Generic.List<string>();
        public global::System.Threading.Tasks.Task<StorageFolder?> PickSingleFolderAsync() => global::System.Threading.Tasks.Task.FromResult<StorageFolder?>(new StorageFolder());
    }
    public class FileSavePicker
    {
        public string SuggestedFileName { get; set; } = "";
        public global::System.Collections.Generic.IDictionary<string, global::System.Collections.Generic.IList<string>> FileTypeChoices { get; } =
            new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.IList<string>>();
        public global::System.Threading.Tasks.Task<StorageFile?> PickSaveFileAsync() => global::System.Threading.Tasks.Task.FromResult<StorageFile?>(new StorageFile());
    }
}

namespace WinRT.Interop
{
    public static class WindowNative { public static global::System.IntPtr GetWindowHandle(object w) => global::System.IntPtr.Zero; }
    public static class InitializeWithWindow { public static void Initialize(object o, global::System.IntPtr h) { } }
}

namespace System.Runtime.InteropServices
{
    // 占位, 保持隐式引用完整
}

namespace Microsoft.UI.Xaml.Input
{
    public class DoubleTappedRoutedEventArgs { }
}
