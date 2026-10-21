using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace Qivoryx;

public partial class MainWindow
{
    FrameCapture? frameA, frameB;
    FrameStatistics? frameStatsA, frameStatsB;
    FrameLibraryEntry[] frameLibrary = [];
    bool frameWorking, frameLibraryLoaded;
    string frameOperation = "";
    double frameThreshold = 50;
    string FrameDirectory => Path.Combine(Engine.Store, "Captures");

    // Off-screen visual QA only. Never recorded or persisted as a real capture.
    internal async Task FrameLabSmoke()
    {
        double[] Make(bool after) => Enumerable.Range(0, 6000).Select(i => (after ? 7.6 : 9.4) + 1.4 * Math.Sin(i * .075) + .6 * Math.Cos(i * .21) + (i % (after ? 1701 : 731) == 320 ? (after ? 35 : 72) : 0)).ToArray();
        frameA = new() { Title = "ТЕСТОВЫЕ ДАННЫЕ · A", ProcessName = "visual-test.exe", ProcessId = "100", SwapChain = "0xTEST", Metric = "MsBetweenPresents", Scenario = "Тестовая сцена · 1920 × 1080 · одинаковые настройки · это не реальная запись", Frames = Make(false), Warnings = ["Синтетические данные только для проверки интерфейса. Не являются результатом измерения производительности."] };
        frameB = frameA with { Id = Guid.NewGuid().ToString("N"), Title = "ТЕСТОВЫЕ ДАННЫЕ · B", Frames = Make(true) };
        await RecalculateFrames();RenderFrameLab();
    }

    void FrameLab()
    {
        if (!frameLibraryLoaded) { frameLibraryLoaded = true;_ = RefreshFrameLibrary(); }
        var hero = new Grid();hero.ColumnDefinitions.Add(new());hero.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        hero.Children.Add(Stack(Text("ИЗМЕРИТЬ  /  СРАВНИТЬ  /  ПРОВЕРИТЬ", 9, "Muted", true), new Border { Height = 12 }, Text("Меньше догадок. Больше кадров.", 25, bold: true), new Border { Height = 12 }, Text("Проверьте изменения на реальном игровом замере: темп кадров, редкие просадки и длинные интервалы. Все вычисления — локально.", 12, "Muted")));
        var mark = Stack(Text("A  ↔  B", 29, bold: true), new Border { Height = 8 }, Text("FRAME LAB", 9, "Muted", true));mark.Margin = new(30, 0, 0, 0);mark.VerticalAlignment = VerticalAlignment.Center;Grid.SetColumn(mark, 1);hero.Children.Add(mark);
        content.Children.Add(Card(hero, new Thickness(25)));
        if (frameWorking)
        {
            var loading = new ProgressBar { Height = 3, IsIndeterminate = true, Margin = new(0, 0, 0, 10) };
            content.Children.Add(Card(Stack(loading, Text(frameOperation, 12, "Muted")), new Thickness(18)));
        }

        var slots = new Grid();slots.ColumnDefinitions.Add(new());slots.ColumnDefinitions.Add(new());
        var aCard = CaptureCard(false);aCard.Margin = new(0, 0, 12, 16);slots.Children.Add(aCard);
        var bCard = CaptureCard(true);Grid.SetColumn(bCard, 1);slots.Children.Add(bCard);content.Children.Add(slots);

        if (frameA != null || frameB != null)
        {
            var actions = new WrapPanel();
            actions.Children.Add(FrameButton("⇄  Поменять A и B", () => { (frameA, frameB) = (frameB, frameA);(frameStatsA, frameStatsB) = (frameStatsB, frameStatsA);RenderFrameLab(); }));
            actions.Children.Add(FrameButton("Экспорт HTML", () => _ = ExportFrames(true)));
            actions.Children.Add(FrameButton("CSV · " + (frameA != null ? "A" : "B"), () => _ = ExportFrames(false)));
            actions.Margin = new(0, 0, 0, 16);content.Children.Add(actions);
            if (frameStatsA != null || frameStatsB != null) FrameResults();
        }
        else
        {
            var empty = Stack(Text("Покажите системе конкретную задачу.", 20, bold: true), new Border { Height = 12 }, Text("1. Запишите 30–60 секунд одной и той же сцены в PresentMon или совместимом инструменте.\n2. Импортируйте исходный CSV в A. Измените одну настройку.\n3. Повторите маршрут, импортируйте B и сравните плавность.", 13, "Muted"), new Border { Height = 16 }, Text("Лаборатория анализирует запись кадров. Загрузка CPU сама по себе не превращается в FPS.", 11, "Muted"));
            content.Children.Add(Card(empty, new Thickness(26)));
        }

        var libraryBody = Stack(Text("Библиотека замеров", 18, bold: true), new Border { Height = 9 }, Text("Сохраняйте исходную точку и возвращайтесь к ней после настройки системы. Файлы остаются в папке данных приложения.", 12, "Muted"), new Border { Height = 18 });
        if (frameLibrary.Length == 0) libraryBody.Children.Add(Text("Сохранённых замеров пока нет. После импорта нажмите «Сохранить» в карточке A или B.", 12, "Muted"));
        else
        {
            var libraryCombo = new ComboBox { ItemsSource = frameLibrary, DisplayMemberPath = nameof(FrameLibraryEntry.Label), SelectedIndex = 0, MinHeight = 39, Margin = new(0, 0, 0, 14) };libraryBody.Children.Add(libraryCombo);
            var libraryActions = new WrapPanel();
            libraryActions.Children.Add(FrameButton("Открыть в A", () => { if (libraryCombo.SelectedItem is FrameLibraryEntry entry) _ = LoadFrame(entry, false); }));
            libraryActions.Children.Add(FrameButton("Открыть в B", () => { if (libraryCombo.SelectedItem is FrameLibraryEntry entry) _ = LoadFrame(entry, true); }));
            libraryActions.Children.Add(FrameButton("Удалить запись", () =>
            {
                if (libraryCombo.SelectedItem is not FrameLibraryEntry entry || !Confirm("Удалить замер из библиотеки?", entry.Title + "\nИсходный CSV не удаляется.", "Удалить")) return;
                _ = RunFrameOperation("Удаляем сохранённый замер…", async () => { await Task.Run(() => FrameLibrary.Delete(FrameDirectory, entry.Id));frameLibrary = await Task.Run(() => FrameLibrary.List(FrameDirectory));Notify("Запись удалена из библиотеки."); });
            }));
            libraryBody.Children.Add(libraryActions);
        }
        content.Children.Add(Card(libraryBody));
        content.Children.Add(Card(Stack(Text("Что именно измеряется", 16, bold: true), new Border { Height = 10 }, Text(FrameAnalyzer.Formula, 11, "Muted"), new Border { Height = 10 }, Text("Поддерживаются CSV с MsBetweenPresents, CPUFrameTime, MsBetweenAppStart и FrameTime в миллисекундах; запятая, точка с запятой или табуляция. При нескольких процессах или swapchain вы выбираете один поток. Разные метрики не объединяются. Ограничение: 64 МБ и 500 000 строк.", 11, "Muted"))));
    }

    Button FrameButton(string label, Action action, bool primary = false)
    {
        var button = Button(label, action, primary);button.IsEnabled = !frameWorking;button.Margin = new(0, 0, 8, 6);return button;
    }

    Border CaptureCard(bool slotB)
    {
        var capture = slotB ? frameB : frameA;var stats = slotB ? frameStatsB : frameStatsA;string slot = slotB ? "B" : "A";
        var body = Stack(Text(slot + (slotB ? "  /  ПОСЛЕ ИЗМЕНЕНИЙ" : "  /  ИСХОДНАЯ ТОЧКА"), 10, "Muted", true), new Border { Height = 16 });
        if (capture == null)
        {
            body.Children.Add(Text(slotB ? "С чем сравнить?" : "С чего начинаем?", 21, bold: true));Gap(body, 12);
            body.Children.Add(Text(slotB ? "Повторите ту же сцену после настройки. Одинаковые условия делают сравнение полезным." : "Импортируйте запись кадров игры или рабочей сцены. Название процесса и источник метрики сохранятся.", 12, "Muted"));Gap(body, 20);
            var import = FrameButton("＋  Импортировать CSV", () => _ = ImportFrames(slotB), true);import.HorizontalAlignment = HorizontalAlignment.Left;body.Children.Add(import);
        }
        else
        {
            var title = new TextBox { Text = capture.Title, MaxLength = 160, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 10), IsEnabled = !frameWorking };
            title.ToolTip = "Название замера. Изменения сохраняются в библиотеку кнопкой «Сохранить».";
            title.LostKeyboardFocus += (_, _) => UpdateFrameMetadata(slotB, title: title.Text);body.Children.Add(title);
            body.Children.Add(Text(capture.StreamLabel, 11, "Muted"));Gap(body, 8);body.Children.Add(Text(capture.Metric + (stats == null ? "" : $" · {stats.DurationSeconds:0.0} с"), 11, "Muted"));Gap(body, 14);
            body.Children.Add(Text("УСЛОВИЯ ЗАМЕРА", 9, "Muted", true));Gap(body, 8);
            var scenario = new TextBox { Text = capture.Scenario, MaxLength = 2000, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 70, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsEnabled = !frameWorking, ToolTip = "Укажите сцену, разрешение, пресет графики, лимит FPS, VSync и генерацию кадров." };
            scenario.LostKeyboardFocus += (_, _) => UpdateFrameMetadata(slotB, scenario: scenario.Text);body.Children.Add(scenario);Gap(body, 8);
            body.Children.Add(Text("Сцена · разрешение · пресет · лимит FPS · генерация кадров", 10, "Muted"));Gap(body, 16);
            var actions = new WrapPanel();actions.Children.Add(FrameButton("Сохранить", () =>
            {
                UpdateFrameMetadata(slotB, title.Text, scenario.Text);
                var current = slotB ? frameB : frameA;
                if (current != null) _ = RunFrameOperation("Сохраняем интервалы и условия…", async () => { await Task.Run(() => FrameLibrary.Save(FrameDirectory, current));frameLibrary = await Task.Run(() => FrameLibrary.List(FrameDirectory));Notify("Замер " + slot + " сохранён в библиотеке."); });
            }, true));
            actions.Children.Add(FrameButton("Заменить", () => _ = ImportFrames(slotB)));
            actions.Children.Add(FrameButton("Убрать", () => { if (slotB) { frameB = null;frameStatsB = null; } else { frameA = null;frameStatsA = null; }RenderFrameLab(); }));body.Children.Add(actions);
        }
        return Card(body, new Thickness(22));
    }

    void UpdateFrameMetadata(bool slotB, string? title = null, string? scenario = null)
    {
        var capture = slotB ? frameB : frameA;if (capture == null) return;
        var updated = capture with { Title = title == null ? capture.Title : (string.IsNullOrWhiteSpace(title) ? "Замер" : title.Trim()), Scenario = scenario ?? capture.Scenario };
        if (slotB) frameB = updated;else frameA = updated;
    }

    void FrameResults()
    {
        var metrics = new Grid();for (int i = 0; i < 4; i++) metrics.ColumnDefinitions.Add(new());
        bool comparable = frameA != null && frameB != null && frameA.Metric == frameB.Metric;
        void Metric(int col, string label, Func<FrameStatistics, double> select, string unit, bool higher)
        {
            var a = frameStatsA;var b = frameStatsB;var primary = b ?? a!;
            string currentLabel = b != null ? "B" : "A";
            var body = Stack(Text(label, 9, "Muted", true), new Border { Height = 12 }, Text(select(primary).ToString("0.0") + unit, 26, bold: true), new Border { Height = 9 }, Text(currentLabel + (a != null && b != null ? $"  ·  A: {select(a):0.0}{unit}" : "  ·  текущий замер"), 10, "Muted"));
            if (comparable && a != null && b != null)
            {
                double delta = (select(b) / select(a) - 1) * 100;Gap(body, 8);body.Children.Add(Text($"{delta:+0.0;-0.0;0.0}% к A  ·  {(higher ? "выше лучше" : "ниже лучше")}", 10, "Muted"));
            }
            var card = Card(body, new Thickness(17));card.Margin = new(0, 0, col < 3 ? 10 : 0, 16);Grid.SetColumn(card, col);metrics.Children.Add(card);
        }
        Metric(0, "СРЕДНИЙ FPS", s => s.AverageFps, "", true);Metric(1, "1% LOW", s => s.LowOnePercentFps, "", true);Metric(2, "P99 КАДРА", s => s.P99Ms, " мс", false);Metric(3, "МЕДИАНА", s => s.MedianMs, " мс", false);content.Children.Add(metrics);

        var chart = new FrameTimeChart { CaptureA = frameA, CaptureB = frameB, Threshold = frameThreshold, Height = 260, Margin = new(0, 10, 0, 0) };
        var graph = Stack(Text("Ритм кадра", 18, bold: true), new Border { Height = 8 }, Text("Меньше высота линии — быстрее кадр. Реже пики — ровнее темп. A — сплошная, B — пунктирная линия. Наведите курсор для точного значения.", 11, "Muted"), chart, Text("Ось X — доля последовательности кадров. Длительность записей может различаться; пики сохранены при сокращении точек.", 10, "Muted"));content.Children.Add(Card(graph));

        var thresholdBox = new TextBox { Text = frameThreshold.ToString("0.##"), Width = 82, Margin = new(0, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = !frameWorking };
        var thresholdActions = new WrapPanel();thresholdActions.Children.Add(thresholdBox);thresholdActions.Children.Add(FrameButton("Обновить порог", () =>
        {
            if (!double.TryParse(thresholdBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < 1 || value > 10_000) { Notify("Введите порог от 1 до 10 000 мс.");return; }
            _ = RunFrameOperation("Пересчитываем распределение интервалов…", async () => { frameThreshold = value;await RecalculateFrames(); });
        }));
        var threshold = Stack(Text("Длинные интервалы", 17, bold: true), new Border { Height = 8 }, Text($"Считаем каждый интервал строго больше {frameThreshold:0.##} мс. Это настраиваемый ориентир плавности; один длинный кадр не раскрывает причину задержки.", 12, "Muted"), new Border { Height = 16 }, thresholdActions, new Border { Height = 12 });
        void Interval(string slot, FrameStatistics s) => threshold.Children.Add(Text($"{slot}: {s.Stutters:N0} из {s.Count:N0} кадров  ·  {100.0 * s.Stutters / s.Count:0.00}%  ·  {s.Stutters / s.DurationSeconds * 60:0.0} в минуту  ·  максимум {s.MaxMs:0.00} мс  ·  P95 {s.P95Ms:0.00} мс", 12));
        if (frameStatsA != null) Interval("A", frameStatsA);if (frameStatsB != null) { Gap(threshold, 8);Interval("B", frameStatsB); }content.Children.Add(Card(threshold));

        var warnings = (frameA?.Warnings ?? []).Select(w => "A: " + w).Concat((frameB?.Warnings ?? []).Select(w => "B: " + w)).ToList();
        if (frameA != null && frameB != null) warnings.AddRange(FrameAnalyzer.ComparisonWarnings(frameA, frameB));
        if (warnings.Count > 0)
        {
            var notes = Stack(Text("Контекст результата", 16, bold: true));foreach (string warning in warnings) { Gap(notes, 12);notes.Children.Add(Text("•  " + warning, 11, "Muted")); }content.Children.Add(Card(notes));
        }
    }

    async Task ImportFrames(bool slotB)
    {
        if (frameWorking) return;
        var picker = new OpenFileDialog { Title = "Импортировать запись кадров", Filter = "Запись кадров (*.csv;*.tsv)|*.csv;*.tsv|Все файлы (*.*)|*.*", CheckFileExists = true, Multiselect = false };
        if (picker.ShowDialog(this) != true) return;
        await RunFrameOperation("Читаем CSV, разделяем процессы и проверяем интервалы…", async () =>
        {
            var imported = await Task.Run(() => FrameAnalyzer.Parse(picker.FileName));
            var selected = imported.Streams.Count == 1 ? imported.Streams[0] : SelectFrameStream(imported);
            if (selected == null) return;
            var stats = await Task.Run(() => FrameAnalyzer.Statistics(selected, frameThreshold));
            if (slotB) { frameB = selected;frameStatsB = stats; } else { frameA = selected;frameStatsA = stats; }
            Notify($"Импортирован замер {(slotB ? "B" : "A")}: {selected.Frames.Length:N0} кадров · {selected.ProcessName}.");
        });
    }

    FrameCapture? SelectFrameStream(FrameImport imported)
    {
        var dialog = new Window { Owner = this, Title = "Выберите поток кадров", Width = 680, Height = 480, MinWidth = 480, MinHeight = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, ResizeMode = ResizeMode.CanResize };
        dialog.SetResourceReference(Window.BackgroundProperty, "Bg");dialog.SetResourceReference(Window.ForegroundProperty, "Fg");
        var layout = new Grid { Margin = new(26) };layout.RowDefinitions.Add(new() { Height = GridLength.Auto });layout.RowDefinitions.Add(new());layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var title = Stack(Text("Один замер — один поток.", 23, bold: true), new Border { Height = 10 }, Text($"В CSV найдено потоков: {imported.Streams.Count}. Процессы, PID и swapchain разделены, чтобы FPS не складывался из разных источников.", 12, "Muted"), new Border { Height = 18 });layout.Children.Add(title);
        var list = new ListBox { ItemsSource = imported.Streams, DisplayMemberPath = nameof(FrameCapture.StreamLabel), SelectedIndex = 0, Padding = new(4), BorderThickness = new(1) };list.SetResourceReference(Control.BackgroundProperty, "Input");list.SetResourceReference(Control.ForegroundProperty, "Fg");list.SetResourceReference(Control.BorderBrushProperty, "Line");Grid.SetRow(list, 1);layout.Children.Add(list);
        FrameCapture? selected = null;
        var accept = Button("Использовать поток", () => { selected = list.SelectedItem as FrameCapture;dialog.DialogResult = selected != null; }, true);accept.IsDefault = true;
        var cancel = Button("Отмена", () => dialog.DialogResult = false);cancel.IsCancel = true;
        var footer = Row(accept, cancel);footer.Margin = new(0, 18, 0, 0);Grid.SetRow(footer, 2);layout.Children.Add(footer);dialog.Content = layout;
        return dialog.ShowDialog() == true ? selected : null;
    }

    async Task LoadFrame(FrameLibraryEntry entry, bool slotB)
    {
        await RunFrameOperation("Открываем сохранённые интервалы…", async () =>
        {
            var capture = await Task.Run(() => FrameLibrary.Load(FrameDirectory, entry.Id));var stats = await Task.Run(() => FrameAnalyzer.Statistics(capture, frameThreshold));
            if (slotB) { frameB = capture;frameStatsB = stats; } else { frameA = capture;frameStatsA = stats; }
        });
    }
    async Task RefreshFrameLibrary()
    {
        try { frameLibrary = await Task.Run(() => FrameLibrary.List(FrameDirectory));if (!frameWorking) RenderFrameLab(); }
        catch (Exception ex) { Notify("Библиотека замеров недоступна: " + ex.Message); }
    }
    async Task RecalculateFrames()
    {
        var a = frameA;var b = frameB;var threshold = frameThreshold;
        var result = await Task.Run(() => (A: a == null ? null : FrameAnalyzer.Statistics(a, threshold), B: b == null ? null : FrameAnalyzer.Statistics(b, threshold)));
        frameStatsA = result.A;frameStatsB = result.B;
    }
    async Task RunFrameOperation(string label, Func<Task> operation)
    {
        if (frameWorking) return;frameWorking = true;frameOperation = label;RenderFrameLab();
        try { await operation(); }
        catch (Exception ex) { Notify("Лаборатория кадров: " + ex.Message); }
        finally { frameWorking = false;frameOperation = "";RenderFrameLab(); }
    }
    void RenderFrameLab()
    {
        if (page != 12) return;content.Children.Clear();FrameLab();
    }
    async Task ExportFrames(bool html)
    {
        var a = frameA ?? frameB;if (a == null || frameWorking) return;
        var b = frameA != null ? frameB : null;
        var picker = new SaveFileDialog { Title = html ? "Экспорт отчёта лаборатории" : "Экспорт интервалов выбранного потока", Filter = html ? "HTML-отчёт (*.html)|*.html" : "CSV (*.csv)|*.csv", DefaultExt = html ? ".html" : ".csv", FileName = "Qivoryx-frames-" + DateTime.Now.ToString("yyyyMMdd-HHmm") };
        if (picker.ShowDialog(this) != true) return;
        await RunFrameOperation("Готовим локальный отчёт…", async () =>
        {
            await Task.Run(() => File.WriteAllText(picker.FileName, html ? FrameAnalyzer.ToHtml(a, b, frameThreshold) : FrameAnalyzer.ToCsv(a), new UTF8Encoding(true)));
            Notify(html ? "HTML-отчёт сохранён. Его можно открыть в браузере без интернета." : "CSV сохранён: один поток, исходная метрика и валидные интервалы.");
        });
    }
}

/// <summary>Native WPF chart. It plots imported intervals, never inferred FPS from CPU load.</summary>
public sealed class FrameTimeChart : FrameworkElement
{
    public FrameCapture? CaptureA { get; init; }
    public FrameCapture? CaptureB { get; init; }
    public double Threshold { get; init; } = 50;
    readonly Typeface typeface = new("Segoe UI");
    double pointer = -1;
    double? maximum;
    int cachedBuckets;
    FramePoint[] cachedA = [], cachedB = [];
    public FrameTimeChart()
    {
        SnapsToDevicePixels = true;Cursor = Cursors.Cross;
        MouseMove += (_, e) => { pointer = e.GetPosition(this).X;InvalidateVisual();UpdateToolTip(); };
        MouseLeave += (_, _) => { pointer = -1;InvalidateVisual(); };
        Loaded += (_, _) => { if (SystemParameters.ClientAreaAnimation) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))); };
    }
    Brush Theme(string name) => TryFindResource(name) as Brush ?? Brushes.Gray;
    void Label(DrawingContext dc, string text, Point point, Brush brush, double size = 10)
    {
        dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), point);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);if (ActualWidth < 100 || ActualHeight < 80) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var plot = new Rect(48, 20, ActualWidth - 60, ActualHeight - 50);
        maximum ??= Math.Max(CaptureA?.Frames.Max() ?? 0, CaptureB?.Frames.Max() ?? 0);double scale = Math.Max(1, maximum.Value * 1.05);
        int buckets = (int)Math.Max(1, plot.Width);
        if (cachedBuckets != buckets)
        {
            cachedBuckets = buckets;cachedA = CaptureA == null ? [] : FrameAnalyzer.Downsample(CaptureA.Frames, buckets);cachedB = CaptureB == null ? [] : FrameAnalyzer.Downsample(CaptureB.Frames, buckets);
        }
        var gridPen = new Pen(Theme("Line"), 1);
        for (int row = 0; row <= 4; row++)
        {
            double y = plot.Y + row * plot.Height / 4;dc.DrawLine(gridPen, new Point(plot.X, y), new Point(plot.Right, y));
            Label(dc, (scale * (4 - row) / 4).ToString("0.#"), new Point(0, y - 7), Theme("Muted"));
        }
        for (int col = 0; col <= 4; col++)
        {
            double x = plot.X + plot.Width * col / 4;dc.DrawLine(gridPen, new Point(x, plot.Y), new Point(x, plot.Bottom));Label(dc, $"{col * 25}%", new Point(x - (col == 4 ? 25 : 0), plot.Bottom + 8), Theme("Muted"));
        }
        dc.PushClip(new RectangleGeometry(plot));
        if (Threshold < scale)
        {
            double y = plot.Bottom - Threshold / scale * plot.Height;dc.DrawLine(new Pen(Theme("Muted"), 1) { DashStyle = DashStyles.Dot }, new Point(plot.Left, y), new Point(plot.Right, y));
        }
        void DrawCapture(FrameCapture? capture, string color, bool dashed)
        {
            if (capture == null) return;
            var points = ReferenceEquals(capture, CaptureA) ? cachedA : cachedB;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                Point Map(FramePoint p) => new(plot.X + p.Index * plot.Width / Math.Max(1, capture.Frames.Length - 1), plot.Bottom - p.Milliseconds / scale * plot.Height);
                context.BeginFigure(Map(points[0]), false, false);foreach (var p in points.Skip(1)) context.LineTo(Map(p), true, false);
            }
            geometry.Freeze();var pen = new Pen(Theme(color), 1.3);if (dashed) pen.DashStyle = DashStyles.Dash;dc.DrawGeometry(null, pen, geometry);
        }
        DrawCapture(CaptureA, "Fg", false);DrawCapture(CaptureB, "Muted", true);
        if (pointer >= plot.Left && pointer <= plot.Right) dc.DrawLine(new Pen(Theme("Muted"), 1), new Point(pointer, plot.Top), new Point(pointer, plot.Bottom));
        dc.Pop();
        Label(dc, "мс", new Point(0, 0), Theme("Muted"));
    }
    void UpdateToolTip()
    {
        double ratio = Math.Clamp((pointer - 48) / Math.Max(1, ActualWidth - 60), 0, 1);
        string Value(string slot, FrameCapture? capture)
        {
            if (capture == null) return "";int index = Math.Clamp((int)Math.Round(ratio * (capture.Frames.Length - 1)), 0, capture.Frames.Length - 1);
            return $"{slot}: кадр {index + 1:N0} · {capture.Frames[index]:0.00} мс";
        }
        ToolTip = string.Join("\n", new[] { Value("A", CaptureA), Value("B", CaptureB) }.Where(s => s.Length > 0));
    }
}
