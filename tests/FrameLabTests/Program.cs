using Qivoryx;

int checks = 0;
var checkNames = new List<string>();
void Check(bool condition, string title)
{
    checks++;if (!condition) throw new Exception("FAIL: " + title);checkNames.Add(title);Console.WriteLine("PASS: " + title);
}
void Close(double actual, double expected, string title, double precision = 0.000001) => Check(Math.Abs(actual - expected) < precision, title + $" ({actual:0.######})");
FrameImport Parse(string input) => FrameAnalyzer.Parse(new StringReader(input));
void Reject(string input, string title)
{
    bool rejected = false;try { Parse(input); } catch (InvalidDataException) { rejected = true; }Check(rejected, title);
}

var constant = new FrameCapture { Metric = "MsBetweenPresents", Frames = Enumerable.Repeat(10d, 100).ToArray() };
var stats = FrameAnalyzer.Statistics(constant, 10);
Close(stats.AverageFps, 100, "1000 / mean frame time");Close(stats.LowOnePercentFps, 100, "constant frame 1% low");Close(stats.DurationSeconds, 1, "duration sums intervals");Check(stats.Stutters == 0, "threshold is strictly greater");
var heterogeneous = constant with { Frames = new[] { 10d, 30d } };
Close(FrameAnalyzer.Statistics(heterogeneous).AverageFps, 50, "FPS is not arithmetic mean of per-frame FPS");
var tail = constant with { Frames = Enumerable.Repeat(10d, 99).Append(100).Append(200).ToArray() };
stats = FrameAnalyzer.Statistics(tail);
Close(stats.LowOnePercentFps, 1000d / 150, "ceil(101 * 1%) = two worst frames");Check(stats.Stutters == 2, "both long intervals counted");Close(stats.MedianMs, 10, "median");Close(stats.P99Ms, 100, "P99 linear interpolation");
Close(FrameAnalyzer.Statistics(constant with { Frames = [10, 20] }).MedianMs, 15, "even-length interpolated median");

var parsed = Parse("Application,ProcessID,SwapChainAddress,MsBetweenPresents,CPUFrameTime\r\n\"game, one.exe\",7,0x1,10,999\r\n\"game, one.exe\",7,0x1,20,999\r\n\"game, one.exe\",8,0x1,30,999\r\nother.exe,7,0x1,40,999\r\n\"game, one.exe\",7,0x2,50,999\r\n");
Check(parsed.Streams.Count == 4, "process name, PID and swapchain are separate streams");Check(parsed.Streams[0].Frames.SequenceEqual([10d, 20d]), "quoted commas and row order preserved");Check(parsed.Metric == "MsBetweenPresents", "one metric chosen per file");
parsed = Parse("Application;CPUFrameTime;MsGPUBusy\ngame.exe;16,5;4\ngame.exe;bad;5\ngame.exe;NaN;5\ngame.exe;Infinity;5\ngame.exe;-1;5\ngame.exe;0;5\ngame.exe;25;4\n");
Check(parsed.InvalidRows == 5, "bad, NaN, Infinity, zero and negative rows excluded");Check(parsed.Streams[0].Frames.SequenceEqual([16.5d, 25]), "semicolon delimiter and decimal comma");
Check(parsed.Streams[0].Warnings.Any(w => w.Contains("CPU")), "CPU metric caveat retained");
parsed = Parse("sep=;\n\uFEFFApplication;FrameTime\n\"game \"\"special\"\".exe\";12.25\n");Check(parsed.Streams[0].ProcessName == "game \"special\".exe", "Excel separator, BOM and escaped quotes");
parsed = Parse("Application\tCPUFrameTime\n\"game\nname.exe\"\t15\n");Check(parsed.Streams[0].ProcessName == "game\nname.exe", "tab delimiter and quoted multiline field");
parsed = Parse("Application,MsBetweenPresents,CPUFrameTime\ngame.exe,NA,12\ngame.exe,10,12\n");Check(parsed.InvalidRows == 1 && parsed.Streams[0].Frames.Length == 1, "invalid selected metric never falls back to another per-row metric");
Reject("Application,MsBetweenPresents\n\"unterminated,10\n", "unterminated quoted record is rejected");
Reject("Application,FPS\ngame.exe,120\n", "FPS-only columns rejected");
Reject("Application,GPUBusy,DisplayLatency\ngame.exe,12,20\n", "latency and busy metrics are not frame intervals");
Reject("Application,FrameTime,FrameTime\ngame.exe,10,10\n", "duplicate columns rejected");
Reject("Application,FrameTime\ngame.exe,garbage\n", "empty valid stream rejected");
Reject("", "empty file rejected");
parsed = Parse("Application,FrameTime\ngame.exe,10,extra\ngame.exe,20\n");Check(parsed.InvalidRows == 1 && parsed.Streams[0].Frames[0] == 20, "field count mismatch skipped without shifting values");

var waveform = Enumerable.Repeat(10d, 100_000).ToArray();waveform[49_999] = 280;waveform[50_001] = 1;
var points = FrameAnalyzer.Downsample(waveform, 300);
Check(points.Any(p => p.Index == 49_999 && p.Milliseconds == 280), "downsampling preserves isolated maximum spike");Check(points.Any(p => p.Index == 50_001 && p.Milliseconds == 1), "downsampling preserves isolated minimum");
Check(points.Length <= 602 && points[0].Index == 0 && points[^1].Index == 99_999, "downsampling bounded and retains endpoints");
Check(points.Zip(points.Skip(1)).All(p => p.First.Index < p.Second.Index), "downsampling keeps temporal ordering");

var malicious = constant with { Title = "<script>alert('x')</script>", Scenario = "<img src=x onerror=alert(1)> & text", ProcessName = "\"game, \"special\".exe", SwapChain = "0x1", Warnings = ["<iframe>unsafe</iframe>"] };
string html = FrameAnalyzer.ToHtml(malicious, constant, 50);
Check(!html.Contains(malicious.Title) && html.Contains("&lt;script&gt;"), "HTML escapes capture title");Check(!html.Contains("<img src=x") && html.Contains("&lt;img"), "HTML escapes scenario");Check(!html.Contains("<iframe>") && html.Contains("&lt;iframe&gt;"), "HTML escapes warnings");
Check(html.Contains("data-line=\"a\"") && html.Contains("series-b"), "standalone report contains interactive A/B series controls");
var roundTrip = Parse(FrameAnalyzer.ToCsv(malicious));Check(roundTrip.Streams[0].Frames.SequenceEqual(malicious.Frames), "CSV normalized intervals round trip");Check(roundTrip.Streams[0].ProcessName == malicious.ProcessName, "CSV quoted metadata round trip");Check(roundTrip.Metric == malicious.Metric, "CSV export preserves metric identity");
Check(FrameAnalyzer.ComparisonWarnings(constant, constant with { Metric = "CPUFrameTime" }).Any(w => w.Contains("разные метрики")), "comparison flags incompatible metrics");
Check(FrameAnalyzer.ComparisonWarnings(constant with { Scenario = "1080p Ultra" }, constant with { Scenario = "4k Low" }).Any(w => w.Contains("различаются")), "comparison flags different scenarios");

string directory = Path.Combine(Path.GetTempPath(), "qivoryx-framelab-test-" + Guid.NewGuid().ToString("N"));
try
{
    FrameLibrary.Save(directory, malicious);var library = FrameLibrary.List(directory);Check(library.Length == 1 && library[0].Id == malicious.Id, "saved library metadata");
    var restored = FrameLibrary.Load(directory, malicious.Id);Check(restored.Frames.SequenceEqual(malicious.Frames) && restored.Scenario == malicious.Scenario, "saved library lossless round trip");
    bool rejected = false;try { FrameLibrary.Load(directory, "../outside"); } catch (InvalidDataException) { rejected = true; }Check(rejected, "library refuses path traversal");
    FrameLibrary.Delete(directory, malicious.Id);Check(FrameLibrary.List(directory).Length == 0, "delete removes saved data and metadata");
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

Console.WriteLine($"\nAll {checks} FrameLab checks passed.");
var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root.Parent != null && !File.Exists(Path.Combine(root.FullName, "Tweaker", "FrameAnalysis.cs"))) root = root.Parent;
var reportDirectory = Path.Combine(root.FullName, "artifacts");Directory.CreateDirectory(reportDirectory);
File.WriteAllText(Path.Combine(reportDirectory, "framelab-tests.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = true, Checks = checks, TimestampUtc = DateTimeOffset.UtcNow, TestNames = checkNames }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
