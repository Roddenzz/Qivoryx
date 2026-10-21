using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Qivoryx;

public record GpuAdapter(string Name, string Vendor, string Driver, string Status, string PnpId);
public record GpuDisplay(string Name, string Adapter, int Width, int Height, int Frequency, List<int> Modes, bool CanRestore, string? Error);
public record GpuX3D(string Model, string Service, string GameBar, string GameMode);
public record GpuReport(List<GpuAdapter> Adapters, List<GpuDisplay> Displays, bool HasNvidia, bool HasAmd, bool HasIntel, GpuX3D? X3D);
public record GpuPreviewState(string Phase, DateTimeOffset DeadlineUtc, string? Device, int Frequency);

public partial class MainWindow
{
    async Task Gpu(int token)
    {
        content.Children.Add(Note("Проверяем графическую систему", "Определяем адаптеры, драйверы и поддерживаемые частоты подключённых экранов. Настройки не меняются."));
        var reply = await Query(new { op = "gpuRead" }, "Диагностика графики");
        if (token != generation) return;
        var report = Engine.Read<GpuReport>(reply.data);content.Children.Clear();

        var hero = new Grid();hero.ColumnDefinitions.Add(new());hero.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        hero.Children.Add(Stack(Text("ГРАФИКА / ПЛАВНОСТЬ / ЗАДЕРЖКА", 9, "Muted", true), new Border { Height = 13 }, Text("Каждый кадр — вовремя.", 28, bold: true), new Border { Height = 11 }, Text("Настройте экран и профиль игры под свою видеокарту.\nНачните с того, что ограничивает именно вашу систему.", 12, "Muted")));
        var refresh = Button("↻  Обновить", () => _ = Navigate(14));refresh.VerticalAlignment = VerticalAlignment.Top;refresh.Margin = new(16, 0, 0, 0);Grid.SetColumn(refresh, 1);hero.Children.Add(refresh);content.Children.Add(Card(hero, new Thickness(25)));

        if (report.Adapters.Count == 0) content.Children.Add(Note("Адаптеры не обнаружены", "Windows не вернула сведения о видеокартах. Проверьте драйвер в диспетчере устройств."));
        foreach (var adapter in report.Adapters)
        {
            var hardware = new Grid();hardware.ColumnDefinitions.Add(new());hardware.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            hardware.Children.Add(Stack(Text(adapter.Vendor.ToUpperInvariant(), 9, "Muted", true), new Border { Height = 9 }, Text(adapter.Name, 18, bold: true), new Border { Height = 9 }, Text("Драйвер " + adapter.Driver + "   ·   " + (adapter.Status == "OK" ? "Устройство работает" : "Статус: " + adapter.Status), 11, "Muted")));
            if (adapter.Vendor is "NVIDIA" or "AMD")
            {
                var name = adapter.Vendor == "NVIDIA" ? "nvidia" : "amd";
                var panel = Button(adapter.Vendor == "NVIDIA" ? "Панель NVIDIA  ↗" : "AMD Software  ↗", () => _ = Act(new { op = "gpuPanel", name }, "Открытие панели графики", false));panel.Margin = new(18, 0, 0, 0);panel.VerticalAlignment = VerticalAlignment.Center;Grid.SetColumn(panel, 1);hardware.Children.Add(panel);
            }
            content.Children.Add(Card(hardware));
        }

        content.Children.Add(Text("Частота экрана", 19, bold: true));Gap(content, 8);
        content.Children.Add(Text("Более высокая поддерживаемая частота делает движение плавнее. Она не увеличивает скорость рендеринга игры. Разрешение, ориентация и цветность сохраняются.", 12, "Muted"));Gap(content, 16);
        if (report.Displays.Count == 0) content.Children.Add(Note("Нет доступных экранов", "В удалённой сессии или с виртуальным драйвером изменение частоты может быть недоступно. Используйте параметры Windows."));
        int displayNumber = 0;
        foreach (var display in report.Displays)
        {
            displayNumber++;
            var title = new Grid();title.ColumnDefinitions.Add(new());title.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            title.Children.Add(Stack(Text("ЭКРАН " + displayNumber, 9, "Muted", true), new Border { Height = 9 }, Text($"{display.Width} × {display.Height}", 19, bold: true), new Border { Height = 7 }, Text(display.Adapter, 11, "Muted")));
            var hz = Stack(Text(display.Frequency.ToString(), 34, bold: true), Text("ГЦ СЕЙЧАС", 9, "Muted", true));hz.Margin = new(16, 0, 0, 0);Grid.SetColumn(hz, 1);title.Children.Add(hz);
            var choice = new ComboBox { ItemsSource = display.Modes, SelectedItem = display.Frequency, Width = 165, Margin = new(0, 0, 10, 6) };
            var preview = Button("Проверить частоту", () =>
            {
                if (choice.SelectedItem is int frequency)
                    _ = PreviewGpuDisplay(display, frequency, false);
            }, true);preview.Margin = new(0, 0, 8, 6);preview.IsEnabled = display.Modes.Count > 1;
            var restore = Button("Вернуть исходную", () => _ = PreviewGpuDisplay(display, display.Frequency, true));restore.IsEnabled = display.CanRestore;restore.Margin = new(0, 0, 0, 6);
            var actions = new WrapPanel();actions.Children.Add(choice);actions.Children.Add(preview);actions.Children.Add(restore);
            var body = Stack(title, new Border { Height = 20 }, actions, new Border { Height = 7 }, Text("Экран переключится на 15 секунд. Подтвердите, если изображение в порядке. Без подтверждения обработчик вернёт прежний режим, даже если окно приложения закроется.", 11, "Muted"));
            if (!string.IsNullOrWhiteSpace(display.Error)) { Gap(body, 9);body.Children.Add(Text(display.Error, 11, "Muted")); }
            content.Children.Add(Card(body));
        }

        if (report.X3D is { } x3d)
        {
            content.Children.Add(Card(Stack(Text("RYZEN 9 X3D / ИГРОВОЙ ПЛАНИРОВЩИК", 9, "Muted", true), new Border { Height = 10 }, Text(x3d.Model, 17, bold: true), new Border { Height = 12 }, Text("3D V-Cache Optimizer: " + x3d.Service, 12), new Border { Height = 7 }, Text("Xbox Game Bar: " + x3d.GameBar, 12), new Border { Height = 7 }, Text("Game Mode: " + x3d.GameMode, 11, "Muted"), new Border { Height = 14 }, Text("У этой модели два CCD с разными свойствами. Сохраните штатное управление парковкой, Xbox Game Bar и актуальный драйвер чипсета AMD: они участвуют в выборе ядер для игры. Принудительное снятие парковки со всех ядер может мешать этому механизму.", 12), new Border { Height = 9 }, Text("Это проверка компонентов, а не измерение правильности размещения потоков. Отсутствующее явное значение Game Mode не доказывает отключение. Настройки процессора не изменяются.", 11, "Muted"))));
        }
        content.Children.Add(Text("Настройки под вашу задачу", 19, bold: true));Gap(content, 14);
        void Advice(string label, string title, string benefit, string condition, string action, string panel, bool enabled = true)
        {
            var body = Stack(Text(label, 9, "Muted", true), new Border { Height = 9 }, Text(title, 17, bold: true), new Border { Height = 10 }, Text(benefit, 12), new Border { Height = 9 }, Text(condition, 11, "Muted"), new Border { Height = 16 });
            var b = Button(action, () => _ = Act(new { op = "gpuPanel", name = panel }, "Открытие параметров графики", false));b.IsEnabled = enabled;b.HorizontalAlignment = HorizontalAlignment.Left;body.Children.Add(b);content.Children.Add(Card(body));
        }
        if (report.HasNvidia)
        {
            Advice("NVIDIA / ЗАДЕРЖКА", "Reflex в поддерживаемой игре", "Если игра поддерживает NVIDIA Reflex, начните с него в её настройках. Он согласует работу CPU и GPU и может уменьшить очередь рендеринга, особенно при ограничении со стороны GPU.", "Включается внутри совместимой игры. Наличие NVIDIA само по себе не подтверждает поддержку Reflex. Для игр без Reflex проверьте режим низкой задержки в профиле драйвера; эффект зависит от API и нагрузки.", "Открыть панель NVIDIA  ↗", "nvidia");
            Advice("NVIDIA / ПРОФИЛЬ ИГРЫ", "Частоты — по ситуации", "Режим максимальной производительности в отдельном профиле иногда помогает, если частоты GPU скачут и это связано с подёргиваниями.", "Не используйте его глобально без причины: растут потребление, нагрев и шум. Если GPU уже работает на стабильной частоте под нагрузкой, улучшения может не быть.", "Настроить профиль игры  ↗", "nvidia");
        }
        if (report.HasAmd)
        {
            Advice("AMD / ЗАДЕРЖКА", "Anti-Lag — с проверкой совместимости", "В совместимых играх и на поддерживаемой видеокарте Radeon Anti-Lag может уменьшить задержку управления. Anti-Lag 2 требует интеграции разработчиком игры.", "Проверьте доступность в AMD Software и настройках конкретной игры. Это не гарантия роста FPS. Не включайте одновременно несколько технологий задержки без сравнения результата и проверки рекомендаций игры.", "Открыть AMD Software  ↗", "amd");
            Advice("AMD / ПЛАВНОСТЬ", "FreeSync и ограничение кадров", "При поддержке FreeSync монитором и выбранным подключением переменная частота помогает согласовать вывод с кадрами игры. Ограничитель FPS может уменьшить колебания времени кадра и нагрузку.", "Наличие AMD не означает, что монитор поддерживает FreeSync. Настройте диапазон VRR и лимит под конкретную игру; универсального лимита и обязательного прироста FPS нет.", "Проверить настройки дисплея  ↗", "amd");
        }
        Advice("WINDOWS / ПРОФИЛЬ ПРИЛОЖЕНИЯ", "Нужная видеокарта для игры", "На компьютере с несколькими GPU назначьте требуемой игре производительный адаптер в параметрах графики Windows. Для подходящих оконных игр доступны оптимизации презентации Windows 11.", "На системе с одной GPU выбор адаптера обычно ничего не меняет. Аппаратное планирование GPU и оконные оптимизации показываются Windows только при поддержке драйвером; сравнивайте эффект в своей игре.", "Параметры графики Windows  ↗", "windows");
        Advice("ВСЕ ВИДЕОКАРТЫ / ИЗМЕРЕНИЯ", "Стабильное время кадра", "Сравнивайте одинаковую сцену, время кадра и задержку. Лимит FPS, VRR и режим задержки решают разные задачи: выберите сочетание под загрузку GPU и возможности монитора.", "Универсальная запись драйверных ключей не заменяет профиль игры. Перегрев, ограничение CPU, нехватка VRAM или компиляция шейдеров требуют разных решений.", "Расширенные параметры экрана  ↗", "display");
    }

    static string GpuPreviewPath(string token, string name)
    {
        if (token.Length != 32 || token.Any(c => !"0123456789abcdef".Contains(c))) throw new InvalidOperationException("Неверный идентификатор предпросмотра.");
        var directory = Path.Combine(Engine.Store, "GpuPreview");
        for (DirectoryInfo? parent = new(directory); parent != null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Папка предпросмотра является ссылкой.");
        var path = Path.Combine(directory, name + "-" + token + (name == "state" ? ".json" : ".txt"));
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Файл предпросмотра является ссылкой.");
        return path;
    }
    async Task PreviewGpuDisplay(GpuDisplay display, int frequency, bool restore)
    {
        if (busy > 0) { Notify("Дождитесь завершения текущей операции.");return; }
        if (!restore && display.Frequency == frequency) { Notify("Эта частота уже используется.");return; }
        var token = Guid.NewGuid().ToString("N");Task<Reply>? operation = null;
        try
        {
            var statePath = GpuPreviewPath(token, "state");
            operation = Query(new { op = "gpuDisplayPreview", token, device = display.Name, frequency, restore }, "Проверка режима экрана");
            while (!operation.IsCompleted)
            {
                if (File.Exists(statePath))
                {
                    GpuPreviewState? state = null;
                    try { state = JsonSerializer.Deserialize<GpuPreviewState>(await File.ReadAllTextAsync(statePath), Engine.Json); } catch (JsonException) { } catch (IOException) { }
                    if (state?.Phase == "preview")
                    {
                        bool accept = page == 14 && ConfirmGpuPreview(state.DeadlineUtc, state.Frequency);
                        await File.WriteAllTextAsync(GpuPreviewPath(token, "answer"), accept ? "confirm" : "cancel");break;
                    }
                }
                await Task.Delay(120);
            }
            var reply = await operation;Notify(reply.message);
            if (page == 14) await Navigate(14);
        }
        catch (Exception ex)
        {
            try { var answer = GpuPreviewPath(token, "answer");if (Directory.Exists(Path.GetDirectoryName(answer))) await File.WriteAllTextAsync(answer, "cancel"); } catch { }
            if (operation != null && !operation.IsCompleted) try { await operation; } catch { }
            Notify(ex.Message);
        }
    }
    bool ConfirmGpuPreview(DateTimeOffset deadline, int frequency)
    {
        var dialog = new Window { Owner = this, Title = "Проверка изображения", Width = 510, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.None, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, FontFamily = FontFamily };
        dialog.Resources.MergedDictionaries.Add(Resources);dialog.SetResourceReference(BackgroundProperty, "Card");dialog.SetResourceReference(ForegroundProperty, "Fg");
        var seconds = Text("", 33, bold: true);
        var body = Stack(Text("Изображение в порядке?", 23, bold: true), new Border { Height = 16 }, Text($"Выбрано {frequency} Гц. Если изображение пропало или мерцает, дождитесь автоматического возврата.", 13, "Muted"), new Border { Height = 18 }, seconds, new Border { Height = 20 });
        var buttons = new WrapPanel();buttons.Children.Add(Button("Вернуть", () => dialog.DialogResult = false));buttons.Children.Add(Button("Сохранить частоту", () => dialog.DialogResult = true, true));body.Children.Add(buttons);
        dialog.Content = Card(body, new Thickness(27));
        var initialRemaining = Math.Clamp((deadline - DateTimeOffset.UtcNow).TotalSeconds, 0, 15);
        var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        void Tick() { var remaining = Math.Max(0, (int)Math.Ceiling(Math.Min((deadline - DateTimeOffset.UtcNow).TotalSeconds, initialRemaining - watch.Elapsed.TotalSeconds)));seconds.Text = remaining + " с";if (remaining == 0 && dialog.IsVisible) dialog.DialogResult = false; }
        timer.Tick += (_, _) => Tick();dialog.Closed += (_, _) => timer.Stop();Tick();timer.Start();
        return dialog.ShowDialog() == true && watch.Elapsed.TotalSeconds < initialRemaining && DateTimeOffset.UtcNow < deadline;
    }
}
