using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AZOTH.Core.Files;

namespace AZOTH.Platform.MacOS;

[SupportedOSPlatform("macos")]
public sealed class MacOSFileMetadata : SafeFileMetadata
{
    // XNU bsd/sys/stat.h __DARWIN_STRUCT_STAT64: LP64 struct is 144 bytes on x64 and arm64.
    // x64 uses the INODE64 alias; arm64 has only the 64-bit inode ABI and uses the plain symbol.
    // https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/stat.h
    protected override FileMetadataResult ReadEntry(string path)
    {
        try
        {
            var arch = RuntimeInformation.ProcessArchitecture;
            if (arch is not (Architecture.X64 or Architecture.Arm64))
                return new(null, new(FileRefusalKind.Unsupported, "macOS metadata requires the documented LP64 ABI."));
            DarwinStat stat;
            var result = arch == Architecture.X64 ? LStatInode64(path, out stat) : LStat(path, out stat);
            if (result != 0)
            {
                var error = Marshal.GetLastPInvokeError();
                return new(null, new(error switch
                {
                    2 or 20 => FileRefusalKind.Missing,
                    1 or 13 => FileRefusalKind.AccessDenied,
                    62 => FileRefusalKind.Link,
                    _ => FileRefusalKind.IoError
                }, $"lstat failed ({error}): {path}"));
            }
            if (stat.Size < 0) return new(null, new(FileRefusalKind.IdentityUnavailable, "Invalid native file size."));
            var kind = (stat.Mode & 0xf000) switch { 0x8000 => FileEntryKind.RegularFile, 0x4000 => FileEntryKind.Directory, 0xa000 => FileEntryKind.Link, _ => FileEntryKind.Other };
            return new(new(path, new($"macos:{stat.Device:X8}", stat.Inode.ToString("X16")), stat.LinkCount, stat.Size,
                DateTimeOffset.FromUnixTimeSeconds(stat.ModifiedSeconds).AddTicks(stat.ModifiedNanoseconds / 100), kind,
                $"{stat.ChangeSeconds}:{stat.ChangeNanoseconds}:{stat.ModifiedNanoseconds}:{stat.Generation}"), null);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or ArgumentOutOfRangeException)
        { return new(null, new(FileRefusalKind.IdentityUnavailable, ex.Message)); }
    }
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "lstat$INODE64", SetLastError = true)]
    private static extern int LStatInode64([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out DarwinStat result);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "lstat", SetLastError = true)]
    private static extern int LStat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out DarwinStat result);
    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct DarwinStat
    {
        [FieldOffset(0)] public int Device;
        [FieldOffset(4)] public ushort Mode;
        [FieldOffset(6)] public ushort LinkCount;
        [FieldOffset(8)] public ulong Inode;
        [FieldOffset(48)] public long ModifiedSeconds;
        [FieldOffset(56)] public long ModifiedNanoseconds;
        [FieldOffset(64)] public long ChangeSeconds;
        [FieldOffset(72)] public long ChangeNanoseconds;
        [FieldOffset(96)] public long Size;
        [FieldOffset(120)] public uint Generation;
    }
}
