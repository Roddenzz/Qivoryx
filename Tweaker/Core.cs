using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Qivoryx;

public record Package(string Id, string Name, string Category, string Description, string Website, string? Url = null, string? Extension = null, string[]? Args = null, string? Signer = null);
public record ServiceInfo(string Name, string DisplayName, string? Description, string State, string StartMode, bool Delayed, int ProcessId, double Cpu, double MemoryMB, int Shared)
{
    public string StateLabel => State == "Running" ? "Работает" : State == "Stopped" ? "Остановлена" : State;
    public string StartLabel => StartMode == "Auto" ? Delayed ? "Авто (отл.)" : "Авто" : StartMode == "Manual" ? "Вручную" : StartMode == "Disabled" ? "Отключена" : StartMode;
    public string Need => ServiceGuide.Level(this);
}
public record InstalledApp(string Id, string Name, string? Version, string? Publisher, string Type, double SizeMB, bool Removable, bool Quiet, string Scope);
public record CleanInfo(string Id, string Name, string Path, int Count, long? Bytes);
public record Reply(bool ok, JsonElement data, string message, string? log);

public static class Engine
{
    public static bool Portable => File.Exists(Path.Combine(Root, "portable.flag"));
    public static string Store
    {
        get
        {
            if (Portable) return Path.Combine(Root, "Data");
            var parent = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var legacy = Path.Combine(parent, "Monochrome");
            // Keep existing rollback history at its original location after rebranding.
            return Directory.Exists(legacy) ? legacy : Path.Combine(parent, "Qivoryx");
        }
    }
    public static string Root => AppContext.BaseDirectory;
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    static readonly SemaphoreSlim Gate = new(1, 1);
    public static string LogPath => Path.Combine(Store, "activity.log");
    public static async Task<Reply> Run(object request, bool elevate = false)
    {
        await Gate.WaitAsync();
        Directory.CreateDirectory(Store);
        var output = Path.Combine(Store, "response-" + Guid.NewGuid().ToString("N") + ".json");
        var json = JsonSerializer.Serialize(request);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        try
        {
            var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            var si = new ProcessStartInfo(shell)
            {
                Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{Path.Combine(Root, "Backend.ps1")}\" -Request {payload} -Output \"{output}\"",
                UseShellExecute = elevate, CreateNoWindow = !elevate,
                RedirectStandardOutput = !elevate, RedirectStandardError = !elevate,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Root
            };
            if (elevate) si.Verb = "runas";
            using var process = Process.Start(si) ?? throw new Exception("Не удалось запустить системный обработчик.");
            var stdout = elevate ? Task.FromResult("") : process.StandardOutput.ReadToEndAsync();
            var stderr = elevate ? Task.FromResult("") : process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (!File.Exists(output)) { await File.AppendAllTextAsync(LogPath, $"\nОбработчик: {json}\n{await stdout}\n{await stderr}\n");throw new Exception($"Обработчик не вернул результат (код {process.ExitCode}). Подробности: {LogPath}"); }
            var result = JsonSerializer.Deserialize<Reply>(await File.ReadAllTextAsync(output), Json) ?? throw new Exception("Пустой ответ обработчика.");
            await File.AppendAllTextAsync(LogPath, $"\n[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {json}\n{(result.ok ? "OK" : "ERROR")} {result.message}\n{result.log}\n", Encoding.UTF8);
            if (!result.ok) throw new Exception(result.message);
            return result;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new Exception("Запрос прав администратора отменён."); }
        finally { if (File.Exists(output)) File.Delete(output); Gate.Release(); }
    }
    public static T Read<T>(JsonElement element) => element.Deserialize<T>(Json)!;
    public static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    public static string Size(long bytes) => bytes >= 1073741824 ? $"{bytes / 1073741824.0:0.00} ГБ" : $"{bytes / 1048576.0:0.0} МБ";
}

public static class ServiceGuide
{
    public static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase) { "RpcSs", "RpcEptMapper", "DcomLaunch", "PlugPlay", "Power", "ProfSvc", "SamSs", "LSM", "EventLog", "BFE", "MpsSvc", "WinDefend", "WdNisSvc", "SecurityHealthService", "wscsvc", "gpsvc", "Schedule", "nsi", "Dhcp", "Dnscache", "Winmgmt", "CryptSvc", "UserManager", "StateRepository", "Appinfo", "BrokerInfrastructure", "CoreMessagingRegistrar", "SystemEventsBroker", "SENS", "KeyIso" };
    static readonly Dictionary<string, string> Guides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SysMain"] = "Предзагрузка часто используемых данных. Обычно помогает запуску приложений; отключение может уменьшить фоновую работу диска, но замедлить загрузки. Сравните поведение на своём ПК.",
        ["WSearch"] = "Индекс поиска файлов и почты. Можно перевести в ручной режим, если поиск не нужен. Поиск в Пуске и Проводнике может стать медленнее или неполным.",
        ["Spooler"] = "Очередь печати, включая Microsoft Print to PDF. Отключайте только если совсем не используете печать.",
        ["DiagTrack"] = "Диагностические данные Windows. Не влияет напрямую на FPS. Отключение ограничивает диагностику и сбор сведений о сбоях.",
        ["W32Time"] = "Синхронизация времени. Неверное время вызывает проблемы с HTTPS, входом и сетевыми играми. Рекомендуется оставить включённой.",
        ["wuauserv"] = "Центр обновления Windows. Отключение мешает исправлениям безопасности, драйверам и Microsoft Store. Постоянно отключать не рекомендуется.",
        ["BITS"] = "Фоновая передача данных. Нужна обновлениям и части установщиков. Рекомендуется сохранить стандартный запуск.",
        ["bthserv"] = "Bluetooth: наушники, мыши, геймпады. Можно отключить при полном отсутствии Bluetooth-устройств.",
        ["XboxGipSvc"] = "Поддержка аксессуаров Xbox. Отключение может нарушить работу контроллеров и периферии.",
        ["XblAuthManager"] = "Авторизация Xbox Live. Нужна некоторым играм и Game Pass. При отключении возможны ошибки входа.",
        ["XblGameSave"] = "Синхронизация сохранений Xbox Live. Отключение может нарушить облачные сохранения.",
        ["XboxNetApiSvc"] = "Сетевые функции Xbox Live. Может требоваться мультиплееру игр Microsoft.",
        ["RemoteRegistry"] = "Удалённое управление реестром. На домашнем ПК обычно не требуется; может использоваться корпоративным администрированием.",
        ["WerSvc"] = "Отчёты об ошибках и дампы. Отключение уменьшает диагностическую информацию; стабильный прирост FPS не ожидается.",
        ["lfsvc"] = "Геолокация. Нужна картам, погоде и автоматическому определению местоположения.",
        ["MapsBroker"] = "Управление офлайн-картами. Обычно не нужна, если карты не загружаются.",
        ["WbioSrvc"] = "Биометрия и Windows Hello. Отключение лишает входа по лицу или отпечатку.",
        ["WlanSvc"] = "Настройка Wi-Fi. Отключение разрывает беспроводную сеть.",
        ["Audiosrv"] = "Звук Windows. Отключение прекращает воспроизведение и запись звука.",
        ["AudioEndpointBuilder"] = "Поиск и настройка аудиоустройств. Не отключайте, если нужен звук.",
        ["SCardSvr"] = "Смарт-карты, электронные подписи и аппаратные средства аутентификации. Отключайте только если они не используются.",
        ["TrkWks"] = "Слежение за перемещением связанных файлов NTFS. Может быть нужно ярлыкам и документам со связями.",
        ["WMPNetworkSvc"] = "Потоковая передача медиатеки по сети. Не требуется без общего доступа DLNA.",
        ["SSDPSRV"] = "Обнаружение UPnP-устройств. Используется сетевыми медиаплеерами и частью оборудования.",
        ["DoSvc"] = "Оптимизация доставки обновлений. Изменять предпочтительно через Параметры Windows, ограничив скорость и обмен с другими ПК."
    };
    public static string Level(ServiceInfo s) => Protected.Contains(s.Name) ? "Системная" : Guides.ContainsKey(s.Name) ? "Есть рекомендация" : "Зависит от ПО";
    public static string Explain(ServiceInfo s) => Protected.Contains(s.Name) ? "Базовая служба Windows. Изменение заблокировано, чтобы сохранить вход в систему, сеть и защиту. " + s.Description : Guides.TryGetValue(s.Name, out var g) ? g : "Индивидуальная рекомендация пока отсутствует. Назначение ниже получено из самой Windows. Перед изменением проверьте зависимости и требования установленного ПО. Низкая нагрузка сама по себе не означает, что служба лишняя.";
}
