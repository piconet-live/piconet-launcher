using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Launcher.Core;

namespace Launcher.App;

/// <summary>
/// The launcher's button on the Windows taskbar. Windows puts windows under one button when they carry one
/// application id (<see cref="Branding.AppId"/>): the launcher takes it for its process before it makes a window
/// (<see cref="Claim"/>), says it on its window too (<see cref="Mark"/>), and hands it to every game it starts
/// (<see cref="Group"/>, the launch file's <c>"taskbar"</c>), whose window then stands under the launcher's button
/// as a second window of one program. Without the id each program file has a button of its own. On another system
/// than Windows nothing here does anything.
/// </summary>
internal static partial class Taskbar
{
    private const ushort Text = 31;   // VT_LPWSTR

    /// <summary>The window property "System.AppUserModel.ID".</summary>
    private static readonly PropertyKey AppIdKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    /// <summary>What this launcher gives its games; null until <see cref="Claim"/> took the id: in a mode without
    /// a window, on another system, when Windows refused, and when the process is .NET's own host (a development
    /// start, <c>dotnet Launcher.App.dll</c>: a pinned button would start that). The program file is the running
    /// process's own, whatever it is called and wherever it is: nothing here names it.</summary>
    public static TaskbarGroup? Group { get; private set; }

    /// <summary>Takes the application id for this process. Before the first window is made: Windows reads it when
    /// it makes the window's button.</summary>
    public static void Claim()
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } program ||
            string.Equals(Path.GetFileNameWithoutExtension(program), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (SetCurrentProcessExplicitAppUserModelID(Branding.AppId) == 0)
        {
            Group = new TaskbarGroup(Branding.AppId, program, Branding.AppName);
        }
    }

    /// <summary>Says the id on a window of the launcher's as well. The button needs only the process's; the
    /// window's is the one another program can read, which is how a check sees that the launcher's window and a
    /// game's carry the same.</summary>
    public static void Mark(nint window)
    {
        if (Group is null || window == 0 || SHGetPropertyStoreForWindow(window, typeof(IPropertyStore).GUID, out var store) != 0 || store is null)
        {
            return;
        }
        var value = new PropertyValue { Kind = Text, Pointer = Marshal.StringToCoTaskMemUni(Group.Id) };
        try
        {
            _ = store.SetValue(AppIdKey, value);
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.Pointer);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string id);

    [LibraryImport("shell32.dll")]
    private static partial int SHGetPropertyStoreForWindow(nint window, in Guid kind, out IPropertyStore? store);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct PropertyKey(Guid format, uint id)
    {
        public readonly Guid Format = format;
        public readonly uint Id = id;
    }

    /// <summary>Windows' PROPVARIANT, as far as a text needs it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyValue
    {
        public ushort Kind;
        public ushort Reserved1, Reserved2, Reserved3;
        public nint Pointer;
        public nint More;
    }

    /// <summary>A window's property store (Windows' IPropertyStore).</summary>
    [GeneratedComInterface]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    internal partial interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int GetAt(uint index, out PropertyKey key);

        [PreserveSig]
        int GetValue(in PropertyKey key, out PropertyValue value);

        [PreserveSig]
        int SetValue(in PropertyKey key, in PropertyValue value);

        [PreserveSig]
        int Commit();
    }
}
