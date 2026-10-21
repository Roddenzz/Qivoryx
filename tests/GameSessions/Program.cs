using System.Diagnostics;
using System.Text.Json;
using Qivoryx;

var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception("FAILED: " + name);checks.Add(name); }
async Task Reject(Func<Task> action, string name)
{
    bool rejected = false;try { await action(); }catch (Exception) { rejected = true; }Check(rejected, name);
}
GameSessionManager Manager(FakePlatform p, MemoryStore s, FakeLeases? leases = null) => new(p, s, leases ?? new FakeLeases());

{
    var p = new FakePlatform();var s = new MemoryStore();await using var m = Manager(p, s);
    await m.InitializeAsync();Check(p.Writes.Count == 0, "Empty initialization is read-only");
    await Reject(() => m.StartExistingAsync(p.Process, Guid.NewGuid(), true), "Unknown plan is rejected before mutation");
    p.Process = p.Process with { OwnerSid = "other-user" };
    await Reject(() => m.StartExistingAsync(p.Process, FakePlatform.B, true), "Foreign-user process cannot start a session");
    p.Process = p.Process with { OwnerSid = p.UserSid, Eligible = false };
    await Reject(() => m.StartExistingAsync(p.Process, FakePlatform.B, true), "Protected process cannot start a session");
    p.Process = p.Process with { Eligible = true };
    await Reject(() => m.StartExistingAsync(p.Process with { StartedUtcTicks = p.Process.StartedUtcTicks - 1 }, FakePlatform.B, true), "Reused or stale PID is rejected before startup");
    p.Priority = ProcessPriorityClass.High;
    await Reject(() => m.StartExistingAsync(p.Process, FakePlatform.B, true), "An existing High priority is never overwritten or persisted for restoration");
    Check(p.Writes.Count == 0 && s.Value == null, "All rejected starts leave power, priority and recovery untouched");
}
{
    var p = new FakePlatform();var s = new MemoryStore();p.BeforeWrite = () => { if (s.Value == null) throw new Exception("Missing recovery intent"); };
    await using var m = Manager(p, s);await m.StartExistingAsync(p.Process, FakePlatform.B, true);
    Check(p.ActivePlan == FakePlatform.B && p.Priority == ProcessPriorityClass.AboveNormal, "Session applies the selected plan and optional AboveNormal priority");
    Check(s.Value!.OriginalPlan == FakePlatform.A && s.Value.OriginalPriority == ProcessPriorityClass.Normal, "Recovery stores exact original plan and priority before the first OS write");
    await Reject(() => m.StartExistingAsync(p.Process, FakePlatform.C, false), "A second session cannot overwrite the active recovery");
    Check(await m.StopAsync(), "Explicit stop completes recovery");
    Check(p.ActivePlan == FakePlatform.A && p.Priority == ProcessPriorityClass.Normal && s.Value == null, "Stop restores both exact originals and clears completed recovery");
    Check(p.Launches == 0 && p.Exists, "Stopping an attached session does not launch or terminate the game");
    Check(p.Writes.All(x => !x.Contains("High") && !x.Contains("RealTime")), "No test path requests High or Realtime priority");
}
{
    var p = new FakePlatform();var s = new MemoryStore();await using var m = Manager(p, s);
    await m.StartExistingAsync(p.Process, FakePlatform.B, true);
    p.ActivePlan = FakePlatform.C;p.Priority = ProcessPriorityClass.BelowNormal;int writes = p.Writes.Count;
    await m.StopAsync();
    Check(p.ActivePlan == FakePlatform.C && p.Priority == ProcessPriorityClass.BelowNormal && p.Writes.Count == writes, "External plan and priority changes survive session rollback");
}
{
    var p = new FakePlatform();var s = new MemoryStore();await using var m = Manager(p, s);
    await m.StartExistingAsync(p.Process, FakePlatform.B, true);
    p.Process = p.Process with { StartedUtcTicks = p.Process.StartedUtcTicks + TimeSpan.TicksPerSecond };p.Priority = ProcessPriorityClass.Normal;
    int priorityWrites = p.Writes.Count(x => x.StartsWith("priority"));await m.PollAsync();
    Check(m.Current == null && p.ActivePlan == FakePlatform.A, "PID reuse ends the original session and restores its power plan");
    Check(p.Writes.Count(x => x.StartsWith("priority")) == priorityWrites, "PID reuse never changes the replacement process priority");
}
{
    var p = new FakePlatform();var s = new MemoryStore();await using var m = Manager(p, s);
    await m.StartExistingAsync(p.Process, FakePlatform.B, true);p.Exists = false;
    await m.PollAsync();Check(m.Current == null && p.ActivePlan == FakePlatform.A, "Observed process exit restores the plan without touching a vanished process");
}
{
    var p = new FakePlatform();var s = new MemoryStore();await using var m = Manager(p, s);
    await m.StartExistingAsync(p.Process, FakePlatform.B, false);p.QueryFails = true;
    await m.PollAsync();Check(m.IsRunning && p.ActivePlan == FakePlatform.B, "A query-access failure is not mistaken for game exit");
    p.QueryFails = false;await m.StopAsync();
}
{
    var p = new FakePlatform { RejectPlan = FakePlatform.B };var s = new MemoryStore();await using var m = Manager(p, s);
    await Reject(() => m.StartExistingAsync(p.Process, FakePlatform.B, true), "Power policy rejection reports a failed start");
    Check(p.ActivePlan == FakePlatform.A && p.Priority == ProcessPriorityClass.Normal && s.Value == null, "Failed plan application leaves no partial session");
}
{
    var p = new FakePlatform { RejectAboveNormal = true };var s = new MemoryStore();await using var m = Manager(p, s);
    await Reject(() => m.StartExistingAsync(p.Process, FakePlatform.B, true), "Priority denial reports a failed start");
    Check(p.ActivePlan == FakePlatform.A && p.Priority == ProcessPriorityClass.Normal && s.Value == null, "Priority denial rolls back an already applied plan");
}
{
    var p = new FakePlatform();var s = new MemoryStore();await using var m = Manager(p, s);
    await m.StartExistingAsync(p.Process, FakePlatform.B, true);p.Plans.RemoveAll(x => x.Id == FakePlatform.A);
    Check(!await m.StopAsync() && m.HasPendingRecovery && s.Value!.PlanPending, "Deleted original plan retains a retryable recovery record");
    Check(p.Priority == ProcessPriorityClass.Normal && !s.Value!.PriorityPending, "An independent successful priority rollback is preserved while plan recovery waits");
    p.Plans.Add(new(FakePlatform.A, "Original"));Check(await m.StopAsync(), "Recovery retry succeeds when the original plan becomes available");
}
{
    var p = new FakePlatform { ActivePlan = FakePlatform.B, Priority = ProcessPriorityClass.AboveNormal };var s = new MemoryStore { Value = FakePlatform.Recovery() };
    p.Process = s.Value.Process;
    await using var m = Manager(p, s);await m.InitializeAsync();
    Check(p.ActivePlan == FakePlatform.A && p.Priority == ProcessPriorityClass.Normal && s.Value == null, "Startup recovery cancels unfinished temporary settings");
    Check(p.Launches == 0, "Recovery never starts an executable from its record");
}
{
    var p = new FakePlatform { ActivePlan = FakePlatform.C, Priority = ProcessPriorityClass.BelowNormal };var s = new MemoryStore { Value = FakePlatform.Recovery() };
    p.Process = s.Value.Process;
    await using var m = Manager(p, s);await m.InitializeAsync();
    Check(p.Writes.Count == 0 && p.ActivePlan == FakePlatform.C && p.Priority == ProcessPriorityClass.BelowNormal, "Crash recovery also preserves external changes");
}
{
    var p = new FakePlatform { ActivePlan = FakePlatform.B };var s = new MemoryStore { Value = FakePlatform.Recovery() };
    p.Process = s.Value.Process;
    await using var m = Manager(p, s);await m.InitializeAsync(false);
    Check(p.Writes.Count == 0 && m.HasPendingRecovery, "Read-only initialization previews recovery without applying it");
    await m.StopAsync();
}
{
    var p = new FakePlatform();var s = new MemoryStore { Value = FakePlatform.Recovery() with { MachineId = "another-machine" } };await using var m = Manager(p, s);
    await Reject(() => m.InitializeAsync(), "A foreign-machine recovery record is rejected");
    await Reject(() => m.StartExistingAsync(p.Process, FakePlatform.B, true), "An invalid recovery cannot be overwritten by a new session");
    Check(p.Writes.Count == 0 && s.Value != null, "Rejected recovery performs no OS writes and remains available for inspection");
}
{
    var p = new FakePlatform();var s = new MemoryStore { Value = FakePlatform.Recovery() with { OriginalPriority = ProcessPriorityClass.RealTime } };await using var m = Manager(p, s);
    await Reject(() => m.InitializeAsync(), "A forged Realtime rollback request is rejected");Check(p.Writes.Count == 0, "A malformed priority recovery cannot reach the platform setter");
}
{
    var p = new FakePlatform();var leases = new FakeLeases();var s1 = new MemoryStore();var s2 = new MemoryStore();
    await using var first = Manager(p, s1, leases);await using var second = Manager(p, s2, leases);
    await first.StartExistingAsync(p.Process, FakePlatform.B, false);
    await using var recoveryViewer = Manager(p, s1, leases);
    await Reject(() => recoveryViewer.InitializeAsync(), "Another instance cannot recover a session while its owner is still active");
    Check(recoveryViewer.Current == null && await recoveryViewer.StopAsync(), "An instance that failed to acquire recovery ownership can still close safely");
    await Reject(() => second.StartExistingAsync(p.Process, FakePlatform.C, false), "Cross-instance lease prevents overlapping sessions");
    await first.StopAsync();await second.StartExistingAsync(p.Process, FakePlatform.C, false);await second.StopAsync();
    Check(!leases.Held, "Completed session releases its cross-instance lease");
}
{
    var p = new FakePlatform();var s = new MemoryStore();await using var m = Manager(p, s);
    await m.LaunchAsync(@"C:\Games\Example.exe", FakePlatform.B, false);
    Check(p.Launches == 1 && p.LastLaunch == @"C:\Games\Example.exe", "Launch mode receives the selected EXE as a path without shell arguments");
    await m.StopAsync();
}

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string fixture = Path.Combine(root, "artifacts", "session-test-" + Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
{
    var s = new GameSessionRecoveryStore(fixture);var value = FakePlatform.Recovery();s.Save(value);
    var loaded = s.Load();Check(loaded!.OriginalPlan == value.OriginalPlan && loaded.Process.StartedUtcTicks == value.Process.StartedUtcTicks, "DPAPI JSON roundtrip preserves recovery identity and settings");
    string path = Path.Combine(fixture, "game-session.json");string json = File.ReadAllText(path);
    Check(!json.Contains("Example.exe") && !json.Contains("OriginalPriority"), "Recovery payload is authenticated and encrypted instead of executable plaintext instructions");
    var envelope = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    byte[] protectedBytes = Convert.FromBase64String(envelope["ProtectedPayload"].GetString()!);protectedBytes[^1] ^= 0x5a;
    File.WriteAllText(path, JsonSerializer.Serialize(new { Version = 1, ProtectedPayload = Convert.ToBase64String(protectedBytes) }));
    await Reject(() => Task.Run(() => s.Load()), "Changing authenticated recovery bytes causes rejection before execution");
    s.Clear();Check(s.Load() == null, "Completed recovery cleanup removes only its own JSON record");
}
{
    // Native API validation is read-only: never launch or set power/priority.
    var native = new WindowsGameSessionPlatform();var own = native.InspectProcess(Environment.ProcessId);
    Check(own != null && own.OwnerSid == native.UserSid && !own.Eligible, "Native process inspection verifies SID and excludes the utility itself");
    Check(native.GetPowerPlans().Any(p => p.Id == native.GetActivePlan()), "Native power APIs enumerate the active existing plan without changing it");
}
Directory.CreateDirectory(Path.Combine(root, "artifacts"));
File.WriteAllText(Path.Combine(root, "artifacts", "gamesession-tests.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, TestNames = checks, NativeMutations = 0, RealGameLaunches = 0 }, new JsonSerializerOptions { WriteIndented = true }));
foreach (string check in checks) Console.WriteLine(check);
Console.WriteLine($"{checks.Count} checks passed. No real game was launched and no native power/priority setter was called.");

sealed class MemoryStore : IGameSessionStore
{
    public GameSessionRecord? Value;
    public GameSessionRecord? Load() => Value == null ? null : JsonSerializer.Deserialize<GameSessionRecord>(JsonSerializer.Serialize(Value));
    public void Save(GameSessionRecord value) => Value = JsonSerializer.Deserialize<GameSessionRecord>(JsonSerializer.Serialize(value));
    public void Clear() => Value = null;
}
sealed class FakeLeases : IGameSessionLeaseFactory
{
    public bool Held;
    public IDisposable Acquire(string userSid) { if (Held) throw new Exception("Lease held");Held = true;return new Lease(this); }
    sealed class Lease(FakeLeases source) : IDisposable { public void Dispose() => source.Held = false; }
}
sealed class FakePlatform : IGameSessionPlatform
{
    public static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid C = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public string UserSid => "fixture-user";
    public string MachineId => "fixture-machine";
    public GameProcessInfo Process = new(4400, DateTime.UtcNow.AddMinutes(-2).Ticks, @"C:\Games\Example.exe", "Example", "fixture-user", true);
    public List<GamePowerPlan> Plans = new() { new(A, "Original"), new(B, "Gaming"), new(C, "External") };
    public Guid ActivePlan = A;
    public ProcessPriorityClass Priority = ProcessPriorityClass.Normal;
    public bool Exists = true, QueryFails, RejectAboveNormal;
    public Guid? RejectPlan;
    public int Launches;
    public string? LastLaunch;
    public List<string> Writes = new();
    public Action? BeforeWrite;
    public IReadOnlyList<GamePowerPlan> GetPowerPlans() => Plans;
    public Guid GetActivePlan() => ActivePlan;
    public void SetActivePlan(Guid plan) { BeforeWrite?.Invoke();if (RejectPlan == plan) throw new Exception("Policy rejected plan");Writes.Add("plan " + plan);ActivePlan = plan; }
    public IReadOnlyList<GameProcessInfo> GetEligibleProcesses() => new[] { Process };
    public GameProcessInfo? InspectProcess(int id) { if (QueryFails) throw new Exception("Access denied");return Exists && id == Process.Id ? Process : null; }
    public void ValidateExecutable(string path) { if (!path.EndsWith(".exe")) throw new Exception("Not an EXE"); }
    public GameProcessInfo LaunchExecutable(string path) { ValidateExecutable(path);Launches++;LastLaunch = path;return Process; }
    public ProcessPriorityClass GetPriority(GameProcessInfo process) => Priority;
    public void SetPriority(GameProcessInfo process, ProcessPriorityClass priority) { BeforeWrite?.Invoke();if (priority == ProcessPriorityClass.AboveNormal && RejectAboveNormal) throw new Exception("Anticheat denied priority");Writes.Add("priority " + priority);Priority = priority; }
    public static GameSessionRecord Recovery() => new()
    {
        UserSid = "fixture-user", MachineId = "fixture-machine", Process = new(4400, DateTime.UtcNow.AddMinutes(-2).Ticks, @"C:\Games\Example.exe", "Example", "fixture-user", true),
        OriginalPlan = A, AppliedPlan = B, AppliedPlanName = "Gaming", OriginalPriority = ProcessPriorityClass.Normal,
        PlanPending = true, PriorityPending = true, Running = true
    };
}
