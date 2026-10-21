using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace Qivoryx;

public partial class MainWindow
{
    bool themeLoaded;
    void SetTheme()
    {
        string[] keys = { "Bg", "Side", "Card", "Fg", "Muted", "Line", "Hover", "Accent", "OnAccent", "Input", "ScrollThumb", "CardTop", "CardBottom" };
        var colors = light
            ? new[] { "#F3F3F3", "#FAFAFA", "#FFFFFF", "#161616", "#737373", "#DEDEDE", "#EBEBEB", "#191919", "#FFFFFF", "#F8F8F8", "#C4C4C4", "#FFFFFF", "#FDFDFD" }
            : new[] { "#111111", "#141414", "#1B1B1B", "#F3F3F3", "#959595", "#303030", "#282828", "#F1F1F1", "#171717", "#171717", "#454545", "#202020", "#1A1A1A" };
        for (int i = 0; i < keys.Length; i++) Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        Resources[SystemColors.WindowBrushKey] = Resources["Card"];Resources[SystemColors.ControlBrushKey] = Resources["Card"];
        Resources[SystemColors.WindowTextBrushKey] = Resources["Fg"];Resources[SystemColors.ControlTextBrushKey] = Resources["Fg"];
        Resources[SystemColors.HighlightBrushKey] = Resources["Hover"];Resources[SystemColors.HighlightTextBrushKey] = Resources["Fg"];
        Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Resources["Hover"];Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Resources["Fg"];
        Resources["CardSurface"] = new LinearGradientBrush(((SolidColorBrush)Resources["CardTop"]).Color, ((SolidColorBrush)Resources["CardBottom"]).Color, 90);
        if (!themeLoaded)
        {
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Qivoryx;component/Themes/Controls.xaml", UriKind.Relative) });
            themeLoaded = true;
        }
        SetResourceReference(BackgroundProperty, "Bg");SetResourceReference(ForegroundProperty, "Fg");
        progress.SetResourceReference(Control.ForegroundProperty, "Accent");progress.SetResourceReference(Control.BackgroundProperty, "Line");
    }
    void BuildChrome()
    {
        WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 46, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(12), UseAeroCaptionButtons = false });
        var shell = new Grid();shell.RowDefinitions.Add(new() { Height = new GridLength(46) });shell.RowDefinitions.Add(new());
        shell.SetResourceReference(Panel.BackgroundProperty, "Bg");
        var titlebar = new Grid();titlebar.ColumnDefinitions.Add(new());titlebar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        titlebar.SetResourceReference(Panel.BackgroundProperty, "Side");
        var caption = Row(Text("Q", 15, bold: true), new Border { Width = 12 }, Text("QIVORYX", 10, "Muted", true), new Border { Width = 12 }, Text("/  DESKTOP TOOLKIT", 9, "Muted"));
        caption.Margin = new Thickness(23, 0, 0, 0);caption.VerticalAlignment = VerticalAlignment.Center;titlebar.Children.Add(caption);
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        Button CaptionButton(string glyph, Action action, string help)
        {
            var b = Button(glyph, action);b.FontFamily = new FontFamily("Segoe MDL2 Assets");b.FontSize = 11;b.Width = 46;b.Height = 44;b.Padding = new Thickness(0);b.Margin = new Thickness(0);b.BorderThickness = new Thickness(0);b.Background = Brushes.Transparent;b.ToolTip = help;
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);return b;
        }
        controls.Children.Add(CaptionButton("\uE921", () => SystemCommands.MinimizeWindow(this), "Свернуть"));
        var maximize = CaptionButton("\uE922", () => { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this); }, "Развернуть / восстановить");controls.Children.Add(maximize);
        controls.Children.Add(CaptionButton("\uE8BB", Close, "Закрыть"));
        StateChanged += (_, _) => { maximize.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922"; };
        Grid.SetColumn(controls, 1);titlebar.Children.Add(controls);shell.Children.Add(titlebar);
        Content = null;Grid.SetRow(root, 1);shell.Children.Add(root);
        var frame = new Border { Child = shell, BorderThickness = new Thickness(1) };frame.SetResourceReference(Border.BorderBrushProperty, "Line");Content = frame;
        UseLayoutRounding = true;SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
    }
    void AnimatePage()
    {
        if (!SystemParameters.ClientAreaAnimation) { content.Opacity = 1;return; }
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        var movement = new TranslateTransform();content.RenderTransform = movement;
        movement.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = easing });
    }
}
