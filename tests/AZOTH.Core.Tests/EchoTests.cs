using AZOTH.Core.Echo;
using AZOTH.Core.Files;

// These cases deliberately cover the optional default token and independently controlled cancellation.
#pragma warning disable xUnit1051

namespace AZOTH.Core.Tests;

public sealed class EchoTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("azoth-echo-").FullName;
    private readonly TestMetadata _metadata = new();
    private EchoService Service(FileMutationCoordinator? gate = null) => new(_metadata, new FileFingerprintService(_metadata), gate ?? new());
    private string Write(string name, string text = "same")
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
    private Task<EchoScanResult> Scan(EchoService service) => service.ScanAsync([_root]);

    [Fact]
    public async Task Groups_content_across_names_overlapping_roots_and_empty_files_without_selecting_anything()
    {
        Write("a"); Write("nested/b"); Write("different", "diff"); Write("unique-size", "12345");
        Write("zero1", ""); Write("zero2", "");
        var service = Service();
        var scan = await service.ScanAsync([_root, Path.Combine(_root, "nested")]);
        Assert.Equal(2, scan.Groups.Count);
        Assert.Equal(4, scan.EstimatedSavingsBytes);
        Assert.Equal(5, scan.FilesHashed); // unique-size never opened for hashing
        Assert.All(scan.Groups, group => Assert.Equal(2, group.Files.Count));
        var cleanup = await service.CleanupAsync(scan, [], (_, _) => throw new Exception("No implicit selection"));
        Assert.Empty(cleanup.Outcomes);
    }

    [Fact]
    public async Task Hardlink_aliases_are_consolidated_and_not_counted_as_reclaimable()
    {
        var a = Write("a"); var alias = Write("alias"); var b = Write("b");
        _metadata.IdentityAliases[alias] = a;
        _metadata.Links[a] = _metadata.Links[alias] = 2;
        var service = Service(); var scan = await Scan(service);
        Assert.Equal(2, Assert.Single(scan.Groups).Files.Count);
        Assert.Equal(4, scan.EstimatedSavingsBytes);
        Assert.Equal(2, scan.FilesHashed);
        var refused = await service.CleanupAsync(scan, [a], (_, _) => throw new Exception("Hardlink must remain"));
        Assert.Equal(EchoCleanupStatus.Refused, Assert.Single(refused.Outcomes).Status);
        var success = await service.CleanupAsync(scan, [b], (_, _) => Task.FromResult(TrashResult.Success));
        Assert.Equal(EchoCleanupStatus.Trashed, Assert.Single(success.Outcomes).Status);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("missing")]
    [InlineData("denied")]
    [InlineData("identity")]
    [InlineData("hardlink")]
    [InlineData("unknown")]
    [InlineData("link")]
    public async Task Stale_or_unsafe_candidate_is_never_trashed(string change)
    {
        var a = Write("a"); Write("b"); var service = Service(); var scan = await Scan(service);
        switch (change)
        {
            case "changed": File.WriteAllText(a, "edit"); break;
            case "missing": File.Delete(a); break;
            case "denied": _metadata.Denied.Add(a); break;
            case "identity": _metadata.IdentityAliases[a] = "replacement"; break;
            case "hardlink": _metadata.Links[a] = 2; break;
            case "unknown": _metadata.Unknown.Add(a); break;
            case "link": _metadata.Linked.Add(a); break;
        }
        var result = await service.CleanupAsync(scan, [a], (_, _) => throw new Exception("Unsafe candidate"));
        Assert.Equal(EchoCleanupStatus.Refused, Assert.Single(result.Outcomes).Status);
    }

    [Fact]
    public async Task Hashes_current_bytes_even_when_length_timestamp_and_identity_are_unchanged()
    {
        var a = Write("a"); Write("b"); var service = Service(); var scan = await Scan(service);
        var time = File.GetLastWriteTimeUtc(a);
        File.WriteAllText(a, "edit"); File.SetLastWriteTimeUtc(a, time);
        var result = await service.CleanupAsync(scan, [a], (_, _) => throw new Exception("Changed bytes"));
        Assert.Equal(EchoCleanupStatus.Refused, Assert.Single(result.Outcomes).Status);
    }

    [Fact]
    public async Task Survivor_is_rehashed_before_each_individual_trash_and_partial_success_is_reported()
    {
        var a = Write("a"); var b = Write("b"); var survivor = Write("survivor");
        var service = Service(); var scan = await Scan(service);
        var result = await service.CleanupAsync(scan, [a, b], (path, _) =>
        {
            Assert.Equal(a, path);
            File.Delete(path); File.WriteAllText(survivor, "gone");
            return Task.FromResult(TrashResult.Success);
        });
        Assert.Equal([EchoCleanupStatus.Trashed, EchoCleanupStatus.Refused], result.Outcomes.Select(x => x.Status));
        Assert.True(File.Exists(b));
    }

    [Fact]
    public async Task Selected_all_unknown_paths_and_trash_refusal_never_delete_a_file()
    {
        var a = Write("a"); var b = Write("b"); var service = Service(); var scan = await Scan(service);
        var all = await service.CleanupAsync(scan, [a, b, Path.Combine(_root, "unknown")], (_, _) => throw new Exception("No survivor"));
        Assert.All(all.Outcomes, x => Assert.Equal(EchoCleanupStatus.Refused, x.Status));
        var refused = await service.CleanupAsync(scan, [a], (_, _) => Task.FromResult(new TrashResult(false, "unavailable")));
        Assert.Equal(EchoCleanupStatus.TrashRefused, Assert.Single(refused.Outcomes).Status);
        Assert.True(File.Exists(a)); Assert.True(File.Exists(b));
    }

    [Fact]
    public async Task Trash_exception_is_indeterminate_and_preserves_prior_success()
    {
        var a = Write("a"); var b = Write("b"); Write("c"); var service = Service(); var scan = await Scan(service);
        var result = await service.CleanupAsync(scan, [a, b], (path, _) => path == a
            ? Task.FromResult(TrashResult.Success) : throw new IOException("Trash response lost"));
        Assert.Equal([EchoCleanupStatus.Trashed, EchoCleanupStatus.Indeterminate], result.Outcomes.Select(x => x.Status));
    }

    [Fact]
    public async Task Scan_skips_links_unknown_identity_and_reports_access_refusals()
    {
        var a = Write("a"); Write("b"); var u = Write("unknown"); var d = Write("denied");
        Write("linked/inside"); _metadata.Linked.Add(Path.Combine(_root, "linked"));
        _metadata.Unknown.Add(u); _metadata.Denied.Add(d);
        var scan = await Scan(Service());
        Assert.Equal(2, Assert.Single(scan.Groups).Files.Count);
        Assert.Equal(3, scan.Issues.Count);
        Assert.Contains(scan.Groups[0].Files, f => f.Snapshot.Path == a);
    }

    [Fact]
    public async Task Nonrecursive_scan_and_cancellation_are_honored()
    {
        Write("a"); Write("nested/b");
        Assert.Empty((await Service().ScanAsync([_root], recursive: false)).Groups);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().ScanAsync([_root], cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Shared_mutation_gate_blocks_cleanup_and_cancellation_reports_unattempted_files()
    {
        var a = Write("a"); Write("b"); var gate = new FileMutationCoordinator(); var service = Service(gate); var scan = await Scan(service);
        using var held = await gate.AcquireAsync();
        using var cts = new CancellationTokenSource();
        var cleanup = service.CleanupAsync(scan, [a], (_, _) => throw new Exception("Must await gate"), cts.Token);
        Assert.False(cleanup.IsCompleted); cts.Cancel();
        Assert.Equal(EchoCleanupStatus.Cancelled, Assert.Single((await cleanup).Outcomes).Status);
    }

    [Fact]
    public async Task Fingerprint_refuses_metadata_changed_while_streaming_and_returns_known_sha256()
    {
        var a = Write("a", "abc"); var fingerprints = new FileFingerprintService(_metadata);
        var result = await fingerprints.ComputeAsync(a);
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", result.Fingerprint?.Sha256);
        var reads = 0;
        _metadata.OnRead = path => { if (path == a && ++reads == 2) _metadata.Links[a] = 2; };
        Assert.Equal(FileRefusalKind.Changed, (await fingerprints.ComputeAsync(a)).Refusal?.Kind);
    }

    [Fact]
    public async Task Multiple_visible_aliases_are_not_estimated_as_removable_even_when_native_link_count_is_one()
    {
        var a = Write("a"); var alias = Write("alias"); Write("b"); Write("c");
        _metadata.IdentityAliases[alias] = a;
        var scan = await Scan(Service());
        Assert.Equal(3, Assert.Single(scan.Groups).Files.Count);
        Assert.Equal(8, scan.EstimatedSavingsBytes);
        _metadata.IdentityAliases[Path.Combine(_root, "c")] = Path.Combine(_root, "b");
        scan = await Scan(Service());
        Assert.Equal(0, scan.EstimatedSavingsBytes);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("denied")]
    [InlineData("unknown")]
    [InlineData("same-metadata-different-bytes")]
    public async Task Unverifiable_survivor_blocks_cleanup(string change)
    {
        var a = Write("a"); var b = Write("b"); var service = Service(); var scan = await Scan(service);
        switch (change)
        {
            case "missing": File.Delete(b); break;
            case "denied": _metadata.Denied.Add(b); break;
            case "unknown": _metadata.Unknown.Add(b); break;
            default:
                var time = File.GetLastWriteTimeUtc(b); File.WriteAllText(b, "edit"); File.SetLastWriteTimeUtc(b, time); break;
        }
        var result = await service.CleanupAsync(scan, [a], (_, _) => throw new Exception("Unverified survivor"));
        Assert.Equal(EchoCleanupStatus.Refused, Assert.Single(result.Outcomes).Status);
    }

    [Fact]
    public async Task Newly_linked_candidate_after_both_hashes_is_refused_by_final_metadata_check()
    {
        var a = Write("a"); var b = Write("b"); var service = Service(); var scan = await Scan(service); var reads = 0;
        _metadata.OnRead = path => { if (path == b && ++reads == 2) _metadata.Links[a] = 2; };
        var result = await service.CleanupAsync(scan, [a], (_, _) => throw new Exception("Late hardlink"));
        Assert.Equal(EchoCleanupStatus.Refused, Assert.Single(result.Outcomes).Status);
    }

    [Fact]
    public async Task Cancellation_after_success_preserves_outcomes_and_skips_remaining_candidates()
    {
        var a = Write("a"); var b = Write("b"); Write("c"); var service = Service(); var scan = await Scan(service);
        using var cts = new CancellationTokenSource();
        var result = await service.CleanupAsync(scan, [a, b], (_, _) =>
        { cts.Cancel(); return Task.FromResult(TrashResult.Success); }, cts.Token);
        Assert.Equal([EchoCleanupStatus.Trashed, EchoCleanupStatus.Cancelled], result.Outcomes.Select(x => x.Status));
    }

    [Fact]
    public async Task Shared_gate_is_held_until_the_trash_operation_finishes()
    {
        var a = Write("a"); Write("b"); var gate = new FileMutationCoordinator(); var service = Service(gate); var scan = await Scan(service);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = service.CleanupAsync(scan, [a], async (_, _) =>
        { entered.SetResult(); await release.Task; return TrashResult.Success; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var nextMutation = gate.AcquireAsync().AsTask();
        try { Assert.False(nextMutation.IsCompleted); }
        finally { release.SetResult(); }
        Assert.Equal(EchoCleanupStatus.Trashed, Assert.Single((await cleanup).Outcomes).Status);
        using var acquired = await nextMutation.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Returned_scan_collections_cannot_be_mutated_by_selection_ui()
    {
        Write("a"); Write("b"); var scan = await Scan(Service());
        Assert.Throws<NotSupportedException>(() => ((IList<EchoGroup>)scan.Groups).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<EchoFile>)scan.Groups[0].Files).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)scan.Groups[0].Files[0].Aliases).Clear());
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class TestMetadata : IFileMetadata
    {
        public Dictionary<string, string> IdentityAliases { get; } = [];
        public Dictionary<string, ulong> Links { get; } = [];
        public HashSet<string> Unknown { get; } = [];
        public HashSet<string> Denied { get; } = [];
        public HashSet<string> Linked { get; } = [];
        public Action<string>? OnRead { get; set; }
        public FileMetadataResult Read(string path)
        {
            path = Path.GetFullPath(path); OnRead?.Invoke(path);
            if (Denied.Contains(path)) return new(null, new(FileRefusalKind.AccessDenied, path));
            if (!File.Exists(path) && !Directory.Exists(path)) return new(null, new(FileRefusalKind.Missing, path));
            var directory = Directory.Exists(path);
            return new(new(path, Unknown.Contains(path) ? null : new("test", IdentityAliases.GetValueOrDefault(path, path)),
                Links.GetValueOrDefault(path, 1ul), directory ? 0 : new FileInfo(path).Length,
                File.GetLastWriteTimeUtc(path), Linked.Contains(path) ? FileEntryKind.Link : directory ? FileEntryKind.Directory : FileEntryKind.RegularFile), null);
        }
    }
}
