using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AZOTH.Core.Files;
using Microsoft.Win32.SafeHandles;

namespace AZOTH.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsFileMetadata : SafeFileMetadata
{
    // Documented FILE_ID_INFO (volume + 128-bit ID), FILE_STANDARD_INFO and FILE_BASIC_INFO.
    // https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getfileinformationbyhandleex
    protected override FileMetadataResult ReadEntry(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)
            || path.AsSpan(Path.GetPathRoot(path)!.Length).Contains(':'))
            return new(null, new(FileRefusalKind.Unsupported, "Device namespaces and alternate data streams are excluded."));
        using var handle = CreateFileW(path, 0x80, 7, 0, 3, 0x02200000, 0); // READ_ATTRIBUTES, all sharing, OPEN_EXISTING, BACKUP_SEMANTICS | OPEN_REPARSE_POINT
        if (handle.IsInvalid) return Error(path, identityQuery: false);
        if (GetFileType(handle) != 1) return new(null, new(FileRefusalKind.NotRegularFile, path));
        if (!ReadBasic(handle, 0, out var basic, 40) || !ReadStandard(handle, 1, out var standard, 24)) return Error(path, identityQuery: false);
        if ((basic.Attributes & 0x400) != 0)
            return new(new(path, null, standard.Links, standard.Length, DateTimeOffset.MinValue, FileEntryKind.Link), null);
        if (!ReadId(handle, 18, out var id, 24)) return Error(path, identityQuery: true);
        if (standard.DeletePending != 0 || standard.Length < 0)
            return new(null, new(FileRefusalKind.Changed, "File is pending deletion or has invalid metadata."));
        try
        {
            return new(new(path, new($"windows:{id.Volume:X16}", $"{id.IdLow:X16}{id.IdHigh:X16}"), standard.Links,
                standard.Length, new DateTimeOffset(DateTime.FromFileTimeUtc(basic.LastWrite)),
                standard.Directory != 0 ? FileEntryKind.Directory : FileEntryKind.RegularFile, basic.Change.ToString("X16")), null);
        }
        catch (ArgumentOutOfRangeException ex) { return new(null, new(FileRefusalKind.IdentityUnavailable, ex.Message)); }
    }
    private static FileMetadataResult Error(string path, bool identityQuery)
    {
        var error = Marshal.GetLastPInvokeError();
        return new(null, new(error switch
        {
            2 or 3 => FileRefusalKind.Missing,
            5 => FileRefusalKind.AccessDenied,
            _ when identityQuery => FileRefusalKind.IdentityUnavailable,
            _ => FileRefusalKind.IoError
        }, $"Windows metadata query failed ({error}): {path}"));
    }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct IdInfo
    { [FieldOffset(0)] public ulong Volume; [FieldOffset(8)] public ulong IdLow; [FieldOffset(16)] public ulong IdHigh; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct StandardInfo
    { [FieldOffset(8)] public long Length; [FieldOffset(16)] public uint Links; [FieldOffset(20)] public byte DeletePending; [FieldOffset(21)] public byte Directory; }
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct BasicInfo
    { [FieldOffset(16)] public long LastWrite; [FieldOffset(24)] public long Change; [FieldOffset(32)] public uint Attributes; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(SafeFileHandle file);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReadId(SafeFileHandle file, int infoClass, out IdInfo info, uint size);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReadStandard(SafeFileHandle file, int infoClass, out StandardInfo info, uint size);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReadBasic(SafeFileHandle file, int infoClass, out BasicInfo info, uint size);
}
