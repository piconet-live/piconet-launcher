using System.Text.Json;
using System.Text.Json.Serialization;

namespace Launcher.Core;

/// <summary>How the stored settings were when they were last read.</summary>
public enum SettingsRead
{
    /// <summary>Read as stored, or there were none yet.</summary>
    Fine,

    /// <summary>Read, with some of them not sound: those are back to their defaults (<see cref="SettingsCheck"/>).</summary>
    Repaired,

    /// <summary>Not read (not JSON, not settings, too long, or the file could not be opened): all defaults, and no
    /// server (<see cref="LauncherSettings.NoServer"/>).</summary>
    Unreadable,
}

/// <summary>
/// Reads and writes the settings as JSON in one file. The file is the player's, on their disk: whatever is in it,
/// <see cref="Load"/> gives settings the launcher can run on (the defaults where the file gives none) and never
/// throws; <see cref="LastRead"/> and <see cref="Notice"/> say when it had to, for the launcher to tell the player
/// once. A file that was not read as it stood is kept beside the settings (".damaged", then ".damaged.2" and so on:
/// a copy is never written over) before the launcher writes its own over it; **without that copy the file is not
/// written over** (<see cref="LeftAlone"/>): a file that could not be opened (another program holds it, a moment
/// of the disk's) is as likely whole as not, and the launcher's defaults over it would cost the player everything
/// they had set.
/// </summary>
public sealed class SettingsStore(string path)
{
    /// <summary>The longest settings file read (the launcher's own is a few kilobytes).</summary>
    public const long MostBytes = 1 << 20;

    public const string UnreadableNotice =
        "Settings unreadable · defaults · " + ChooseServer;

    public const string RepairedNotice = "Some settings unreadable · back to defaults";

    /// <summary>With <see cref="RepairedNotice"/> when the server's address was among them.</summary>
    public const string ServerLostNotice = ChooseServer;

    /// <summary>How a player with no server (<see cref="LauncherSettings.NoServer"/>) gets one.</summary>
    public const string ChooseServer = "No server · choose one in Settings";

    public const string NotSavedNotice = "Settings not saved · changes last until you close";

    /// <summary>The file is there and could not be opened (<see cref="OpenTries"/> times).</summary>
    public const string NotOpenedNotice =
        "Settings file not opened · defaults, no server; changes last until you close";

    /// <summary>After the notice of a file that was not read as it stood, when no copy of it could be kept.</summary>
    public const string LeftAloneNotice = "no copy kept · file left as it is until you close";

    /// <summary>How often a file that is there is tried before it counts as not opened, <see cref="OpenWait"/> apart
    /// (a virus scanner or a backup holds a file for a moment).</summary>
    public const int OpenTries = 5;

    public static TimeSpan OpenWait { get; } = TimeSpan.FromMilliseconds(100);

    /// <summary>The copies kept of files that were not read as they stood (<see cref="DamagedPath"/> and its
    /// numbered followers); with all of them there, no further copy is made (<see cref="CopiesFull"/>). The oldest
    /// is not dropped to make room: the first copy is the likeliest to hold what the player had set, the later ones
    /// the launcher's own defaults damaged again.</summary>
    public const int MostDamaged = 9;

    /// <summary>The notice of a file that was not read as it stood when every place for a copy is taken: what is
    /// the matter and what ends it, with the folder by its name (the launcher's window offers to open it). Short:
    /// it is the whole notice then, and stands in the left column at every start until the player has acted.</summary>
    public string CopiesFullNotice =>
        $"Settings unreadable · {MostDamaged} damaged copies kept, nothing saved · delete old .damaged files in {System.IO.Path.GetFileName(Folder)}";

    /// <summary>The folder the settings and their kept copies are in.</summary>
    public string Folder => System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) ?? ".";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new ChoiceConverter() },
    };

    public string Path { get; } = path;

    private bool _serverLost;
    private bool _notOpened;

    /// <summary>How the last <see cref="Load"/> found the file.</summary>
    public SettingsRead LastRead { get; private set; }

    /// <summary>What to tell the player about the last <see cref="Load"/>; null when there is nothing to tell.</summary>
    public string? Notice => LastRead switch
    {
        _ when _notOpened => NotOpenedNotice,
        _ when CopiesFull => CopiesFullNotice,
        SettingsRead.Unreadable => UnreadableNotice + NoCopy,
        SettingsRead.Repaired => (_serverLost ? RepairedNotice + " · " + ServerLostNotice : RepairedNotice) + NoCopy,
        _ => null,
    };

    /// <summary>The file was there at the last <see cref="Load"/>, was not read as it stood, and no copy of it was
    /// kept (it could not be opened, or the copy could not be made): it is not written over (<see cref="Save"/>
    /// refuses, <see cref="TrySave"/> says false) until a later <see cref="Load"/>.</summary>
    public bool LeftAlone { get; private set; }

    /// <summary>The file is left alone because every place for a copy is taken (<see cref="MostDamaged"/>): it
    /// stays so at every start until the player removes copies, and the notice says that.</summary>
    public bool CopiesFull { get; private set; }

    /// <summary>What the notice adds when the file is left alone for want of a copy.</summary>
    private string NoCopy => LeftAlone ? " · " + LeftAloneNotice : "";

    /// <summary>Where the first file that was not read as it stood is kept; the next ones beside it, numbered
    /// (<see cref="KeptAt"/>).</summary>
    public string DamagedPath => Path + ".damaged";

    /// <summary>Where the last <see cref="Load"/> kept the file it did not read as it stood; null: it kept none.</summary>
    public string? KeptAt { get; private set; }

    /// <summary>The stored settings, sound (<see cref="SettingsCheck.Sound"/>); the defaults when there are none or
    /// they cannot be read. Settings that cannot be read, or whose server's address cannot, come with no server
    /// (<see cref="LauncherSettings.NoServer"/>): the defaults' own server is the official one, and that is the
    /// player's to choose, not the launcher's to fall back to. No file at all is a first start: the defaults as they
    /// are. A file that is there and cannot be opened is tried <see cref="OpenTries"/> times. It does not throw.</summary>
    public LauncherSettings Load()
    {
        LastRead = SettingsRead.Fine;
        _serverLost = false;
        _notOpened = false;
        LeftAlone = false;
        CopiesFull = false;
        KeptAt = null;
        if (!File.Exists(Path))
        {
            return new LauncherSettings();
        }
        byte[]? bytes = null;
        for (var attempt = 1; bytes is null; attempt++)
        {
            try
            {
                bytes = ReadBounded();
            }
            catch (InvalidDataException)
            {
                break;   // longer than settings are: read, and no settings
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException
                                          or NotSupportedException or ArgumentException)
            {
                if (!File.Exists(Path))
                {
                    return new LauncherSettings();   // gone meanwhile: as a first start
                }
                if (attempt < OpenTries)
                {
                    Thread.Sleep(OpenWait);
                    continue;
                }
                // not opened: nothing says the file is damaged, so it is neither copied nor written over
                LastRead = SettingsRead.Unreadable;
                _notOpened = true;
                LeftAlone = true;
                return new LauncherSettings { NoServer = true };
            }
        }
        LauncherSettings? read = null;
        try
        {
            read = bytes is null ? null : JsonSerializer.Deserialize<LauncherSettings>(bytes, Json);
        }
        catch (Exception e) when (Unreadable(e))
        {
        }
        if (read is null)
        {
            LastRead = SettingsRead.Unreadable;
            LeftAlone = !KeepDamaged();
            return new LauncherSettings { NoServer = true };
        }
        // "the official server" as older launchers stored it: blank, as it is stored now
        if (read.Server is { } server && (server.Trim().Length == 0 || LauncherSettings.FormerDefaultServers.Contains(server.Trim())))
        {
            read = read with { Server = null };
        }
        var (sound, repaired) = SettingsCheck.Sound(read);
        if (repaired)
        {
            LastRead = SettingsRead.Repaired;
            _serverLost = sound.NoServer && !read.NoServer;
            LeftAlone = !KeepDamaged();
        }
        return sound;
    }

    /// <summary>The file's bytes, if it is no longer than <see cref="MostBytes"/>.</summary>
    private byte[] ReadBounded()
    {
        // (shared with a program that has it open, where that program lets it be)
        using var file = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length > MostBytes)
        {
            throw new InvalidDataException("the settings file is too long");
        }
        var bytes = new byte[file.Length];
        file.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>What a file that holds no settings throws when it is read as settings: JSON that is none or is of
    /// another shape, and a value that does not fit its kind.</summary>
    private static bool Unreadable(Exception e) =>
        e is JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException or OverflowException;

    /// <summary>Keeps the file as it stands beside the settings, in the first place of its
    /// <see cref="MostDamaged"/> that is free (an earlier copy is the player's too). False: no copy was made.</summary>
    private bool KeepDamaged()
    {
        var taken = 0;
        for (var n = 1; n <= MostDamaged; n++)
        {
            var place = n == 1 ? DamagedPath : $"{DamagedPath}.{n}";
            try
            {
                if (File.Exists(place) || Directory.Exists(place))
                {
                    taken++;
                    continue;
                }
                File.Copy(Path, place, overwrite: false);
                KeptAt = place;
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException
                                          or System.Security.SecurityException)
            {
                // taken meanwhile, or it cannot be written or the file not read: the next place, if any
            }
        }
        CopiesFull = taken == MostDamaged;
        return false;
    }

    /// <summary>Writes the settings. It refuses (an <see cref="IOException"/>) while the file is left alone
    /// (<see cref="LeftAlone"/>).</summary>
    public void Save(LauncherSettings settings)
    {
        if (LeftAlone)
        {
            throw new IOException("the settings file was not read and no copy of it is kept: it is left as it is");
        }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        Online.Protocol.AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(settings, Json));
    }

    /// <summary><see cref="Save"/> for the launcher while it runs: false when the file could not be written (the
    /// disk is full, the folder is gone or not the player's to write) or is left alone (<see cref="LeftAlone"/>),
    /// and the launcher goes on.</summary>
    public bool TrySave(LauncherSettings settings)
    {
        try
        {
            Save(settings);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException
                                      or System.Security.SecurityException)
        {
            return false;
        }
    }
}

/// <summary>
/// A choice (an enum) in the settings file, by its name in camel case ("quieter"). A name this launcher does not know
/// (a newer launcher's choice, a slip of the hand), or a value of another kind, is read as a value that is none of
/// the choices: <see cref="SettingsCheck.Sound"/> then puts that one setting back to its default and the rest of
/// the file stays. (Refused as the library's own converter refuses it, one unknown choice made the whole file
/// unreadable: every setting back to its default, and no server.)
/// </summary>
internal sealed class ChoiceConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Of<>).MakeGenericType(typeToConvert))!;

    private sealed class Of<T> : JsonConverter<T>
        where T : struct, Enum
    {
        /// <summary>None of the choices (no choice of the settings has this number).</summary>
        private static readonly T None = (T)Enum.ToObject(typeof(T), -1);

        public override bool HandleNull => true;

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    var name = reader.GetString();
                    foreach (var choice in Enum.GetValues<T>())
                    {
                        if (string.Equals(choice.ToString(), name, StringComparison.OrdinalIgnoreCase))
                        {
                            return choice;
                        }
                    }
                    return None;
                case JsonTokenType.Number:
                    return reader.TryGetInt32(out var number) ? (T)Enum.ToObject(typeof(T), number) : None;
                default:
                    reader.Skip();   // an object or a list, whole
                    return None;
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (Enum.IsDefined(value))
            {
                writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
            }
            else
            {
                writer.WriteNumberValue(Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
}

/// <summary>
/// A setting of the settings file that is read as it was written, whatever its kind: a value of another kind than
/// the setting's (a number in quotation marks, a word for true, a list) reads as a value that is none of the
/// setting's, and <see cref="SettingsCheck.Sound"/> then puts that one setting back to its default while the rest of
/// the file stays. (Read by the library's own converters, one such value made the whole file unreadable: every
/// setting back to its default, and no server. Text that is no JSON at all, a missing comma, still does that: the
/// file is kept beside the settings as ".damaged".) For the settings a tester wrote by hand before a page had them.
/// </summary>
public static class SettingAsWritten
{
    /// <summary>A whole number; anything else: <see cref="int.MinValue"/>, which is no setting's value.</summary>
    public sealed class Whole : JsonConverter<int>
    {
        public override bool HandleNull => true;

        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
            {
                return number;
            }
            reader.Skip();   // an object or a list, whole
            return int.MinValue;
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteNumberValue(value);
        }
    }

    /// <summary>A text; anything else: the empty text, which is none of a choice's words.</summary>
    public sealed class Words : JsonConverter<string>
    {
        public override bool HandleNull => true;

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString() ?? "";
            }
            reader.Skip();
            return "";
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(value);
        }
    }

    /// <summary>True or false; anything else: false.</summary>
    public sealed class Flag : JsonConverter<bool>
    {
        public override bool HandleNull => true;

        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)
            {
                return reader.GetBoolean();
            }
            reader.Skip();
            return false;
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteBooleanValue(value);
        }
    }

    /// <summary>The picture's shape (<see cref="WidescreenMode"/>), written as its choice's name ("off", "game",
    /// "trueWide"). The one setting had true or false before true widescreen: true reads as the game's 16:9, false as
    /// Off, so a player's choice carries over. Anything else: the game's 16:9, the default.</summary>
    public sealed class Widescreen : JsonConverter<WidescreenMode>
    {
        public override bool HandleNull => true;

        public override WidescreenMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True:
                    return WidescreenMode.Game;
                case JsonTokenType.False:
                    return WidescreenMode.Off;
                case JsonTokenType.String:
                    var name = reader.GetString();
                    foreach (var choice in Enum.GetValues<WidescreenMode>())
                    {
                        if (string.Equals(choice.ToString(), name, StringComparison.OrdinalIgnoreCase))
                        {
                            return choice;
                        }
                    }
                    return WidescreenMode.Game;
                default:
                    reader.Skip();
                    return WidescreenMode.Game;
            }
        }

        public override void Write(Utf8JsonWriter writer, WidescreenMode value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(
                Enum.IsDefined(value) ? value.ToString() : nameof(WidescreenMode.Game)));
        }
    }

    /// <summary>A setting made of several values (a practice's set-up); a value that is no such thing, or one of
    /// whose parts is of another kind: none (null), which is its default.</summary>
    public sealed class Whole<T> : JsonConverter<T?>
        where T : class
    {
        private static readonly JsonSerializerOptions Plain = new(JsonSerializerDefaults.Web);

        public override bool HandleNull => true;

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var value = JsonDocument.ParseValue(ref reader);
            try
            {
                return value.RootElement.ValueKind == JsonValueKind.Object ? value.RootElement.Deserialize<T>(Plain) : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, Plain);
    }
}
