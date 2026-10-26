using System.Text.Json.Nodes;
using Launcher.App;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.Tests;

/// <summary>The launcher's secret (<see cref="LauncherSettings.PlayerKey"/>), which the server works the player's tag
/// out from: made once, at the first start, from the cryptographic source, and kept; one that is not a secret is
/// replaced. Settings saved before it, which held a "tag", still load, and the tag is not written again.</summary>
public sealed class PlayerKeyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Data => Path.Combine(_dir, "data");

    private JsonObject Stored() => JsonNode.Parse(File.ReadAllText(Path.Combine(Data, "settings.json")))!.AsObject();

    [Fact]
    public void TheSecretIsMadeOnceAndKept()
    {
        var first = AppState.Load(Data);                         // a first start: no settings yet
        var key = first.Settings.PlayerKey;
        Assert.True(PlayerTags.IsKey(key), key);
        Assert.Equal(key, (string?)Stored()["playerKey"]);       // saved at once
        first.Update(s => s with { Widescreen = WidescreenMode.Off });        // any later change keeps it
        Assert.Equal(key, AppState.Load(Data).Settings.PlayerKey);
        Assert.Equal(key, AppState.Load(Data).Settings.PlayerKey);
        Assert.NotEqual(key, AppState.Load(Path.Combine(_dir, "other")).Settings.PlayerKey);   // another launcher's own
        Assert.Same(first.Settings, first.Settings.WithPlayerKey());

        // a saved value that is no secret is replaced by a new one
        var file = Stored();
        file["playerKey"] = "4F2A";
        File.WriteAllText(Path.Combine(Data, "settings.json"), file.ToJsonString());
        var replaced = AppState.Load(Data).Settings.PlayerKey;
        Assert.True(PlayerTags.IsKey(replaced) && replaced != key, replaced);
        Assert.Equal(replaced, (string?)Stored()["playerKey"]);
    }

    [Fact]
    public void SettingsSavedWithATagLoadAndTheTagIsNotWrittenAgain()
    {
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "settings.json"), """
            {"racer": {"name": "ANA", "machine": 3, "color": {"r": 200, "g": 0, "b": 0}},
             "server": "ws://192.168.1.20:47810/ws", "tag": "4F2A", "widescreen": false}
            """);
        var store = new SettingsStore(Path.Combine(Data, "settings.json"));
        var read = store.Load();
        Assert.Equal(SettingsRead.Fine, store.LastRead);
        Assert.Equal(("ANA", "ws://192.168.1.20:47810/ws", WidescreenMode.Off, null), (read.Racer.Name, read.Server, read.Widescreen, read.PlayerKey));

        var state = AppState.Load(Data);
        Assert.Null(state.Problem);
        Assert.Equal("ANA", state.Settings.Racer.Name);
        Assert.True(PlayerTags.IsKey(state.Settings.PlayerKey));
        Assert.Null(Stored()["tag"]);
        Assert.Equal(state.Settings.PlayerKey, (string?)Stored()["playerKey"]);
    }
}
