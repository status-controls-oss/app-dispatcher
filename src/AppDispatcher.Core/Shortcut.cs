using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace AppDispatcher.Core;

/// <summary>
/// The per-user Start menu entry. Only Settings may use this: it relies on COM interop, which
/// Native AOT doesn't support, so it can stay out of the picker only while nothing there calls it.
/// </summary>
public static class Shortcut
{
    /// <summary>Named for Settings, which also makes it the match for a search on "App Dispatcher".</summary>
    public static string StartMenuPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "App Dispatcher Settings.lnk");

    /// <summary>Creates or replaces a shortcut.</summary>
    public static void Create(string path, string target, string description)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(target);
        link.SetWorkingDirectory(Path.GetDirectoryName(target) ?? "");
        link.SetDescription(description);
        ((IPersistFile)link).Save(path, true);
    }

    public static void Delete(string path) => File.Delete(path);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink;

    // COM calls by position, so the unused methods must stay, in this order.
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        void GetPath();
        void GetIDList();
        void SetIDList();
        void GetDescription();
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory();
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments();
        void SetArguments();
        void GetHotkey();
        void SetHotkey();
        void GetShowCmd();
        void SetShowCmd();
        void GetIconLocation();
        void SetIconLocation();
        void SetRelativePath();
        void Resolve();
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
