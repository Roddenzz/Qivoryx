using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Qivoryx;

public record GamePowerPlan(Guid Id, string Name)
{
    public string DisplayName => Name;
}

public record GameProcessInfo(int Id, long StartedUtcTicks, string ExecutablePath, string Name, string OwnerSid, bool Eligible)
{
    public string DisplayName => $"{Name}  ·  PID {Id}";
}

public record GameSessionRecord
{
    public int Version { get; init; } = 1;
    public required string UserSid { get; init; }
    public required string MachineId { get; init; }
    public required GameProcessInfo Process { get; init; }
    public required Guid OriginalPlan { get; init; }
    public required Guid AppliedPlan { get; init; }
    public required string AppliedPlanName { get; init; }
    public ProcessPriorityClass? OriginalPriority { get; init; }
    public ProcessPriorityClass AppliedPriority { get; init; } = ProcessPriorityClass.AboveNormal;
    public bool PlanPending { get; set; }
    public bool PriorityPending { get; set; }
    public bool Running { get; set; }
    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;
}

public interface IGameSessionPlatform
{
    string UserSid { get; }
    string MachineId { get; }
    IReadOnlyList<GamePowerPlan> GetPowerPlans();
    Guid GetActivePlan();
    void SetActivePlan(Guid plan);
    IReadOnlyList<GameProcessInfo> GetEligibleProcesses();
    GameProcessInfo? InspectProcess(int id);
    void ValidateExecutable(string path);
    GameProcessInfo LaunchExecutable(string path);
    ProcessPriorityClass GetPriority(GameProcessInfo process);
    void SetPriority(GameProcessInfo process, ProcessPriorityClass priority);
}

public interface IGameSessionStore
{
    GameSessionRecord? Load();
    void Save(GameSessionRecord record);
    void Clear();
}

public interface IGameSessionLeaseFactory
{
    IDisposable Acquire(string userSid);
}

public sealed class GameSessionManager : IAsyncDisposable
{
    readonly IGameSessionPlatform platform;
    readonly IGameSessionStore store;
    readonly IGameSessionLeaseFactory leases;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly Timer monitor;
    IDisposable? lease;
    GameSessionRecord? record;
    bool disposed;
    bool recoveryBlocked;
    public event Action? Changed;
    public GameSessionRecord? Current => record;
    public bool IsRunning => record?.Running == true;
    public bool HasPendingRecovery => record != null && !record.Running;
    public string LastMessage { get; private set; } = "Сессия ещё не запущена.";
    public IReadOnlyList<GamePowerPlan> PowerPlans => platform.GetPowerPlans();
    public IReadOnlyList<GameProcessInfo> Processes => platform.GetEligibleProcesses();
    public Guid ActivePlan => platform.GetActivePlan();

    public GameSessionManager(IGameSessionPlatform platform, IGameSessionStore store, IGameSessionLeaseFactory? leases = null)
    {
        this.platform = platform;this.store = store;this.leases = leases ?? new NamedGameSessionLeaseFactory();
        monitor = new Timer(_ => _ = PollAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task InitializeAsync(bool recover = true)
    {
        await gate.WaitAsync();
        try
        {
            var saved = store.Load();
            if (saved == null) return;
            ValidateRecovery(saved);
            LastMessage = "Найдена незавершённая сессия. Можно восстановить прежние параметры; игра запускаться не будет.";
            if (recover)
            {
                lease = leases.Acquire(platform.UserSid);
                saved.Running = false;record = saved;
                RollbackCore();
            }
            else { saved.Running = false;record = saved; }
        }
        catch { recoveryBlocked = true;throw; }
        finally { gate.Release();Changed?.Invoke(); }
    }

    public async Task StartExistingAsync(GameProcessInfo selected, Guid plan, bool aboveNormal)
    {
        await gate.WaitAsync();
        try { EnsureStartAllowed(plan);lease = leases.Acquire(platform.UserSid);StartCore(selected, plan, aboveNormal); }
        catch { if (record == null) ReleaseLease();throw; }
        finally { gate.Release();Changed?.Invoke(); }
    }

    public async Task LaunchAsync(string path, Guid plan, bool aboveNormal)
    {
        await gate.WaitAsync();
        try
        {
            EnsureStartAllowed(plan);platform.ValidateExecutable(path);
            lease = leases.Acquire(platform.UserSid);
            var process = platform.LaunchExecutable(path);
            try { StartCore(process, plan, aboveNormal); }
            catch (Exception ex) { throw new InvalidOperationException("EXE был запущен, но сессия не началась. Приложение не закрывалось. " + ex.Message, ex); }
        }
        catch { if (record == null) ReleaseLease();throw; }
        finally { gate.Release();Changed?.Invoke(); }
    }

    void EnsureStartAllowed(Guid plan)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (recoveryBlocked) throw new InvalidOperationException("Сначала проверьте недоступную запись восстановления. Новая сессия не перезапишет её.");
        if (record != null) throw new InvalidOperationException("Сначала завершите текущую сессию или восстановите оставшиеся параметры.");
        if (plan == Guid.Empty || !platform.GetPowerPlans().Any(p => p.Id == plan)) throw new InvalidOperationException("Выбранный план питания больше не существует. Обновите список.");
    }

    void StartCore(GameProcessInfo selected, Guid plan, bool aboveNormal)
    {
        var process = platform.InspectProcess(selected.Id);
        if (!SameProcess(process, selected) || process!.OwnerSid != platform.UserSid || !process.Eligible)
            throw new InvalidOperationException("Процесс завершился, сменился или недоступен. Выберите свою запущенную игру заново.");
        ProcessPriorityClass? originalPriority = null;
        bool priorityPending = false;
        if (aboveNormal)
        {
            var value = platform.GetPriority(process);
            if (!AllowedPriority(value)) throw new InvalidOperationException("Процесс уже имеет высокий или неподдерживаемый приоритет. Отключите изменение приоритета для этой сессии.");
            originalPriority = value;priorityPending = value != ProcessPriorityClass.AboveNormal;
        }
        Guid original = platform.GetActivePlan();
        if (!platform.GetPowerPlans().Any(p => p.Id == original)) throw new InvalidOperationException("Исходный план питания недоступен для последующего восстановления.");
        var prepared = new GameSessionRecord
        {
            UserSid = platform.UserSid, MachineId = platform.MachineId, Process = process, OriginalPlan = original,
            AppliedPlan = plan, AppliedPlanName = platform.GetPowerPlans().Single(p => p.Id == plan).Name,
            OriginalPriority = originalPriority, PlanPending = original != plan, PriorityPending = priorityPending, Running = false
        };
        // Persist the intent before changing either setting. A crash between a
        // successful OS call and the following write is still recoverable.
        store.Save(prepared);record = prepared;
        try
        {
            if (prepared.PlanPending) platform.SetActivePlan(plan);
            if (prepared.PriorityPending) platform.SetPriority(process, ProcessPriorityClass.AboveNormal);
            prepared.Running = true;store.Save(prepared);
            monitor.Change(1000, 1000);
            LastMessage = "Сессия активна. После выхода из выбранного процесса прежние параметры восстановятся.";
        }
        catch (Exception ex)
        {
            prepared.Running = false;
            RollbackCore();
            throw new InvalidOperationException("Не удалось применить настройки сессии: " + ex.Message + " " + LastMessage, ex);
        }
    }

    public async Task PollAsync()
    {
        if (disposed || !await gate.WaitAsync(0)) return;
        bool changed = false;
        try
        {
            if (record?.Running != true) return;
            // Loss of query access is not proof of exit. Leave the session and
            // recovery record intact so the user can explicitly stop it.
            var process = platform.InspectProcess(record.Process.Id);
            if (!SameProcess(process, record.Process)) { record.Running = false;RollbackCore();changed = true; }
        }
        catch (Exception ex) { LastMessage = "Проверка процесса временно недоступна: " + ex.Message;changed = true; }
        finally { gate.Release();if (changed) Changed?.Invoke(); }
    }

    public async Task<bool> StopAsync()
    {
        await gate.WaitAsync();
        try
        {
            monitor.Change(Timeout.Infinite, Timeout.Infinite);
            if (record == null) return true;
            ValidateRecovery(record);
            lease ??= leases.Acquire(platform.UserSid);
            record.Running = false;
            RollbackCore();
            return record == null;
        }
        finally { gate.Release();Changed?.Invoke(); }
    }

    void RollbackCore()
    {
        if (record == null) return;
        var errors = new List<string>();var kept = new List<string>();
        monitor.Change(Timeout.Infinite, Timeout.Infinite);
        if (record.PriorityPending)
        {
            try
            {
                var process = platform.InspectProcess(record.Process.Id);
                if (SameProcess(process, record.Process))
                {
                    var current = platform.GetPriority(record.Process);
                    if (current == record.AppliedPriority) platform.SetPriority(record.Process, record.OriginalPriority!.Value);
                    else kept.Add("приоритет изменён извне и сохранён");
                }
                record.PriorityPending = false;
            }
            catch (Exception ex) { errors.Add("Приоритет: " + ex.Message); }
        }
        if (record.PlanPending)
        {
            try
            {
                if (platform.GetActivePlan() == record.AppliedPlan)
                {
                    if (!platform.GetPowerPlans().Any(p => p.Id == record.OriginalPlan)) throw new InvalidOperationException("Исходный план удалён. Выберите подходящий план в настройках питания Windows.");
                    platform.SetActivePlan(record.OriginalPlan);
                }
                else kept.Add("план изменён извне и сохранён");
                record.PlanPending = false;
            }
            catch (Exception ex) { errors.Add("Питание: " + ex.Message); }
        }
        record.Running = false;
        if (!record.PlanPending && !record.PriorityPending)
        {
            // A failed cleanup must retain the recovery record rather than
            // allowing another session to overwrite it.
            store.Clear();record = null;ReleaseLease();
            LastMessage = "Сессия завершена. Временные настройки отменены." + (kept.Count > 0 ? " " + string.Join("; ", kept) + "." : "");
        }
        else
        {
            store.Save(record);
            LastMessage = "Часть параметров ещё не восстановлена. " + string.Join(" ", errors) + " Повторите восстановление; запись сохранена.";
        }
    }

    void ValidateRecovery(GameSessionRecord value)
    {
        if (value.Version != 1 || value.UserSid != platform.UserSid || value.MachineId != platform.MachineId)
            throw new InvalidOperationException("Запись сессии принадлежит другому компьютеру или профилю.");
        if (value.Process == null || !value.Process.Eligible || value.Process.Id <= 4 || value.Process.OwnerSid != platform.UserSid || value.Process.StartedUtcTicks < DateTime.UnixEpoch.Ticks || value.Process.StartedUtcTicks > DateTime.UtcNow.AddMinutes(2).Ticks ||
            string.IsNullOrWhiteSpace(value.Process.ExecutablePath) || !Path.IsPathFullyQualified(value.Process.ExecutablePath) || !value.Process.ExecutablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || value.OriginalPlan == Guid.Empty || value.AppliedPlan == Guid.Empty)
            throw new InvalidOperationException("Запись сессии содержит некорректные данные. Никакие действия из неё не выполнены.");
        if (value.PriorityPending && (value.AppliedPriority != ProcessPriorityClass.AboveNormal || !value.OriginalPriority.HasValue || !AllowedPriority(value.OriginalPriority.Value)))
            throw new InvalidOperationException("Недопустимый приоритет в записи восстановления.");
    }

    static bool AllowedPriority(ProcessPriorityClass value) => value is ProcessPriorityClass.Idle or ProcessPriorityClass.BelowNormal or ProcessPriorityClass.Normal or ProcessPriorityClass.AboveNormal;
    static bool SameProcess(GameProcessInfo? actual, GameProcessInfo expected) => actual != null && actual.Id == expected.Id && actual.StartedUtcTicks == expected.StartedUtcTicks && actual.OwnerSid == expected.OwnerSid && string.Equals(actual.ExecutablePath, expected.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    void ReleaseLease() { lease?.Dispose();lease = null; }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        try { await StopAsync(); }
        finally { disposed = true;monitor.Dispose();ReleaseLease(); }
    }
}

public sealed class NamedGameSessionLeaseFactory : IGameSessionLeaseFactory
{
    public IDisposable Acquire(string userSid)
    {
        // File sharing locks work across UI/thread-pool continuations, unlike
        // thread-affine mutex ownership. Windows releases the lock on a crash.
        return new FileLease(userSid);
    }
    sealed class FileLease : IDisposable
    {
        readonly FileStream stream;
        public FileLease(string userSid)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Qivoryx", "Sessions");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "active-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(userSid))) + ".lock");
            try { stream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new InvalidOperationException("Игровая сессия уже управляется другим экземпляром приложения."); }
        }
        public void Dispose() => stream.Dispose();
    }
}

public sealed class GameSessionRecoveryStore : IGameSessionStore
{
    readonly string path;
    public GameSessionRecoveryStore(string directory) { path = Path.Combine(directory, "game-session.json"); }
    sealed record Envelope(int Version, string ProtectedPayload);
    public GameSessionRecord? Load()
    {
        AssertPlainPath(path);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 131072) throw new InvalidOperationException("Запись сессии превышает допустимый размер.");
        var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path)) ?? throw new InvalidOperationException("Пустая запись сессии.");
        if (envelope.Version != 1) throw new InvalidOperationException("Версия записи сессии не поддерживается.");
        byte[] plain = GameSessionDpapi.Unprotect(Convert.FromBase64String(envelope.ProtectedPayload));
        return JsonSerializer.Deserialize<GameSessionRecord>(plain) ?? throw new InvalidOperationException("Запись восстановления повреждена.");
    }
    public void Save(GameSessionRecord record)
    {
        AssertPlainPath(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, Convert.ToBase64String(GameSessionDpapi.Protect(JsonSerializer.SerializeToUtf8Bytes(record)))));
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(bytes);output.Flush(true); }
            AssertPlainPath(path);File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Clear() { AssertPlainPath(path);if (File.Exists(path)) File.Delete(path); }
    static void AssertPlainPath(string file)
    {
        string? current = Path.GetFullPath(file);
        while (current != null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Путь данных сессии содержит ссылку. Запись остановлена.");
            current = Path.GetDirectoryName(current);
        }
    }
}

static class GameSessionDpapi
{
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Length;public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool CryptProtectData(ref Blob input, string description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] static extern bool CryptUnprotectData(ref Blob input, IntPtr description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr value);
    public static byte[] Protect(byte[] input) => Transform(input, true);
    public static byte[] Unprotect(byte[] input) => Transform(input, false);
    static byte[] Transform(byte[] input, bool protect)
    {
        var data = new Blob { Length = input.Length, Data = Marshal.AllocHGlobal(input.Length) };
        byte[] extra = Encoding.UTF8.GetBytes("Qivoryx.GameSession.v1");
        var entropy = new Blob { Length = extra.Length, Data = Marshal.AllocHGlobal(extra.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(input, 0, data.Data, input.Length);Marshal.Copy(extra, 0, entropy.Data, extra.Length);
            bool ok = protect ? CryptProtectData(ref data, "Qivoryx session recovery", ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptUnprotectData(ref data, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "Запись сессии повреждена или принадлежит другому профилю. Из неё ничего не выполнялось.");
            var result = new byte[output.Length];Marshal.Copy(output.Data, result, 0, result.Length);return result;
        }
        finally { Marshal.FreeHGlobal(data.Data);Marshal.FreeHGlobal(entropy.Data);if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
    }
}

public sealed class WindowsGameSessionPlatform : IGameSessionPlatform
{
    public string UserSid { get; } = WindowsIdentity.GetCurrent().User!.Value;
    public string MachineId { get; } = Environment.MachineName + ":" + Convert.ToString(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", ""));
    static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase) { "System", "Registry", "Memory Compression", "smss", "csrss", "wininit", "services", "lsass", "svchost", "winlogon", "dwm", "conhost", "sihost", "taskhostw", "ctfmon", "explorer", "RuntimeBroker", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchIndexer", "SecurityHealthSystray", "MsMpEng", "Qivoryx", "Monochrome", "cmd", "powershell", "pwsh", "msiexec", "wscript", "cscript", "rundll32", "regsvr32", "dllhost", "avp", "avast", "avguard", "ekrn", "mbam", "MsSense" };
    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid guid);
    [DllImport("powrprof.dll")] static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subgroup, uint access, uint index, byte[] buffer, ref uint size);
    [DllImport("powrprof.dll")] static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, byte[]? buffer, ref uint size);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)] static extern SafeProcessHandle OpenProcess(uint rights, bool inherit, int id);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint GetPriorityClass(SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetPriorityClass(SafeProcessHandle process, uint priority);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);

    public IReadOnlyList<GamePowerPlan> GetPowerPlans()
    {
        var result = new List<GamePowerPlan>();
        for (uint index = 0; index < 256; index++)
        {
            uint size = 16;byte[] buffer = new byte[size];uint code = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, index, buffer, ref size);
            if (code == 259) break;
            if (code != 0) throw new Win32Exception((int)code, "Не удалось прочитать планы питания.");
            Guid guid = new(buffer);uint count = 0;PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, null, ref count);
            string name = guid.ToString();
            if (count > 0 && count < 65536)
            {
                byte[] text = new byte[count];if (PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, text, ref count) == 0) name = Encoding.Unicode.GetString(text).TrimEnd('\0');
            }
            result.Add(new(guid, name));
        }
        return result;
    }
    public Guid GetActivePlan()
    {
        uint code = PowerGetActiveScheme(IntPtr.Zero, out var pointer);
        if (code != 0) throw new Win32Exception((int)code);
        try { return Marshal.PtrToStructure<Guid>(pointer); }finally { LocalFree(pointer); }
    }
    public void SetActivePlan(Guid plan)
    {
        if (!GetPowerPlans().Any(p => p.Id == plan)) throw new InvalidOperationException("План питания больше не существует.");
        uint code = PowerSetActiveScheme(IntPtr.Zero, ref plan);
        if (code != 0) throw new Win32Exception((int)code, "Windows не разрешила переключить план питания.");
        if (GetActivePlan() != plan) throw new InvalidOperationException("Windows не подтвердила применение плана питания.");
    }
    public IReadOnlyList<GameProcessInfo> GetEligibleProcesses()
    {
        var result = new List<GameProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process) { try { var item = InspectProcess(process.Id);if (item?.Eligible == true) result.Add(item); }catch (Win32Exception) { }catch (InvalidOperationException) { } }
        }
        return result.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    public GameProcessInfo? InspectProcess(int id)
    {
        using var handle = OpenProcess(0x101000, false, id);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();if (error is 87 or 1168) return null;
            throw new Win32Exception(error, "Процесс недоступен для чтения.");
        }
        return InspectHandle(handle, id);
    }
    GameProcessInfo? InspectHandle(SafeProcessHandle handle, int id)
    {
        uint state = WaitForSingleObject(handle, 0);
        if (state == 0) return null;
        if (state != 258) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!GetProcessTimes(handle, out long created, out _, out _, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var path = new StringBuilder(32768);int size = path.Capacity;
        if (!QueryFullProcessImageName(handle, 0, path, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!OpenProcessToken(handle, 8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        string owner;using (token) using (var identity = new WindowsIdentity(token.DangerousGetHandle())) owner = identity.User?.Value ?? "";
        string executable = Path.GetFullPath(path.ToString());string name = Path.GetFileNameWithoutExtension(executable);
        bool eligible = id > 4 && id != Environment.ProcessId && owner == UserSid && !ProtectedNames.Contains(name) && !IsWindowsPath(executable);
        return new(id, DateTime.FromFileTimeUtc(created).Ticks, executable, name, owner, eligible);
    }
    static bool IsWindowsPath(string path) => path.StartsWith(Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public void ValidateExecutable(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || IsWindowsPath(path) || ProtectedNames.Contains(Path.GetFileNameWithoutExtension(path)))
            throw new InvalidOperationException("Выберите существующий EXE игры или обычного приложения. Системные процессы и командные оболочки исключены.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Выберите сам EXE, а не ссылку на исполняемый файл.");
    }
    public GameProcessInfo LaunchExecutable(string path)
    {
        ValidateExecutable(path);path = Path.GetFullPath(path);
        var info = new ProcessStartInfo { FileName = path, UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! };
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Windows не вернула запущенный процесс.");
        var result = InspectProcess(process.Id);
        if (result == null || !result.Eligible || !string.Equals(result.ExecutablePath, path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("EXE завершился или передал запуск другому приложению. Выберите фактический процесс игры после её запуска.");
        return result;
    }
    SafeProcessHandle OpenVerified(GameProcessInfo process, bool change)
    {
        var handle = OpenProcess(0x101000u | (change ? 0x0200u : 0), false, process.Id);
        try
        {
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var actual = InspectHandle(handle, process.Id);
            if (actual == null || !actual.Eligible || actual.OwnerSid != UserSid || actual.StartedUtcTicks != process.StartedUtcTicks || !string.Equals(actual.ExecutablePath, process.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Идентификатор процесса больше не соответствует выбранной игре.");
            return handle;
        }
        catch { handle.Dispose();throw; }
    }
    public ProcessPriorityClass GetPriority(GameProcessInfo process)
    {
        using var handle = OpenVerified(process, false);uint value = GetPriorityClass(handle);
        if (value == 0) throw new Win32Exception(Marshal.GetLastWin32Error());return (ProcessPriorityClass)value;
    }
    public void SetPriority(GameProcessInfo process, ProcessPriorityClass priority)
    {
        if (priority is not (ProcessPriorityClass.Idle or ProcessPriorityClass.BelowNormal or ProcessPriorityClass.Normal or ProcessPriorityClass.AboveNormal)) throw new InvalidOperationException("Высокий и реальный приоритет не используются.");
        using var handle = OpenVerified(process, true);
        if (!SetPriorityClass(handle, (uint)priority)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows или античит не разрешили изменить приоритет.");
        if (GetPriorityClass(handle) != (uint)priority) throw new InvalidOperationException("Изменение приоритета не подтверждено Windows.");
    }
}
