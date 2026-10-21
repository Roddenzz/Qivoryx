using System.Windows;
using System.Windows.Controls;

namespace Qivoryx;

public record PowerSetting(string Key, string Name, int Min, int Max, string Unit, string Description, string Impact, string[]? Options = null);

public partial class MainWindow
{
    static readonly PowerSetting[] PowerSettings =
    {
        new("min", "Минимальное состояние процессора", 0, 100, "%", "Нижняя граница запроса производительности CPU. Это процент состояния производительности, а не загрузки. Современные процессоры могут управлять частотой самостоятельно.", "Высокое значение иногда сокращает задержку выхода на частоту, но повышает потребление в простое и температуру. 100% не гарантирует FPS и может ухудшить плавность при перегреве."),
        new("max", "Максимальное состояние процессора", 1, 100, "%", "Верхняя граница запроса производительности CPU. Значения ниже 100% ограничивают доступную производительность. На части CPU 99% отключает турбо, но это не универсальное правило.", "Для игр обычно оставляют 100%. Снижение помогает ограничить нагрев и шум, ценой возможного падения FPS в сценах с нагрузкой на CPU."),
        new("boost", "Режим повышения частоты (Boost)", 0, 6, "", "Политика турбоускорения при наличии температурного и электрического запаса. Поддержка режимов зависит от CPU, BIOS и драйвера.", "Турбо может улучшить FPS и время кадра при ограничении со стороны CPU. Агрессивный режим увеличивает нагрев; при длительной нагрузке возможен троттлинг, если охлаждение не справляется.", new[] { "Отключён", "Включён", "Агрессивный", "Эффективный", "Эффективный агрессивный", "Агрессивный при гарантии", "Эффективный при гарантии" }),
        new("parking", "Минимум активных ядер", 0, 100, "%", "Минимальная доля логических процессоров без парковки. 100% запрещает парковку всех ядер; 0% позволяет планировщику управлять ими свободно.", "Иногда уменьшает задержки пробуждения ядер. Современный планировщик и так учитывает нагрузку; запрет парковки может только увеличить потребление."),
        new("epp", "Предпочтение энергоэффективности (EPP)", 0, 100, "%", "0 — предпочтение производительности, 100 — экономии. Применяется аппаратным управлением CPU (HWP/CPPC), если оно поддерживается.", "Низкий EPP помогает быстрее набирать частоту, но увеличивает потребление. В играх с ограничением со стороны GPU разница FPS может отсутствовать; результат зависит от охлаждения и лимитов мощности CPU."),
        new("cooling", "Политика охлаждения", 0, 1, "", "Активная политика сначала ускоряет вентиляторы, пассивная — сначала снижает производительность CPU. Прошивка и утилиты производителя могут иметь приоритет.", "Активная политика помогает удерживать частоты при длительной нагрузке, ценой шума. Пассивная тише, но может снижать FPS из-за ограничения CPU.", new[] { "Пассивная", "Активная" }),
        new("pcie", "Энергосбережение PCI Express", 0, 2, "", "Управляет энергосбережением связи PCIe (ASPM). Умеренное и максимальное значения разрешают более глубокие состояния сна.", "Отключение иногда помогает при задержках выхода устройств из сна. Прирост FPS не гарантирован; расход энергии и нагрев могут увеличиться.", new[] { "Отключено", "Умеренное", "Максимальное" }),
        new("usb", "Выборочное приостановление USB", 0, 1, "", "Позволяет усыплять отдельные USB-порты при простое. Это не настройка частоты опроса мыши и не разгон USB.", "Отключение может помочь устройствам, теряющим соединение после простоя. Обычный FPS не меняется; энергопотребление в простое может вырасти.", new[] { "Отключено", "Включено" }),
        new("disk", "Отключение диска после простоя", 0, 3600, " с", "Тайм-аут остановки диска в секундах; 0 — никогда. Наиболее актуален для HDD, поддержка SSD зависит от драйвера и накопителя.", "Для HDD значение 0 исключает ожидание раскрутки после простоя. На средний FPS активной игры обычно не влияет; увеличивает время вращения диска."),
        new("display", "Выключение экрана после простоя", 0, 7200, " с", "Время бездействия до выключения экрана. 0 — никогда. Это не настройка сна компьютера, частоты обновления или задержки матрицы.", "Не влияет на FPS и время кадра активной игры. Меняет энергопотребление и поведение экрана при отсутствии ввода.")
    };
    async Task Power(int token)
    {
        content.Children.Add(Note("Читаем активный план", "Определяем поддерживаемые настройки через powercfg без изменения системы."));
        var response = await Query(new { op = "powerRead" }, "Чтение параметров питания");if (token != generation) return;content.Children.Clear();
        var data = response.data;var settings = data.GetProperty("Values");const string source = "AC";
        var staged = new Dictionary<string, Dictionary<string, int>> { ["AC"] = new() };
        foreach (var s in PowerSettings) if (settings.TryGetProperty(s.Key, out var val)) { staged["AC"][s.Key] = val.GetProperty("AC").GetInt32(); }
        var editors = new StackPanel();
        var hint = Text("Изменения ещё не применены", 12, "Muted");hint.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(Card(Stack(Text("ПРОИЗВОДИТЕЛЬНОСТЬ НАСТОЛЬНОГО ПК", 10, "Muted", true), new Border { Height = 12 }, Text("Частоты. Температура. Время кадра.", 25, bold: true), new Border { Height = 12 }, Text("Настройки применяются к копии вашего плана. Исходный план остаётся доступен для возврата.", 12, "Muted"), new Border { Height = 14 }, Text("Текущий план  /  " + data.GetProperty("Plan").GetString(), 11, "Muted"))));
        void Render()
        {
            editors.Children.Clear();
            if (AsymmetricX3d) editors.Children.Add(Note("Ваш Ryzen X3D использует два разных CCD", "Оставьте парковку под управлением планировщика и AMD Chipset Driver. Принудительные 100% активных ядер могут мешать выбору игрового CCD. Сохраните Game Mode и Xbox Game Bar; состояние компонентов можно проверить в разделе «Графика и дисплей»."));
            foreach (var s in PowerSettings)
            {
                var available = staged[source].ContainsKey(s.Key);
                if (s.Key == "min") editors.Children.Add(new Border { Child = Text("01  /  ПРОЦЕССОР И ОХЛАЖДЕНИЕ", 10, "Muted", true), Margin = new(2, 10, 0, 18) });
                if (s.Key == "pcie") editors.Children.Add(new Border { Child = Text("02  /  УСТРОЙСТВА И ПРОСТОЙ", 10, "Muted", true), Margin = new(2, 12, 0, 18) });
                var body = Stack(Text(s.Name, 17, bold: true), new Border { Height = 9 }, Text(s.Description, 12, "Muted"), new Border { Height = 20 });
                if (available)
                {
                    int current = staged[source][s.Key];
                    if (s.Options != null)
                    {
                        var options = s.Options.Select((v, i) => $"{i} · {v}").ToList();
                        if (current >= options.Count) options.Add($"{current} · Другое значение Windows");
                        var combo = new ComboBox { ItemsSource = options, SelectedIndex = current < s.Options.Length ? current : options.Count - 1, MaxWidth = 370, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 260 };
                        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex < s.Options.Length) staged[source][s.Key] = combo.SelectedIndex;hint.Text = "Есть неприменённые изменения"; };body.Children.Add(combo);
                    }
                    else
                    {
                        var number = Text($"{current}{s.Unit}", 22, bold: true);number.MinWidth = 100;
                        var slider = new Slider { Minimum = s.Min, Maximum = s.Max, Value = current, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 360, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 24, 0) };
                        slider.ValueChanged += (_, _) => { int v = (int)slider.Value;staged[source][s.Key] = v;number.Text = $"{v}{s.Unit}";hint.Text = "Есть неприменённые изменения"; };body.Children.Add(Row(slider, number));
                    }
                }
                else body.Children.Add(Text("Недоступно на этом устройстве или в этом плане", 12, "Muted"));
                Gap(body, 18);
                var impact = new Border { Child = Stack(Text("КАК ЭТО ВЛИЯЕТ НА ПРОИЗВОДИТЕЛЬНОСТЬ", 9, "Muted", true), new Border { Height = 8 }, Text(s.Impact, 12)), Padding = new(14), CornerRadius = new(8), BorderThickness = new(1) };
                impact.SetResourceReference(Border.BackgroundProperty, "Input");impact.SetResourceReference(Border.BorderBrushProperty, "Line");body.Children.Add(impact);editors.Children.Add(Card(body));
            }
        }
        var preset = Button("Игровой профиль", () =>
        {
            foreach (var p in new Dictionary<string, int> { ["min"] = 5, ["max"] = 100, ["boost"] = 1, ["epp"] = 0, ["cooling"] = 1 }) if (staged[source].ContainsKey(p.Key)) staged[source][p.Key] = p.Value;
            hint.Text = "Профиль выбран, но ещё не применён";Render();
        });
        var actions = new WrapPanel();actions.Children.Add(Button("Применить к копии плана", () =>
        {
            if (Confirm("Применить параметры питания?", "Будет создана копия текущего плана, изменены доступные параметры настольного ПК и включён новый план. Предыдущий план сохранится.\n\nЗначения могут увеличить нагрев, шум и энергопотребление. На устройствах с Modern Standby часть настроек может не поддерживаться; в этом случае операция откатится.", "Применить"))
                _ = Act(new { op = "powerApply", source, values = staged[source] }, "Применение плана питания", true, async () => { if (page == 2) await Navigate(2); });
        }, true));actions.Children.Add(preset);actions.Children.Add(Button("Предыдущий план", () => { if (Confirm("Вернуть предыдущий план?", "Будет включён исходный план из последней сохранённой операции Qivoryx.")) _ = Act(new { op = "powerRestore" }, "Восстановление плана", true, async () => { if (page == 2) await Navigate(2); }); }));
        content.Children.Add(actions);Gap(content, 12);content.Children.Add(hint);
        Gap(content);content.Children.Add(Note("Измеряйте результат на своём компьютере", "Игровой профиль: минимум CPU 5%, максимум 100%, Boost включён, EPP 0 и активное охлаждение — только для поддерживаемых настроек. Параметры USB, PCIe и парковки он не трогает. Универсального прироста FPS нет. Сравнивайте одинаковую игровую сцену, время кадра, 1% low и температуру до и после. Параметры производителя и режим питания Windows могут переопределять часть настроек."));
        content.Children.Add(editors);Render();
    }
}
