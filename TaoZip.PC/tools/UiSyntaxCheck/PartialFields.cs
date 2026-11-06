// ==== 补齐 XAML 生成器在真实工程里生成的部分类成员 (仅语法检查用) ====
namespace TaoZip.PC.App
{
    public partial class MainWindow
    {
        internal Microsoft.UI.Xaml.Controls.ListView ListMain = null!;
        internal Microsoft.UI.Xaml.Controls.TextBlock StatusText = null!;
        internal Microsoft.UI.Xaml.Controls.TextBlock EmptyHint = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnOpen = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnExtract = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnSmart = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnTest = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnHistory = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnAssoc = null!;
        internal Microsoft.UI.Xaml.Controls.ComboBox CmbTheme = null!;
        // v87 历史记录工具条
        internal Microsoft.UI.Xaml.Controls.StackPanel HistoryBar = null!;
        internal Microsoft.UI.Xaml.Controls.TextBox TxtHistSearch = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnHistClear = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnHistPurge = null!;
    }

    public partial class App
    {
        private void InitializeComponent() { }
    }

    public partial class ExtractWindow
    {
        internal Microsoft.UI.Xaml.Controls.Grid RootGrid = null!;
        internal Microsoft.UI.Xaml.Controls.StackPanel BodyPanel = null!;
        internal Microsoft.UI.Xaml.Controls.TextBlock TxtFile = null!;
        internal Microsoft.UI.Xaml.Controls.TextBlock TxtPercent = null!;
        internal Microsoft.UI.Xaml.Controls.ProgressBar BarProgress = null!;
    }

    public partial class CompressWindow
    {
        internal Microsoft.UI.Xaml.Controls.Grid RootGrid = null!;
        internal Microsoft.UI.Xaml.Controls.StackPanel BodyPanel = null!;
        internal Microsoft.UI.Xaml.Controls.TextBlock TxtSummary = null!;
        internal Microsoft.UI.Xaml.Controls.TextBox TxtDest = null!;
        internal Microsoft.UI.Xaml.Controls.TextBlock TxtPercent = null!;
        internal Microsoft.UI.Xaml.Controls.ProgressBar BarProgress = null!;
        internal Microsoft.UI.Xaml.Controls.StackPanel PanelProgress = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnBrowse = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnStart = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnBackground = null!;
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public class Button : FrameworkElement
    {
        public string Content { get; set; } = "";
        public Microsoft.UI.Xaml.Thickness Padding { get; set; }
        public Microsoft.UI.Xaml.Media.Brush? Foreground { get; set; }
        public event System.EventHandler<RoutedEventArgs> Click { add { } remove { } }
    }
    public class TextBox : FrameworkElement
    {
        public string Text { get; set; } = "";
        public string PlaceholderText { get; set; } = "";
        public new bool IsEnabled { get; set; }
        public Microsoft.UI.Xaml.Thickness Padding { get; set; }
        public Microsoft.UI.Xaml.Media.Brush? Foreground { get; set; }
        public event System.EventHandler<TextChangedEventArgs> TextChanged { add { } remove { } }
    }
    public class TextChangedEventArgs : System.EventArgs { }
}
