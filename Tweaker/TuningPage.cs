using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Qivoryx;

public record TuningStatus(string Id, bool Available, bool Optimized, string CurrentLabel, bool CanRestore, bool RestoreBlocked, string? Error);
public record TuningTweak(string Id, string Category, string Title, string Description, string Benefit, string Caution, string Technical);

public partial class MainWindow
{
    static readonly TuningTweak[] TuningTweaks =
    {
        new("menu", "Отзывчивость", "Меню без долгого ожидания", "Сократить задержку раскрытия вложенных меню до 100 мс. Если она уже меньше, сохранить ваш выбор.",
            "Меню реагируют быстрее на наведение. Это изменение задержки интерфейса; FPS, частота мыши и скорость процессора от него не меняются.",
            "Короткая задержка может мешать точному выбору пункта. Значение 100 мс оставляет время на наведение, чтобы меню не раскрывались случайно.",
            "Системный API SPI_GETMENUSHOWDELAY / SPI_SETMENUSHOWDELAY. Сохраняется точное прежнее значение, Windows получает уведомление об изменении."),
        new("mouse", "Ввод", "Предсказуемое движение указателя", "Выключить ускорение мыши для указателя Windows.",
            "Одинаковое физическое перемещение мыши даёт более постоянное перемещение указателя. Подходит тем, кто предпочитает линейное управление.",
            "Изменится привычное ощущение мыши. Это пользовательское предпочтение, а не снижение задержки ввода. Игры с Raw Input обычно обходят эти параметры. DPI и скорость указателя сохраняются.",
            "SPI_GETMOUSE / SPI_SETMOUSE. Передаётся массив из трёх значений: два порога и режим ускорения. Для отката сохраняется весь исходный массив."),
        new("capture", "Игры", "Игровая запись — по вашему выбору", "Отключить встроенный захват игр Windows для текущего пользователя.",
            "Может убрать работу встроенного захвата, если он был активен. При уже выключенной записи или использовании другого рекордера эффекта может не быть.",
            "Штатная запись клипов Xbox Game Bar станет недоступна. Это не удаляет Xbox, не отключает Game Mode и не меняет OBS, ShadowPlay или запись драйвера. Поддержка ключей зависит от сборки Windows.",
            "GameDVR_Enabled=0 в HKCU\\System\\GameConfigStore и AppCaptureEnabled=0 в HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR. Проверяется запись значений; для применения может потребоваться повторный вход в Windows."),
        new("transparency", "Оформление", "Непрозрачные поверхности Windows", "Выключить системную прозрачность Пуска, панели задач и поддерживаемых поверхностей.",
            "Упрощает оформление оболочки и может уменьшить работу композиции на слабой графике. На современном игровом ПК заметной разницы производительности может не быть.",
            "Это изменение внешнего вида. Оно не отключает DWM и не меняет оформление Qivoryx. Не заявляется как способ повысить FPS в играх.",
            "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize: EnableTransparency=0. Если оболочка не обновилась сразу, выйдите из учётной записи и войдите снова.")
    };

    async Task Tuning(int token)
    {
        content.Children.Add(Note("Проверяем выбранные твики", "Читаем текущие параметры Windows и доступность обратного восстановления."));
        var reply = await Query(new { op = "tuningRead" }, "Проверка тонких настроек");
        if (token != generation) return;
        var states = Engine.Read<List<TuningStatus>>(reply.data).ToDictionary(x => x.Id);
        content.Children.Clear();

        var hero = new Grid();hero.ColumnDefinitions.Add(new());hero.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var intro = Stack(Text("МАЛЕНЬКИЕ ДЕТАЛИ. ЗАМЕТНЫЙ КОМФОРТ.", 9, "Muted", true), new Border { Height = 12 }, Text("Быстрее отклик.\nВаши правила.", 28, bold: true), new Border { Height = 12 }, Text("Настройте поведение системы под свои привычки.\nВыберите нужное и оцените результат на своём ПК.", 12, "Muted"));
        hero.Children.Add(intro);
        var stamp = Stack(Text("CONTROL", 18, bold: true), Text("ТОЧНАЯ НАСТРОЙКА", 9, "Muted"), new Border { Height = 18 }, Text("04", 48, bold: true), Text("обратимых настройки", 10, "Muted"));stamp.Margin = new(26, 6, 0, 0);Grid.SetColumn(stamp, 1);hero.Children.Add(stamp);
        content.Children.Add(Card(hero, new Thickness(26)));

        string category = "Все";
        var cards = new StackPanel();var filters = new WrapPanel { Margin = new(0, 0, 0, 16) };
        var filterButtons = new Dictionary<string, Button>();
        foreach (var name in new[] { "Все", "Отзывчивость", "Ввод", "Игры", "Оформление" })
        {
            var value = name;
            var b = Button(name, () => { category = value;Render(); });b.Padding = new(13, 9, 13, 9);b.Margin = new(0, 0, 7, 6);filterButtons.Add(name, b);filters.Children.Add(b);
        }
        content.Children.Add(filters);content.Children.Add(cards);

        void Render()
        {
            foreach (var pair in filterButtons)
            {
                pair.Value.SetResourceReference(Control.BackgroundProperty, pair.Key == category ? "Accent" : "Card");
                pair.Value.SetResourceReference(Control.ForegroundProperty, pair.Key == category ? "OnAccent" : "Fg");
            }
            cards.Children.Clear();
            foreach (var tweak in TuningTweaks.Where(t => category == "Все" || t.Category == category))
            {
                if (!states.TryGetValue(tweak.Id, out var state)) continue;
                var header = new Grid();header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                header.Children.Add(Stack(Text(tweak.Category.ToUpperInvariant(), 9, "Muted", true), new Border { Height = 8 }, Text(tweak.Title, 18, bold: true)));
                var badge = new Border { Child = Text(state.CurrentLabel, 11), CornerRadius = new(6), Padding = new(10, 7, 10, 7), VerticalAlignment = VerticalAlignment.Top, Margin = new(14, 0, 0, 0) };badge.SetResourceReference(Border.BackgroundProperty, "Hover");Grid.SetColumn(badge, 1);header.Children.Add(badge);
                var body = Stack(header, new Border { Height = 14 }, Text(tweak.Description, 13), new Border { Height = 8 }, Text(tweak.Benefit, 12, "Muted"), new Border { Height = 18 });
                var details = Stack(Text("ЧТО УЧЕСТЬ", 9, "Muted", true), new Border { Height = 8 }, Text(tweak.Caution, 12), new Border { Height = 14 }, Text("МЕХАНИЗМ", 9, "Muted", true), new Border { Height = 8 }, Text(tweak.Technical, 11, "Muted"));
                var detailBox = new Border { Child = details, Padding = new(16), CornerRadius = new(8), Visibility = Visibility.Collapsed, Margin = new(0, 16, 0, 0) };detailBox.SetResourceReference(Border.BackgroundProperty, "Bg");
                var apply = Button(state.Optimized ? "Уже настроено" : "Применить", () =>
                {
                    if (Confirm(tweak.Title, tweak.Description + "\n\n" + tweak.Caution + "\n\nQivoryx сохранит исходные значения перед изменением. Если вы измените их другой программой, автоматический откат будет заблокирован, чтобы сохранить ваш новый выбор.", "Применить"))
                        _ = Act(new { op = "tuningApply", id = tweak.Id }, "Применение настройки: " + tweak.Title, false, async () => { if (page == 11) await Navigate(11); });
                }, !state.Optimized);apply.IsEnabled = state.Available && !state.Optimized;
                var restore = Button("Вернуть исходное", () =>
                {
                    if (Confirm("Вернуть исходные значения?", "Будут восстановлены параметры до последнего применения этого твика в Qivoryx. Отсутствовавшие значения реестра будут удалены. Другие твики не затрагиваются.", "Восстановить"))
                        _ = Act(new { op = "tuningRestore", id = tweak.Id }, "Восстановление настройки", false, async () => { if (page == 11) await Navigate(11); });
                });restore.IsEnabled = state.Available && state.CanRestore;
                var more = Button("Подробнее  ↓", () => { detailBox.Visibility = detailBox.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;if (detailBox.Visibility == Visibility.Visible) detailBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))); });
                var actions = new WrapPanel();foreach (var b in new[] { apply, restore, more }) { b.Margin = new(0, 0, 8, 5);actions.Children.Add(b); }body.Children.Add(actions);
                if (state.RestoreBlocked) { Gap(body, 8);body.Children.Add(Text("Настройка изменена другой программой. Автоматический откат приостановлен.", 11, "Muted")); }
                if (!string.IsNullOrWhiteSpace(state.Error)) { Gap(body, 8);body.Children.Add(Text(state.Error, 11, "Muted")); }
                body.Children.Add(detailBox);cards.Children.Add(Card(body));
            }
        }
        Render();
        var extra = Stack(Text("Отклик зависит и от экрана", 17, bold: true), new Border { Height = 10 }, Text("Проверьте частоту обновления монитора. Высокая частота делает движение плавнее, если дисплей и игра её поддерживают. Затем сравните время кадров в лаборатории.", 12, "Muted"), new Border { Height = 16 });
        extra.Children.Add(Row(Button("Частота монитора  ↗", () => Engine.Open("ms-settings:display-advanced")), Button("Лаборатория кадров →", () => _ = Navigate(12))));
        content.Children.Add(Card(extra));
    }
}
