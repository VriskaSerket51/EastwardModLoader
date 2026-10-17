using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using EastwardLib.Assets;
using EastwardLib.Modding;
using EastwardLib.Utils;
using EastwardModLoader;

ArchiveStreamingTests.Run();
var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "Eastward-deployment-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Run("install-restore", fixture =>
{
    var manager = new LoaderModManager(fixture.Root);
    Require(!manager.Inspect().Installed, "Fresh fixture must not have a loader");
    manager.Install();
    var installed = manager.Inspect();
    Require(installed.Installed && installed.Version == LoaderModManager.LoaderVersion && installed.Conflicts.Count == 0, "Installed loader status must be valid");
    var configuration = fixture.Configuration();
    var backup = Path.Combine(fixture.Game, "mods", configuration["bootstrap"]!["backup"]!.GetValue<string>());
    Require(File.ReadAllBytes(backup).AsSpan().SequenceEqual(fixture.OriginalSystem), "Backup must preserve exact original archive bytes");
    var injected = GArchive.Read(Path.Combine(fixture.Game, "_system.g"));
    Require(injected.Keys.Any(key => key.Contains("__eastward_modloader", StringComparison.Ordinal)), "Loader must be inserted into fixture bootstrap");
    Require(injected["_system/unrelated"].AsSpan().SequenceEqual(new byte[] { 0, 255, 42 }), "Injection must preserve unrelated bootstrap resources");
    Require(injected.ContainsKey("_system/__eastward_modloader/overlay.lua"), "The direct resource reader must be bundled");
    configuration["bootstrap"]!["version"] = "3";
    File.WriteAllText(fixture.ConfigurationPath, configuration.ToJsonString());
    Reject(() => manager.SetEnabled([]));
    manager.Install();
    Require(manager.Inspect().Version == LoaderModManager.LoaderVersion, "Install must update an old loader before enabling direct packages");
    Require(manager.Inspect().Conflicts.Count == 0, "Managed reinstall must remain valid");
    manager.Uninstall();
    Require(!manager.Inspect().Installed, "Uninstall must clear installed state");
    Require(File.ReadAllBytes(Path.Combine(fixture.Game, "_system.g")).AsSpan().SequenceEqual(fixture.OriginalSystem), "Uninstall must restore exact original archive bytes");
    fixture.AssertOriginalResources();
});
Run("resources", fixture =>
{
    var first = fixture.Mod("one", "first replacement");
    var duplicate = fixture.Mod("same-content", "first replacement");
    var conflicting = fixture.Mod("different-content", "other replacement");
    var manager = new LoaderModManager(fixture.Root);
    manager.Install();
    manager.Import(first); manager.Import(duplicate); manager.Import(conflicting);
    Require(manager.Inspect().Mods.All(mod => !mod.Enabled), "Imports must start disabled");
    Reject(() => manager.Import(first));
    var mods = manager.Inspect().Mods;
    manager.SetEnabled([mods[0].Id, mods[1].Id]);
    var configuration = fixture.Configuration();
    Require(configuration["deployment"]!["resources"]!.AsArray().Count == 1, "Identical overlapping resources must be deduplicated");
    Require(configuration["deployment"]!["mode"]!.GetValue<string>() == "ewmod", "Resources must be deployed directly from packages");
    Require(configuration["deployment"]!["packages"]!.AsArray().Count == 2, "Both identical source packages must remain verified");
    Require(configuration["deployment"]!["archives"] == null && !Directory.Exists(Path.Combine(fixture.Game, "mods", "cache")), "Enabling mods must not compile or cache G archives");
    using (var package = ZipFile.OpenRead(Path.Combine(fixture.Game, "mods", mods[0].File)))
    using (var entry = package.GetEntry("resources/asset/dialog")!.Open())
    using (var reader = new StreamReader(entry))
        Require(reader.ReadToEnd() == "first replacement", "Direct resource payload must remain in the original ewmod");
    var savedConfig = File.ReadAllBytes(fixture.ConfigurationPath);
    Reject(() => manager.SetEnabled([mods[0].Id, mods[2].Id]));
    Require(File.ReadAllBytes(fixture.ConfigurationPath).AsSpan().SequenceEqual(savedConfig), "Conflicting enable must not commit a partial configuration");
    Reject(() => manager.SetEnabled([mods[0].Id, mods[0].Id]));
    Reject(() => manager.SetEnabled(["missing"]));
    manager.SetEnabled([]);
    Require(fixture.Configuration()["deployment"]!["packages"]!.AsArray().Count == 0 && fixture.Configuration()["deployment"]!["resources"]!.AsArray().Count == 0, "Disabling all packages must clear the direct resource plan");
    var imported = Path.Combine(fixture.Game, "mods", mods[0].File);
    manager.Remove(mods[0].Id);
    Require(File.Exists(imported), "Removing an entry must preserve imported user source files");
    fixture.AssertOriginalResources();
});
Run("lua-import", fixture =>
{
    var source = Path.Combine(fixture.Root, "lua-source");
    Directory.CreateDirectory(Path.Combine(source, "scripts"));
    File.WriteAllText(Path.Combine(source, "main.lua"), "-- fixture Lua mod, never executed\nreturn {}\n");
    File.WriteAllText(Path.Combine(source, "scripts", "helper.lua"), "return 'fixture'\n");
    var manager = new LoaderModManager(fixture.Root);
    manager.Install(); manager.Import(source);
    var mod = manager.Inspect().Mods.Single();
    Require(mod.Kind == "lua" && !mod.Enabled, "Folder Lua mod must be tracked and disabled by default");
    manager.SetEnabled([mod.Id]);
    var imported = Path.Combine(fixture.Game, "mods", Path.GetDirectoryName(mod.File)!);
    Require(File.ReadAllText(Path.Combine(imported, "scripts", "helper.lua")) == "return 'fixture'\n", "Folder dependencies must be copied");
    File.AppendAllText(Path.Combine(imported, "scripts", "helper.lua"), "-- changed");
    Require(manager.Inspect().Conflicts.Count > 0, "Changed Lua dependencies must be reported");
    Reject(() => manager.SetEnabled([mod.Id]));
    fixture.AssertOriginalResources();
});
Run("stale-system", fixture =>
{
    var manager = new LoaderModManager(fixture.Root);
    manager.Install();
    var archive = Path.Combine(fixture.Game, "_system.g");
    File.AppendAllText(archive, "fixture external update");
    var changed = File.ReadAllBytes(archive);
    Require(manager.Inspect().Conflicts.Count > 0, "An external system archive update must be reported");
    Reject(manager.Install); Reject(manager.Uninstall);
    Require(File.ReadAllBytes(archive).AsSpan().SequenceEqual(changed), "Manager must not overwrite an external update");
});
Run("stale-resource-and-package", fixture =>
{
    var source = fixture.Mod("valid", "replacement");
    var manager = new LoaderModManager(fixture.Root);
    manager.Install(); manager.Import(source);
    var mod = manager.Inspect().Mods.Single();
    var imported = Path.Combine(fixture.Game, "mods", mod.File);
    var originalPackage = File.ReadAllBytes(imported);
    File.AppendAllText(imported, "changed fixture package");
    Require(manager.Inspect().Conflicts.Count > 0, "Package SHA mismatch must be reported");
    Reject(() => manager.SetEnabled([mod.Id]));
    File.WriteAllBytes(imported, originalPackage);
    var resource = GArchive.Read(Path.Combine(fixture.Game, "asset.g"));
    resource["asset/dialog"] = Encoding.UTF8.GetBytes("external game update");
    resource.Write(Path.Combine(fixture.Game, "asset.g"));
    Require(manager.Inspect().Conflicts.Count > 0, "Changed baseline resource must be reported");
    Reject(() => manager.SetEnabled([mod.Id]));
});
Run("unsafe-package", fixture =>
{
    var manager = new LoaderModManager(fixture.Root);
    manager.Install();
    var reserved = fixture.Mod("system-replacement", "forbidden", "_system/main.lua");
    Reject(() => manager.Import(reserved));
    var valid = fixture.Mod("valid", "replacement");
    var duplicate = Path.Combine(fixture.Root, "duplicate.ewmod");
    File.Copy(valid, duplicate);
    using (var zip = ZipFile.Open(duplicate, ZipArchiveMode.Update))
    {
        using var output = zip.CreateEntry("resources/asset/dialog").Open();
        output.Write("duplicate"u8);
    }
    Reject(() => manager.Import(duplicate));
    var traversal = Path.Combine(fixture.Root, "traversal.ewmod");
    File.Copy(valid, traversal);
    using (var zip = ZipFile.Open(traversal, ZipArchiveMode.Update))
    {
        using var output = zip.CreateEntry("resources/../../outside").Open();
        output.Write("unsafe"u8);
    }
    Reject(() => manager.Import(traversal));
    Require(manager.Inspect().Mods.Count == 0, "Rejected packages must not add entries");
    fixture.AssertOriginalResources();
});
Console.WriteLine("Deployment tests passed using synthetic installations only: " + root);

void Run(string name, Action<Fixture> test)
{
    var fixture = new Fixture(Path.Combine(root, name));
    test(fixture);
    Console.WriteLine("PASS " + name);
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static void Reject(Action action)
{
    try { action(); }
    catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException) { return; }
    throw new Exception("Invalid deployment operation was accepted");
}

internal sealed class Fixture
{
    public string Root { get; }
    public string Game { get; }
    public string ConfigurationPath => Path.Combine(Game, "mods", "loader.json");
    public byte[] OriginalSystem { get; }
    private readonly Dictionary<string, byte[]> originalResources = new(StringComparer.Ordinal);
    public Fixture(string root)
    {
        Root = root; Game = Path.Combine(root, "content", "game");
        Directory.CreateDirectory(Game);
        var config = GArchive.Create("config");
        config["config/asset_index"] = "{}"u8.ToArray();
        config["config/script_library"] = "{\"source\":{},\"export\":{}}"u8.ToArray();
        config["config/texture_index"] = "{\"root\":\"root\",\"map\":{\"root\":{\"body\":{\"groups\":[]},\"model\":\"mock.TextureLibrary\"}}}"u8.ToArray();
        config.Write(Path.Combine(Game, "config.g"));
        var system = GArchive.Create("_system");
        system["_system/main.lua"] = "-- synthetic game bootstrap, never executed\nmock.setupEnvironment(false, 'config/game')\nmock.init('config/game_config')\nmock.start()\n"u8.ToArray();
        system["_system/unrelated"] = [0, 255, 42];
        system.Write(Path.Combine(Game, "_system.g"));
        OriginalSystem = File.ReadAllBytes(Path.Combine(Game, "_system.g"));
        var assets = GArchive.Create("asset");
        assets["asset/dialog"] = "original fixture text"u8.ToArray();
        assets["asset/unrelated"] = [8, 9, 10];
        assets.Write(Path.Combine(Game, "asset.g"));
        foreach (var file in new[] { "config.g", "asset.g" }) originalResources[file] = File.ReadAllBytes(Path.Combine(Game, file));
    }
    public JsonNode Configuration() => JsonNode.Parse(File.ReadAllBytes(ConfigurationPath))!;
    public string Mod(string name, string text, string resource = "asset/dialog")
    {
        var manager = AssetManager.Create(Path.Combine(Game, "config.g"));
        foreach (var file in Directory.EnumerateFiles(Game, "*.g")) manager.RegisterArchive(file);
        var package = ModPackage.Create(name, manager, new Dictionary<string, byte[]> { [resource] = Encoding.UTF8.GetBytes(text) });
        var path = Path.Combine(Root, name + ".ewmod");
        package.SaveTo(path);
        return path;
    }
    public void AssertOriginalResources()
    {
        foreach (var (file, bytes) in originalResources) if (!File.ReadAllBytes(Path.Combine(Game, file)).AsSpan().SequenceEqual(bytes)) throw new InvalidOperationException("Fixture original archive was modified: " + file);
    }
}
