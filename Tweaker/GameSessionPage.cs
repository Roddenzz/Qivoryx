using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Qivoryx;

public partial class MainWindow
{
    GameSessionManager? gameSessionManager;
    Task? gameSessionInitialization;
    string gameSessionError = "";
    string gameExecutable = "";
    Guid gamePlan;
    GameProcessInfo? selectedGameProcess;
    bool gameLaunchMode = true;
    bool gameRaisePriority;
    bool gameSessionActionBusy;
    static string GameRecoveryDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Qivoryx", "Sessions");

    // Call once after the main window has loaded. A single recovery location is
    // shared by installed and portable copies to prevent overlapping sessions.
    void InitializeGameSessions()
    {
        if (gameSessionInitialization != null) return;
        gameSessionInitialization = InitializeGameSessionsCoreAsync();
    }
    async Task InitializeGameSessionsCoreAsync()
    {
        try
        {
            gameSessionManager = new GameSessionManager(new WindowsGameSessionPlatform(), new GameSessionRecoveryStore(GameRecoveryDirectory));
            gameSessionManager.Changed += () =>
            {
                if (!Dispatcher.HasShutdownStarted) _ = Dispatcher.BeginInvoke(new Action(() => { if (page == 13) GameSessions(); }));
            };
            await gameSessionManager.InitializeAsync(recover: !args.Contains("--smoke"));
        }
        catch (Exception ex) { gameSessionError = ex.Message;if (!args.Contains("--smoke")) Notify("Восстановление игровой сессии: " + ex.Message); }
        finally { if (page == 13) GameSessions(); }
    }

    // In Closing: cancel the first close, await this method, then close again
    // using a guard. False keeps the window open for recovery retry.
    async Task<bool> EndGameSessionsForCloseAsync()
    {
        if (args.Contains("--smoke")) return true;
        if (gameSessionInitialization != null) await gameSessionInitialization;
        if (gameSessionManager == null || gameSessionManager.Current == null) return true;
        try
        {
            bool complete = await gameSessionManager.StopAsync();
            if (!complete) Notify(gameSessionManager.LastMessage);
            return complete;
        }
        catch (Exception ex) { Notify("Не удалось закончить игровую сессию: " + ex.Message);return false; }
    }

    void GameSessions()
    {
        content.Children.Clear();
        if (gameSessionManager == null)
        {
            content.Children.Add(Note(string.IsNullOrEmpty(gameSessionError) ? "Готовим игровые сессии" : "Игровые сессии недоступны", string.IsNullOrEmpty(gameSessionError) ? "Проверяем планы питания и запись восстановления. Игра не запускается." : gameSessionError));
            if (string.IsNullOrEmpty(gameSessionError)) InitializeGameSessions();return;
        }
        var manager = gameSessionManager;
        if (!string.IsNullOrEmpty(gameSessionError))
        {
            content.Children.Add(Note("Запись восстановления требует внимания", gameSessionError + " Новая сессия не перезапишет недоступную запись. Проверьте текущий план питания и сохраните файл восстановления перед ручным удалением."));
            content.Children.Add(Row(Button("Папка восстановления  ↗", OpenGameRecovery), Button("Питание Windows  ↗", () => Engine.Open("powercfg.cpl"))));Gap(content);
        }
        var hero = new Grid();hero.ColumnDefinitions.Add(new());hero.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        hero.Children.Add(Stack(Text("Настройки на время игры.", 26, bold: true), new Border { Height = 10 }, Text("Выберите процесс и существующий план питания. После выхода из игры Qivoryx вернёт прежние параметры.", 13, "Muted")));
        var badge = Text(manager.IsRunning ? "●  СЕССИЯ АКТИВНА" : manager.HasPendingRecovery ? "○  НУЖЕН ОТКАТ" : "○  ГОТОВО К ЗАПУСКУ", 10, "Muted", true);badge.Margin = new(24, 6, 0, 0);Grid.SetColumn(badge, 1);hero.Children.Add(badge);
        content.Children.Add(Card(hero, new Thickness(26)));
        if (manager.Current is GameSessionRecord current)
        {
            var active = Stack(Text(current.Process.Name, 23, bold: true), new Border { Height = 7 }, Text($"PID {current.Process.Id}  ·  начата {current.StartedUtc.ToLocalTime():HH:mm:ss}", 11, "Muted"), new Border { Height = 16 }, Text(current.Process.ExecutablePath, 12, "Muted"), new Border { Height = 18 });
            active.Children.Add(Text("ПЛАН НА ВРЕМЯ СЕССИИ", 9, "Muted", true));Gap(active, 7);active.Children.Add(Text(current.AppliedPlanName, 17, bold: true));Gap(active, 14);
            active.Children.Add(Text(current.OriginalPriority.HasValue ? "Приоритет: выше обычного, если он изменялся этой сессией." : "Приоритет процесса сохраняется.", 12, "Muted"));Gap(active, 18);
            active.Children.Add(Text(manager.LastMessage, 12));Gap(active, 20);
            var stop = Button(manager.IsRunning ? "Завершить сессию и вернуть параметры" : "Повторить восстановление", async () => await RunGameSessionAction(async () => { await manager.StopAsync(); }), true);
            stop.IsEnabled = !gameSessionActionBusy;active.Children.Add(stop);
            active.Children.Add(new Border { Height = 12 });active.Children.Add(Text("Игра останется открытой. Изменённые вами или другой программой план и приоритет сохранятся.", 11, "Muted"));
            content.Children.Add(Card(active));AddGameSessionRules();return;
        }
        IReadOnlyList<GamePowerPlan> plans;
        try { plans = manager.PowerPlans;if (gamePlan == Guid.Empty || !plans.Any(p => p.Id == gamePlan)) gamePlan = manager.ActivePlan; }
        catch (Exception ex) { content.Children.Add(Note("Планы питания недоступны", ex.Message));return; }
        var body = new StackPanel();
        body.Children.Add(Text("01  /  ВЫБЕРИТЕ ИГРУ", 10, "Muted", true));Gap(body, 15);
        var launchMode = Button("Запустить EXE", () => { gameLaunchMode = true;GameSessions(); }, gameLaunchMode);
        var runningMode = Button("Уже запущенный процесс", () => { gameLaunchMode = false;GameSessions(); }, !gameLaunchMode);
        var modes = new WrapPanel();modes.Children.Add(launchMode);modes.Children.Add(runningMode);body.Children.Add(modes);Gap(body, 17);
        if (gameLaunchMode)
        {
            var file = Text(string.IsNullOrWhiteSpace(gameExecutable) ? "Исполняемый файл ещё не выбран" : gameExecutable, 12, "Muted");body.Children.Add(file);Gap(body, 13);
            body.Children.Add(Button("Выбрать EXE  ↗", () =>
            {
                var dialog = new OpenFileDialog { Filter = "Приложения (*.exe)|*.exe", CheckFileExists = true, Multiselect = false, Title = "Выберите исполняемый файл игры" };
                if (dialog.ShowDialog(this) == true) { gameExecutable = Path.GetFullPath(dialog.FileName);GameSessions(); }
            }));
        }
        else
        {
            try
            {
                var processes = manager.Processes;
                var selected = processes.FirstOrDefault(p => p.Id == selectedGameProcess?.Id && p.StartedUtcTicks == selectedGameProcess.StartedUtcTicks);
                selectedGameProcess = selected;
                var process = new ComboBox { ItemsSource = processes, DisplayMemberPath = "DisplayName", SelectedItem = selected, MinWidth = 320, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
                process.SelectionChanged += (_, _) => selectedGameProcess = process.SelectedItem as GameProcessInfo;
                body.Children.Add(process);Gap(body, 12);body.Children.Add(Button("↻  Обновить процессы", GameSessions));
                if (processes.Count == 0) body.Children.Add(Text("Доступных процессов нет. Сначала запустите игру обычным способом.", 12, "Muted"));
            }
            catch (Exception ex) { body.Children.Add(Text("Не удалось прочитать процессы: " + ex.Message, 12, "Muted")); }
        }
        Gap(body, 24);body.Children.Add(Text("02  /  ПАРАМЕТРЫ СЕССИИ", 10, "Muted", true));Gap(body, 15);
        var plan = new ComboBox { ItemsSource = plans, DisplayMemberPath = "DisplayName", SelectedItem = plans.FirstOrDefault(p => p.Id == gamePlan), MinWidth = 320, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        plan.SelectionChanged += (_, _) => { if (plan.SelectedItem is GamePowerPlan selected) gamePlan = selected.Id; };
        body.Children.Add(Text("План питания", 12));Gap(body, 8);body.Children.Add(plan);Gap(body, 17);
        var priority = new CheckBox { Content = "Приоритет процесса «Выше обычного»", IsChecked = gameRaisePriority };
        priority.Checked += (_, _) => gameRaisePriority = true;priority.Unchecked += (_, _) => gameRaisePriority = false;body.Children.Add(priority);Gap(body, 10);
        body.Children.Add(Text("Необязательный эксперимент при конкуренции за CPU. На свободном процессоре пользы может не быть; иногда страдают фоновые приложения. High и Realtime не используются. Античит может запретить изменение.", 12, "Muted"));Gap(body, 23);
        var start = Button(gameSessionActionBusy ? "Подготавливаем сессию…" : gameLaunchMode ? "Запустить игровую сессию  →" : "Начать сессию для процесса  →", async () =>
        {
            if (gameLaunchMode && string.IsNullOrWhiteSpace(gameExecutable)) { Notify("Выберите EXE игры.");return; }
            if (!gameLaunchMode && selectedGameProcess == null) { Notify("Выберите фактический процесс игры.");return; }
            var chosen = plans.FirstOrDefault(p => p.Id == gamePlan);
            if (chosen == null) { Notify("Выберите существующий план питания.");return; }
            string target = gameLaunchMode ? gameExecutable : selectedGameProcess!.DisplayName;
            if (!Confirm("Начать игровую сессию?", $"{target}\n\nПлан: {chosen.Name}. Приоритет: {(gameRaisePriority ? "выше обычного" : "без изменения")}.\n\nБудет отслеживаться только выбранный процесс. После его выхода или завершения сессии вернутся прежние параметры, если их не меняли извне. Запуск другого EXE вместо лаунчера автоматически не отслеживается.", "Начать")) return;
            await RunGameSessionAction(() => gameLaunchMode ? manager.LaunchAsync(gameExecutable, gamePlan, gameRaisePriority) : manager.StartExistingAsync(selectedGameProcess!, gamePlan, gameRaisePriority));
        }, true);
        start.IsEnabled = !gameSessionActionBusy && string.IsNullOrEmpty(gameSessionError) && plans.Count > 0;body.Children.Add(start);
        content.Children.Add(Card(body));AddGameSessionRules();
    }
    async Task RunGameSessionAction(Func<Task> action)
    {
        if (gameSessionActionBusy || busy > 0) { Notify("Дождитесь текущей операции.");return; }
        gameSessionActionBusy = true;if (page == 13) GameSessions();
        try { await action();Notify(gameSessionManager!.LastMessage); }
        catch (Exception ex) { Notify(ex.Message); }
        finally { gameSessionActionBusy = false;if (page == 13) GameSessions(); }
    }
    void AddGameSessionRules()
    {
        content.Children.Add(Note("Следим за игрой, а не за лаунчером", "Сессия привязана к конкретному PID, времени запуска, EXE и вашей учётной записи. Если EXE только открывает Steam, Epic или другой лаунчер и завершается, сессия закончится. В таком случае запустите игру обычным способом и выберите её фактический процесс. Дочерние процессы автоматически не подхватываются."));
        content.Children.Add(Note("Временная настройка с проверяемым откатом", "Новый план не создаётся: используется существующий. При обычном выходе из Qivoryx сессия завершается. После аварийного закрытия запись восстановления будет проверена при следующем запуске; никакой EXE из неё не запускается. Запись привязана к вашему профилю и компьютеру и хранится отдельно от portable-папки, чтобы разные копии приложения не накладывали сессии друг на друга. Это не измеритель FPS и не обещание прироста в любой игре."));
    }
    void OpenGameRecovery() { Directory.CreateDirectory(GameRecoveryDirectory);Engine.Open(GameRecoveryDirectory); }
}
