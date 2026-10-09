using System.Runtime.InteropServices;

namespace Tyrant.Core.Mods;

/// <summary>Where deleted mods and skin files go, so a mistake can be restored.</summary>
public interface IRecycleBin
{
    /// <summary>Moves a file or folder to the bin; throws IOException when it cannot (in use, or no bin on that drive).</summary>
    void Recycle(string path);
}

public static class RecycleBin
{
    /// <summary>The bin every deletion uses; test assemblies replace it so tests never fill the real one.</summary>
    public static IRecycleBin Current { get; set; } = new ShellRecycleBin();
}

/// <summary>The Windows Recycle Bin, through the shell; never a permanent delete without the shell warning first.</summary>
public sealed class ShellRecycleBin : IRecycleBin
{
    public void Recycle(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full) && !Directory.Exists(full)) throw new IOException($"'{full}' does not exist.");
        // Network and removable drives have no Recycle Bin: the shell would delete for good.
        if (full.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(full)!).DriveType != DriveType.Fixed)
            throw new IOException($"'{full}' is on a drive without a Recycle Bin; move it to a local drive or delete it yourself.");
        var op = new ShFileOpStruct
        {
            wFunc = FoDelete,
            pFrom = full + "\0", // the list ends with two NULs; marshalling adds the second
            fFlags = FofAllowUndo | FofNoConfirmation | FofNoErrorUi | FofSilent | FofWantNukeWarning,
        };
        var result = SHFileOperation(ref op);
        if (result != 0 || op.fAnyOperationsAborted != 0)
            throw new IOException($"'{full}' could not go to the Recycle Bin (shell code {result}); is a file open in another program?");
    }

    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x4, FofNoConfirmation = 0x10, FofAllowUndo = 0x40, FofNoErrorUi = 0x400, FofWantNukeWarning = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct op);
}
