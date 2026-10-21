using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Qivoryx;

public partial class MainWindow
{
    ListView Table(params (string title, string binding, double width)[] columns)
    {
        var view = new GridView();
        foreach (var c in columns)
        {
            var template = new DataTemplate();var factory = new FrameworkElementFactory(typeof(TextBlock));
            factory.SetBinding(TextBlock.TextProperty, new Binding(c.binding));factory.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            factory.SetValue(TextBlock.FontSizeProperty, 12.0);factory.SetValue(TextBlock.MarginProperty, new Thickness(4, 12, 4, 12));factory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            template.VisualTree = factory;view.Columns.Add(new GridViewColumn { Header = c.title, Width = c.width, CellTemplate = template });
        }
        var list = new ListView { View = view, Height = 390, BorderThickness = new(0), SelectionMode = SelectionMode.Single };
        list.SetResourceReference(Control.BackgroundProperty, "Card");list.SetResourceReference(Control.ForegroundProperty, "Fg");
        list.ItemContainerStyle = (Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ListViewItem"><Setter Property="Background" Value="Transparent"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListViewItem"><Border x:Name="b" Background="{TemplateBinding Background}" BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1"><GridViewRowPresenter Content="{TemplateBinding Content}" Columns="{Binding View.Columns,RelativeSource={RelativeSource AncestorType=ListView}}"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="{DynamicResource Hover}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="b" Property="Background" Value="{DynamicResource Hover}"/><Setter TargetName="b" Property="BorderThickness" Value="3,0,0,1"/><Setter TargetName="b" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
        """);
        VirtualizingPanel.SetIsVirtualizing(list, true);VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);return list;
    }
    TextBox Search(string hint, Action<string> changed)
    {
        var input = new TextBox { MinWidth = 300, Width = 330, Height = 40, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = hint, Tag = "Поиск…", Margin = new(0, 0, 12, 0) };
        input.TextChanged += (_, _) => changed(input.Text.Trim());
        input.SetValue(AutomationPropertiesHelper.HintProperty, hint);
        return input;
    }
    async Task Services(int token)
    {
        if (services == null)
        {
            content.Children.Add(Note("Считываем службы Windows", "Полный список служб, назначение и измерения процессов-хостов."));
            var r = await Query(new { op = "services" }, "Чтение служб и нагрузки");if (generation != token) return;services = Engine.Read<List<ServiceInfo>>(r.data);content.Children.Clear();
        }
        var all = services;
        var count = Text($"{all.Count} служб", 12, "Muted");count.VerticalAlignment = VerticalAlignment.Center;
        var list = Table(("СЛУЖБА", "DisplayName", 340), ("СТАТУС", "StateLabel", 100), ("ЗАПУСК", "StartLabel", 90), ("НУЖНОСТЬ", "Need", 140), ("CPU %", "Cpu", 65), ("RAM МБ¹", "MemoryMB", 90));
        var search = Search("Поиск по имени, описанию или названию службы", q => { var filtered = all.Where(s => $"{s.Name} {s.DisplayName} {s.Description}".Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();list.ItemsSource = filtered;count.Text = $"{filtered.Count} из {all.Count} служб"; });
        content.Children.Add(Row(search, Button("↻  Обновить", () => { services = null;_ = Navigate(1); }), count));Gap(content);
        list.ItemsSource = all;content.Children.Add(Card(list, new Thickness(6)));
        var detail = new StackPanel();content.Children.Add(Card(detail));
        void Select()
        {
            detail.Children.Clear();if (list.SelectedItem is not ServiceInfo s) { detail.Children.Add(Text("Выберите службу, чтобы увидеть назначение и действия.", color: "Muted"));return; }
            detail.Children.Add(Text(s.DisplayName, 19, bold: true));detail.Children.Add(Text($"{s.Name}  /  {ServiceGuide.Level(s)}  /  PID {s.ProcessId}", 11, "Muted"));Gap(detail, 12);
            detail.Children.Add(Text(ServiceGuide.Explain(s), 13));Gap(detail, 8);detail.Children.Add(Text(s.Description ?? "Windows не предоставила описание.", 12, "Muted"));Gap(detail, 12);
            detail.Children.Add(Text(s.Shared > 1 ? $"¹ Нагрузка всего процесса-хоста, общего для {s.Shared} служб. Не складывайте эти показатели." : "¹ Значения относятся к процессу-хосту. CPU — кратковременный снимок; 0% не означает отсутствие фоновой работы.", 11, "Muted"));Gap(detail);
            var actions = new WrapPanel();
            foreach (var pair in new[] { ("Запустить", "start"), ("Остановить", "stop"), ("Автоматически", "auto"), ("Вручную", "manual"), ("Отключить", "disable") })
            {
                var (label, action) = pair;
                var b = Button(label, () =>
                {
                    if (Confirm($"{label}: {s.Name}", $"{ServiceGuide.Explain(s)}\n\nТекущий запуск: {s.StartMode}; состояние: {s.State}. Зависимые службы могут перестать работать. Перед действием будет сохранена резервная копия настроек. Восстановление доступно в журнале."))
                        _ = Act(new { op = "service", name = s.Name, action }, "Изменение службы", true, async () => { services = null;if (page == 1) await Navigate(1); });
                });
                b.IsEnabled = !ServiceGuide.Protected.Contains(s.Name);actions.Children.Add(b);
            }
            detail.Children.Add(actions);
        }
        list.SelectionChanged += (_, _) => Select();Select();
        content.Children.Add(Note("Без автоматического отключения наугад", "В списке все службы, которые сообщает Win32_Service. Для известных служб есть рекомендации; для остальных показано описание Windows. Критические службы защищены от изменений. Отключение десятков служб не гарантирует рост FPS."));
    }
    async Task Uninstall(int token)
    {
        if (installed == null)
        {
            content.Children.Add(Note("Находим установленные приложения", "Проверяем реестр x64/x86, профиль пользователя и пакеты Appx."));
            var r = await Query(new { op = "apps" }, "Сбор списка программ");if (generation != token) return;installed = Engine.Read<List<InstalledApp>>(r.data);content.Children.Clear();
        }
        var all = installed;var list = Table(("ПРИЛОЖЕНИЕ", "Name", 410), ("ВЕРСИЯ", "Version", 150), ("ТИП", "Type", 70), ("ОБЛАСТЬ", "Scope", 140));
        var count = Text($"{all.Count} записей", 12, "Muted");count.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(Row(Search("Поиск приложений, пакетов, кодеков и издателей", q => { var found = all.Where(a => $"{a.Name} {a.Publisher} {a.Type}".Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();list.ItemsSource = found;count.Text = $"{found.Count} из {all.Count}"; }), Button("↻  Обновить", () => { installed = null;_ = Navigate(4); }), count));Gap(content);
        list.ItemsSource = all;content.Children.Add(Card(list, new Thickness(6)));
        var detail = new StackPanel();content.Children.Add(Card(detail));
        list.SelectionChanged += (_, _) =>
        {
            detail.Children.Clear();if (list.SelectedItem is not InstalledApp a) return;
            detail.Children.Add(Text(a.Name, 19, bold: true));Gap(detail, 10);detail.Children.Add(Text($"{a.Publisher}\n{a.Type}  ·  {a.Scope}  ·  {(a.SizeMB > 0 ? a.SizeMB + " МБ (оценка)" : "Размер неизвестен")}", 12, "Muted"));Gap(detail);
            if (!a.Removable) { detail.Children.Add(Text("Windows пометила пакет как защищённый или не зарегистрировала деинсталлятор.", 12, "Muted"));return; }
            bool x3dGameBar = AsymmetricX3d && (a.Id.Contains("XboxGamingOverlay", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("XboxGamingOverlay", StringComparison.OrdinalIgnoreCase));
            const string x3dMessage = "На вашем Ryzen X3D распознавание игр через Xbox Game Bar используется механизмом выбора игрового CCD AMD. Удаление Game Bar может ухудшить выбор ядер. Для отключения записи используйте «Тонкую настройку», сохранив сам компонент.";
            if (x3dGameBar) { detail.Children.Add(Text(x3dMessage, 12, "Muted"));Gap(detail); }
            var uninstall = Button("Удалить приложение", () =>
            {
                if (Confirm("Удалить «" + a.Name + "»?", $"Будет запущен зарегистрированный деинсталлятор. Настройки и данные приложения могут быть удалены. Это действие не имеет автоматического отката.\n\n{(a.Quiet ? "Доступен тихий режим." : "Тихий режим не заявлен издателем: появится окно деинсталлятора.")} Для Appx удаление касается текущего пользователя. Удаление кодеков, Store и библиотек может нарушить работу зависимых программ.{(x3dGameBar ? "\n\n" + x3dMessage : "")}", "Удалить"))
                    _ = Act(new { op = "uninstall", id = a.Id }, "Удаление " + a.Name, a.Type == "Win32" && a.Scope == "Компьютер", async () => { installed = null;if (page == 4) await Navigate(4); });
            }, true);detail.Children.Add(uninstall);
        };
        detail.Children.Add(Text("Выберите приложение для просмотра сведений и удаления.", color: "Muted"));
        content.Children.Add(Card(Stack(Text("Microsoft Edge", 17, bold: true), new Border { Height = 10 }, Text("Отдельный запуск штатного деинсталлятора Edge. Возможность удаления зависит от региона и сборки Windows. WebView2 не удаляется, поскольку используется другими программами.", 12, "Muted"), new Border { Height = 16 }, Button("Удалить Edge", () => { if (Confirm("Удаление Microsoft Edge", "Закройте браузер и сохраните закладки. Windows может запретить удаление или установить Edge повторно с обновлениями. Будет использован деинсталлятор Microsoft, без ручного вырезания системных файлов.", "Удалить Edge")) _ = Act(new { op = "edge" }, "Удаление Edge", true, async () => { installed = null;if (page == 4) await Navigate(4); }); }))));
        content.Children.Add(Text("Портативные приложения без регистрации и приложения других профилей могут отсутствовать. Не используем Win32_Product, чтобы не запускать восстановление MSI-пакетов.", 11, "Muted"));
    }
    void Catalog()
    {
        var selected = new HashSet<string>();var results = new WrapPanel();var picked = Text("Ничего не выбрано", 12, "Muted");var filter = "";var category = "Все категории";
        var elevated = new CheckBox { Content = "С правами администратора (для системных пакетов)", Margin = new(0, 0, 0, 12), IsChecked = false };
        var categories = new ComboBox { Width = 200, Margin = new(0, 0, 12, 0), ItemsSource = new[] { "Все категории" }.Concat(packages.Select(p => p.Category).Distinct().Order()).ToArray(), SelectedIndex = 0 };
        var install = Button("Установить выбранное  ↓", async () =>
        {
            if (busy > 0) { Notify("Дождитесь текущей операции.");return; }
            var queue = packages.Where(p => selected.Contains(p.Id)).ToList();if (queue.Count == 0) { Notify("Выберите приложения.");return; }
            if (!Confirm("Установить приложения?", string.Join(", ", queue.Select(p => p.Name)) + "\n\nУстановка принимает лицензионные соглашения выбранных пакетов. Сначала используется winget в тихом режиме. При ошибке скачивается установщик из сохранённого официального манифеста с проверкой SHA-256 (или подписи издателя для динамических ссылок).", "Установить")) return;
            busy++;int done = 0;int failed = 0;
            try
            {
                foreach (var p in queue)
                {
                    try { await Query(new { op = "install", id = p.Id }, $"Установка {done + failed + 1}/{queue.Count}: {p.Name}", elevated.IsChecked == true);done++;selected.Remove(p.Id); }
                    catch (Exception ex) { failed++;Notify(p.Name + ": " + ex.Message); }
                }
            }
            finally { busy--;progress.Visibility = Visibility.Collapsed;status.Text = "●  Очередь завершена"; }
            Notify($"Установлено: {done}. Ошибок: {failed}. Подробности в журнале.");if (page == 3) await Navigate(3);
        }, true);
        void Render()
        {
            results.Children.Clear();var found = packages.Where(p => (category == "Все категории" || p.Category == category) && $"{p.Name} {p.Description} {p.Id}".Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var p in found)
            {
                var check = new CheckBox { Content = Text(p.Name, 15, bold: true), IsChecked = selected.Contains(p.Id), Margin = new(0, 0, 0, 12) };
                check.Checked += (_, _) => { selected.Add(p.Id);picked.Text = $"Выбрано: {selected.Count}"; };check.Unchecked += (_, _) => { selected.Remove(p.Id);picked.Text = $"Выбрано: {selected.Count}"; };
                var body = Stack(check, Text(p.Category.ToUpperInvariant(), 9, "Muted", true), new Border { Height = 10 }, new Border { Child = Text(p.Description, 12, "Muted"), Height = 42 }, new Border { Height = 10 });
                var id = Text(p.Id, 10, "Muted");id.TextTrimming = TextTrimming.CharacterEllipsis;id.TextWrapping = TextWrapping.NoWrap;body.Children.Add(id);Gap(body, 12);
                var web = Button("Сайт ↗", () => Engine.Open(p.Website));web.Padding = new(10, 6, 10, 6);body.Children.Add(web);
                var card = Card(body, new Thickness(18));card.Width = 288;card.Margin = new(0, 0, 12, 12);results.Children.Add(card);
            }
            if (found.Count == 0) results.Children.Add(Text("Ничего не найдено.", color: "Muted"));
        }
        categories.SelectionChanged += (_, _) => { category = (string)categories.SelectedItem;Render(); };
        content.Children.Add(Row(Search("Поиск по каталогу", q => { filter = q;Render(); }), categories));Gap(content);content.Children.Add(elevated);
        var toolbar = Row(install, Button("Снять выбор", () => { selected.Clear();picked.Text = "Ничего не выбрано";Render(); }), picked);picked.VerticalAlignment = VerticalAlignment.Center;content.Children.Add(toolbar);Gap(content);
        content.Children.Add(Text($"{packages.Count} приложений  /  {packages.Select(p => p.Category).Distinct().Count()} категорий  /  winget + прямые источники", 11, "Muted"));Gap(content);
        content.Children.Add(results);Render();
        content.Children.Add(Note("Установка без лишних окон", "Сначала применяется winget --silent, затем при необходимости — прямое скачивание по сохранённому манифесту. Проверяется SHA-256 или подпись издателя. UAC и обязательные диалоги Windows не подавляются. Версии прямых установщиков зафиксированы в каталоге; если издатель удалит файл, понадобится обновление каталога. Portable-утилиты хранятся в папке данных приложения (Programs) и появляются в списке удаления."));
    }
    void Cleanup()
    {
        var results = new StackPanel();var selected = new HashSet<string>();List<CleanInfo>? scanned = null;
        var scan = Button("Анализировать  →", async () =>
        {
            if (busy > 0) { Notify("Дождитесь текущей операции.");return; }
            try
            {
                var r = await Query(new { op = "cleanScan" }, "Анализ временных файлов", true);if (page != 5) return;scanned = Engine.Read<List<CleanInfo>>(r.data);results.Children.Clear();selected.Clear();
                foreach (var item in scanned)
                {
                    var check = new CheckBox { Content = Text(item.Name, 16, bold: true), IsChecked = item.Id != "shader" };
                    if (check.IsChecked == true) selected.Add(item.Id);
                    check.Checked += (_, _) => selected.Add(item.Id);check.Unchecked += (_, _) => selected.Remove(item.Id);
                    results.Children.Add(Card(Stack(check, new Border { Height = 10 }, Text($"{Engine.Size(item.Bytes ?? 0)}  ·  {item.Count} файлов", 22, bold: true), new Border { Height = 10 }, Text(item.Path, 11, "Muted"))));
                }
                results.Children.Add(Button("Очистить выбранное", () =>
                {
                    var total = scanned.Where(x => selected.Contains(x.Id)).Sum(x => x.Bytes ?? 0);
                    if (selected.Count == 0) { Notify("Выберите категории.");return; }
                    if (Confirm("Удалить временные файлы?", $"Выбрано до {Engine.Size(total)}. Удаляются только файлы старше 48 часов в указанных категориях. Операция необратима. Занятые файлы и ссылки будут пропущены. Кэш шейдеров после удаления пересоздаётся и временно может вызвать подёргивания в играх.", "Очистить"))
                        _ = Act(new { op = "clean", ids = selected.ToArray() }, "Очистка файлов", true, async () => { if (page == 5) await Navigate(5); });
                }, true));
            }
            catch (Exception ex) { Notify(ex.Message); }
        }, true);
        content.Children.Add(Card(Stack(Text("Место для важного", 27, bold: true), new Border { Height = 12 }, Text("Временные файлы пользователя и Windows, дампы сбоев, кэш DirectX.\nСначала посмотрите, сколько можно освободить, и выберите категории.", 13, "Muted"), new Border { Height = 22 }, scan), new Thickness(28)));
        content.Children.Add(results);
        content.Children.Add(Note("Личные файлы сохраняются", "Загрузки, документы, рабочий стол, сохранения игр, cookies и пароли браузеров не входят в очистку. Корзина и точки восстановления не очищаются. Удаляются только файлы старше 48 часов; системные ссылки и занятые файлы пропускаются."));
        content.Children.Add(Card(Stack(Text("Другие диски и системное хранилище", 18, bold: true), new Border { Height = 10 }, Text("Откройте штатное управление хранилищем для анализа всех дисков, больших файлов, компонентов обновлений и рекомендаций Windows. Ручное удаление WinSxS не используется.", 12, "Muted"), new Border { Height = 16 }, Button("Хранилище Windows  ↗", () => Engine.Open("ms-settings:storagesense")))));
    }
    void Defender()
    {
        var state = Text("Нажмите «Проверить состояние» для актуальных данных.", 13, "Muted");
        content.Children.Add(Card(Stack(Text("Состояние защиты", 20, bold: true), new Border { Height = 16 }, state, new Border { Height = 18 }, Row(Button("Проверить состояние", async () =>
        {
            try { var r = await Query(new { op = "defenderStatus" }, "Проверка Defender");var d = r.data;state.Text = $"Антивирус включён: {d.GetProperty("Enabled")}\nЗащита в реальном времени: {d.GetProperty("Realtime")}\nЗащита от подделки: {d.GetProperty("Tamper")}\nВерсия: {d.GetProperty("Version")}"; }
            catch (Exception ex) { Notify(ex.Message); }
        }), Button("Безопасность Windows  ↗", () => Engine.Open("windowsdefender:"))))));
        content.Children.Add(Card(Stack(Text("DefenderKiller / ваш архив", 21, bold: true), new Border { Height = 12 }, Text("Интеграция оригинального DefenderKiller.bat из переданного архива. Кнопка запускает его с параметром /DelWD — ручной выбор пункта меню не нужен.", 13), new Border { Height = 14 }, Text("Этот инструмент удаляет также SmartScreen, службы центра безопасности и другие компоненты. Его бинарные помощники не проверены исходным кодом. Может потребоваться перезагрузка; восстановление зависит от созданной им копии. Это не штатное удаление Microsoft.", 13, "Muted"), new Border { Height = 14 }, Text("Несмотря на название «тихий режим» в BAT, в нём остаются окна резервного копирования и паузы. Поэтому запуск видимый: приложение не скрывает запросы, не отвечает за вас и не сообщает об успехе только по коду выхода BAT.", 12, "Muted"), new Border { Height = 22 }, Row(Button("Удалить через DefenderKiller", LaunchDefender), Button("Открыть папку инструмента", () => Engine.Open(Path.Combine(Engine.Root, "Integrations", "DefenderKiller")))))));
        content.Children.Add(Note("До запуска", "Создайте образ системы или другую проверенную резервную копию. Удаление антивируса снижает защиту и может нарушить обслуживание Windows. Автоматическое восстановление Defender в Qivoryx не реализовано; исходный инструмент содержит собственное меню восстановления."));
    }
    void LaunchDefender()
    {
        if (busy > 0) { Notify("Дождитесь текущей операции.");return; }
        var folder = Path.Combine(Engine.Root, "Integrations", "DefenderKiller");
        var bat = Path.Combine(folder, "DefenderKiller.bat");var manifest = Path.Combine(folder, "sha256.json");
        try
        {
            if (!File.Exists(bat) || !File.Exists(manifest)) throw new Exception("Файлы интеграции не найдены. Распакуйте портативную сборку полностью.");
            var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifest))!;
            if (folder.IndexOfAny(new[] { '(', ')', '!' }) >= 0) throw new Exception("Исходный BAT не поддерживает пути со скобками или !. Перенесите portable в простой путь без этих символов.");
            foreach (var entry in hashes)
            {
                var path = Path.GetFullPath(Path.Combine(folder, entry.Key));
                if (!path.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Неверный манифест интеграции.");
                using var stream = File.OpenRead(path);if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(entry.Value, StringComparison.OrdinalIgnoreCase)) throw new Exception("Файл инструмента изменён: " + entry.Key + ". Запуск отменён.");
            }
            if (!Confirm("Удалить Defender сторонним инструментом?", "Будет запущен DefenderKiller.bat /DelWD из вашего архива с правами администратора. Он изменяет защищённые файлы и службы Windows, включая компоненты помимо Defender.\n\nУбедитесь, что резервная копия создана и вы готовы восстановить систему. Внешний инструмент покажет собственные диалоги. После запуска не закрывайте его до завершения. Это действие не тестировалось на вашем компьютере.", "Запустить удаление")) return;
            Engine.Open(folder); // Keep its recovery files easy to locate.
            var p = Process.Start(new ProcessStartInfo("cmd.exe") { Arguments = $"/d /c \"\"{bat}\" /DelWD\"", WorkingDirectory = folder, UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Normal });
            File.AppendAllText(Engine.LogPath, $"\n[{DateTimeOffset.Now:u}] DefenderKiller /DelWD: передан внешний запуск. Результат не подтверждён.\n");
            Notify("Инструмент запущен отдельно. Следуйте его диалогам; затем проверьте состояние защиты.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { Notify("Запуск отменён в UAC."); }
        catch (Exception ex) { Notify(ex.Message); }
    }
}

// Attached hint shown as tooltip and exposed to accessibility clients.
public static class AutomationPropertiesHelper
{
    public static readonly DependencyProperty HintProperty = DependencyProperty.RegisterAttached("Hint", typeof(string), typeof(AutomationPropertiesHelper), new PropertyMetadata("", (d, e) => System.Windows.Automation.AutomationProperties.SetHelpText(d, (string)e.NewValue)));
}
