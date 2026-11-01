using AZOTH.Core.Files;
using AZOTH.Core.Ordo;
using AZOTH.Platform.Linux;
#pragma warning disable xUnit1051
namespace AZOTH.Platform.Tests;

public sealed class OrdoNativeTests
{
    [Fact]
    public async Task Linux_cross_volume_move_records_new_identity_and_undo_restores_content()
    {
        if (!OperatingSystem.IsLinux()) return;
        // Explicit opt-in: the escalated verification command sets this for its owned /dev/shm fixture.
        if (Environment.GetEnvironmentVariable("AZOTH_TEST_CROSS_VOLUME") != "1") return;
        var root=Directory.CreateTempSubdirectory("azoth-ordo-native-").FullName;
        var target=Path.Combine("/dev/shm","azoth-ordo-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(target);
        try
        {
            var source=Path.Combine(root,"source");Directory.CreateDirectory(source);var file=Path.Combine(source,"a.txt");await File.WriteAllTextAsync(file,"native cross-volume content");
            var metadata=new LinuxFileMetadata();var fingerprint=new FileFingerprintService(metadata);var planner=new OrdoPlanner(metadata,fingerprint);
            var config=new OrdoConfiguration(source,[new("r",target)]);var plan=await planner.PreviewAsync(config);var journal=new OrdoJournal(Path.Combine(root,"journal"));
            Assert.NotEqual(metadata.Read(source).Snapshot!.Identity!.Volume,metadata.Read(target).Snapshot!.Identity!.Volume);
            var service=new OrdoService(metadata,fingerprint,new(),journal);var op=await service.ApplyAsync(plan,config);var entry=Assert.Single(op.Entries);
            Assert.Equal(OrdoEntryState.Completed,entry.State);Assert.NotEqual(entry.Move.Source.Snapshot.Identity,entry.DestinationFingerprint!.Snapshot.Identity);Assert.False(File.Exists(file));
            var restart=new OrdoService(metadata,fingerprint,new(),new OrdoJournal(Path.Combine(root,"journal")));
            Assert.Equal(OrdoEntryState.Undone,(await restart.UndoAsync(op.Id)).Entries[0].State);Assert.Equal("native cross-volume content",await File.ReadAllTextAsync(file));
        }
        finally { Directory.Delete(root,true);Directory.Delete(target,true); }
    }
    [Fact]
    public async Task Linux_destination_parent_replaced_by_symlink_is_refused()
    {
        if(!OperatingSystem.IsLinux())return;
        var root=Directory.CreateTempSubdirectory("azoth-ordo-native-").FullName;
        try
        {
            var source=Directory.CreateDirectory(Path.Combine(root,"source")).FullName;var output=Directory.CreateDirectory(Path.Combine(source,"sorted")).FullName;
            var outside=Directory.CreateDirectory(Path.Combine(root,"outside")).FullName;var file=Path.Combine(source,"a.txt");File.WriteAllText(file,"content");
            var metadata=new LinuxFileMetadata();var fingerprint=new FileFingerprintService(metadata);var config=new OrdoConfiguration(source,[new("r","sorted")]);var plan=await new OrdoPlanner(metadata,fingerprint).PreviewAsync(config);
            Directory.Delete(output);Directory.CreateSymbolicLink(output,outside);
            var service=new OrdoService(metadata,fingerprint,new(),new OrdoJournal(Path.Combine(root,"journal")));var result=await service.ApplyAsync(plan,config);
            Assert.Equal(OrdoEntryState.Skipped,result.Entries[0].State);Assert.True(File.Exists(file));Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally{Directory.Delete(root,true);}
    }
}
