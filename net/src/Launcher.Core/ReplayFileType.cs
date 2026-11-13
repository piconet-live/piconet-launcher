using System.Runtime.Versioning;

namespace Launcher.Core;

/// <summary>The player's own file types (<c>HKEY_CURRENT_USER\Software\Classes</c>), as far as the replay's needs
/// them: a key is a path under that key, a value's name "" its default value. The launcher's is
/// <see cref="WindowsClasses"/>; a test's holds them in memory, so that no test writes the registry of the PC it
/// runs on.</summary>
public interface IClassesStore
{
    /// <summary>The text value, or null: no such key or value, or not text.</summary>
    string? Read(string key, string name);

    /// <summary>The text value written, its key made if it was not there.</summary>
    void Write(string key, string name, string value);

    /// <summary>The value gone (nothing when it was not there).</summary>
    void DeleteValue(string key, string name);

    /// <summary>The key and everything under it gone (nothing when it was not there).</summary>
    void DeleteTree(string key);

    /// <summary>The key gone when it holds no value and no key (nothing otherwise).</summary>
    void DeleteIfEmpty(string key);
}

/// <summary>One value of a file type's registration.</summary>
public sealed record ClassValue(string Key, string Name, string Value);

/// <summary>
/// The replay's file type: a <c>.fzrp</c> file opens with the launcher (a double-click in Explorer plays it), shown
/// as the type's name with the launcher's icon. It is the player's own registration (no administrator), and one
/// launcher owns it at a time: the program its open command names.
/// <para>Who owns it (<see cref="Takes"/>): an **installed** launcher whenever it is there (Velopack's Setup and
/// each update register it, its uninstall takes it away: <see cref="Release"/>); else the **portable** launcher or
/// player copy started last, which takes it at its start when nobody has it, when its owner's program has gone (the
/// copy moved or deleted) or when its owner is another portable copy. A development copy (no marker,
/// <see cref="Install"/>) never takes it.</para>
/// </summary>
public static class ReplayFileType
{
    public const string Extension = ".fzrp";

    /// <summary>The type's id, which <see cref="Extension"/> names.</summary>
    public const string ProgId = "Piconet.Replay";

    public const string OpenWithKey = Extension + @"\OpenWithProgids";
    public const string IconKey = ProgId + @"\DefaultIcon";
    public const string CommandKey = ProgId + @"\shell\open\command";

    /// <summary>Every value of the registration of <paramref name="program"/> (the launcher's own program file, full
    /// path), the type shown as <paramref name="typeName"/>: the extension's type, the type's name, icon (the
    /// program's own) and open command (the file as the launcher's one argument).</summary>
    public static IReadOnlyList<ClassValue> Values(string program, string typeName) =>
    [
        new(Extension, "", ProgId),
        new(OpenWithKey, ProgId, ""),
        new(ProgId, "", typeName),
        new(IconKey, "", $"\"{program}\",0"),
        new(CommandKey, "", $"\"{program}\" \"%1\""),
    ];

    /// <summary>The program the type's open command names now; null: none (not registered, or a command that names
    /// no program).</summary>
    public static string? Owner(IClassesStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var command = store.Read(CommandKey, "")?.Trim();
        if (string.IsNullOrEmpty(command))
        {
            return null;
        }
        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        var space = command.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? command : command[..space];
    }

    /// <summary>Whether the launcher <paramref name="install"/>, whose program is <paramref name="program"/>, owns
    /// the type now that <paramref name="owner"/> does (<see cref="Owner"/>): an installed launcher always; a
    /// portable one unless the owner is an installed launcher that is there (<paramref name="installedLauncher"/>:
    /// that program file is there and is an installed launcher's); a development copy never.</summary>
    public static bool Takes(Install install, string program, string? owner, Func<string, bool> installedLauncher)
    {
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(installedLauncher);
        if (install.Installed)
        {
            return true;
        }
        if (!install.Portable)
        {
            return false;
        }
        return owner is null || Same(owner, program) || !installedLauncher(owner);
    }

    /// <summary>The program file <paramref name="program"/> is there and is an installed launcher's (its folder has
    /// the installed marker and is no portable zip's: <see cref="Install.Of"/>).</summary>
    public static bool IsInstalledLauncher(string program) =>
        File.Exists(program) && Path.GetDirectoryName(Path.GetFullPath(program)) is { Length: > 0 } folder && Install.Of(folder).Installed;

    /// <summary>The type registered for <paramref name="program"/> when it takes it (<see cref="Takes"/>): only the
    /// values that differ are written. True when something was written (the shell is then told,
    /// <c>SHChangeNotify</c>); false: it does not take the type, or it was registered as it is already.</summary>
    public static bool Claim(IClassesStore store, Install install, string program, string typeName, Func<string, bool>? installedLauncher = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!Takes(install, program, Owner(store), installedLauncher ?? IsInstalledLauncher))
        {
            return false;
        }
        var written = false;
        foreach (var value in Values(program, typeName))
        {
            if (store.Read(value.Key, value.Name) != value.Value)
            {
                store.Write(value.Key, value.Name, value.Value);
                written = true;
            }
        }
        return written;
    }

    /// <summary>The registration taken away when it is <paramref name="program"/>'s (an installed launcher's
    /// uninstall): the type, and the extension's link to it. Another launcher's registration is left. True when
    /// something was taken away.</summary>
    public static bool Release(IClassesStore store, string program)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (Owner(store) is not { } owner || !Same(owner, program))
        {
            return false;
        }
        store.DeleteTree(ProgId);
        if (store.Read(Extension, "") == ProgId)
        {
            store.DeleteValue(Extension, "");
        }
        store.DeleteValue(OpenWithKey, ProgId);
        store.DeleteIfEmpty(OpenWithKey);
        store.DeleteIfEmpty(Extension);
        return true;
    }

    /// <summary>The same program file (Windows' paths: case does not count).</summary>
    public static bool Same(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>The player's file types in the registry (<c>HKEY_CURRENT_USER\Software\Classes</c>): the launcher's
/// <see cref="IClassesStore"/>. Never used by a test.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsClasses : IClassesStore
{
    private const string Root = @"Software\Classes\";

    public string? Read(string key, string name)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Root + key);
        return k?.GetValue(name, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void Write(string key, string name, string value)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Root + key, writable: true);
        k.SetValue(name, value, Microsoft.Win32.RegistryValueKind.String);
    }

    public void DeleteValue(string key, string name)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Root + key, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void DeleteTree(string key) => Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(Root + key, throwOnMissingSubKey: false);

    public void DeleteIfEmpty(string key)
    {
        bool empty;
        using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Root + key))
        {
            empty = k is { SubKeyCount: 0, ValueCount: 0 };
        }
        if (empty)
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(Root + key, throwOnMissingSubKey: false);
        }
    }
}
