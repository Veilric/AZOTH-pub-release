using System.Text.Json;
using AZOTH.Core.Files;

namespace AZOTH.Core.Ordo;

/// <summary>Process-wide source ownership. Release only after the owning worker has stopped.</summary>
public sealed class OrdoSourceClaims
{
    private readonly HashSet<string> _roots = [];
    private readonly Dictionary<string, string[]> _destinations = [];
    private readonly HashSet<string> _organizers = [];
    public IDisposable Acquire(string root, IEnumerable<string>? destinations = null, bool organizer = false)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var outputs = (destinations ?? []).Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).ToArray();
        lock (_roots)
        {
            if (_roots.Any(r => OrdoPaths.Within(root, r) || OrdoPaths.Within(r, root)))
                throw new InvalidOperationException("Another ORDO or conversion watcher owns an overlapping source.");
            foreach (var other in _roots.Where(r => organizer || _organizers.Contains(r)))
            {
                if (_destinations[other].Any(d => OrdoPaths.Within(root, d) || OrdoPaths.Within(d, root)) ||
                    outputs.Any(d => OrdoPaths.Within(other, d) || OrdoPaths.Within(d, other)))
                    throw new InvalidOperationException("An automatic destination overlaps another watched source. Choose separate folders to prevent a loop.");
            }
            _roots.Add(root);
            _destinations[root] = outputs;
            if (organizer) _organizers.Add(root);
            return new Claim(() =>
            {
                lock (_roots) { _roots.Remove(root); _destinations.Remove(root); _organizers.Remove(root); }
            });
        }
    }
    private sealed class Claim(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

internal sealed record OrdoMonitorState(OrdoConfigurationData Configuration, string Signature,
    FileIdentity RootIdentity, bool Enabled, string Status, string[] Baseline, FileSnapshot[] Pending, int Version = 1);

/// <summary>Durable enrollment and polling catch-up. Stable metadata is a heuristic, not proof of writer completion.</summary>
public sealed class OrdoMonitor : IAsyncDisposable
{
    private readonly IFileMetadata _metadata;
    private readonly OrdoPlanner _planner;
    private readonly OrdoService _service;
    private readonly OrdoJournal _journal;
    private readonly string _path;
    private readonly OrdoSourceClaims _claims;
    private readonly TimeSpan _settle;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _serial = new(1);
    private readonly object _sync = new();
    private readonly Dictionary<string, (FileSnapshot Snapshot, DateTimeOffset Since)> _observed;
    private readonly Task _loaded;
    private OrdoMonitorState? _state;
    private OrdoMonitorState? _lastSaved;
    private CancellationTokenSource _approval = new();
    private IDisposable? _claim;
    private string? _loadError;
    private string? _requestedPause;
    private bool _futureVersion;
    private bool _disposed;
    private bool _organizing;
    private static StringComparer Paths => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public event EventHandler? Changed;
    public string Status { get { lock (_sync) return _loadError ?? _state?.Status ?? "Monitoring is off. Review rules before enrolling."; } }
    public bool Enabled { get { lock (_sync) return _state?.Enabled == true; } }
    public bool IsOrganizing { get { lock (_sync) return _organizing; } }
    public int PendingCount { get { lock (_sync) return _state?.Pending.Length ?? 0; } }

    public OrdoMonitor(IFileMetadata metadata, OrdoPlanner planner, OrdoService service, OrdoJournal journal,
        string statePath, OrdoSourceClaims claims, TimeSpan? settle = null, TimeProvider? time = null)
    {
        (_metadata, _planner, _service, _journal, _path, _claims) = (metadata, planner, service, journal, statePath, claims);
        _settle = settle ?? TimeSpan.FromSeconds(5);
        _time = time ?? TimeProvider.System;
        _observed = new(Paths);
        _loaded = Task.Run(Load);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var state = JsonSerializer.Deserialize(File.ReadAllText(_path), OrdoJsonContext.Default.OrdoMonitorState)
                ?? throw new InvalidDataException("Empty monitoring state.");
            if (state.Version != 1)
            {
                lock (_sync) { _futureVersion = true; _loadError = "Paused: monitoring state is from an unsupported version; it has been preserved."; }
                return;
            }
            if (state.Configuration is null || state.RootIdentity is null || state.Baseline is null || state.Pending is null)
                throw new InvalidDataException("Incomplete monitoring state.");
            var config = new OrdoConfiguration(state.Configuration.SourceRoot, state.Configuration.Rules, state.Configuration.Recursive);
            OrdoPlanner.Validate(config);
            if (OrdoPlanner.Signature(config) != state.Signature) throw new InvalidDataException("Monitoring rules need review.");
            lock (_sync)
            {
                _state = _lastSaved = state;
                if (_requestedPause is not null) { _state = state with { Enabled = false, Status = _requestedPause }; Save(); }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        { lock (_sync) _loadError = "Paused: monitoring state could not be read. Review and enroll again. " + ex.Message; }
    }

    // Caller holds _sync and runs outside the UI thread. Unchanged scans do not rewrite the baseline every five seconds.
    private void Save()
    {
        if (_state is null || _futureVersion) return;
        if (_lastSaved is { } saved && saved.Configuration == _state.Configuration && saved.Signature == _state.Signature &&
            saved.RootIdentity == _state.RootIdentity && saved.Enabled == _state.Enabled && saved.Status == _state.Status &&
            saved.Baseline.SequenceEqual(_state.Baseline) && saved.Pending.SequenceEqual(_state.Pending)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, _state, OrdoJsonContext.Default.OrdoMonitorState); stream.Flush(true); }
            File.Move(temp, _path, true);
            _lastSaved = _state;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    /// <summary>Revocation is immediate; persistence completes asynchronously before a caller may enroll/apply again.</summary>
    public Task PauseAsync(string reason)
    {
        lock (_sync)
        {
            _approval.Cancel();
            _requestedPause = reason;
            if (_state is not null) _state = _state with { Enabled = false, Status = reason };
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.Run(async () => { await _loaded.ConfigureAwait(false); lock (_sync) Save(); });
    }
    public void Pause(string reason) => PauseAsync(reason).GetAwaiter().GetResult();

    public async Task PauseAndWaitAsync(string reason)
    {
        await PauseAsync(reason).ConfigureAwait(false);
        await _serial.WaitAsync().ConfigureAwait(false);
        try { _claim?.Dispose(); _claim = null; }
        finally { _serial.Release(); }
    }

    public Task EnrollAsync(OrdoConfiguration configuration) => Task.Run(async () =>
    {
        await _loaded.ConfigureAwait(false);
        if (_futureVersion) throw new InvalidOperationException("Monitoring state from a newer version cannot be overwritten.");
        await PauseAndWaitAsync("Reviewing enrollment.").ConfigureAwait(false);
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _planner.PreviewAsync(configuration, eligibleSourcePaths: []).ConfigureAwait(false);
            var root = OrdoPaths.DirectorySnapshot(_metadata, configuration.SourceRoot);
            var claim = _claims.Acquire(configuration.SourceRoot, OrdoPlanner.Validate(configuration), organizer: true);
            try
            {
                var scan = Enumerate(configuration);
                lock (_sync)
                {
                    if (_loadError is not null && File.Exists(_path))
                        File.Move(_path, _path + ".broken-" + Guid.NewGuid().ToString("N"));
                    _loadError = null; _requestedPause = null;
                    _approval.Dispose(); _approval = new(); _observed.Clear();
                    // Inaccessible/linked paths present at enrollment remain protected if they later become readable.
                    var baseline = scan.Files.Select(f => f.Path).Concat(scan.Issues.Select(i => i.Path)).Distinct(Paths).OrderBy(p => p, Paths).ToArray();
                    _state = new(new(configuration.SourceRoot, configuration.Recursive, configuration.Rules), OrdoPlanner.Signature(configuration),
                        root.Identity!, true, "Monitoring reviewed rules; existing files stay.", baseline, []);
                    Save(); _claim = claim;
                }
            }
            catch { claim.Dispose(); throw; }
        }
        finally { _serial.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    });

    private FileEnumerationResult Enumerate(OrdoConfiguration config)
    {
        var scan = new FileTreeEnumerator(_metadata).Enumerate([config.SourceRoot], config.Recursive,
            excludedRoots: OrdoPlanner.Validate(config).Concat(_planner.ProtectedRoots));
        if (scan.Issues.Any(i => Paths.Equals(Path.GetFullPath(i.Path), config.SourceRoot)))
            throw new IOException("Source folder could not be scanned.");
        return scan;
    }

    private static bool BaselineContains(HashSet<string> baseline, string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if (baseline.Contains(current)) return true;
        return false;
    }

    public Task PollAsync() => Task.Run(PollCoreAsync);
    private async Task PollCoreAsync()
    {
        await _loaded.ConfigureAwait(false);
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            OrdoMonitorState state; CancellationToken token;
            lock (_sync)
            {
                if (_disposed || _state is not { Enabled: true }) return;
                state = _state; token = _approval.Token;
            }
            var config = new OrdoConfiguration(state.Configuration.SourceRoot, state.Configuration.Rules, state.Configuration.Recursive);
            if (OrdoPlanner.Signature(config) != state.Signature) throw new IOException("Rules changed. Review required.");
            if (OrdoPaths.DirectorySnapshot(_metadata, config.SourceRoot).Identity != state.RootIdentity)
                throw new IOException("Source directory identity changed. Review required.");
            _claim ??= _claims.Acquire(config.SourceRoot, OrdoPlanner.Validate(config), organizer: true);
            if (_journal.List().Any(o => o.Entries.Any(e => e.State is OrdoEntryState.Prepared or OrdoEntryState.UndoPrepared or OrdoEntryState.Conflict)))
                throw new IOException("Unresolved journal state. Reconcile or undo before monitoring.");
            var scan = Enumerate(config);
            var baseline = state.Baseline.ToHashSet(Paths);
            var pending = scan.Files.Where(f => !BaselineContains(baseline, f.Path)).OrderBy(f => f.Path, Paths).ToArray();
            var present = pending.Select(f => f.Path).ToHashSet(Paths);
            foreach (var path in _observed.Keys.Where(p => !present.Contains(p)).ToArray()) _observed.Remove(path);
            var ready = new List<string>(); var now = _time.GetUtcNow();
            foreach (var file in pending)
            {
                if (_observed.TryGetValue(file.Path, out var prior) && prior.Snapshot == file)
                {
                    if (now - prior.Since >= _settle && !OrdoPlanner.IsTemporary(file.Path)) ready.Add(file.Path);
                }
                else _observed[file.Path] = (file, now);
            }
            lock (_sync)
            {
                token.ThrowIfCancellationRequested();
                _state = _state! with { Pending = pending, Status = $"Monitoring: {pending.Length} pending arrivals; {scan.Issues.Count} skipped paths." };
                Save();
            }
            Changed?.Invoke(this, EventArgs.Empty);
            if (ready.Count == 0) return;
            var plan = await _planner.PreviewAsync(config, cancellationToken: token, eligibleSourcePaths: ready).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var planned = plan.Moves.Select(m => m.Source.Snapshot.Path).ToHashSet(Paths);
            foreach (var path in ready.Where(p => !planned.Contains(p) && !plan.Issues.Any(i => i.Retryable && OrdoPaths.Within(p, i.Path))))
            { baseline.Add(path); _observed.Remove(path); } // Unmatched files stay and are not repeatedly retried.
            var completed = new HashSet<string>(Paths);
            if (plan.Moves.Count > 0)
            {
                lock (_sync) _organizing = true;
                Changed?.Invoke(this, EventArgs.Empty);
                var result = await _service.ApplyAsync(plan, config, token).ConfigureAwait(false);
                foreach (var entry in result.Entries)
                {
                    _observed.Remove(entry.Move.Source.Snapshot.Path);
                    if (entry.State == OrdoEntryState.Completed) completed.Add(entry.Move.Source.Snapshot.Path);
                }
                if (result.Entries.Any(e => e.State is OrdoEntryState.Conflict or OrdoEntryState.Prepared or OrdoEntryState.UndoPrepared))
                    await PauseAsync("Paused: a move needs review in operation history.").ConfigureAwait(false);
            }
            lock (_sync)
            {
                _state = _state! with
                {
                    Baseline = baseline.OrderBy(p => p, Paths).ToArray(),
                    Pending = pending.Where(f => !completed.Contains(f.Path) && !BaselineContains(baseline, f.Path)).ToArray()
                };
                Save();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException)
        { await PauseAsync("Paused: " + ex.Message).ConfigureAwait(false); }
        finally
        {
            lock (_sync) _organizing = false;
            if (!Enabled) { _claim?.Dispose(); _claim = null; }
            _serial.Release(); Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Keep persisted Enabled for catch-up after restart; only cancel this process's work.
        lock (_sync) { _disposed = true; _approval.Cancel(); }
        await _loaded.ConfigureAwait(false);
        await _serial.WaitAsync().ConfigureAwait(false);
        try { _claim?.Dispose(); _claim = null; }
        finally { _serial.Release(); }
    }
}
