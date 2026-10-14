using System.Security.Cryptography;
using System.Text;
using EastwardLib.Assets;
using EastwardModLoader;

var root = Path.Combine(Path.GetTempPath(), "EastwardModLoader-tests-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    var source = Path.Combine(root, "source", "_system.g");
    Directory.CreateDirectory(Path.GetDirectoryName(source)!);
    var fixture = GArchive.Create("_system");
    fixture["_system/main.lua"] = Encoding.UTF8.GetBytes("-- original settings\nmock.setupEnvironment(false, 'config/game')\nmock.init('config/game_config')\nmock.start()\n");
    fixture["_system/main_package.lua"] = [1, 2, 3, 4];
    fixture["_system/unrelated"] = [0, 255, 42];
    fixture.Write(source);
    Verify(source, args[0]);
    if (args.Length > 1)
    {
        Verify(args[1], args[0]);
    }
    fixture["_system/main.lua"] = [0x1b, (byte)'L', (byte)'J'];
    fixture.Write(source);
    Throws(() => ArchiveInjector.Inject(source, args[0], Path.Combine(root, "compiled", "_system.g")));
    fixture["_system/main.lua"] = Encoding.UTF8.GetBytes("mock.init('a')\nmock.init('b')");
    fixture.Write(source);
    Throws(() => ArchiveInjector.Inject(source, args[0], Path.Combine(root, "ambiguous", "_system.g")));
    Console.WriteLine("Archive injection and restore tests passed.");
}
finally
{
    Directory.Delete(root, true);
}

void Verify(string source, string loader)
{
    var directory = Path.Combine(root, Guid.NewGuid().ToString());
    var output = Path.Combine(directory, "injected", "_system.g");
    var restored = Path.Combine(directory, "restored", "_system.g");
    var originalHash = SHA256.HashData(File.ReadAllBytes(source));
    var original = GArchive.Read(source);
    ArchiveInjector.Inject(source, loader, output);
    var injected = GArchive.Read(output);
    Require(injected.Count == original.Count + 2, "Unexpected added entries");
    foreach (var (name, bytes) in original)
    {
        if (name != "_system/main.lua")
        {
            Require(bytes.SequenceEqual(injected[name]), "Unrelated entry changed: " + name);
        }
    }
    Require(injected["_system/__eastward_modloader/original_main.lua"].SequenceEqual(original["_system/main.lua"]), "Original bootstrap was not preserved");
    var bootstrap = Encoding.UTF8.GetString(injected["_system/main.lua"]);
    Require(bootstrap.IndexOf("EastwardModLoader bootstrap", StringComparison.Ordinal) < bootstrap.IndexOf("mock.init(", StringComparison.Ordinal), "Hook must precede game init");
    Throws(() => ArchiveInjector.Inject(source, loader, source));
    Throws(() => ArchiveInjector.Inject(source, loader, output));
    Throws(() => ArchiveInjector.Inject(output, loader, Path.Combine(directory, "duplicate", "_system.g")));
    ArchiveInjector.Restore(output, restored);
    var roundtrip = GArchive.Read(restored);
    Require(roundtrip.Count == original.Count, "Restore did not remove loader entries");
    foreach (var (name, bytes) in original)
    {
        Require(bytes.SequenceEqual(roundtrip[name]), "Restored payload differs: " + name);
    }
    Require(originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))), "Input archive was modified");
    Console.WriteLine("Verified original/bootstrap/restore: " + source);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void Throws(Action action)
{
    try { action(); }
    catch (Exception error) when (error is IOException or ArgumentException or InvalidDataException) { return; }
    throw new Exception("Expected refusal");
}
