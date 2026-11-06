using AZOTH.Core.Files;
using AZOTH.Core.Ordo;
#pragma warning disable xUnit1051
namespace AZOTH.Core.Tests;

public sealed class OrdoTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("azoth-ordo-").FullName;
    private readonly Metadata _metadata = new();
    private string Source => Path.Combine(_root, "source");
    private OrdoJournal Journal => new(Path.Combine(_root, "journal"));
    private OrdoPlanner Planner => new(_metadata, new(_metadata));
    private OrdoService Service(IOrdoFileMover? mover = null, FileMutationCoordinator? gate = null) => new(_metadata, new(_metadata), gate ?? new(), Journal, mover);
    private OrdoConfiguration Config(params OrdoRule[] rules) => new(Source, rules.Length == 0 ? [new("all", "sorted")] : rules);
    private string Write(string name, string contents = "content")
    {
        var path = Path.Combine(Source, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, contents); return path;
    }
    [Fact]
    public async Task Ordered_rules_combine_type_extension_and_date_and_leave_unmatched_temp_and_outputs()
    {
        var a = Write("a.JPG"); Write("b.txt"); Write("stay.bin"); Write("partial.tmp"); Write("sorted/old.JPG");
        File.SetLastWriteTimeUtc(a, new DateTime(2025, 3, 12, 0, 0, 0, DateTimeKind.Utc));
        var config = Config(new OrdoRule("first", "sorted", OrdoFileType.Image, "jpg", new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), true), new("second", "elsewhere", OrdoFileType.Image), new("text", "docs", Extension: ".txt"));
        var plan = await Planner.PreviewAsync(config);
        Assert.Equal(2, plan.Moves.Count);
        Assert.Equal(Path.Combine(Source, "sorted", "2025-03", "a.JPG"), plan.Moves[0].Destination);
        Assert.Equal("first", plan.Moves[0].RuleId);
        Assert.Equal("text", plan.Moves[1].RuleId);
    }
    [Fact]
    public async Task Reserves_duplicate_names_deterministically_and_supports_absolute_destination()
    {
        Write("a/same.txt"); Write("b/same.txt"); var target = Path.Combine(_root, "external"); Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "same.txt"), "existing");
        var config = Config(new OrdoRule("r", target)); var plan = await Planner.PreviewAsync(config);
        Assert.Equal(new[] { "same (1).txt", "same (2).txt" }, plan.Moves.Select(m => Path.GetFileName(m.Destination)));
        Assert.Equal(plan.Moves, (await Planner.PreviewAsync(config)).Moves);
        Assert.Throws<NotSupportedException>(() => ((IList<OrdoPlannedMove>)plan.Moves).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<OrdoRule>)config.Rules).Clear());
    }
    [Theory]
    [InlineData("../escape")]
    [InlineData("child/../../escape")]
    [InlineData(".")]
    public async Task Invalid_destinations_are_rejected(string destination)
    { Write("a"); await Assert.ThrowsAsync<ArgumentException>(() => Planner.PreviewAsync(Config(new OrdoRule("r", destination)))); }
    [Fact]
    public async Task Destination_subtree_is_pruned_before_metadata_reads()
    {
        Write("a"); Write("sorted/deep/existing"); var reads = new List<string>(); _metadata.OnRead = p => reads.Add(p);
        await Planner.PreviewAsync(Config());
        Assert.DoesNotContain(reads, p => p == Path.Combine(Source, "sorted") || p.StartsWith(Path.Combine(Source, "sorted/deep"), StringComparison.Ordinal));
    }
    [Theory]
    [InlineData("bytes")]
    [InlineData("identity")]
    [InlineData("link")]
    [InlineData("hardlink")]
    [InlineData("occupied")]
    public async Task Apply_refuses_stale_or_occupied_files_without_mutating(string change)
    {
        var source = Write("a"); var config = Config(); var plan = await Planner.PreviewAsync(config);
        switch (change)
        {
            case "bytes": var time = File.GetLastWriteTimeUtc(source); File.WriteAllText(source, "changed"); File.SetLastWriteTimeUtc(source, time); break;
            case "identity": _metadata.Identities[source] = "replacement"; break;
            case "link": _metadata.Links.Add(source); break;
            case "hardlink": _metadata.Hardlinks.Add(source); break;
            default: Write("sorted/a", "existing"); break;
        }
        var result = await Service().ApplyAsync(plan, config);
        Assert.Equal(OrdoEntryState.Skipped, Assert.Single(result.Entries).State); Assert.True(File.Exists(source));
    }
    [Fact]
    public async Task Configuration_edit_invalidates_approved_plan()
    {
        var source = Write("a"); var plan = await Planner.PreviewAsync(Config());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().ApplyAsync(plan, Config(new OrdoRule("all", "other")))); Assert.True(File.Exists(source));
    }
    [Fact]
    public async Task Prepared_is_durable_before_move_and_completed_afterward_and_restart_can_undo()
    {
        var source = Write("a"); var config = Config(); var plan = await Planner.PreviewAsync(config);
        var service = Service(new Mover((s, d) => { Assert.Equal(OrdoEntryState.Prepared, Assert.Single(Assert.Single(Journal.List()).Entries).State); File.Move(s, d, false); }));
        var result = await service.ApplyAsync(plan, config);
        Assert.Equal(OrdoEntryState.Completed, Assert.Single(Journal.Load(result.Id).Entries).State);
        Assert.NotNull(result.Entries[0].DestinationFingerprint); Assert.False(File.Exists(source));
        var undone = await Service().UndoAsync(result.Id); Assert.Equal(OrdoEntryState.Undone, undone.Entries[0].State);
        Assert.Equal("content", File.ReadAllText(source)); Assert.False(Directory.Exists(Path.Combine(Source, "sorted")));
    }
    [Theory]
    [InlineData("both", OrdoEntryState.Conflict)]
    [InlineData("neither", OrdoEntryState.Conflict)]
    [InlineData("source", OrdoEntryState.Skipped)]
    [InlineData("destination", OrdoEntryState.Completed)]
    [InlineData("changed", OrdoEntryState.Conflict)]
    public async Task Prepared_recovery_uses_conservative_existence_and_hash_matrix(string situation, OrdoEntryState expected)
    {
        var source = Write("a"); var move = Assert.Single((await Planner.PreviewAsync(Config())).Moves); var dest = move.Destination;
        var op = new OrdoOperation(1, Guid.NewGuid(), DateTimeOffset.UtcNow, "config", [new(move, OrdoEntryState.Prepared)], []); Journal.Save(op);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        if (situation is "both") File.Copy(source, dest);
        if (situation is "destination" or "changed") File.Move(source, dest);
        if (situation is "changed") File.WriteAllText(dest, "changed");
        if (situation is "neither") File.Delete(source);
        var result = await Service().ReconcileAsync(op.Id); Assert.Equal(expected, result.Entries[0].State);
        if (situation == "both") { Assert.True(File.Exists(source)); Assert.True(File.Exists(dest)); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ambiguous_move_outcome_keeps_both_paths_even_if_mover_returns(bool throws)
    {
        var source = Write("a"); var config = Config(); var plan = await Planner.PreviewAsync(config);
        var result = await Service(new Mover((s, d) => { File.Copy(s, d); if (throws) throw new IOException("partial copy"); })).ApplyAsync(plan, config);
        Assert.Equal(OrdoEntryState.Conflict, result.Entries[0].State); Assert.True(File.Exists(source)); Assert.True(File.Exists(plan.Moves[0].Destination));
    }
    [Theory]
    [InlineData("bytes")]
    [InlineData("identity")]
    [InlineData("original")]
    [InlineData("parent")]
    public async Task Undo_conflicts_are_retryable_and_do_not_overwrite(string conflict)
    {
        var source = Write("nested/a"); var config = Config(); var plan = await Planner.PreviewAsync(config); var op = await Service().ApplyAsync(plan, config); var dest = plan.Moves[0].Destination;
        var expectedIdentity = _metadata.Identities.GetValueOrDefault(dest, dest); var time = File.GetLastWriteTimeUtc(dest);
        switch (conflict) { case "bytes": File.WriteAllText(dest, "changed"); break; case "identity": _metadata.Identities[dest] = "replaced"; break; case "original": Write("nested/a", "user"); break; default: Directory.Delete(Path.GetDirectoryName(source)!); break; }
        var refused = await Service().UndoAsync(op.Id); Assert.Equal(OrdoEntryState.Conflict, refused.Entries[0].State); Assert.True(File.Exists(dest));
        switch (conflict) { case "bytes": File.WriteAllText(dest, "content"); File.SetLastWriteTimeUtc(dest, time); break; case "identity": _metadata.Identities[dest] = expectedIdentity; break; case "original": File.Delete(source); break; default: Directory.CreateDirectory(Path.GetDirectoryName(source)!); break; }
        var retry = await Service().UndoAsync(op.Id); Assert.Equal(OrdoEntryState.Undone, retry.Entries[0].State); Assert.True(File.Exists(source));
    }
    [Fact]
    public async Task Cancellation_occurs_between_moves_and_undo_is_reverse_order_and_retains_nonempty_owned_directories()
    {
        Write("a"); Write("b"); var config = Config(); var plan = await Planner.PreviewAsync(config); using var cancel = new CancellationTokenSource();
        var op = await Service(new Mover((s, d) => { File.Move(s, d); cancel.Cancel(); })).ApplyAsync(plan, config, cancel.Token);
        Assert.True(op.Cancelled); Assert.Equal(OrdoEntryState.Completed, op.Entries[0].State); Assert.True(File.Exists(Path.Combine(Source, "b")));
        Write("sorted/user"); await Service().UndoAsync(op.Id); Assert.False(Journal.Load(op.Id).Cancelled); Assert.True(File.Exists(Path.Combine(Source, "sorted/user")));
        File.Delete(Path.Combine(Source, "sorted/user")); var full = await Service().ApplyAsync(await Planner.PreviewAsync(config), config); var order = new List<string>();
        await Service(new Mover((s, d) => { order.Add(Path.GetFileName(s)); File.Move(s, d); })).UndoAsync(full.Id); Assert.Equal(new[] { "b", "a" }, order);
    }
    [Fact]
    public async Task Shared_gate_blocks_apply_before_validation_and_is_held_during_move()
    {
        Write("a"); var config = Config(); var plan = await Planner.PreviewAsync(config); var gate = new FileMutationCoordinator();
        using (var lease = await gate.AcquireAsync()) { using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(gate: gate).ApplyAsync(plan, config, cancel.Token)); }
        Task<IDisposable>? waiter = null; var op = await Service(new Mover((s, d) => { waiter = gate.AcquireAsync().AsTask(); Assert.False(waiter.IsCompleted); File.Move(s, d); }), gate).ApplyAsync(plan, config);
        using var next = await waiter!.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(OrdoEntryState.Completed, op.Entries[0].State);
    }
    [Theory]
    [InlineData("a.png", OrdoFileType.Image)]
    [InlineData("a.mp4", OrdoFileType.Video)]
    [InlineData("a.mp3", OrdoFileType.Audio)]
    [InlineData("a.pdf", OrdoFileType.Document)]
    [InlineData("a.hwp", OrdoFileType.Document)]
    [InlineData("a.hwpx", OrdoFileType.Document)]
    [InlineData("a.epub", OrdoFileType.Document)]
    [InlineData("a.avif", OrdoFileType.Image)]
    [InlineData("a.zip", OrdoFileType.Archive)]
    [InlineData("a.cs", OrdoFileType.Code)]
    [InlineData("a.unknown", OrdoFileType.Other)]
    public async Task File_categories_match_only_the_requested_rule(string name, OrdoFileType type)
    {
        Write(name); var plan = await Planner.PreviewAsync(Config(new OrdoRule("match", "sorted", type)));
        Assert.Single(plan.Moves);
    }
    [Fact]
    public async Task Modified_interval_is_inclusive_from_exclusive_before_and_recursion_is_optional()
    {
        var a = Write("a"); var b = Write("b"); Write("nested/c");
        File.SetLastWriteTimeUtc(a, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)); File.SetLastWriteTimeUtc(b, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var config = new OrdoConfiguration(Source, [new("r", "sorted", ModifiedFromUtc: new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), ModifiedBeforeUtc: new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))], false);
        Assert.Equal(a, Assert.Single((await Planner.PreviewAsync(config)).Moves).Source.Snapshot.Path);
    }
    [Theory]
    [InlineData("source", OrdoEntryState.Undone)]
    [InlineData("both", OrdoEntryState.Conflict)]
    [InlineData("neither", OrdoEntryState.Conflict)]
    public async Task Interrupted_undo_reconciles_without_deleting_ambiguous_paths(string state, OrdoEntryState expected)
    {
        var source = Write("a"); var config = Config(); var op = await Service().ApplyAsync(await Planner.PreviewAsync(config), config); var entry = op.Entries[0];
        Journal.Save(op with { Entries = [entry with { State = OrdoEntryState.UndoPrepared, UndoPending = true }] });
        if (state == "source") File.Move(entry.Move.Destination, source); else if (state == "both") File.Copy(entry.Move.Destination, source); else File.Delete(entry.Move.Destination);
        var result = await Service().ReconcileAsync(op.Id); Assert.Equal(expected, result.Entries[0].State);
        if (state == "both") { Assert.True(File.Exists(source)); Assert.True(File.Exists(entry.Move.Destination)); }
    }
    [Fact]
    public async Task Preexisting_directories_are_never_owned_or_removed()
    {
        Write("a"); Directory.CreateDirectory(Path.Combine(Source, "sorted")); var config = Config(); var op = await Service().ApplyAsync(await Planner.PreviewAsync(config), config);
        Assert.Empty(op.CreatedDirectories); await Service().UndoAsync(op.Id); Assert.True(Directory.Exists(Path.Combine(Source, "sorted")));
    }
    [Fact]
    public async Task Later_operation_blocks_earlier_undo_until_it_is_undone()
    {
        var original = Write("a"); var config = Config(); var first = await Service().ApplyAsync(await Planner.PreviewAsync(config), config);
        var secondConfig = new OrdoConfiguration(Path.Combine(Source, "sorted"), [new("r", Path.Combine(_root, "later"))]);
        var second = await Service().ApplyAsync(await Planner.PreviewAsync(secondConfig), secondConfig);
        Assert.Equal(OrdoEntryState.Conflict, (await Service().UndoAsync(first.Id)).Entries[0].State);
        await Service().UndoAsync(second.Id); Assert.Equal(OrdoEntryState.Undone, (await Service().UndoAsync(first.Id)).Entries[0].State); Assert.True(File.Exists(original));
    }
    [Fact]
    public async Task Broken_journal_prevents_any_move_and_journal_contains_no_file_contents()
    {
        var source = Write("a", "secret-content-not-for-journal"); var config = Config(); var plan = await Planner.PreviewAsync(config);
        var badPath = Path.Combine(_root, "not-a-directory"); File.WriteAllText(badPath, "block");
        var service = new OrdoService(_metadata, new(_metadata), new(), new OrdoJournal(badPath), new Mover((_, _) => throw new Exception("Must not move")));
        await Assert.ThrowsAnyAsync<IOException>(() => service.ApplyAsync(plan, config)); Assert.True(File.Exists(source));
        var op = await Service().ApplyAsync(plan, config);
        foreach (var path in Directory.EnumerateFiles(Path.Combine(_root, "journal"), "*.json", SearchOption.AllDirectories)) Assert.DoesNotContain("secret-content-not-for-journal", File.ReadAllText(path));
        Assert.Throws<InvalidDataException>(() => Journal.Save(op with { Version = 99 }));
    }
    [Fact]
    public async Task Final_parent_link_recheck_blocks_move_after_preview()
    {
        var source = Write("a"); Directory.CreateDirectory(Path.Combine(Source, "sorted")); var config = Config(); var plan = await Planner.PreviewAsync(config); _metadata.Links.Add(Path.Combine(Source, "sorted"));
        var result = await Service().ApplyAsync(plan, config); Assert.Equal(OrdoEntryState.Skipped, result.Entries[0].State); Assert.True(File.Exists(source));
    }
    [Fact]
    public async Task Apply_of_many_entries_does_not_rewrite_completed_entry_records()
    {
        Write("a"); Write("b"); var config = Config(); var plan = await Planner.PreviewAsync(config); string? firstRecord = null; DateTime marker = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var op = await Service(new Mover((s, d) =>
        {
            if (Path.GetFileName(s) == "b")
            {
                firstRecord = Directory.EnumerateFiles(Path.Combine(_root, "journal"), "entry-00000000.json", SearchOption.AllDirectories).Single();
                File.SetLastWriteTimeUtc(firstRecord, marker);
            }
            File.Move(s, d);
        })).ApplyAsync(plan, config);
        Assert.Equal(marker, File.GetLastWriteTimeUtc(firstRecord!)); Assert.All(Journal.Load(op.Id).Entries, e => Assert.Equal(OrdoEntryState.Completed, e.State));
    }
    [Fact]
    public async Task Fully_undone_operation_preserves_directory_needed_by_another_operations_undo()
    {
        Write("a"); var config = Config(); var first = await Service().ApplyAsync(await Planner.PreviewAsync(config), config); Write("sorted/b");
        var secondConfig = new OrdoConfiguration(Path.Combine(Source, "sorted"), [new("r", Path.Combine(_root, "later"), Extension: ".bin")]);
        File.Move(Path.Combine(Source, "sorted/b"), Path.Combine(Source, "sorted/b.bin"));
        var second = await Service().ApplyAsync(await Planner.PreviewAsync(secondConfig), secondConfig);
        Assert.Equal(OrdoEntryState.Undone, (await Service().UndoAsync(first.Id)).Entries[0].State);
        Assert.True(Directory.Exists(Path.Combine(Source, "sorted")));
        Assert.Equal(OrdoEntryState.Undone, (await Service().UndoAsync(second.Id)).Entries[0].State);
        Assert.True(File.Exists(Path.Combine(Source, "sorted/b.bin")));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Original_parent_identity_is_rechecked_after_prepared_record(bool undo)
    {
        var source = Write("a"); var config = Config(); var plan = await Planner.PreviewAsync(config); OrdoOperation? op = null;
        if (undo) op = await Service().ApplyAsync(plan, config);
        var observed = undo ? plan.Moves[0].Destination : source;
        var fired = false;
        _metadata.OnRead = path =>
        {
            if (path != observed || fired) return;
            var existing = Journal.List().FirstOrDefault();
            if (existing?.Entries[0].State == (undo ? OrdoEntryState.UndoPrepared : OrdoEntryState.Prepared))
            { fired = true; _metadata.Identities[Source] = "replaced-parent"; }
        };
        var result = undo ? await Service().UndoAsync(op!.Id) : await Service().ApplyAsync(plan, config);
        Assert.True(fired); Assert.Equal(OrdoEntryState.Conflict, result.Entries[0].State); Assert.True(File.Exists(observed));
    }
    [Fact]
    public async Task Refused_undo_does_not_claim_later_matching_external_file_as_its_completed_move()
    {
        var source = Write("a"); var config = Config(); var op = await Service().ApplyAsync(await Planner.PreviewAsync(config), config);
        Write("a", "content"); Assert.Equal(OrdoEntryState.Conflict, (await Service().UndoAsync(op.Id)).Entries[0].State);
        File.Delete(op.Entries[0].Move.Destination);
        Assert.Equal(OrdoEntryState.Conflict, (await Service().ReconcileAsync(op.Id)).Entries[0].State); Assert.True(File.Exists(source));
    }
    [Fact]
    public async Task Undo_refused_after_preparation_cannot_claim_later_unrelated_content_as_restored()
    {
        var source = Write("a"); var config = Config(); var op = await Service().ApplyAsync(await Planner.PreviewAsync(config), config);
        var destination = op.Entries[0].Move.Destination; var injected = false; var moverCalls = 0;
        _metadata.OnRead = path =>
        {
            if (path != destination || injected || Journal.Load(op.Id).Entries[0].State != OrdoEntryState.UndoPrepared) return;
            injected = true; Write("a", "content");
        };
        var refused = await Service(new Mover((s, d) => { moverCalls++; File.Move(s, d); })).UndoAsync(op.Id);
        Assert.True(injected); Assert.Equal(0, moverCalls); Assert.Equal(OrdoEntryState.Conflict, refused.Entries[0].State);
        Assert.True(File.Exists(source)); Assert.True(File.Exists(destination));
        File.Delete(destination);
        var reconciled = await Service().ReconcileAsync(op.Id);
        Assert.Equal(OrdoEntryState.Conflict, reconciled.Entries[0].State);
        Assert.False(refused.Entries[0].UndoPending); Assert.False(Journal.Load(op.Id).Entries[0].UndoPending);
        Assert.Equal(OrdoEntryState.Conflict, (await Service().UndoAsync(op.Id)).Entries[0].State);
        Assert.Equal("content", File.ReadAllText(source)); Assert.True(Directory.Exists(Path.GetDirectoryName(destination)));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Entered_undo_mover_failure_preserves_pending_recovery_and_ambiguous_paths(bool leaveDestination)
    {
        var source = Write("a"); var config = Config(); var op = await Service().ApplyAsync(await Planner.PreviewAsync(config), config);
        var destination = op.Entries[0].Move.Destination; var moverCalls = 0;
        var result = await Service(new Mover((s, d) =>
        {
            moverCalls++;
            if (leaveDestination) File.Copy(s, d); else File.Move(s, d);
            throw new IOException("Move may have completed before failure.");
        })).UndoAsync(op.Id);
        Assert.Equal(1, moverCalls); Assert.Equal(OrdoEntryState.Conflict, result.Entries[0].State);
        Assert.True(result.Entries[0].UndoPending); Assert.True(Journal.Load(op.Id).Entries[0].UndoPending);
        Assert.Equal("content", File.ReadAllText(source)); Assert.Equal(leaveDestination, File.Exists(destination));
        var recovered = await Service().ReconcileAsync(op.Id);
        Assert.Equal(leaveDestination ? OrdoEntryState.Conflict : OrdoEntryState.Undone, recovered.Entries[0].State);
        Assert.Equal(leaveDestination, File.Exists(destination)); Assert.Equal("content", File.ReadAllText(source));
    }
    [Fact]
    public async Task Explicit_monitor_allowlist_is_normalized_deduplicated_and_empty_means_no_moves()
    {
        var allowed = Write("a"); Write("b"); var output = Write("sorted/existing"); var config = Config(); var full = await Planner.PreviewAsync(config);
        var filtered = await Planner.PreviewAsync(config, eligibleSourcePaths: [allowed, "a", allowed, output]);
        Assert.Equal(allowed, Assert.Single(filtered.Moves).Source.Snapshot.Path); Assert.Equal(full.ConfigurationSignature, filtered.ConfigurationSignature);
        Assert.Empty((await Planner.PreviewAsync(config, eligibleSourcePaths: [])).Moves); Assert.Equal(2, full.Moves.Count);
    }
    [Fact]
    public async Task Monitor_allowlist_rejects_paths_outside_configured_root()
    {
        Write("a"); await Assert.ThrowsAsync<ArgumentException>(() => Planner.PreviewAsync(Config(), eligibleSourcePaths: [Path.Combine(_root, "outside")]));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shared_enumerator_keeps_typed_invalid_root_refusals_with_optional_exclusions(bool exclude)
    {
        var result = new FileTreeEnumerator(new RefusingMetadata()).Enumerate(["\0invalid"], excludedRoots: exclude ? [Source] : null);
        Assert.Equal(FileRefusalKind.InvalidPath, Assert.Single(result.Issues).Refusal.Kind); Assert.Empty(result.Files);
    }
    [Fact]
    public async Task Monitor_baselines_existing_paths_and_catches_new_arrivals_after_restart()
    {
        var old = Write("old.txt");
        var state = Path.Combine(_root, "monitor.json");
        var claims = new OrdoSourceClaims();
        await using (var monitor = new OrdoMonitor(_metadata, Planner, Service(), Journal, state, claims, TimeSpan.Zero))
        {
            await monitor.EnrollAsync(Config());
            File.WriteAllText(old, "edited");
            await monitor.PollAsync();
            Assert.True(File.Exists(old));
        }
        var arrival = Write("new.txt");
        await using var resumed = new OrdoMonitor(_metadata, Planner, Service(), Journal, state, claims, TimeSpan.Zero);
        await resumed.PollAsync(); await resumed.PollAsync();
        Assert.True(File.Exists(old)); Assert.False(File.Exists(arrival));
        Assert.True(File.Exists(Path.Combine(Source, "sorted/new.txt")));
    }
    [Fact]
    public async Task Monitor_pause_revokes_approval_and_resume_baselines_paused_arrivals()
    {
        Write("old");
        await using var monitor = new OrdoMonitor(_metadata, Planner, Service(), Journal, Path.Combine(_root, "monitor.json"), new(), TimeSpan.Zero);
        await monitor.EnrollAsync(Config()); monitor.Pause("Rules changed");
        var arrival = Write("new"); await monitor.PollAsync(); Assert.True(File.Exists(arrival));
        await monitor.EnrollAsync(Config()); await monitor.PollAsync(); Assert.True(File.Exists(arrival));
    }
    [Fact]
    public void Source_claims_reject_ancestor_and_descendant_until_released()
    {
        var claims = new OrdoSourceClaims(); using var first = claims.Acquire(Source);
        Assert.Throws<InvalidOperationException>(() => claims.Acquire(Path.Combine(Source, "nested")));
        Assert.Throws<InvalidOperationException>(() => claims.Acquire(_root));
    }
    [Fact]
    public void Disposing_an_old_source_claim_twice_does_not_release_its_successor()
    {
        var claims = new OrdoSourceClaims();
        var old = claims.Acquire(Source);
        old.Dispose();
        using var current = claims.Acquire(Source);
        old.Dispose();
        Assert.Throws<InvalidOperationException>(() => claims.Acquire(Path.Combine(Source, "child")));
    }

    [Fact]
    public async Task Monitor_keeps_case_distinct_new_files_on_case_sensitive_systems()
    {
        var probe = Write("case-sensitivity-probe");
        bool caseSensitive;
        try
        {
            caseSensitive = !File.Exists(Path.Combine(Source, "CASE-SENSITIVITY-PROBE"));
        }
        finally
        {
            File.Delete(probe);
        }
        Assert.SkipUnless(caseSensitive, "The temporary source directory is case-insensitive.");
        var baseline = Write("old.txt");
        await using var monitor = new OrdoMonitor(_metadata, Planner, Service(), Journal,
            Path.Combine(_root, "monitor.json"), new(), TimeSpan.Zero);
        await monitor.EnrollAsync(Config());
        var arrival = Write("OLD.txt");
        await monitor.PollAsync();
        await monitor.PollAsync();
        Assert.True(File.Exists(baseline));
        Assert.False(File.Exists(arrival));
        Assert.True(File.Exists(Path.Combine(Source, "sorted", "OLD.txt")));
    }

    [Theory]
    [InlineData("unfinished.opdownload")]
    [InlineData("unfinished.!ut")]
    [InlineData("unfinished.filepart")]
    public async Task Monitor_leaves_incomplete_download_names_in_place(string name)
    {
        Directory.CreateDirectory(Source);
        await using var monitor = new OrdoMonitor(_metadata, Planner, Service(), Journal,
            Path.Combine(_root, "monitor.json"), new(), TimeSpan.Zero);
        await monitor.EnrollAsync(Config());
        var arrival = Write(name);
        await monitor.PollAsync();
        await monitor.PollAsync();
        Assert.True(File.Exists(arrival));
        Assert.Empty(Journal.List());
    }
    [Fact]
    public async Task Protected_app_state_is_excluded_and_cannot_be_a_destination()
    {
        Write("state/secrets.json"); var planner = new OrdoPlanner(_metadata, new(_metadata), [Path.Combine(Source, "state")]);
        Assert.Empty((await planner.PreviewAsync(Config())).Moves);
        await Assert.ThrowsAsync<ArgumentException>(() => planner.PreviewAsync(Config(new OrdoRule("r", "state"))));
    }

    [Fact]
    public void Case_distinct_source_roots_can_be_claimed_on_case_sensitive_systems()
    {
        if (OperatingSystem.IsWindows()) return;
        var claims = new OrdoSourceClaims();
        using var first = claims.Acquire(Source);
        IDisposable? second = null;
        var error = Record.Exception(() => second = claims.Acquire(Path.Combine(_root, "SOURCE")));
        second?.Dispose();
        Assert.Null(error);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Automatic_organization_cannot_feed_a_conversion_watcher_loop(bool organizerFirst, bool organizerProduces)
    {
        var claims = new OrdoSourceClaims();
        var organize = Path.Combine(_root, "organize");
        var convert = Path.Combine(_root, "convert");
        string[] organizedOutputs = organizerProduces ? [convert] : [];
        string[] conversionOutputs = organizerProduces ? [] : [organize];
        using var first = organizerFirst
            ? claims.Acquire(organize, organizedOutputs, organizer: true)
            : claims.Acquire(convert, conversionOutputs);
        Assert.Throws<InvalidOperationException>(() => organizerFirst
            ? claims.Acquire(convert, conversionOutputs)
            : claims.Acquire(organize, organizedOutputs, organizer: true));
    }

    [Fact]
    public void Existing_conversion_only_pipelines_are_not_restricted_by_ORDO_output_claims()
    {
        var claims = new OrdoSourceClaims();
        var second = Path.Combine(_root, "second");
        using var first = claims.Acquire(Source, [second]);
        IDisposable? other = null;
        var error = Record.Exception(() => other = claims.Acquire(second));
        other?.Dispose();
        Assert.Null(error);
    }

    [Fact]
    public async Task Monitor_respects_settle_time_and_restarts_the_delay_on_change()
    {
        Directory.CreateDirectory(Source);
        var clock = new MonitorClock();
        await using var monitor = new OrdoMonitor(_metadata, Planner, Service(), Journal,
            Path.Combine(_root, "monitor.json"), new(), TimeSpan.FromSeconds(5), clock);
        await monitor.EnrollAsync(Config());
        var arrival = Write("new.txt");
        await monitor.PollAsync();
        clock.Advance(TimeSpan.FromSeconds(4));
        File.WriteAllText(arrival, "still writing");
        await monitor.PollAsync();
        clock.Advance(TimeSpan.FromSeconds(4));
        await monitor.PollAsync();
        Assert.True(File.Exists(arrival));
        clock.Advance(TimeSpan.FromSeconds(1));
        await monitor.PollAsync();
        Assert.False(File.Exists(arrival));
    }

    [Fact]
    public async Task Monitor_retries_a_file_that_changes_between_preview_and_apply()
    {
        Directory.CreateDirectory(Source);
        await using var monitor = new OrdoMonitor(_metadata, Planner, Service(), Journal,
            Path.Combine(_root, "monitor.json"), new(), TimeSpan.Zero);
        await monitor.EnrollAsync(Config());
        var arrival = Write("new.txt");
        var changed = false;
        monitor.Changed += (_, _) =>
        {
            if (!changed && monitor.IsOrganizing)
            {
                changed = true;
                File.WriteAllText(arrival, "changed after preview");
            }
        };
        await monitor.PollAsync();
        await monitor.PollAsync();
        Assert.True(changed);
        Assert.True(File.Exists(arrival));
        await monitor.PollAsync();
        await monitor.PollAsync();
        Assert.False(File.Exists(arrival));
        Assert.Equal("changed after preview", File.ReadAllText(Path.Combine(Source, "sorted", "new.txt")));
    }

    [Fact]
    public async Task Monitor_skips_links_without_preventing_safe_arrivals()
    {
        var link = Write("shortcut.txt");
        _metadata.Links.Add(link);
        await using var monitor = new OrdoMonitor(_metadata, Planner, Service(), Journal,
            Path.Combine(_root, "monitor.json"), new(), TimeSpan.Zero);
        Assert.Null(await Record.ExceptionAsync(() => monitor.EnrollAsync(Config())));
        var arrival = Write("new.txt");
        await monitor.PollAsync();
        await monitor.PollAsync();
        Assert.True(File.Exists(link));
        Assert.False(File.Exists(arrival));
    }

    [Fact]
    public async Task Preview_reports_unmatched_incomplete_and_hardlinked_files_that_will_stay()
    {
        Write("document.pdf");
        var unmatched = Write("other.xyz");
        var incomplete = Write("copy.crdownload");
        var linked = Write("linked.txt"); _metadata.Hardlinks.Add(linked);
        var plan = await Planner.PreviewAsync(Config(new OrdoRule("documents", "docs", OrdoFileType.Document)));
        Assert.Single(plan.Moves);
        Assert.Equal(new[] { incomplete, linked, unmatched }.OrderBy(p => p), plan.Issues.Select(i => i.Path).OrderBy(p => p));
        Assert.All(plan.Issues, i => Assert.False(i.Retryable));
    }

    [Fact]
    public async Task Monitor_finishes_handling_unmatched_arrivals_without_repeated_retry()
    {
        Directory.CreateDirectory(Source);
        await using var monitor = new OrdoMonitor(_metadata, Planner, Service(), Journal,
            Path.Combine(_root, "monitor.json"), new(), TimeSpan.Zero);
        await monitor.EnrollAsync(Config(new OrdoRule("documents", "docs", OrdoFileType.Document)));
        var arrival = Write("unmatched.xyz");
        await monitor.PollAsync(); await monitor.PollAsync();
        Assert.Equal(0, monitor.PendingCount);
        Assert.True(File.Exists(arrival)); Assert.Empty(Journal.List());
    }

    [Fact]
    public async Task Monitor_pauses_on_source_replacement_or_unresolved_journal()
    {
        Write("existing.txt");
        await using var monitor = new OrdoMonitor(_metadata, Planner, Service(), Journal,
            Path.Combine(_root, "monitor.json"), new(), TimeSpan.Zero);
        await monitor.EnrollAsync(Config());
        _metadata.Identities[Source] = "unrelated replacement directory";
        await monitor.PollAsync();
        Assert.False(monitor.Enabled);
        await monitor.EnrollAsync(Config());
        var move = Assert.Single((await Planner.PreviewAsync(Config())).Moves);
        Journal.Save(new(1, Guid.NewGuid(), DateTimeOffset.UtcNow, "config", [new(move, OrdoEntryState.Prepared)], []));
        await monitor.PollAsync();
        Assert.False(monitor.Enabled);
        Assert.True(File.Exists(move.Source.Snapshot.Path));
    }

    [Theory]
    [InlineData("{invalid")]
    [InlineData("{\"Version\":1,\"Configuration\":null}")]
    public async Task Corrupt_monitor_state_is_preserved_and_does_not_crash_the_workspace(string content)
    {
        var state = Path.Combine(_root, "monitor.json");
        File.WriteAllText(state, content);
        OrdoMonitor? monitor = null;
        Assert.Null(Record.Exception(() => monitor = new(_metadata, Planner, Service(), Journal, state, new())));
        await using var owned = monitor!;
        Assert.Null(await Record.ExceptionAsync(owned.PollAsync));
        Assert.False(owned.Enabled);
        Assert.Equal(content, File.ReadAllText(state));
    }

    [Fact]
    public async Task Future_monitor_state_is_not_enabled_or_overwritten()
    {
        Write("existing.txt");
        var state = Path.Combine(_root, "monitor.json");
        await using (var initial = new OrdoMonitor(_metadata, Planner, Service(), Journal, state, new()))
            await initial.EnrollAsync(Config());
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(state))!;
        json["Version"] = 999;
        File.WriteAllText(state, json.ToJsonString());
        var original = File.ReadAllText(state);
        await using var monitor = new OrdoMonitor(_metadata, Planner, Service(), Journal, state, new());
        await monitor.PollAsync();
        Assert.False(monitor.Enabled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => monitor.EnrollAsync(Config()));
        Assert.Equal(original, File.ReadAllText(state));
    }

    private sealed class MonitorClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
    private sealed class RefusingMetadata : SafeFileMetadata
    {
        protected override FileMetadataResult ReadEntry(string path) => new(null, new(FileRefusalKind.Missing, path));
    }
    public void Dispose() => Directory.Delete(_root, true);
    private sealed class Mover(Action<string, string> move) : IOrdoFileMover { public void Move(string source, string destination) => move(source, destination); }
    private sealed class Metadata : IFileMetadata
    {
        private readonly Dictionary<string, string> _directories = [];
        public Dictionary<string, string> Identities { get; } = [];
        public HashSet<string> Links { get; } = [];
        public HashSet<string> Hardlinks { get; } = [];
        public Action<string>? OnRead { get; set; }
        public FileMetadataResult Read(string path)
        {
            path = Path.GetFullPath(path); OnRead?.Invoke(path);
            if (!File.Exists(path) && !Directory.Exists(path)) return new(null, new(FileRefusalKind.Missing, path));
            var dir = Directory.Exists(path);
            if (dir && !_directories.ContainsKey(path))
            {
                var moved = _directories.Keys.FirstOrDefault(p => Path.GetFileName(p).StartsWith(".azoth-ordo-", StringComparison.Ordinal) && Path.GetDirectoryName(p) == Path.GetDirectoryName(path) && !Directory.Exists(p));
                _directories[path] = moved is null ? Guid.NewGuid().ToString() : _directories[moved];
                if (moved is not null) _directories.Remove(moved);
            }
            return new(new(path, new("test", Identities.GetValueOrDefault(path, dir ? _directories[path] : path)), Hardlinks.Contains(path) ? 2ul : 1ul, dir ? 0 : new FileInfo(path).Length, File.GetLastWriteTimeUtc(path), Links.Contains(path) ? FileEntryKind.Link : dir ? FileEntryKind.Directory : FileEntryKind.RegularFile), null);
        }
    }
}
