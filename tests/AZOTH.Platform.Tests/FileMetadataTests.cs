using System.Runtime.InteropServices;
using AZOTH.Core.Echo;
using AZOTH.Core.Files;
using AZOTH.Platform.Linux;
using AZOTH.Platform.MacOS;
using AZOTH.Platform.Windows;

namespace AZOTH.Platform.Tests;

public sealed class FileMetadataTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("azoth-metadata-").FullName;
    private static IFileMetadata Native() => OperatingSystem.IsLinux() ? new LinuxFileMetadata()
        : OperatingSystem.IsWindows() ? new WindowsFileMetadata()
        : OperatingSystem.IsMacOS() ? new MacOSFileMetadata() : new UnsupportedFileMetadata();

    [Fact]
    public void Native_identity_is_stable_distinguishes_files_and_reads_directory_identity()
    {
        var a = Path.Combine(_root, "a"); var b = Path.Combine(_root, "b");
        File.WriteAllText(a, "abc"); File.WriteAllText(b, "abc");
        var metadata = Native(); var first = metadata.Read(a);
        Assert.Null(first.Refusal); Assert.NotNull(first.Snapshot?.Identity);
        Assert.Equal(first.Snapshot, metadata.Read(a).Snapshot);
        Assert.NotEqual(first.Snapshot!.Identity, metadata.Read(b).Snapshot!.Identity);
        Assert.Equal(3, first.Snapshot.Length); Assert.Equal(1ul, first.Snapshot.LinkCount);
        Assert.Equal(FileEntryKind.Directory, metadata.Read(_root).Snapshot?.Kind);
        Assert.NotNull(metadata.Read(_root).Snapshot?.Identity);
        Assert.Equal(FileRefusalKind.Missing, metadata.Read(Path.Combine(_root, "absent")).Refusal?.Kind);
    }

    [Fact]
    public async Task Native_links_are_not_followed_and_hardlink_aliases_consolidate()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Skip("Local Linux integration; native Windows/macOS still require target runners."); return; }
        var a = Path.Combine(_root, "a"); var alias = Path.Combine(_root, "alias"); var b = Path.Combine(_root, "b");
        File.WriteAllText(a, "abc"); File.WriteAllText(b, "abc");
        Assert.Equal(0, link(a, alias));
        var metadata = Native();
        Assert.Equal(metadata.Read(a).Snapshot!.Identity, metadata.Read(alias).Snapshot!.Identity);
        Assert.Equal(2ul, metadata.Read(a).Snapshot!.LinkCount);
        File.CreateSymbolicLink(Path.Combine(_root, "file-link"), a);
        Directory.CreateSymbolicLink(Path.Combine(_root, "cycle"), _root);
        Assert.Equal(FileEntryKind.Link, metadata.Read(Path.Combine(_root, "file-link")).Snapshot?.Kind);
        Assert.Equal(FileRefusalKind.Link, metadata.Read(Path.Combine(_root, "cycle", "a")).Refusal?.Kind);
        var echo = new EchoService(metadata, new(metadata), new());
        var scan = await echo.ScanAsync([_root, Path.Combine(_root, "cycle")], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, Assert.Single(scan.Groups).Files.Count);
        Assert.Equal(3, scan.EstimatedSavingsBytes);
        var result = await echo.CleanupAsync(scan, [a], (_, _) => throw new Exception("Do not trash hardlinks"), TestContext.Current.CancellationToken);
        Assert.Equal(EchoCleanupStatus.Refused, Assert.Single(result.Outcomes).Status);
    }

    [Fact]
    public async Task Replacing_a_parent_with_a_symlink_refuses_fingerprint_and_cleanup()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Skip("Symlink integration requires Linux runner."); return; }
        var folder = Directory.CreateDirectory(Path.Combine(_root, "folder")).FullName;
        var a = Path.Combine(folder, "a"); File.WriteAllText(a, "abc"); File.WriteAllText(Path.Combine(_root, "b"), "abc");
        var metadata = Native(); var fingerprints = new FileFingerprintService(metadata); var echo = new EchoService(metadata, fingerprints, new());
        var scan = await echo.ScanAsync([_root], cancellationToken: TestContext.Current.CancellationToken);
        var renamed = Path.Combine(_root, "renamed"); Directory.Move(folder, renamed); Directory.CreateSymbolicLink(folder, renamed);
        Assert.Equal(FileRefusalKind.Link, (await fingerprints.ComputeAsync(a, TestContext.Current.CancellationToken)).Refusal?.Kind);
        var result = await echo.CleanupAsync(scan, [a], (_, _) => throw new Exception("Linked ancestor"), TestContext.Current.CancellationToken);
        Assert.Equal(EchoCleanupStatus.Refused, Assert.Single(result.Outcomes).Status);
    }

    [DllImport("libc", SetLastError = true)] private static extern int link(string oldpath, string newpath);
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
