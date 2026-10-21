using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Qivoryx;

public sealed record FrameCapture
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; init; } = "Замер";
    public string SourceName { get; init; } = "";
    public string ProcessName { get; init; } = "Не указан";
    public string ProcessId { get; init; } = "";
    public string SwapChain { get; init; } = "";
    public string Metric { get; init; } = "";
    public DateTimeOffset ImportedAt { get; init; } = DateTimeOffset.Now;
    public string Scenario { get; init; } = "";
    public double[] Frames { get; init; } = [];
    public int InvalidRows { get; init; }
    public string[] Warnings { get; init; } = [];
    public string StreamLabel => $"{ProcessName}{(ProcessId.Length > 0 ? " · PID " + ProcessId : "")}{(SwapChain.Length > 0 ? " · " + SwapChain : "")} · {Frames.Length:N0} кадров";
}

public sealed record FrameImport(IReadOnlyList<FrameCapture> Streams, int Rows, int InvalidRows, string Metric);
public sealed record FrameStatistics(int Count, double DurationSeconds, double AverageFps, double LowOnePercentFps, double MedianMs, double P95Ms, double P99Ms, double MaxMs, int Stutters, double ThresholdMs);
public sealed record FramePoint(int Index, double Milliseconds);
public sealed record FrameLibraryEntry(string Id, string Title, string ProcessName, string Metric, int Count, DateTimeOffset ImportedAt, string Scenario)
{
    public string Label => $"{Title} · {ProcessName} · {Count:N0} кадров · {ImportedAt:dd.MM HH:mm}";
}

/// <summary>Pure analysis: no process launch, Windows settings, UI or hardware sampling.</summary>
public static class FrameAnalyzer
{
    public const int MaximumRows = 500_000;
    public const long MaximumFileBytes = 64L * 1024 * 1024;
    public const string Formula = "Средний FPS = 1000 / среднее время кадра. 1% low = 1000 / среднее самых медленных ceil(N × 1%) кадров. P95/P99 — перцентили времени кадра (линейная интерполяция). Длительность — сумма валидных интервалов; пропущенные строки в неё не входят.";
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static FrameImport Parse(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaximumFileBytes) throw new InvalidDataException("CSV больше 64 МБ. Разделите запись на несколько файлов.");
        using var reader = new StreamReader(file, Encoding.UTF8, true);
        return Parse(reader, Path.GetFileName(path));
    }

    public static FrameImport Parse(TextReader reader, string sourceName = "capture.csv")
    {
        string? header = reader.ReadLine();
        if (header == null) throw new InvalidDataException("CSV пуст.");
        header = header.TrimStart('\uFEFF');
        char? specifiedSeparator = null;
        if (header.StartsWith("sep=", StringComparison.OrdinalIgnoreCase) && header.Length == 5)
        {
            specifiedSeparator = header[4];
            header = reader.ReadLine() ?? throw new InvalidDataException("Нет заголовка CSV.");
        }
        var separator = specifiedSeparator ?? DetectSeparator(header);
        if (separator is not (',' or ';' or '\t')) throw new InvalidDataException("Поддерживаются разделители: запятая, точка с запятой и табуляция.");
        using var combined = new PrefixReader(header + "\n", reader);
        var records = CsvRecords(combined, separator).GetEnumerator();
        if (!records.MoveNext() || records.Current.Malformed) throw new InvalidDataException("Некорректный заголовок CSV.");
        var columns = records.Current.Fields.Select(NormalizeHeader).ToArray();
        if (columns.GroupBy(c => c).Any(g => g.Count() > 1)) throw new InvalidDataException("В CSV повторяются имена столбцов.");
        int Column(params string[] names) => Array.FindIndex(columns, c => names.Any(n => c == NormalizeHeader(n)));
        var metricNames = new[] { "MsBetweenPresents", "CPUFrameTime", "MsBetweenAppStart", "FrameTime" };
        string metric = metricNames.FirstOrDefault(n => Column(n) >= 0) ?? throw new InvalidDataException("Нет столбца времени кадра: MsBetweenPresents, CPUFrameTime, MsBetweenAppStart или FrameTime (мс). FPS, GPUBusy и DisplayLatency для этого не подходят.");
        int frameColumn = Column(metric), appColumn = Column("Application", "ProcessName", "Process"), pidColumn = Column("ProcessID", "PID"), chainColumn = Column("SwapChainAddress", "SwapChain");
        int frameTypeColumn = Column("FrameType");
        var groups = new Dictionary<string, FrameGroup>(StringComparer.Ordinal);
        int rows = 0, invalid = 0;
        while (records.MoveNext())
        {
            var record = records.Current;
            if (record.Fields.Count == 1 && string.IsNullOrWhiteSpace(record.Fields[0]) && !record.Malformed) continue;
            if (++rows > MaximumRows) throw new InvalidDataException("В записи больше 500 000 строк. Экспортируйте более короткий участок.");
            if (record.Malformed || record.Fields.Count != columns.Length) { invalid++; continue; }
            string Field(int index) => index >= 0 ? record.Fields[index].Trim() : "";
            var app = Field(appColumn);var pid = Field(pidColumn);var chain = Field(chainColumn);
            var key = app + "\0" + pid + "\0" + chain;
            if (!groups.TryGetValue(key, out var group))
            {
                if (groups.Count >= 256) throw new InvalidDataException("В CSV слишком много потоков (больше 256). Сначала ограничьте запись одним приложением.");
                groups[key] = group = new(app.Length == 0 ? "Не указан" : app, pid, chain);
            }
            // A missing value never falls back to another metric on that row.
            if (!TryMilliseconds(Field(frameColumn), separator, out var value)) { invalid++;group.Invalid++;continue; }
            group.Frames.Add(value);
            if (frameTypeColumn >= 0) group.FrameTypes.Add(Field(frameTypeColumn));
        }
        var streams = new List<FrameCapture>();
        foreach (var group in groups.Values.Where(g => g.Frames.Count > 0).OrderByDescending(g => g.Frames.Count))
        {
            var warnings = new List<string>();
            if ((appColumn < 0 && pidColumn < 0) || (group.Name == "Не указан" && group.Pid.Length == 0)) warnings.Add("В файле нет идентификатора процесса: подтвердите, что запись относится к одному приложению и одному потоку.");
            if (chainColumn < 0 || group.Chain.Length == 0) warnings.Add("Swapchain не указан: убедитесь, что экспорт содержит один поток кадров выбранного процесса.");
            if (invalid > 0) warnings.Add($"Пропущено некорректных строк во всём файле: {invalid:N0}; в этом потоке: {group.Invalid:N0}. Сумма интервалов не включает пропуски.");
            if (metric == "CPUFrameTime" || metric == "MsBetweenAppStart") warnings.Add("Используется интервал начала работы CPU над кадрами. Это темп подготовки кадров, а не измерение задержки ввода или частоты обновления дисплея.");
            if (metric == "FrameTime") warnings.Add("Общий столбец FrameTime принят в миллисекундах. Проверьте единицы и смысл метрики в источнике записи.");
            if (group.FrameTypes.Count > 1 || group.FrameTypes.Any(t => !string.IsNullOrEmpty(t) && t is not ("0" or "1" or "NotSet" or "Application"))) warnings.Add("В записи есть типы кадров FrameType: " + string.Join(", ", group.FrameTypes.Take(8)) + ". Генерация кадров и повторные кадры требуют одинаковой методики в A и B.");
            if (group.Frames.Count < 100) warnings.Add("Меньше 100 кадров: 1% low и редкие задержки недостаточно устойчивы. Используйте повторяемый участок длительностью 30–60 секунд.");
            streams.Add(new() { Title = Path.GetFileNameWithoutExtension(sourceName), SourceName = Path.GetFileName(sourceName), ProcessName = group.Name, ProcessId = group.Pid, SwapChain = group.Chain, Metric = metric, Frames = group.Frames.ToArray(), InvalidRows = group.Invalid, Warnings = warnings.ToArray() });
        }
        if (streams.Count == 0) throw new InvalidDataException("Нет валидных интервалов. Нужны положительные конечные значения времени кадра в миллисекундах.");
        return new(streams, rows, invalid, metric);
    }

    static bool TryMilliseconds(string text, char separator, out double value)
    {
        if (separator != ',' && text.Count(c => c == ',') == 1 && !text.Contains('.')) text = text.Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, Invariant, out value) && double.IsFinite(value) && value > 0 && value <= 600_000;
    }

    public static FrameStatistics Statistics(FrameCapture capture, double thresholdMs = 50)
    {
        if (!double.IsFinite(thresholdMs) || thresholdMs <= 0) throw new ArgumentOutOfRangeException(nameof(thresholdMs));
        Validate(capture);
        var sorted = (double[])capture.Frames.Clone();Array.Sort(sorted);
        double total = sorted.Sum();int tail = Math.Max(1, (int)Math.Ceiling(sorted.Length * 0.01));
        return new(sorted.Length, total / 1000, 1000 * sorted.Length / total, 1000 / sorted.Skip(sorted.Length - tail).Average(), Percentile(sorted, .5), Percentile(sorted, .95), Percentile(sorted, .99), sorted[^1], sorted.Count(v => v > thresholdMs), thresholdMs);
    }

    static double Percentile(double[] sorted, double p)
    {
        double position = (sorted.Length - 1) * p;int lo = (int)position;int hi = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (position - lo);
    }

    public static void Validate(FrameCapture capture)
    {
        if (capture.Frames == null || capture.Frames.Length == 0 || capture.Frames.Length > MaximumRows || capture.Frames.Any(f => !double.IsFinite(f) || f <= 0 || f > 600_000)) throw new InvalidDataException("Повреждены интервалы в сохранённом замере.");
        if (capture.Id == null || capture.Title == null || capture.Scenario == null || capture.ProcessName == null || capture.ProcessId == null || capture.SwapChain == null || capture.SourceName == null || capture.Metric == null || capture.Warnings == null || capture.Id.Length > 80 || capture.Title.Length > 1000 || capture.Scenario.Length > 4000 || capture.ProcessName.Length > 1000 || capture.ProcessId.Length > 100 || capture.SwapChain.Length > 1000 || capture.SourceName.Length > 1000 || capture.Warnings.Length > 256 || capture.Warnings.Any(w => w == null || w.Length > 16_000)) throw new InvalidDataException("Некорректные метаданные замера.");
        if (capture.Metric is not ("MsBetweenPresents" or "CPUFrameTime" or "MsBetweenAppStart" or "FrameTime")) throw new InvalidDataException("Неизвестная метрика времени кадра.");
    }

    public static IReadOnlyList<string> ComparisonWarnings(FrameCapture a, FrameCapture b)
    {
        var warnings = new List<string>();
        if (!string.Equals(a.Metric, b.Metric, StringComparison.Ordinal)) warnings.Add("A и B используют разные метрики. Проценты сравнения отключены: импортируйте одинаковый тип интервалов.");
        if (!string.Equals(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase)) warnings.Add("Названия процессов A и B различаются. Проверьте, что измеряется одна и та же игра или задача.");
        if (string.IsNullOrWhiteSpace(a.Scenario) || string.IsNullOrWhiteSpace(b.Scenario)) warnings.Add("Запишите сцену, разрешение, пресет, лимит FPS и режим генерации кадров для A и B. Без одинаковых условий изменение FPS нельзя приписать твикам.");
        else if (!string.Equals(a.Scenario.Trim(), b.Scenario.Trim(), StringComparison.OrdinalIgnoreCase)) warnings.Add("Описания условий A и B различаются. Результаты могут отражать смену сцены или графики.");
        double durationA = a.Frames.Sum() / 1000, durationB = b.Frames.Sum() / 1000;
        if (a.Id == b.Id) warnings.Add("A и B — один и тот же сохранённый замер. Для проверки изменений импортируйте отдельную повторную запись.");
        if (Math.Min(durationA, durationB) < 30) warnings.Add("Хотя бы один замер короче 30 секунд. Повторите одинаковый маршрут несколько раз, чтобы оценить разброс.");
        if (Math.Max(durationA, durationB) / Math.Min(durationA, durationB) > 1.2) warnings.Add("Длительность замеров отличается больше чем на 20%. Сравнение количества рывков приведено также на минуту.");
        return warnings;
    }

    /// <summary>Temporal min/max buckets retain spikes; result is bounded by approximately 2 points per horizontal pixel.</summary>
    public static FramePoint[] Downsample(double[] frames, int bucketCount)
    {
        if (frames.Length == 0) return [];
        bucketCount = Math.Clamp(bucketCount, 1, 4096);
        if (frames.Length <= bucketCount * 2) return frames.Select((v, i) => new FramePoint(i, v)).ToArray();
        var points = new SortedDictionary<int, double> { [0] = frames[0], [frames.Length - 1] = frames[^1] };
        for (int bucket = 0; bucket < bucketCount; bucket++)
        {
            int start = (int)((long)bucket * frames.Length / bucketCount), end = (int)((long)(bucket + 1) * frames.Length / bucketCount);
            int min = start, max = start;
            for (int i = start + 1; i < end; i++) { if (frames[i] < frames[min]) min = i;if (frames[i] > frames[max]) max = i; }
            points[min] = frames[min];points[max] = frames[max];
        }
        return points.Select(p => new FramePoint(p.Key, p.Value)).ToArray();
    }

    public static string ToCsv(FrameCapture capture)
    {
        Validate(capture);
        string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        // Preserve the original metric identity on re-import.
        var output = new StringBuilder("Application,ProcessID,SwapChainAddress," + capture.Metric + "\r\n");
        foreach (double frame in capture.Frames) output.Append(Quote(capture.ProcessName)).Append(',').Append(Quote(capture.ProcessId)).Append(',').Append(Quote(capture.SwapChain)).Append(',').Append(frame.ToString("R", Invariant)).Append("\r\n");
        return output.ToString();
    }

    public static string ToHtml(FrameCapture a, FrameCapture? b = null, double thresholdMs = 50)
    {
        var x = Statistics(a, thresholdMs);var y = b == null ? null : Statistics(b, thresholdMs);
        string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        string N(double n) => n.ToString("0.00", Invariant);
        var html = new StringBuilder("<!doctype html><html lang=\"ru\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Qivoryx — лаборатория кадров</title><style>body{background:#0d0d0e;color:#f3f3f3;font:15px system-ui;margin:0;padding:5vw;max-width:1300px}h1{font-size:38px;letter-spacing:-1.5px}h2{font-size:20px}p{line-height:1.7;color:#b9b9be}.panel{border:1px solid #343438;border-radius:20px;padding:25px;margin:22px 0;background:#171719}table{width:100%;border-collapse:collapse}td,th{text-align:left;padding:13px 8px;border-bottom:1px solid #343438}th{color:#aaa}svg{width:100%;height:auto}.warning{border-left:2px solid #888;padding-left:16px}small{color:#aaa}button{background:#eee;color:#111;border:0;padding:10px 15px;border-radius:8px;cursor:pointer}.series-b{stroke-dasharray:7 5}label{margin-right:22px}input{accent-color:#999}</style><h1>Лаборатория кадров</h1><p>Qivoryx · локальный отчёт · " + E(DateTimeOffset.Now.ToString("dd.MM.yyyy HH:mm")) + "</p>");
        void Details(string slot, FrameCapture capture, FrameStatistics stats)
        {
            html.Append("<section class=\"panel\"><h2>").Append(slot).Append(" · ").Append(E(capture.Title)).Append("</h2><p>").Append(E(capture.StreamLabel)).Append("<br>Метрика: ").Append(E(capture.Metric)).Append("<br>Условия: ").Append(E(capture.Scenario.Length == 0 ? "Не записаны" : capture.Scenario)).Append("</p><small>").Append(stats.Count.ToString(Invariant)).Append(" кадров · ").Append(N(stats.DurationSeconds)).Append(" с · импорт ").Append(E(capture.ImportedAt.ToString("dd.MM.yyyy HH:mm"))).Append("</small></section>");
        }
        Details("A", a, x);if (b != null) Details("B", b, y!);
        html.Append("<section class=\"panel\"><table><thead><tr><th>Показатель</th><th>A</th>").Append(b != null ? "<th>B</th>" : "").Append("</tr></thead><tbody>");
        void Stat(string title, string va, string? vb) => html.Append("<tr><td>").Append(E(title)).Append("</td><td>").Append(E(va)).Append("</td>").Append(vb == null ? "" : "<td>" + E(vb) + "</td>").Append("</tr>");
        Stat("Средний FPS ↑", N(x.AverageFps), y == null ? null : N(y.AverageFps));Stat("1% low FPS ↑", N(x.LowOnePercentFps), y == null ? null : N(y.LowOnePercentFps));
        Stat("Медиана, мс ↓", N(x.MedianMs), y == null ? null : N(y.MedianMs));Stat("P95, мс ↓", N(x.P95Ms), y == null ? null : N(y.P95Ms));Stat("P99, мс ↓", N(x.P99Ms), y == null ? null : N(y.P99Ms));Stat("Максимум, мс", N(x.MaxMs), y == null ? null : N(y.MaxMs));
        Stat($"Интервалы > {thresholdMs:0.##} мс", x.Stutters.ToString(Invariant), y?.Stutters.ToString(Invariant));Stat("Таких интервалов в минуту ↓", N(x.Stutters / x.DurationSeconds * 60), y == null ? null : N(y.Stutters / y.DurationSeconds * 60));
        html.Append("</tbody></table></section><section class=\"panel\"><h2>Время кадра · мс</h2><p>Ось X — доля последовательности кадров от 0 до 100%. Сплошная линия — A; пунктир — B. Пики сохранены при уменьшении числа точек.</p><label><input type=\"checkbox\" checked data-line=\"a\">A</label>");
        if (b != null) html.Append("<label><input type=\"checkbox\" checked data-line=\"b\">B</label>");
        double max = Math.Max(x.MaxMs, y?.MaxMs ?? 0);double chartMax = Math.Max(1, max * 1.05);
        html.Append("<svg viewBox=\"0 0 1100 330\" role=\"img\" aria-label=\"Время кадра A и B\">");
        for (int i = 0; i <= 4; i++)
        {
            double py = 20 + i * 65;html.Append("<path d=\"M60 ").Append(N(py)).Append("H1080\" stroke=\"#343438\"/><text x=\"0\" y=\"").Append(N(py + 5)).Append("\" fill=\"#aaa\" font-size=\"13\">").Append(N(chartMax * (4 - i) / 4)).Append("</text>");
        }
        void Line(string id, FrameCapture capture, string color)
        {
            html.Append("<polyline id=\"").Append(id).Append("\" class=\"series-").Append(id).Append("\" fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"1.5\" points=\"");
            foreach (var p in Downsample(capture.Frames, 1000)) html.Append(N(60 + 1020.0 * p.Index / Math.Max(1, capture.Frames.Length - 1))).Append(',').Append(N(280 - p.Milliseconds / chartMax * 260)).Append(' ');
            html.Append("\"/>");
        }
        Line("a", a, "#fafafa");if (b != null) Line("b", b, "#909096");
        html.Append("<text x=\"60\" y=\"313\" fill=\"#aaa\">0%</text><text x=\"1045\" y=\"313\" fill=\"#aaa\">100%</text></svg></section>");
        var warnings = a.Warnings.Concat(b?.Warnings ?? []).Concat(b == null ? [] : ComparisonWarnings(a, b)).Distinct();
        foreach (var warning in warnings) html.Append("<p class=\"warning\">").Append(E(warning)).Append("</p>");
        html.Append("<p>").Append(E(Formula)).Append("</p><p>Интервалы > порога — условный индикатор рывков, а не измерение input lag. Для вывода об эффекте повторите одинаковую сцену и условия несколько раз. CPU-интервалы не измеряют отображённые кадры.</p><script>document.querySelectorAll('[data-line]').forEach(el=>el.addEventListener('change',()=>document.getElementById(el.dataset.line).style.display=el.checked?'':'none'));</script></html>");
        return html.ToString();
    }

    static string NormalizeHeader(string s) => new(s.Trim().TrimStart('\uFEFF').Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    static char DetectSeparator(string line)
    {
        char best = ',';int bestCount = -1;
        foreach (char separator in new[] { ',', ';', '\t' })
        {
            bool quoted = false;int count = 0;
            for (int i = 0; i < line.Length; i++) { if (line[i] == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') i++;else quoted = !quoted; } else if (!quoted && line[i] == separator) count++; }
            if (count > bestCount) { best = separator;bestCount = count; }
        }
        return best;
    }

    sealed class FrameGroup(string name, string pid, string chain)
    {
        public string Name = name, Pid = pid, Chain = chain;
        public List<double> Frames = [];
        public HashSet<string> FrameTypes = new(StringComparer.OrdinalIgnoreCase);
        public int Invalid;
    }
    sealed record CsvRecord(List<string> Fields, bool Malformed);
    static IEnumerable<CsvRecord> CsvRecords(TextReader reader, char separator)
    {
        var fields = new List<string>();var field = new StringBuilder();bool quoted = false, afterQuote = false, malformed = false;int length = 0;
        int value;
        while ((value = reader.Read()) >= 0)
        {
            if (++length > 256 * 1024) throw new InvalidDataException("Слишком длинная строка CSV или незакрытые кавычки.");
            char c = (char)value;
            if (quoted)
            {
                if (c == '"') { if (reader.Peek() == '"') { reader.Read();field.Append('"'); } else { quoted = false;afterQuote = true; } }
                else field.Append(c);
                continue;
            }
            if (c == separator) { fields.Add(field.ToString());field.Clear();afterQuote = false; }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && reader.Peek() == '\n') reader.Read();
                fields.Add(field.ToString());yield return new(fields, malformed);fields = [];field.Clear();afterQuote = false;malformed = false;length = 0;
            }
            else if (c == '"') { if (field.Length == 0 && !afterQuote) quoted = true;else { malformed = true;field.Append(c); } }
            else { if (afterQuote && !char.IsWhiteSpace(c)) malformed = true;if (!afterQuote) field.Append(c); }
        }
        if (length > 0 || fields.Count > 0 || field.Length > 0) { fields.Add(field.ToString());yield return new(fields, malformed || quoted); }
    }
    sealed class PrefixReader(string prefix, TextReader rest) : TextReader
    {
        int index;
        public override int Read() => index < prefix.Length ? prefix[index++] : rest.Read();
        public override int Peek() => index < prefix.Length ? prefix[index] : rest.Peek();
    }
}

public static class FrameLibrary
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    static string FilePath(string directory, string id, string suffix)
    {
        if (id.Length != 32 || id.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Некорректный ID замера.");
        return Path.Combine(directory, id + suffix);
    }
    public static void Save(string directory, FrameCapture capture)
    {
        FrameAnalyzer.Validate(capture);Directory.CreateDirectory(directory);
        var path = FilePath(directory, capture.Id, ".capture.json");var meta = FilePath(directory, capture.Id, ".meta.json");
        var entry = new FrameLibraryEntry(capture.Id, capture.Title, capture.ProcessName, capture.Metric, capture.Frames.Length, capture.ImportedAt, capture.Scenario);
        WriteAtomic(path, JsonSerializer.Serialize(capture, Json));WriteAtomic(meta, JsonSerializer.Serialize(entry, Json));
    }
    static void WriteAtomic(string path, string text)
    {
        var temporary = path + ".tmp";
        try { File.WriteAllText(temporary, text, new UTF8Encoding(false));File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static FrameCapture Load(string directory, string id)
    {
        var path = FilePath(directory, id, ".capture.json");
        using var file = File.OpenRead(path);
        if (file.Length > FrameAnalyzer.MaximumFileBytes) throw new InvalidDataException("Сохранённый замер больше 64 МБ.");
        var capture = JsonSerializer.Deserialize<FrameCapture>(file, Json) ?? throw new InvalidDataException("Пустой замер.");
        FrameAnalyzer.Validate(capture);return capture;
    }
    public static FrameLibraryEntry[] List(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        var entries = new List<FrameLibraryEntry>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.meta.json").Take(2000))
        {
            try
            {
                if (new FileInfo(path).Length > 32 * 1024) continue;
                var entry = JsonSerializer.Deserialize<FrameLibraryEntry>(File.ReadAllText(path), Json);
                if (entry != null && Path.GetFileName(path) == entry.Id + ".meta.json") { _ = FilePath(directory, entry.Id, ".capture.json");entries.Add(entry); }
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { }
        }
        return entries.OrderByDescending(e => e.ImportedAt).Take(200).ToArray();
    }
    public static void Delete(string directory, string id)
    {
        File.Delete(FilePath(directory, id, ".meta.json"));File.Delete(FilePath(directory, id, ".capture.json"));
    }
}
