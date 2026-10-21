using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Qivoryx;

public partial class MainWindow
{
    sealed record RemovalCandidate(string Id, string Kind, string Path, string Label);
    sealed record RemovalPlan(string PlanId, string Name, string Type, string Scope, bool Quiet, bool CanRemove,
        string CommandMode, string[]? Steps, RemovalCandidate[]? Candidates, string[]? Warnings);
    sealed record DefenderTarget(string Kind, string Name, string Path, bool Exists)
    {
        public string Presence => Exists ? "Найдено" : "Отсутствует";
    }
    sealed record DefenderResultItem(string Kind, string Target, string State, string Detail);

    Border RemovalFeature(string number, string title, string description, string label, Action action)
    {
        var caption = Text(number, 10, "Muted", true);caption.Margin = new(0, 0, 0, 12);
        var button = Button(label + "  →", action);button.HorizontalAlignment = HorizontalAlignment.Left;
        return Card(Stack(caption, Text(title, 21, bold: true), new Border { Height = 10 },
            Text(description, 12, "Muted"), new Border { Height = 20 }, button));
    }

    Border RemovalSteps(params string[] steps)
    {
        var grid = new Grid();
        for (int i = 0; i < steps.Length; i++)
        {
            grid.ColumnDefinitions.Add(new());
            var section = Stack(Text($"0{i + 1}", 10, "Muted", true), new Border { Height = 6 }, Text(steps[i], 12, bold: true));
            section.Margin = new(i == 0 ? 0 : 18, 0, 8, 0);Grid.SetColumn(section, i);grid.Children.Add(section);
        }
        return Card(grid, new Thickness(20, 16, 20, 16));
    }

    static string RemovalJsonText(JsonElement element, string name, string fallback = "—") =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString() : fallback;

    static bool RemovalJsonBool(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    async Task RemovalCenter(int token)
    {
        var features = new Grid();features.ColumnDefinitions.Add(new());features.ColumnDefinitions.Add(new());
        var appsFeature = RemovalFeature("01 / ПРИЛОЖЕНИЯ", "Приложения и остатки", "Штатный деинсталлятор, проверка результата и отдельная очистка выбранных остатков.", "Выбрать приложение", () => pageScroll.ScrollToVerticalOffset(280));
        appsFeature.Margin = new(0, 0, 8, 16);features.Children.Add(appsFeature);
        var defenderFeature = RemovalFeature("02 / КОМПОНЕНТЫ WINDOWS", "Microsoft Defender", "Проверка защиты, точный список компонентов и отчёт по каждому этапу удаления.", "Открыть Defender", () => _ = Navigate(6));
        defenderFeature.Margin = new(8, 0, 0, 16);Grid.SetColumn(defenderFeature, 1);features.Children.Add(defenderFeature);content.Children.Add(features);
        content.Children.Add(RemovalSteps("Выберите приложение", "Проверьте план", "Удалите и проверьте остатки"));

        var loading = Note("Собираем список приложений", "Программы x64/x86, приложения текущего пользователя и пакеты Microsoft Store.");content.Children.Add(loading);
        if (installed == null)
        {
            var reply = await Query(new { op = "apps" }, "Инвентаризация приложений");
            if (generation != token) return;
            installed = Engine.Read<List<InstalledApp>>(reply.data);
        }
        if (generation != token) return;
        content.Children.Remove(loading);
        var all = installed;
        var list = Table(("ПРИЛОЖЕНИЕ", "Name", 335), ("ИЗДАТЕЛЬ", "Publisher", 200), ("ТИП", "Type", 80), ("ОБЛАСТЬ", "Scope", 130));list.Height = 290;
        list.ItemsSource = all;
        var count = Text($"{all.Count} приложений", 11, "Muted");count.VerticalAlignment = VerticalAlignment.Center;
        var filter = Search("Поиск по приложению, издателю или типу", query =>
        {
            var found = all.Where(app => $"{app.Name} {app.Publisher} {app.Type}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            list.ItemsSource = found;count.Text = $"{found.Count} из {all.Count}";
        });
        content.Children.Add(Row(filter, Button("↻  Обновить", () => { if (busy > 0) { Notify("Дождитесь текущей операции.");return; }installed = null;_ = Navigate(15); }), count));Gap(content, 12);
        content.Children.Add(Card(list, new Thickness(6)));
        var preview = new StackPanel();preview.Children.Add(Text("Выберите приложение", 19, bold: true));Gap(preview, 8);
        preview.Children.Add(Text("Сначала будет подготовлен точный план. Анализ ничего не удаляет.", 12, "Muted"));content.Children.Add(Card(preview));
        content.Children.Add(Text("Очистка остатков выполняется только после подтверждённого удаления приложения. Папки издателя и общие компоненты автоматически не удаляются.", 11, "Muted"));
        int selection = 0;
        list.SelectionChanged += async (_, _) =>
        {
            int selectedToken = ++selection;
            if (list.SelectedItem is not InstalledApp app) return;
            if (busy > 0) { Notify("Дождитесь завершения текущей операции и выберите приложение ещё раз.");return; }
            preview.Children.Clear();preview.Children.Add(Text("Подготавливаем план для «" + app.Name + "»", 17, bold: true));
            list.IsEnabled = false;
            try
            {
                var reply = await Query(new { op = "removalPlan", id = app.Id }, "Анализ удаления: " + app.Name);
                if (generation != token || selectedToken != selection) return;
                var plan = Engine.Read<RemovalPlan>(reply.data);
                RenderAppRemovalPlan(preview, list, plan, app, token);
            }
            catch (Exception ex) { if (generation == token && selectedToken == selection) { preview.Children.Clear();preview.Children.Add(Text("Не удалось подготовить план", 18, bold: true));Gap(preview, 8);preview.Children.Add(Text(ex.Message, 12, "Muted")); }Notify(ex.Message); }
            finally { list.IsEnabled = true; }
        };
    }

    void RenderAppRemovalPlan(StackPanel preview, ListView list, RemovalPlan plan, InstalledApp app, int token)
    {
        preview.Children.Clear();preview.Children.Add(Text("ПЛАН УДАЛЕНИЯ", 10, "Muted", true));Gap(preview, 10);
        preview.Children.Add(Text(plan.Name, 22, bold: true));Gap(preview, 8);
        preview.Children.Add(Text($"{plan.Type}  /  {plan.Scope}  /  {(plan.Quiet ? "Доступен тихий режим" : "Возможны диалоги деинсталлятора")}", 11, "Muted"));Gap(preview, 16);
        foreach (var step in plan.Steps ?? []) { preview.Children.Add(Text("•  " + step, 12));Gap(preview, 6); }
        foreach (var warning in plan.Warnings ?? []) { Gap(preview, 6);preview.Children.Add(Text(warning, 12, "Muted")); }
        bool x3dGameBar = AsymmetricX3d && (app.Id.Contains("XboxGamingOverlay", StringComparison.OrdinalIgnoreCase) || app.Name.Contains("XboxGamingOverlay", StringComparison.OrdinalIgnoreCase));
        const string x3dWarning = "На этом Ryzen X3D Xbox Game Bar участвует в распознавании игр для выбора игрового CCD. Удаление может ухудшить распределение нагрузки по ядрам.";
        if (x3dGameBar) { Gap(preview, 8);preview.Children.Add(Text(x3dWarning, 12, "Muted")); }
        Gap(preview, 16);
        var residuals = plan.Candidates ?? [];
        preview.Children.Add(Text($"Найдено возможных остатков: {residuals.Length}", 13, bold: true));Gap(preview, 8);
        foreach (var candidate in residuals.Take(8))
        {
            preview.Children.Add(Text(candidate.Path, 11, "Muted"));Gap(preview, 4);
        }
        if (residuals.Length > 8) preview.Children.Add(Text($"Ещё {residuals.Length - 8}. Полный список доступен после удаления.", 11, "Muted"));
        Gap(preview, 8);preview.Children.Add(Text("Остатки пока не выбраны. Их очистка потребует отдельного подтверждения.", 11, "Muted"));Gap(preview, 18);
        bool admin = app.Type == "Win32" && app.Scope == "Компьютер";
        var results = new StackPanel();
        Button? execute = null;
        execute = Button("Удалить приложение", async () =>
        {
            if (busy > 0) { Notify("Дождитесь текущей операции.");return; }
            if (!Confirm("Удалить «" + plan.Name + "»?", "Будет выполнен подготовленный план и запущен зарегистрированный деинсталлятор. Настройки и личные данные приложения могут быть удалены. Автоматического отката нет.\n\n" +
                (plan.Quiet ? "Используется заявленный тихий режим." : "Издатель не заявил тихий режим: возможны окна деинсталлятора.") +
                "\n\nОчистка остатков сейчас не выполняется." + (x3dGameBar ? "\n\n" + x3dWarning : ""), "Удалить")) return;
            execute!.IsEnabled = false;list.IsEnabled = false;results.Children.Clear();
            try
            {
                var reply = await Query(new { op = "removalExecute", planId = plan.PlanId }, "Удаление: " + plan.Name, admin);
                installed = null;
                if (generation != token) { Notify(reply.message);return; }
                var data = reply.data;
                bool removed = RemovalJsonBool(data, "Removed");
                results.Children.Add(Text(removed ? "Удаление подтверждено" : "Удаление требует внимания", 18, bold: true));Gap(results, 8);
                results.Children.Add(Text(RemovalJsonText(data, "Message", reply.message), 12, "Muted"));
                if (RemovalJsonBool(data, "RestartRequired")) { Gap(results, 8);results.Children.Add(Text("Деинсталлятор запросил перезагрузку. Qivoryx не перезагружает ПК автоматически.", 12, "Muted")); }
                if (removed)
                {
                    var candidates = data.TryGetProperty("Candidates", out var found) && found.ValueKind == JsonValueKind.Array ? Engine.Read<RemovalCandidate[]>(found) : residuals;
                    RenderRemovalCleanup(results, plan, candidates, admin, token);
                }
                else { Gap(results, 10);results.Children.Add(Text("Очистка остатков заблокирована: приложение ещё зарегистрировано. После перезагрузки обновите список и подготовьте новый план.", 12, "Muted")); }
                Notify(RemovalJsonText(data, "Message", reply.message));
            }
            catch (Exception ex) { if (generation == token) { results.Children.Add(Text("Операция не завершена", 18, bold: true));Gap(results, 8);results.Children.Add(Text(ex.Message, 12, "Muted")); }Notify(ex.Message); }
            finally { list.IsEnabled = true; }
        }, true);
        execute.IsEnabled = plan.CanRemove;preview.Children.Add(execute);Gap(preview, 16);preview.Children.Add(results);
    }

    void RenderRemovalCleanup(StackPanel results, RemovalPlan plan, RemovalCandidate[] candidates, bool admin, int token)
    {
        Gap(results, 20);results.Children.Add(Text("Остатки приложения", 17, bold: true));Gap(results, 8);
        if (candidates.Length == 0) { results.Children.Add(Text("Подтверждённых кандидатов на очистку не найдено.", 12, "Muted"));return; }
        results.Children.Add(Text("Отметьте только ненужные данные. Настройки, профили и сохранения могут быть удалены без возможности восстановления.", 12, "Muted"));Gap(results, 12);
        var selected = new List<(RemovalCandidate candidate, CheckBox check)>();
        foreach (var candidate in candidates)
        {
            var check = new CheckBox { Content = Stack(Text(candidate.Label ?? candidate.Kind, 12, bold: true), Text(candidate.Path, 11, "Muted")), IsChecked = false, Margin = new(0, 0, 0, 12) };
            selected.Add((candidate, check));results.Children.Add(check);
        }
        var report = new StackPanel();Button? cleanup = null;
        cleanup = Button("Очистить выбранные остатки", async () =>
        {
            if (busy > 0) { Notify("Дождитесь текущей операции.");return; }
            var chosen = selected.Where(item => item.check.IsChecked == true).Select(item => item.candidate).ToArray();
            if (chosen.Length == 0) { Notify("Выберите остатки для очистки.");return; }
            if (!Confirm("Очистить остатки «" + plan.Name + "»?", "Будут удалены только выбранные цели:\n\n" + string.Join("\n", chosen.Select(item => item.Path)) + "\n\nФайлы и данные не восстанавливаются автоматически. Для выбранных записей реестра сохраняется экспорт перед удалением.", "Очистить")) return;
            cleanup!.IsEnabled = false;foreach (var item in selected) item.check.IsEnabled = false;report.Children.Clear();
            try
            {
                var reply = await Query(new { op = "removalCleanup", planId = plan.PlanId, ids = chosen.Select(item => item.Id).ToArray() }, "Очистка выбранных остатков", admin);
                if (generation != token) { Notify(reply.message);return; }
                report.Children.Add(Text(RemovalJsonText(reply.data, "Message", reply.message), 13, bold: true));
                foreach (string field in new[] { "Deleted", "Failed" })
                {
                    if (!reply.data.TryGetProperty(field, out var items) || items.ValueKind != JsonValueKind.Array) continue;
                    foreach (var item in items.EnumerateArray()) { Gap(report, 6);report.Children.Add(Text((field == "Deleted" ? "Удалено: " : "Не удалено: ") + item.ToString(), 11, "Muted")); }
                }
                Notify(RemovalJsonText(reply.data, "Message", reply.message));
            }
            catch (Exception ex) { if (generation == token) report.Children.Add(Text(ex.Message, 12, "Muted"));Notify(ex.Message); }
        });
        results.Children.Add(cleanup);Gap(results, 12);results.Children.Add(report);
    }

    async Task NativeDefender(int token)
    {
        content.Children.Add(RemovalSteps("Проверка системы", "Просмотр компонентов", "Удаление и отчёт"));
        var loading = Note("Проверяем Microsoft Defender", "Читаем состояние защиты и готовим список компонентов. На этом этапе система не изменяется.");content.Children.Add(loading);
        var reply = await Query(new { op = "defenderRemovalPlan" }, "Анализ компонентов Defender");
        if (generation != token) return;
        content.Children.Remove(loading);
        var data = reply.data;
        var state = data.TryGetProperty("Status", out var current) ? current : default;
        string StateValue(string key) => state.ValueKind == JsonValueKind.Object && state.TryGetProperty(key, out var value) ? value.ValueKind switch { JsonValueKind.True => "Включено", JsonValueKind.False => "Выключено", JsonValueKind.Null => "Нет данных", _ => value.ToString() } : "Нет данных";
        var metrics = new Grid();
        var metricItems = new[] { ("АНТИВИРУС", StateValue("Enabled")), ("В РЕАЛЬНОМ ВРЕМЕНИ", StateValue("Realtime")), ("ЗАЩИТА ОТ ПОДДЕЛКИ", StateValue("Tamper")), ("ВЕРСИЯ", StateValue("Version")) };
        for (int i = 0; i < metricItems.Length; i++)
        {
            metrics.ColumnDefinitions.Add(new());var metric = Stack(Text(metricItems[i].Item1, 9, "Muted", true), new Border { Height = 9 }, Text(metricItems[i].Item2, i == 3 ? 14 : 18, bold: true));
            metric.Margin = new(i == 0 ? 0 : 16, 0, 0, 0);Grid.SetColumn(metric, i);metrics.Children.Add(metric);
        }
        content.Children.Add(Card(metrics));
        var intro = Stack(Text("Удаление компонентов Defender", 22, bold: true), new Border { Height = 10 }, Text("Операция выполняется внутри Qivoryx. Для каждого компонента будет показан фактический результат, включая отказ Windows и необходимость перезагрузки.", 13, "Muted"));
        if (data.TryGetProperty("Warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array)
            foreach (var warning in warnings.EnumerateArray()) { Gap(intro, 10);intro.Children.Add(Text(warning.ToString(), 12, "Muted")); }
        Gap(intro, 12);intro.Children.Add(Text(RemovalJsonText(data, "Message", "Проверьте список компонентов перед продолжением."), 12, "Muted"));
        content.Children.Add(Card(intro));
        var targets = data.TryGetProperty("Targets", out var found) && found.ValueKind == JsonValueKind.Array ? Engine.Read<DefenderTarget[]>(found) : [];
        var table = Table(("КОМПОНЕНТ", "Name", 180), ("ЦЕЛЬ", "Path", 435), ("СОСТОЯНИЕ", "Presence", 110));table.ItemsSource = targets;table.Height = 280;
        content.Children.Add(Card(Stack(Text($"ПЛАН / {targets.Count(target => target.Exists)} целей найдено", 10, "Muted", true), new Border { Height = 12 }, table), new Thickness(18)));
        var results = new StackPanel();string planId = RemovalJsonText(data, "PlanId", "");
        Button? execute = null;
        execute = Button("Удалить компоненты Defender", async () =>
        {
            if (busy > 0) { Notify("Дождитесь текущей операции.");return; }
            if (!Confirm("Удалить Microsoft Defender?", "Будет выполнен показанный план с правами администратора. Встроенный антивирус может перестать работать. Удаление защищённых файлов необратимо средствами Qivoryx: резервные копии настроек не восстанавливают удалённые компоненты.\n\nПодготовьте полную резервную копию системы. Windows может запретить часть действий или восстановить компоненты после обновления. Полное удаление не гарантируется.\n\nQivoryx покажет результат для каждой цели. Приложение не перезагружает ПК автоматически.", "Подтвердить удаление")) return;
            execute!.IsEnabled = false;results.Children.Clear();
            try
            {
                var operation = await Query(new { op = "defenderRemovalExecute", planId }, "Удаление компонентов Defender", true);
                if (generation != token) { Notify(operation.message);return; }
                var output = operation.data;
                results.Children.Add(Text(RemovalJsonBool(output, "Complete") ? "План выполнен" : "План выполнен не полностью", 22, bold: true));Gap(results, 10);
                results.Children.Add(Text(RemovalJsonText(output, "Message", operation.message), 12, "Muted"));
                if (RemovalJsonBool(output, "RestartRequired")) { Gap(results, 8);results.Children.Add(Text("Требуется перезагрузка. После неё повторно проверьте состояние защиты.", 12, "Muted")); }
                if (output.TryGetProperty("Items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    Gap(results, 16);
                    foreach (var item in Engine.Read<DefenderResultItem[]>(items))
                    {
                        results.Children.Add(Text(item.State + "  /  " + item.Target, 12, bold: true));
                        results.Children.Add(Text(item.Detail, 11, "Muted"));Gap(results, 12);
                    }
                }
                string report = RemovalJsonText(output, "ReportPath", "");
                if (!string.IsNullOrWhiteSpace(report)) { Gap(results, 8);results.Children.Add(Text("Отчёт: " + report, 11, "Muted"));if (File.Exists(report)) { Gap(results, 12);results.Children.Add(Button("Открыть полный отчёт  ↗", () => Engine.Open(report))); } }
                Gap(results, 12);results.Children.Add(Button("Повторно проверить систему", () => _ = Navigate(6)));
                Notify(RemovalJsonText(output, "Message", operation.message));
            }
            catch (Exception ex) { if (generation == token) { results.Children.Add(Text("Операция остановлена", 18, bold: true));Gap(results, 8);results.Children.Add(Text(ex.Message, 12, "Muted")); }Notify(ex.Message); }
        }, true);
        execute.IsEnabled = RemovalJsonBool(data, "CanApply") && !string.IsNullOrWhiteSpace(planId);
        var actions = new WrapPanel();actions.Children.Add(execute);actions.Children.Add(Button("Безопасность Windows  ↗", () => Engine.Open("windowsdefender:")));actions.Children.Add(Button("Центр удаления", () => _ = Navigate(15)));
        content.Children.Add(Card(Stack(actions, new Border { Height = 18 }, results)));
    }
}
