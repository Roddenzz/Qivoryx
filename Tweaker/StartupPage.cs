using System.Windows;
using System.Windows.Controls;

namespace Qivoryx;

public record StartupEntry(string Id, string Name, string Command, string Scope, string Source, string StateLabel, bool RequiresAdmin, bool Protected, bool CanDisable);
public record StartupInventory(List<StartupEntry> Entries, List<StartupEntry> Disabled);

public partial class MainWindow
{
    async Task Startup(int token)
    {
        content.Children.Add(Note("Что запускается вместе с Windows?", "Считываем записи Run и папки автозагрузки. Приложения не запускаются, системные параметры не изменяются."));
        var response = await Query(new { op = "startupList" }, "Анализ автозагрузки");
        if (generation != token) return;
        var data = Engine.Read<StartupInventory>(response.data);
        content.Children.Clear();

        var hero = new Grid();hero.ColumnDefinitions.Add(new());hero.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        hero.Children.Add(Stack(Text("Быстрее войти. Меньше фона.", 24, bold: true), new Border { Height = 9 }, Text("Оставьте в автозагрузке то, что нужно сразу после входа. Остальное можно запускать вручную.", 12, "Muted")));
        var number = Stack(Text(data.Entries.Count.ToString(), 36, bold: true), Text("записей найдено", 10, "Muted"));number.Margin = new(26, 0, 0, 0);Grid.SetColumn(number, 1);hero.Children.Add(number);
        content.Children.Add(Card(hero, new Thickness(25)));

        var list = Table(("ПРИЛОЖЕНИЕ", "Name", 225), ("ИСТОЧНИК", "Source", 130), ("ОБЛАСТЬ", "Scope", 165), ("СОСТОЯНИЕ", "StateLabel", 180));
        list.Height = 315;
        var count = Text($"{data.Entries.Count} записей", 11, "Muted");count.VerticalAlignment = VerticalAlignment.Center;
        var tools = new WrapPanel();
        tools.Children.Add(Search("Поиск по приложению или команде автозапуска", q =>
        {
            var found = data.Entries.Where(e => $"{e.Name} {e.Command} {e.Scope}".Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            list.ItemsSource = found;count.Text = $"{found.Count} из {data.Entries.Count}";
        }));
        tools.Children.Add(Button("↻  Обновить", () => _ = Navigate(10)));
        tools.Children.Add(Button("Диспетчер задач  ↗", () => Engine.Open("taskmgr.exe")));
        tools.Children.Add(count);content.Children.Add(tools);Gap(content);
        list.ItemsSource = data.Entries;content.Children.Add(Card(list, new Thickness(7)));

        var detail = new StackPanel();content.Children.Add(Card(detail));
        void Select()
        {
            detail.Children.Clear();
            if (list.SelectedItem is not StartupEntry entry)
            {
                detail.Children.Add(Text("Выберите приложение", 16, bold: true));Gap(detail, 8);
                detail.Children.Add(Text("Покажем команду, область действия и возможность отключить автоматический запуск с резервной копией.", 12, "Muted"));return;
            }
            detail.Children.Add(Text(entry.Name, 20, bold: true));Gap(detail, 8);
            detail.Children.Add(Text($"{entry.Source}  /  {entry.Scope}  /  {entry.StateLabel}", 11, "Muted"));Gap(detail, 13);
            var command = Text(entry.Command, 12);command.FontFamily = new("Cascadia Mono, Consolas");detail.Children.Add(command);Gap(detail, 15);
            detail.Children.Add(Text(entry.Protected ? "Компонент безопасности: изменение автозапуска защищено." : "Отключение не удаляет программу и не закрывает её сейчас. Проверьте, не нужны ли вам её синхронизация, горячие клавиши или уведомления сразу после входа.", 12, "Muted"));Gap(detail, 16);
            var disable = Button("Отключить автозапуск", () =>
            {
                if (Confirm("Отключить автозапуск?", $"{entry.Name}\n\n{entry.Command}\n\nОтключим только эту запись. Исходное значение или файл сохранятся для восстановления в Qivoryx. Текущий процесс продолжит работать. Изменение действует при следующем входе в Windows.", "Отключить"))
                    _ = Act(new { op = "startupDisable", id = entry.Id }, "Отключение автозапуска", entry.RequiresAdmin, async () => { if (page == 10) await Navigate(10); });
            }, true);disable.IsEnabled = entry.CanDisable;
            detail.Children.Add(disable);
        }
        list.SelectionChanged += (_, _) => Select();Select();

        if (data.Disabled.Count > 0)
        {
            content.Children.Add(Text("ОТКЛЮЧЕНО В QIVORYX", 10, "Muted", true));Gap(content, 13);
            foreach (var entry in data.Disabled)
            {
                var body = new Grid();body.ColumnDefinitions.Add(new());body.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                body.Children.Add(Stack(Text(entry.Name, 15, bold: true), new Border { Height = 7 }, Text($"{entry.Scope}  ·  исходная запись сохранена", 11, "Muted")));
                var restore = Button("Восстановить", () =>
                {
                    if (Confirm("Вернуть автозапуск?", $"{entry.Name}\n\n{entry.Command}\n\nВосстановим исходную запись без запуска программы. Если другая программа уже создала запись с тем же именем, перезаписи не будет.", "Восстановить"))
                        _ = Act(new { op = "startupRestore", id = entry.Id }, "Восстановление автозапуска", entry.RequiresAdmin, async () => { if (page == 10) await Navigate(10); });
                });restore.VerticalAlignment = VerticalAlignment.Center;Grid.SetColumn(restore, 1);body.Children.Add(restore);
                content.Children.Add(Card(body));
            }
        }
        content.Children.Add(Note("Как читать этот список", "Показаны Run текущего пользователя и компьютера (x64/x86), личная и общая папки Startup. Приложения из Store, задания планировщика и службы управляются отдельно. Отметка Windows об отключении учитывается, когда доступна; наличие записи не доказывает её фактический запуск. Влияние на время входа смотрите в разделе «Автозагрузка» Диспетчера задач."));
        content.Children.Add(Note("Где будет заметен результат", "Польза зависит от выбранного приложения: меньше фоновой нагрузки, потребления памяти и обращений к диску. Универсальной прибавки FPS нет. Не отключайте вслепую драйверные панели, резервное копирование, средства доступности и нужные вам программы."));
        content.Children.Add(Button("Справка об автозагрузке  ↗", () => Engine.Open("https://support.microsoft.com/windows/115a420a-0bff-4a6f-90e0-1934c844e473")));
    }
}
