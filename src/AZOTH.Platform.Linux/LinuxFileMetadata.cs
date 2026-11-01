using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AZOTH.Core.Files;

namespace AZOTH.Platform.Linux;

[SupportedOSPlatform("linux")]
public sealed class LinuxFileMetadata : SafeFileMetadata
{
    // Linux UAPI include/linux/stat.h: fixed 256-byte statx ABI, independent of libc stat layouts.
    // https://man7.org/linux/man-pages/man2/statx.2.html
    private const uint Required = 0x3c5; // TYPE | NLINK | MTIME | CTIME | INO | SIZE
    protected override FileMetadataResult ReadEntry(string path)
    {
        try
        {
            if (Statx(-100, path, 0x100, Required, out var stat) != 0) // AT_FDCWD, AT_SYMLINK_NOFOLLOW
            {
                var error = Marshal.GetLastPInvokeError();
                return new(null, new(error switch
                {
                    2 or 20 => FileRefusalKind.Missing,
                    1 or 13 => FileRefusalKind.AccessDenied,
                    38 or 95 => FileRefusalKind.IdentityUnavailable,
                    40 => FileRefusalKind.Link,
                    _ => FileRefusalKind.IoError
                }, $"statx failed ({error}): {path}"));
            }
            if ((stat.Mask & Required) != Required || stat.Size > long.MaxValue)
                return new(null, new(FileRefusalKind.IdentityUnavailable, "Filesystem did not provide all required statx fields."));
            var kind = (stat.Mode & 0xf000) switch { 0x8000 => FileEntryKind.RegularFile, 0x4000 => FileEntryKind.Directory, 0xa000 => FileEntryKind.Link, _ => FileEntryKind.Other };
            var modified = DateTimeOffset.FromUnixTimeSeconds(stat.ModifiedSeconds).AddTicks(stat.ModifiedNanoseconds / 100);
            return new(new(path, new($"linux:{stat.DeviceMajor}:{stat.DeviceMinor}", stat.Inode.ToString("X16")), stat.LinkCount,
                (long)stat.Size, modified, kind, $"{stat.ChangeSeconds}:{stat.ChangeNanoseconds}:{stat.ModifiedNanoseconds}"), null);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or ArgumentOutOfRangeException)
        { return new(null, new(FileRefusalKind.IdentityUnavailable, ex.Message)); }
    }

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int dirfd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out StatxBuffer result);
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct StatxBuffer
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint LinkCount;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(40)] public ulong Size;
        [FieldOffset(96)] public long ChangeSeconds;
        [FieldOffset(104)] public uint ChangeNanoseconds;
        [FieldOffset(112)] public long ModifiedSeconds;
        [FieldOffset(120)] public uint ModifiedNanoseconds;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }
}
