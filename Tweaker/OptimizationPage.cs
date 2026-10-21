using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Qivoryx;

public record PerformanceProcess(string Name, int Pid, double? Cpu, double MemoryMB)
{
    public string CpuLabel => Cpu.HasValue ? $"{Cpu:0.0}%" : "—";
    public string MemoryLabel => $"{MemoryMB:0.0} МБ";
}
public record PerformanceDrive(string Letter, string Name, long Free, long Total, double FreePercent, string FileSystem)
{
    public string Label => $"{Name}   ·   свободно {Engine.Size(Free)}";
}
public record VisualTweak(string Id, string Name, bool Available, bool? Enabled, bool CanRestore, string Error);
public record PerformanceReport(DateTimeOffset Timestamp, double SampleSeconds, double? Cpu, long MemoryTotal, long MemoryAvailable, double MemoryUsedPercent, int Processes, List<PerformanceProcess> TopCpu, List<PerformanceProcess> TopMemory, List<PerformanceDrive> Drives, List<VisualTweak> VisualTweaks);
public record PerformanceSnapshot(DateTimeOffset Timestamp, double? Cpu, double MemoryUsedPercent, int Processes);

public partial class MainWindow
{
    async Task Optimization(int token)
    {
        content.Children.Add(Note("Анализируем нагрузку", "Снимаем нагрузку процессов в течение секунды, проверяем память, свободное место и настройки интерфейса. Система не изменяется."));
        var reply = await Query(new { op = "performanceReport" }, "Диагностика производительности");
        if (token != generation) return;
        var report = Engine.Read<PerformanceReport>(reply.data);
        content.Children.Clear();

        var hero = new Grid();hero.ColumnDefinitions.Add(new());hero.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var intro = Stack(Text("ОТ ДИАГНОСТИКИ К РЕЗУЛЬТАТУ", 9, "Muted", true), new Border { Height = 12 }, Text("Быстрее там, где это важно.", 25, bold: true), new Border { Height = 10 }, Text("Найдите источник нагрузки. Изменяйте по одному параметру.\nСравнивайте результат в одинаковой задаче.", 12, "Muted"));
        hero.Children.Add(intro);
        var refresh = Button("↻  Новый замер", () => _ = Navigate(9));refresh.VerticalAlignment = VerticalAlignment.Top;refresh.Margin = new(18, 0, 0, 0);Grid.SetColumn(refresh, 1);hero.Children.Add(refresh);
        content.Children.Add(Card(hero, new Thickness(24)));

        var metrics = new Grid();for (int i = 0; i < 4; i++) metrics.ColumnDefinitions.Add(new());
        void Metric(int col, string label, string value, string foot)
        {
            var body = Stack(Text(label, 9, "Muted", true), new Border { Height = 13 }, Text(value, 28, bold: true), new Border { Height = 10 }, Text(foot, 11, "Muted"));
            var card = Card(body, new Thickness(18));card.Margin = new(0, 0, col < 3 ? 10 : 0, 18);Grid.SetColumn(card, col);metrics.Children.Add(card);
        }
        Metric(0, "ПРОЦЕССОР", report.Cpu.HasValue ? $"{report.Cpu:0}%" : "—", "Снимок загрузки CPU");
        Metric(1, "ПАМЯТЬ", $"{report.MemoryUsedPercent:0}%", $"Доступно {Engine.Size(report.MemoryAvailable)}");
        Metric(2, "ПРОЦЕССЫ", report.Processes.ToString(), "В текущем снимке");
        Metric(3, "ИЗМЕРЕНО", report.Timestamp.ToLocalTime().ToString("HH:mm"), $"CPU процессов: {report.SampleSeconds:0.0} с");
        content.Children.Add(metrics);

        var playbook = Stack(Text("Точнее настроить систему", 16, bold: true), new Border { Height = 9 }, Text("Настройки отклика меню, указателя, записи игр и оформления. У каждой — текущее состояние и индивидуальный откат. Применяйте постепенно и сравнивайте поведение системы.", 12, "Muted"), new Border { Height = 16 });
        var playbookButton = Button("Тонкая настройка →", () => _ = Navigate(11));playbookButton.HorizontalAlignment = HorizontalAlignment.Left;playbook.Children.Add(playbookButton);content.Children.Add(Card(playbook));

        var findings = new StackPanel();
        void Finding(string title, string details, string action, Action execute)
        {
            var row = new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(Stack(Text(title, 14, bold: true), new Border { Height = 7 }, Text(details, 12, "Muted")));
            var b = Button(action, execute);b.Margin = new(18, 0, 0, 0);b.VerticalAlignment = VerticalAlignment.Center;Grid.SetColumn(b, 1);row.Children.Add(b);
            findings.Children.Add(Card(row, new Thickness(20)));
        }
        content.Children.Add(Text("Ваши точки роста", 19, bold: true));Gap(content, 14);
        if (report.Cpu >= 80) Finding("Процессор сейчас занят", "Загрузка выше 80%. Найдите приложение в списке ниже и проверьте, нужно ли оно во время игры или работы.", "Процессы ↗", () => Engine.Open("taskmgr.exe"));
        if (report.MemoryUsedPercent >= 85) Finding("Память близка к заполнению", "Используется больше 85% физической памяти. Закрытие ненужных приложений может уменьшить обращения к файлу подкачки.", "Автозагрузка →", () => _ = Navigate(10));
        foreach (var drive in report.Drives.Where(d => d.FreePercent < 10)) Finding($"Мало места на {drive.Name}", $"Свободно {drive.FreePercent:0.0}% ({Engine.Size(drive.Free)}). Нехватка места может мешать временным файлам и обновлениям.", "Очистить →", () => _ = Navigate(5));
        if (findings.Children.Count == 0) Finding("Явного дефицита ресурсов в этом снимке нет", "Один замер не отражает тяжёлую игровую сцену. Проверьте автозагрузку и повторите измерение, когда возникает замедление.", "Автозагрузка →", () => _ = Navigate(10));
        content.Children.Add(findings);

        var snapshotPath = Path.Combine(Engine.Store, "performance-baseline.json");
        var comparison = Text("Сохраните этот снимок, чтобы сравнить загрузку CPU и памяти после изменений.", 12, "Muted");
        void RenderBaseline()
        {
            if (!File.Exists(snapshotPath)) return;
            try
            {
                var baseline = JsonSerializer.Deserialize<PerformanceSnapshot>(File.ReadAllText(snapshotPath), Engine.Json);
                if (baseline != null) comparison.Text = $"Снимок от {baseline.Timestamp.ToLocalTime():dd.MM HH:mm}: CPU {(baseline.Cpu.HasValue ? $"{baseline.Cpu:0}%" : "—")}, память {baseline.MemoryUsedPercent:0}%, процессов {baseline.Processes}. Сейчас память {report.MemoryUsedPercent - baseline.MemoryUsedPercent:+0.0;-0.0;0.0} п. п., процессов {report.Processes - baseline.Processes:+0;-0;0}.";
            }
            catch { comparison.Text = "Предыдущий снимок недоступен. Новый замер можно сохранить заново."; }
        }
        RenderBaseline();
        var snapshot = Button("Сохранить снимок", () =>
        {
            try { File.WriteAllText(snapshotPath, JsonSerializer.Serialize(new PerformanceSnapshot(report.Timestamp, report.Cpu, report.MemoryUsedPercent, report.Processes), Engine.Json));RenderBaseline();Notify("Диагностический снимок сохранён в данных Qivoryx."); }
            catch (Exception ex) { Notify("Не удалось сохранить снимок: " + ex.Message); }
        });
        var baselineBody = Stack(Text("До и после", 15, bold: true), new Border { Height = 9 }, comparison, new Border { Height = 10 }, Text("Сравнивайте одинаковую нагрузку. Этот замер не измеряет FPS, задержку ввода или время загрузки Windows.", 11, "Muted"), new Border { Height = 16 }, snapshot);
        snapshot.HorizontalAlignment = HorizontalAlignment.Left;content.Children.Add(Card(baselineBody));

        Gap(content, 8);content.Children.Add(Text("Отзывчивость рабочего стола", 19, bold: true));Gap(content, 8);
        content.Children.Add(Text("Три независимые настройки. Сокращают визуальное ожидание при работе с интерфейсом; производительность игровой сцены обычно не меняют.", 12, "Muted"));Gap(content, 16);
        var visualDescriptions = new Dictionary<string, string>
        {
            ["client"] = "Убирает анимации клиентской области там, где приложения следуют системной настройке. Не отключает композицию DWM и не гарантирует отключение всех переходов Проводника.",
            ["menus"] = "Убирает анимацию появления стандартных меню. Меню открываются без анимационного ожидания; приложения с собственным интерфейсом могут игнорировать настройку.",
            ["tooltips"] = "Убирает анимацию стандартных всплывающих подсказок. Меняет ощущение скорости интерфейса, а не вычислительную мощность компьютера."
        };
        foreach (var tweak in report.VisualTweaks)
        {
            var info = Stack(Text(tweak.Name, 16, bold: true), new Border { Height = 8 }, Text(visualDescriptions.GetValueOrDefault(tweak.Id, "Системная настройка интерфейса."), 12, "Muted"), new Border { Height = 12 });
            var state = Text(!tweak.Available ? "Не удалось прочитать настройку: " + tweak.Error : tweak.Enabled == true ? "●  Анимация включена" : "○  Анимация выключена", 11, "Muted");
            info.Children.Add(state);Gap(info, 16);
            var actions = new WrapPanel();
            var apply = Button("Убрать анимацию", () =>
            {
                if (Confirm(tweak.Name, "Будет выключена только выбранная анимация текущего пользователя. Исходное значение сохранится для отката. Приложения могут применять настройку после повторного открытия. Это не игровой FPS-твик.", "Применить"))
                    _ = Act(new { op = "visualSet", id = tweak.Id }, "Изменение анимации", false, async () => { if (page == 9) await Navigate(9); });
            }, true);
            apply.IsEnabled = tweak.Available && tweak.Enabled == true;
            var restore = Button("Вернуть исходное", () =>
            {
                if (Confirm("Вернуть анимацию?", "Будет восстановлено значение, сохранённое Qivoryx для этого пользователя. Если настройка изменена другим приложением, откат остановится."))
                    _ = Act(new { op = "visualRestore", id = tweak.Id }, "Восстановление анимации", false, async () => { if (page == 9) await Navigate(9); });
            });restore.IsEnabled = tweak.CanRestore;
            actions.Children.Add(apply);actions.Children.Add(restore);info.Children.Add(actions);content.Children.Add(Card(info));
        }

        Gap(content, 8);content.Children.Add(Text("Накопители и обслуживание", 19, bold: true));Gap(content, 14);
        var maintenance = Stack(Text("Оптимизация локального тома", 16, bold: true), new Border { Height = 9 }, Text("Windows выбирает подходящую операцию для носителя: например, TRIM для поддерживаемого SSD или дефрагментацию HDD. Это обслуживание накопителя, а не универсальная прибавка FPS. Дождитесь завершения, не запускайте во время игры.", 12, "Muted"), new Border { Height = 18 });
        var disks = report.Drives.Where(d => d.FileSystem is "NTFS" or "ReFS").ToList();
        var driveChoice = new ComboBox { ItemsSource = disks, DisplayMemberPath = "Label", SelectedIndex = disks.Count > 0 ? 0 : -1, MinWidth = 260, MaxWidth = 400, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 10, 8) };
        var optimize = Button("Обслужить диск", () =>
        {
            if (driveChoice.SelectedItem is not PerformanceDrive selected) return;
            if (Confirm($"Обслужить диск {selected.Name}?", "Windows выберет операцию для этого локального тома. Действие может занять длительное время и нагружать накопитель. Отмены выполненных операций обслуживания нет; личные файлы не удаляются.", "Начать обслуживание"))
                _ = Act(new { op = "storageOptimize", letter = selected.Letter }, "Обслуживание накопителя", true);
        }, true);optimize.IsEnabled = disks.Count > 0;
        var storageActions = new WrapPanel();storageActions.Children.Add(driveChoice);storageActions.Children.Add(optimize);maintenance.Children.Add(storageActions);content.Children.Add(Card(maintenance));

        var components = Stack(Text("Хранилище компонентов Windows", 16, bold: true), new Border { Height = 9 }, Text("Очистка заменённых компонентов освобождает место в WinSxS. Она не ускоряет процессор. Проверка читает уже сохранённый статус повреждений; полное сканирование и восстановление этим действием не выполняются.", 12, "Muted"), new Border { Height = 18 });
        var componentActions = new WrapPanel();
        componentActions.Children.Add(Button("Очистить компоненты", () =>
        {
            if (Confirm("Очистить заменённые компоненты?", "Будет выполнен DISM StartComponentCleanup без ResetBase и без автоматической перезагрузки. Устаревшие версии компонентов могут быть удалены. Это действие не имеет собственного отката и может занять несколько минут.", "Начать очистку"))
                _ = Act(new { op = "componentCleanup" }, "Обслуживание компонентов", true);
        }));
        componentActions.Children.Add(Button("Проверить статус", () =>
        {
            if (Confirm("Проверить статус компонентов?", "DISM CheckHealth прочитает флаг ранее обнаруженного повреждения. Подробный вывод будет в журнале. Это не исправление и не полная проверка файлов.", "Проверить"))
                _ = Act(new { op = "componentCheck" }, "Проверка статуса компонентов", true, () => { Notify("Проверка завершена. Подробный результат доступен в журнале операций.");return Task.CompletedTask; });
        }));components.Children.Add(componentActions);content.Children.Add(Card(components));

        Gap(content, 8);content.Children.Add(Text("Что создаёт нагрузку", 19, bold: true));Gap(content, 8);
        content.Children.Add(Text($"CPU нормирован на весь процессор, измерен за {report.SampleSeconds:0.0} с. Память — рабочий набор; это не объём памяти, который обязательно освободится при закрытии. Недоступные счётчики показаны знаком «—».", 12, "Muted"));Gap(content, 16);
        var processes = Table(("ПРОЦЕСС", "Name", 340), ("PID", "Pid", 80), ("CPU", "CpuLabel", 100), ("ПАМЯТЬ", "MemoryLabel", 140));processes.Height = 320;processes.ItemsSource = report.TopCpu;
        var processChoice = new ComboBox { ItemsSource = new[] { "По загрузке CPU", "По объёму памяти" }, SelectedIndex = 0, Width = 210, Margin = new(0, 0, 12, 0) };
        processChoice.SelectionChanged += (_, _) => processes.ItemsSource = processChoice.SelectedIndex == 0 ? report.TopCpu : report.TopMemory;
        var processTools = new WrapPanel();processTools.Children.Add(processChoice);processTools.Children.Add(Button("Открыть диспетчер задач ↗", () => Engine.Open("taskmgr.exe")));content.Children.Add(processTools);Gap(content, 12);content.Children.Add(Card(processes, new Thickness(6)));

        var gaming = Stack(Text("Игры и драйверы", 16, bold: true), new Border { Height = 9 }, Text("Игровой режим и оптимизация оконных игр настраиваются в Windows. Оконные оптимизации применимы к поддерживаемым играм DirectX 10/11; результат зависит от игры, видеокарты и драйвера. Сравните одинаковую сцену, 1% low и время кадра. Обновления драйверов устанавливайте с сайта производителя.", 12, "Muted"), new Border { Height = 16 });
        var gameActions = new WrapPanel();gameActions.Children.Add(Button("Игровой режим ↗", () => Engine.Open("ms-settings:gaming-gamemode")));gameActions.Children.Add(Button("Настройки графики ↗", () => Engine.Open("ms-settings:display-advancedgraphics")));gameActions.Children.Add(Button("План питания →", () => _ = Navigate(2)));gaming.Children.Add(gameActions);content.Children.Add(Card(gaming));
    }
}
