using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Qivoryx;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) => { if (args.Contains("--smoke")) { Directory.CreateDirectory(Path.Combine(Engine.Store, "Tests"));File.WriteAllText(Path.Combine(Engine.Store, "Tests", "smoke-error.txt"), e.Exception.ToString());app.Shutdown(1); } else MessageBox.Show(e.Exception.Message, "Qivoryx"); e.Handled = true; };
        try { var window = new MainWindow(args);app.Run(window); }
        catch (Exception ex) { Directory.CreateDirectory(Engine.Store);File.WriteAllText(Path.Combine(Engine.Store, "startup-error.txt"), ex.ToString());if (!args.Contains("--smoke")) MessageBox.Show(ex.Message, "Qivoryx — ошибка запуска");Environment.ExitCode = 1; }
    }
}

public partial class MainWindow : Window
{
    readonly Grid root = new();
    readonly StackPanel nav = new();
    readonly StackPanel content = new();
    readonly SmoothScrollViewer pageScroll = new();
    readonly TextBlock heading = new();
    readonly TextBlock subtitle = new();
    readonly TextBlock breadcrumb = new();
    readonly TextBlock status = new();
    readonly ProgressBar progress = new() { Height = 2, Visibility = Visibility.Collapsed };
    readonly Border toast = new() { CornerRadius = new(12), Padding = new(20, 14, 20, 14), Visibility = Visibility.Collapsed, MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(30, 30, 30, 58) };
    readonly TextBlock toastText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    readonly List<Button> navigation = new();
    int page;
    int generation;
    int busy;
    bool light;
    bool sessionCloseApproved, sessionClosePending;
    readonly string[] args;
    List<ServiceInfo>? services;
    List<InstalledApp>? installed;
    readonly List<Package> packages;
    System.Text.Json.JsonElement summary;
    bool AsymmetricX3d => summary.ValueKind == System.Text.Json.JsonValueKind.Object && summary.TryGetProperty("CPU", out var cpu) && IsAsymmetricX3d(cpu.GetString() ?? "");
    static bool IsAsymmetricX3d(string cpu) => System.Text.RegularExpressions.Regex.IsMatch(cpu, @"\bRyzen\s+9\s+(7900|7950|9900|9950)X3D\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    readonly string[] titles = { "Обзор системы", "Службы Windows", "План питания", "Каталог приложений", "Удаление приложений", "Очистка дисков", "Windows Defender", "Журнал операций", "Настройки", "Центр оптимизации", "Автозагрузка", "Тонкая настройка", "Лаборатория кадров", "Игровые сессии", "Графика и дисплей", "Центр удаления" };
    readonly string[] descriptions = { "Всё важное о вашем компьютере — в одном месте.", "Понимайте назначение. Изменяйте осознанно.", "Настройте частоты, охлаждение и поведение устройств.", "Ваши инструменты. Одна очередь установки.", "Классические программы и пакеты Microsoft Store.", "Освободите место, сохранив личные файлы.", "Проверка защиты, план удаления и подробный отчёт.", "Результаты действий, ошибки и резервные копии.", "Внешний вид и данные приложения.", "Меньше фоновой работы. Больше ресурсов для важного.", "Оставьте при запуске только то, что действительно нужно.", "Отзывчивость, ввод и запись игр. Каждое изменение — под контролем.", "Сравните время кадров до и после настройки.", "Временные настройки на время игры. Возврат после выхода.", "Ваша видеокарта, частота экрана и параметры плавности.", "Приложения и компоненты. Предпросмотр, удаление, проверка." };
    public MainWindow(string[] startupArgs)
    {
        args = startupArgs;
        Title = "Qivoryx — Windows Toolkit";
        MinWidth = Math.Min(1100, Math.Max(900, SystemParameters.WorkArea.Width - 32));MinHeight = Math.Min(730, Math.Max(560, SystemParameters.WorkArea.Height - 32));
        Width = Math.Max(MinWidth, Math.Min(1440, SystemParameters.WorkArea.Width - 40));Height = Math.Max(MinHeight, Math.Min(940, SystemParameters.WorkArea.Height - 40));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (args.Contains("--smoke")) { WindowStartupLocation = WindowStartupLocation.Manual;Left = -12000;Top = 0;ShowInTaskbar = false; }
        var iconPath = Path.Combine(Engine.Root, "Assets", "app.ico");if (File.Exists(iconPath)) Icon = BitmapFrame.Create(new Uri(iconPath));
        FontFamily = new("Segoe UI"); FontSize = 13;
        Directory.CreateDirectory(Engine.Store);
        light = File.Exists(Path.Combine(Engine.Store, "light.theme"));
        packages = System.Text.Json.JsonSerializer.Deserialize<List<Package>>(File.ReadAllText(Path.Combine(Engine.Root, "catalog.json")), Engine.Json) ?? new();
        SetTheme(); BuildShell(); BuildChrome();
        Loaded += async (_, _) =>
        {
            await Navigate(0);
            InitializeGameSessions();
            if (args.Contains("--smoke")) await Smoke();
        };
        Closing += async (_, e) =>
        {
            if (sessionCloseApproved) return;
            e.Cancel = true;
            if (busy > 0) { Notify("Дождитесь завершения текущей операции. Принудительный выход может прервать изменение системы.");return; }
            if (sessionClosePending) return;
            sessionClosePending = true;
            try
            {
                if (await EndGameSessionsForCloseAsync())
                {
                    sessionCloseApproved = true;
                    _ = Dispatcher.BeginInvoke(new Action(Close));
                }
            }
            catch (Exception ex) { Notify("Не удалось завершить сессию: " + ex.Message); }
            finally { sessionClosePending = false; }
        };
    }
    TextBlock Text(string value, double size = 13, string color = "Fg", bool bold = false)
    {
        var t = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        t.SetResourceReference(TextBlock.ForegroundProperty, color); return t;
    }
    Button Button(string label, Action click, bool primary = false)
    {
        var b = new Button { Content = label, Margin = new(0, 0, 8, 0) };
        if (primary) { b.SetResourceReference(Control.BackgroundProperty, "Accent"); b.SetResourceReference(Control.ForegroundProperty, "OnAccent"); b.SetResourceReference(Control.BorderBrushProperty, "Accent"); }
        b.Click += (_, _) => click(); return b;
    }
    Border Card(UIElement body, Thickness? padding = null)
    {
        var b = new Border { Child = body, CornerRadius = new(14), BorderThickness = new(1), Padding = padding ?? new Thickness(22), Margin = new(0, 0, 0, 16) };
        b.SetResourceReference(Border.BackgroundProperty, "CardSurface"); b.SetResourceReference(Border.BorderBrushProperty, "Line");
        b.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, Opacity = light ? .035 : .14, BlurRadius = 18, ShadowDepth = 4 };return b;
    }
    static StackPanel Stack(params UIElement[] children) { var s = new StackPanel(); foreach (var c in children) s.Children.Add(c); return s; }
    static StackPanel Row(params UIElement[] children) { var s = Stack(children); s.Orientation = Orientation.Horizontal; return s; }
    void Gap(StackPanel panel, double height = 16) => panel.Children.Add(new Border { Height = height });
    void BuildShell()
    {
        root.SetResourceReference(Panel.BackgroundProperty, "Bg");
        root.ColumnDefinitions.Add(new() { Width = new(226) }); root.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var sidebar = new Grid { Margin = new(0) };
        sidebar.RowDefinitions.Add(new() { Height = GridLength.Auto }); sidebar.RowDefinitions.Add(new()); sidebar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var side = new Border { Child = sidebar, BorderThickness = new(0, 0, 1, 0) };side.SetResourceReference(Border.BackgroundProperty, "Side");side.SetResourceReference(Border.BorderBrushProperty, "Line"); root.Children.Add(side);
        var logo = new Border { Width = 38, Height = 38, CornerRadius = new(9), Child = Text("Q", 23, "OnAccent", true), Padding = new(8, 1, 0, 0) };logo.SetResourceReference(Border.BackgroundProperty, "Accent");
        var brand = Row(logo, new Border { Width = 12 }, Stack(Text("QIVORYX", 14, bold: true), Text("WINDOWS TOOLKIT", 9, "Muted")));brand.Margin = new(22, 28, 12, 32);sidebar.Children.Add(brand);
        nav.Margin = new(12, 0, 12, 0);var navScroll = new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };Grid.SetRow(navScroll, 1); sidebar.Children.Add(navScroll);
        nav.Children.Add(new Border { Child = Text("ПРОИЗВОДИТЕЛЬНОСТЬ", 9, "Muted", true), Margin = new(14, 0, 0, 14) });
        string[] glyphs = { "\uE80F", "\uE713", "\uE945", "\uE71D", "\uE74D", "\uE9D9", "\uEA18", "\uE81C", "\uE115", "\uE9D9", "\uE7E7", "\uE8F1", "\uE9D9", "\uE7FC", "\uE7F4", "\uE74D" };
        foreach (int i in new[] { 0, 9, 12, 13, 14, 11, 10, 1, 2, 3, 4, 15, 5, 6, 7, 8 })
        {
            int index = i;
            if (i is 11 or 3 or 7)
            {
                var section = i == 11 ? "СИСТЕМА" : i == 3 ? "ПРИЛОЖЕНИЯ И ДАННЫЕ" : "ПРИЛОЖЕНИЕ";
                nav.Children.Add(new Border { Child = Text(section, 9, "Muted", true), Margin = new(14, 18, 0, 12) });
            }
            var icon = Text(glyphs[i], 17); icon.FontFamily = new("Segoe MDL2 Assets");icon.Width = 30;
            var b = Button("", () => _ = Navigate(index));b.Content = Row(icon, Text(i == 0 ? "Обзор" : i == 2 ? "Питание" : i == 3 ? "Установить приложения" : i == 4 ? "Удалить приложения" : i == 5 ? "Очистка" : i == 6 ? "Defender" : i == 7 ? "Журнал" : titles[i], 12));
            b.HorizontalContentAlignment = HorizontalAlignment.Left;b.Padding = new(14, 11, 6, 11);b.Margin = new(0, 0, 0, 5);b.BorderThickness = new(0);b.Background = Brushes.Transparent;
            b.Tag = index;navigation.Add(b);nav.Children.Add(b);
        }
        var bottom = Stack(Text("ВАШ ПК. ВАШИ ПРАВИЛА.", 9, "Muted", true), new Border { Height = 10 }, Text("v0.4.0  /  DESKTOP x64", 11, "Muted"));bottom.Margin = new(26, 22, 12, 24);Grid.SetRow(bottom, 2);sidebar.Children.Add(bottom);
        var main = new Grid();Grid.SetColumn(main, 1);root.Children.Add(main);
        main.RowDefinitions.Add(new() { Height = GridLength.Auto });main.RowDefinitions.Add(new());main.RowDefinitions.Add(new() { Height = new(40) });
        var top = new Grid { Margin = new(34, 24, 30, 24) };top.ColumnDefinitions.Add(new());top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        breadcrumb.FontSize = 10;breadcrumb.SetResourceReference(TextBlock.ForegroundProperty, "Muted");breadcrumb.VerticalAlignment = VerticalAlignment.Center;top.Children.Add(breadcrumb);
        var theme = Button(light ? "◐  Тёмная тема" : "◐  Светлая тема", () => { light = !light;SaveTheme();SetTheme();_ = Navigate(page); });theme.Name = "ThemeButton";Grid.SetColumn(theme, 1);top.Children.Add(theme);main.Children.Add(top);
        var body = new Grid();body.RowDefinitions.Add(new() { Height = GridLength.Auto });body.RowDefinitions.Add(new());Grid.SetRow(body, 1);main.Children.Add(body);
        heading.FontSize = 30;heading.FontWeight = FontWeights.SemiBold;heading.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        subtitle.FontSize = 13;subtitle.Margin = new(0, 9, 0, 0);subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var header = Stack(heading, subtitle);header.Margin = new(34, 0, 34, 26);body.Children.Add(header);
        var scroll = pageScroll;scroll.Content = content;scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;scroll.Padding = new(34, 0, 30, 16);Grid.SetRow(scroll, 1);body.Children.Add(scroll);
        var footer = new Grid(); footer.SetResourceReference(Panel.BackgroundProperty, "Side");Grid.SetRow(footer, 2);main.Children.Add(footer);
        status.Text = "●  Система готова";status.FontSize = 11;status.Margin = new(34, 12, 16, 0);status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");footer.Children.Add(status);
        footer.Children.Add(progress);progress.VerticalAlignment = VerticalAlignment.Top;
        toast.Child = toastText;toast.SetResourceReference(Border.BackgroundProperty, "Accent");toastText.SetResourceReference(TextBlock.ForegroundProperty, "OnAccent");Grid.SetColumnSpan(toast, 2);root.Children.Add(toast);
        toastTimer.Tick += (_, _) => { toast.Visibility = Visibility.Collapsed;toastTimer.Stop(); };
        Content = root;
    }
    void SaveTheme() { var p = Path.Combine(Engine.Store, "light.theme");if (light) File.WriteAllText(p, "light");else if (File.Exists(p)) File.Delete(p); }
    async Task Navigate(int index)
    {
        page = index;int token = ++generation;pageScroll.ResetPosition();
        heading.Text = titles[index];subtitle.Text = descriptions[index];breadcrumb.Text = "QIVORYX   /   " + titles[index].ToUpperInvariant();
        foreach (var b in navigation) b.Background = Brushes.Transparent;
        navigation.Single(b => (int)b.Tag == index).SetResourceReference(Control.BackgroundProperty, "Hover");
        if (((Grid)root.Children[1]).Children[0] is Grid top && top.Children[1] is Button theme) theme.Content = light ? "◐  Тёмная тема" : "◐  Светлая тема";
        content.Children.Clear();content.Opacity = 0;
        AnimatePage();
        try
        {
            switch (index)
            {
                case 0: await Overview(token);break;
                case 1: await Services(token);break;
                case 2: await Power(token);break;
                case 3: Catalog();break;
                case 4: await Uninstall(token);break;
                case 5: Cleanup();break;
                case 6: await NativeDefender(token);break;
                case 7: Journal();break;
                case 8: Settings();break;
                case 9: await Optimization(token);break;
                case 10: await Startup(token);break;
                case 11: await Tuning(token);break;
                case 12: FrameLab();break;
                case 13: GameSessions();break;
                case 14: await Gpu(token);break;
                case 15: await RemovalCenter(token);break;
            }
            if (token == generation) AnimatePage();
        }
        catch (Exception ex) { if (args.Contains("--smoke")) throw;if (token == generation) content.Children.Add(Card(Stack(Text("Не удалось получить данные", 18, bold: true), new Border { Height = 12 }, Text(ex.Message, color: "Muted"), new Border { Height = 16 }, Button("Повторить", () => _ = Navigate(index)))));Notify(ex.Message); }
    }
    void Notify(string value) { toast.RenderTransform = new TranslateTransform();((TranslateTransform)toast.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });toastText.Text = value;toast.Visibility = Visibility.Visible;toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));toastTimer.Stop();toastTimer.Start(); }
    async Task<Reply> Query(object request, string label, bool admin = false)
    {
        busy++;progress.IsIndeterminate = true;progress.Visibility = Visibility.Visible;status.Text = "◌  " + label;
        try { return await Engine.Run(request, admin); }
        finally { busy--;if (busy == 0) { progress.IsIndeterminate = false;progress.Visibility = Visibility.Collapsed;status.Text = "●  Система готова  ·  " + DateTime.Now.ToString("HH:mm"); } }
    }
    async Task Act(object request, string label, bool admin = true, Func<Task>? after = null)
    {
        if (busy > 0) { Notify("Дождитесь завершения текущей операции.");return; }
        try { var r = await Query(request, label, admin);Notify(r.message);if (after != null) await after(); }
        catch (Exception ex) { Notify(ex.Message); }
    }
    bool Confirm(string title, string details, string action = "Продолжить")
    {
        var dialog = new Window { Owner = this, Title = title, Width = 560, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = FontFamily, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        dialog.Resources.MergedDictionaries.Add(Resources);
        dialog.Background = (Brush)Resources["Card"];dialog.Foreground = (Brush)Resources["Fg"];
        dialog.MaxHeight = SystemParameters.WorkArea.Height - 48;
        var explanation = new ScrollViewer { Content = Text(details, 13, "Muted"), MaxHeight = Math.Max(180, SystemParameters.WorkArea.Height - 260), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 0) };
        var titleText = Text(title, 22, bold: true);titleText.MouseLeftButtonDown += (_, _) => dialog.DragMove();
        var s = Stack(titleText, new Border { Height = 18 }, explanation, new Border { Height = 26 });s.Margin = new(28);
        var cancel = Button("Отмена", () => dialog.DialogResult = false);cancel.IsCancel = true;var accept = Button(action, () => dialog.DialogResult = true, true);
        var row = Row(cancel, accept);row.HorizontalAlignment = HorizontalAlignment.Right;s.Children.Add(row);
        var layout = new Grid();layout.SetResourceReference(Panel.BackgroundProperty, "Card");layout.Children.Add(s);var frame = new Border { Child = layout, BorderThickness = new Thickness(1) };frame.SetResourceReference(Border.BorderBrushProperty, "Line");dialog.Content = frame;return dialog.ShowDialog() == true;
    }
    Border Note(string title, string detail) => Card(Stack(Text(title, 14, bold: true), new Border { Height = 8 }, Text(detail, 12, "Muted")));
    async Task Overview(int token)
    {
        content.Children.Add(Note("Считываем состояние компьютера", "Данные поступают из Windows. Это может занять несколько секунд."));
        var result = await Query(new { op = "summary" }, "Сбор информации о системе");summary = result.data;if (generation != token) return;content.Children.Clear();
        var d = summary;
        var hero = new Grid();hero.ColumnDefinitions.Add(new());hero.ColumnDefinitions.Add(new() { Width = new(220) });
        var intro = Stack(Text("СИСТЕМА ПОД КОНТРОЛЕМ", 10, "Muted", true), new Border { Height = 16 }, Text("Меньше лишнего.\nБольше возможностей.", 32, bold: true), new Border { Height = 14 }, Text("Измерьте плавность. Настройте систему.\nСохраните то, что помогает именно вашему ПК.", 13, "Muted"), new Border { Height = 24 }, Button("Открыть центр оптимизации  →", () => _ = Navigate(9), true), new Border { Height = 10 }, Button("Создать точку восстановления", () => { if (Confirm("Точка восстановления", "Windows создаст точку восстановления, если включена Защита системы. Это не копия личных файлов. Запрос может быть ограничен частотой создания точек.")) _ = Act(new { op = "restorePoint" }, "Создание точки восстановления"); }));
        var heroActions = new WrapPanel();
        foreach (var action in intro.Children.OfType<Button>().ToList()) { intro.Children.Remove(action);action.HorizontalAlignment = HorizontalAlignment.Left;action.Margin = new(0, 0, 10, 8);heroActions.Children.Add(action); }
        intro.Children.Add(heroActions);hero.Children.Add(intro);
        var mark = new Grid { Width = 180, Height = 180, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 6; i++) { var line = new Border { Width = 180 - i * 24, Height = 180 - i * 24, BorderThickness = new(1), CornerRadius = new(20 + i * 2), RenderTransformOrigin = new(.5, .5), RenderTransform = new RotateTransform(15 + i * 9) };line.SetResourceReference(Border.BorderBrushProperty, "Line");mark.Children.Add(line); }
        var m = Text("Q", 46, bold: true);m.HorizontalAlignment = HorizontalAlignment.Center;m.VerticalAlignment = VerticalAlignment.Center;mark.Children.Add(m);Grid.SetColumn(mark, 1);hero.Children.Add(mark);
        content.Children.Add(Card(hero, new Thickness(28)));
        var stats = new Grid(); for (int i = 0; i < 3; i++) stats.ColumnDefinitions.Add(new());
        void Stat(int col, string label, string value, string hint, double percentage)
        {
            var bar = new ProgressBar { Value = percentage, Maximum = 100, Height = 3, Margin = new(0, 18, 0, 12), Foreground = (Brush)Resources["Accent"], Background = (Brush)Resources["Line"] };
            var c = Card(Stack(Text(label, 11, "Muted"), new Border { Height = 12 }, Text(value, 28, bold: true), bar, Text(hint, 11, "Muted")));c.Margin = new(0, 0, col == 2 ? 0 : 14, 16);Grid.SetColumn(c, col);stats.Children.Add(c);
        }
        var cpuLoad = d.GetProperty("CpuLoad");var hasCpuLoad = cpuLoad.ValueKind == System.Text.Json.JsonValueKind.Number;
        Stat(0, "ПРОЦЕССОР", hasCpuLoad ? $"{cpuLoad}%" : "—", d.GetProperty("CPU").GetString() ?? "CPU", hasCpuLoad ? cpuLoad.GetDouble() : 0);
        var used = d.GetProperty("RAMUsed").GetDouble();var total = d.GetProperty("RAMTotal").GetDouble();
        Stat(1, "ОПЕРАТИВНАЯ ПАМЯТЬ", $"{used:0.0} / {total:0.0} ГБ", "Используется в данный момент", used / total * 100);
        Stat(2, "СЛУЖБЫ WINDOWS", d.GetProperty("Running").ToString(), "Запущено из " + d.GetProperty("Services") + " служб", d.GetProperty("Running").GetDouble() / d.GetProperty("Services").GetDouble() * 100);
        content.Children.Add(stats);
        var workflow = new Grid();for (int i = 0; i < 3; i++) workflow.ColumnDefinitions.Add(new());
        var steps = new[] { ("01 / ИЗМЕРИТЬ", "Лаборатория кадров", "FPS, редкие просадки и сравнение A/B.", 12), ("02 / НАСТРОИТЬ", "Графика и дисплей", "Видеокарта, частота экрана и плавность.", 14), ("03 / ИГРАТЬ", "Игровые сессии", "Ваш план питания на время игры.", 13) };
        for (int i = 0; i < steps.Length; i++)
        {
            var step = steps[i];var open = Button("Открыть →", () => _ = Navigate(step.Item4));open.HorizontalAlignment = HorizontalAlignment.Left;
            var tile = Card(Stack(Text(step.Item1, 9, "Muted", true), new Border { Height = 12 }, Text(step.Item2, 16, bold: true), new Border { Height = 8 }, Text(step.Item3, 12, "Muted"), new Border { Height = 16 }, open));
            tile.Margin = new(0, 0, i == 2 ? 0 : 14, 16);Grid.SetColumn(tile, i);workflow.Children.Add(tile);
        }
        content.Children.Add(workflow);
        var lower = new Grid();lower.ColumnDefinitions.Add(new() { Width = new(1.2, GridUnitType.Star) });lower.ColumnDefinitions.Add(new());
        var diskStack = Stack(Text("Хранилище", 17, bold: true), new Border { Height = 18 });
        foreach (var disk in d.GetProperty("Drives").EnumerateArray())
        {
            var free = disk.GetProperty("Free").GetDouble();var cap = disk.GetProperty("Total").GetDouble();
            diskStack.Children.Add(Text($"{disk.GetProperty("Name")}     {free:0.0} ГБ свободно из {cap:0.0} ГБ", 12));
            diskStack.Children.Add(new ProgressBar { Value = cap == 0 ? 0 : (cap - free) / cap * 100, Maximum = 100, Height = 5, Margin = new(0, 10, 0, 18), Foreground = (Brush)Resources["Accent"], Background = (Brush)Resources["Line"] });
        }
        diskStack.Children.Add(Button("Перейти к очистке  ↗", () => _ = Navigate(5)));
        var dc = Card(diskStack);dc.Margin = new(0, 0, 14, 16);lower.Children.Add(dc);
        var sys = Stack(Text("Этот компьютер", 17, bold: true), new Border { Height = 18 }, Text(d.GetProperty("Computer").GetString() ?? "", 15, bold: true), new Border { Height = 8 }, Text($"{d.GetProperty("OS")}  ·  {d.GetProperty("Build")}", 12, "Muted"), new Border { Height = 14 }, Text($"Без перезагрузки: {d.GetProperty("Uptime")} ч", 12, "Muted"), new Border { Height = 14 }, Text(d.GetProperty("DefenderKnown").GetBoolean() ? d.GetProperty("Realtime").GetBoolean() ? "●  Защита в реальном времени включена" : "○  Защита в реальном времени выключена" : "○  Статус Defender недоступен", 12, "Muted"));
        var sc = Card(sys);Grid.SetColumn(sc, 1);lower.Children.Add(sc);content.Children.Add(lower);
    }
    void Journal()
    {
        content.Children.Clear();
        content.Children.Add(Row(Button("Обновить", Journal), Button("Открыть резервные копии", () => { Directory.CreateDirectory(Path.Combine(Engine.Store, "Backups"));Engine.Open(Path.Combine(Engine.Store, "Backups")); }), Button("Открыть журнал", () => { if (!File.Exists(Engine.LogPath)) File.WriteAllText(Engine.LogPath, "");Engine.Open(Engine.LogPath); })));
        Gap(content);
        var log = File.Exists(Engine.LogPath) ? File.ReadAllText(Engine.LogPath) : "Операций пока нет.";
        var box = new TextBox { Text = log.Length > 80000 ? log[^80000..] : log, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 490, FontFamily = new("Cascadia Mono, Consolas"), FontSize = 11 };content.Children.Add(Card(box));
        content.Children.Add(Note("Резервные копии служб", "Выберите конкретное состояние для восстановления. Записи сохраняются перед каждым изменением."));
        var dir = Path.Combine(Engine.Store, "Backups");
        if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir, "service-*.json").OrderDescending().Take(30))
        {
            var path = f;content.Children.Add(Card(Row(Text(Path.GetFileName(f), 12), new Border { Width = 20 }, Button("Восстановить", () => { if (Confirm("Восстановить службу?", File.ReadAllText(path))) _ = Act(new { op = "restoreService", path }, "Восстановление службы"); }))));
        }
    }
    void Settings()
    {
        content.Children.Add(Note("Qivoryx 0.4.0", $"Набор инструментов для Windows 10/11 x64. Режим: {(Engine.Portable ? "портативный" : "установленный")}. Настройки, журнал и копии: {Engine.Store}. Установленный .NET не требуется."));
        content.Children.Add(Card(Stack(Text("Оформление", 18, bold: true), new Border { Height = 16 }, Row(Button("Тёмная", () => { light = false;SaveTheme();SetTheme();_ = Navigate(8); }), Button("Светлая", () => { light = true;SaveTheme();SetTheme();_ = Navigate(8); })))));
        content.Children.Add(Card(Stack(Text("Данные и диагностика", 18, bold: true), new Border { Height = 12 }, Text("Журнал содержит имена пакетов, служб и пути на вашем компьютере. Приложение не отправляет телеметрию. Установщики и winget обращаются к своим серверам.", 13, "Muted"), new Border { Height = 16 }, Row(Button("Открыть папку данных", () => Engine.Open(Engine.Store)), Button("Справка", () => Engine.Open(Path.Combine(Engine.Root, "README.md"))), Button("Защита системы", () => Engine.Open("SystemPropertiesProtection.exe"))))));
        content.Children.Add(Note("Границы поддержки", "Инвентаризация приложений включает Win32 (HKLM/HKCU, x86/x64) и Appx текущего пользователя. Портативные программы без регистрации, чужие профили, драйверы и компоненты CBS не являются обычными приложениями. Windows может запрещать удаление защищённых пакетов и Edge."));
    }
    async Task Smoke()
    {
        var destination = Path.Combine(Engine.Root, "..", "..", "artifacts", "ui");Directory.CreateDirectory(destination);
        var completed = Path.Combine(destination, "smoke-complete.txt");if (File.Exists(completed)) File.Delete(completed);
        var assertions = new List<string>();
        IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);if (child is T match) yield return match;
                foreach (var nested in Find<T>(child)) yield return nested;
            }
        }
        void Save(FrameworkElement element, string name)
        {
            element.UpdateLayout();var bmp = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);bmp.Render(element);
            var encoder = new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var output = File.Create(Path.Combine(destination, name + ".png"));encoder.Save(output);
        }
        void Check(bool value, string name) { if (!value) throw new Exception("UI test: " + name);assertions.Add(name); }
        Check(IsAsymmetricX3d("AMD Ryzen 9 7950X3D 16-Core Processor") && IsAsymmetricX3d("AMD Ryzen 9 9950X3D 16-Core Processor"), "Scheduler guidance recognizes supported asymmetric X3D models");
        Check(!IsAsymmetricX3d("AMD Ryzen 7 9800X3D") && !IsAsymmetricX3d("AMD Ryzen 9 9950X3D2"), "Scheduler guidance excludes single-CCD and unknown X3D2 models");
        for (int mode = 0; mode < 2; mode++)
        {
            frameA = frameB = null;frameStatsA = frameStatsB = null;
            light = mode == 1;SetTheme();
            foreach (int p in new[] { 0, 9, 12, 13, 14, 11, 10, 1, 2, 3, 4, 15, 5, 6, 7, 8 })
            {
                await Navigate(p);await Task.Delay(350);UpdateLayout();
                Save((FrameworkElement)Content, $"{(light ? "light" : "dark")}-{p}");
                Check(!Find<TextBlock>(content).Any(t => new[] { "FSOS", "Igromanoff", "Playbook", "плейбук" }.Any(origin => t.Text.Contains(origin, StringComparison.OrdinalIgnoreCase))), "Page " + p + " has no source archive branding");
                if (p == 12)
                {
                    Check(Find<Button>(content).Count(b => b.Content is string x && x.Contains("Импорт")) >= 2, "Frame lab renders independent import slots");
                    await FrameLabSmoke();await Task.Delay(350);UpdateLayout();
                    Check(frameA?.Frames.Length > 0 && frameB?.Frames.Length > 0, "Frame lab fixture compares two labelled test captures without saving user data");
                    Save((FrameworkElement)Content, $"{(light ? "light" : "dark")}-frame-comparison");
                    Check(Find<FrameTimeChart>(content).Any(), "Frame lab renders a native frame-time chart");
                    pageScroll.ScrollToVerticalOffset(680);await Task.Delay(150);Save((FrameworkElement)Content, $"{(light ? "light" : "dark")}-frame-chart");pageScroll.ScrollToTop();
                }
                if (p == 13) Check(Find<ComboBox>(content).Any(), "Game session renders plan and process controls without starting a session");
                if (p == 14) Check(Find<Button>(content).Any(), "GPU inventory renders without changing a display mode");
                if (p == 6)
                {
                    Check(Find<ListView>(content).Any(), "Native Defender plan renders exact component targets without applying changes");
                    pageScroll.ScrollToVerticalOffset(550);await Task.Delay(160);Save((FrameworkElement)Content, $"{(light ? "light" : "dark")}-defender-plan");pageScroll.ScrollToTop();
                }
                if (p == 15)
                {
                    var list = Find<ListView>(content).First();
                    var input = Find<TextBox>(content).First();
                    input.Text = "7-Zip";
                    if (list.Items.Count == 0) input.Text = "";
                    Check(list.Items.Count > 0, "Removal center renders real installed application inventory");
                    list.SelectedIndex = 0;
                    for (int wait = 0; wait < 150 && busy > 0; wait++) await Task.Delay(200);
                    Check(Find<TextBlock>(content).Any(t => t.Text == "ПЛАН УДАЛЕНИЯ"), "Application selection creates a readonly removal preview");
                    Check(!Find<CheckBox>(content).Any(c => c.IsChecked == true), "Residual deletion is never preselected during preview");
                    pageScroll.ScrollToVerticalOffset(650);await Task.Delay(160);Save((FrameworkElement)Content, $"{(light ? "light" : "dark")}-removal-plan");pageScroll.ScrollToTop();
                }
                if (p == 1)
                {
                    var list = Find<ListView>(content).First();var input = Find<TextBox>(content).First();
                    input.Text = "RpcSs";Check(list.Items.Count == 1, "Service search filters by internal name");list.SelectedIndex = 0;
                    Check(Find<Button>(content).Where(b => b.Content is string s && new[] { "Запустить", "Остановить", "Автоматически", "Вручную", "Отключить" }.Contains(s)).All(b => !b.IsEnabled), "Critical service actions are disabled");
                    input.Text = "SysMain";if (list.Items.Count > 0) list.SelectedIndex = 0;
                    Save((FrameworkElement)Content, $"{(light ? "light" : "dark")}-service-detail");
                }
                if (p == 2)
                {
                    Check(!Find<ComboBox>(content).Any(c => c.Items.Cast<object>().Any(i => i.ToString()?.Contains("(DC)") == true)), "Desktop power page has no battery selector");
                    var sliders = Find<Slider>(content).ToList();Check(sliders.Count > 0, "Supported power settings render as editors");sliders[0].Value = sliders[0].Value == sliders[0].Minimum ? sliders[0].Minimum + 1 : sliders[0].Minimum;
                    Check(Find<TextBlock>(content).Any(t => t.Text == "Есть неприменённые изменения"), "Power edits remain staged until Apply");
                    var combo = Find<ComboBox>(content).First();combo.Focus();combo.IsDropDownOpen = true;await Task.Delay(200);UpdateLayout();
                    var popup = (System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup", combo);
                    Check(popup.IsOpen && popup.Child is Border, "Dropdown uses the custom themed popup");
                    Check(((SolidColorBrush)combo.Foreground).Color == ((SolidColorBrush)Resources["Fg"]).Color, "Dropdown foreground follows the active theme");
                    Save((FrameworkElement)popup.Child, $"{(light ? "light" : "dark")}-power-dropdown");combo.IsDropDownOpen = false;
                    var scroll = Find<ScrollViewer>(root).First(s => s.Content == content);scroll.ScrollToVerticalOffset(720);await Task.Delay(160);Save((FrameworkElement)Content, $"{(light ? "light" : "dark")}-power-scrolled");scroll.ScrollToTop();
                }
                if (p == 9) Check(Find<ComboBox>(content).Any(), "Optimization renders real storage and process controls");
                if (p == 10) Check(Find<ListView>(content).Any(), "Startup inventory renders without modifying entries");
                if (p == 11)
                {
                    Check(!Find<TextBlock>(content).Any(t => t.Text == "Недоступно"), "Tuning native getters read all four settings on this PC");
                    var before = Find<Button>(content).Count(b => b.Content is string text && text.StartsWith("Подробнее"));
                    Check(before == 4, "Tuning renders four independently reviewed tweaks");
                    Find<Button>(content).First(b => Equals(b.Content, "Игры")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    Check(Find<Button>(content).Count(b => b.Content is string text && text.StartsWith("Подробнее")) == 1, "Tuning category filters the cards");
                }
                if (p == 3)
                {
                    var first = Find<CheckBox>(content).Skip(1).First();first.IsChecked = true;
                    var search = Find<TextBox>(content).First();search.Text = "Telegram";Check(Find<CheckBox>(content).Count() == 2, "Catalog search narrows the package cards");
                    Check(Find<CheckBox>(content).Skip(1).First().IsChecked == true, "Catalog selection survives filtering");search.Text = "";
                }
                if (p == 4)
                {
                    var input = Find<TextBox>(content).First();input.Text = "Microsoft.WindowsStore";
                    var list = Find<ListView>(content).First();Check(list.Items.Count > 0, "Microsoft Store appears in the application inventory");list.SelectedIndex = 0;
                }
            }
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                var dialog = OwnedWindows.Cast<Window>().Single();Save((FrameworkElement)dialog.Content, $"{(light ? "light" : "dark")}-confirmation");dialog.DialogResult = false;
            }));
            Check(!Confirm("Проверка диалога", "Пример подтверждения системного действия. Отмена ничего не изменяет. Проверяем читаемость текста и кнопок в выбранной теме."), "Confirmation Cancel returns without a mutation");
            Width = 1100;Height = 730;
            foreach (int minimumPage in new[] { 3, 6, 12, 13, 14, 15 })
            {
                await Navigate(minimumPage);await Task.Delay(350);Save((FrameworkElement)Content, $"{(light ? "light" : "dark")}-minimum-{minimumPage}");
            }
            Width = 1440;Height = 940;
        }
        await File.WriteAllTextAsync(Path.Combine(destination, "ui-assertions.json"), System.Text.Json.JsonSerializer.Serialize(assertions, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(destination, "smoke-complete.txt"), "All 32 page/theme combinations rendered. No mutation operations were invoked.");
        Close();
    }
}
