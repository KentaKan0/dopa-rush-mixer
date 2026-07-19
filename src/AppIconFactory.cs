using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DopaRushMixer;

internal static class AppIconFactory
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;

    internal static ImageSource? GetForProcess(uint processId)
    {
        if (processId == 0) return null;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var executablePath = process.MainModule?.FileName;
            return string.IsNullOrWhiteSpace(executablePath) ? null : GetForFile(executablePath);
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    private static ImageSource? GetForFile(string executablePath)
    {
        var result = SHGetFileInfo(executablePath, 0, out var info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiSmallIcon);
        if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero) return null;

        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(info.IconHandle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(16, 16));
            image.Freeze();
            return image;
        }
        finally
        {
            DestroyIcon(info.IconHandle);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, out ShFileInfo fileInfo, uint fileInfoSize, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        internal IntPtr IconHandle;
        internal int IconIndex;
        internal uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] internal string TypeName;
    }
}
