using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EastwardLib.Assets;
using EastwardLib.Modding;
using EastwardLib.Utils;

namespace EastwardModLoader;

public sealed class LoaderModManager
{
    public const string LoaderVersion = "4";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public string GameDirectory { get; }
    private string ModsDirectory => Path.Combine(GameDirectory, "mods");
    private string SystemArchive => Path.Combine(GameDirectory, "_system.g");
    private string ConfigurationPath => Path.Combine(ModsDirectory, "loader.json");

    public LoaderModManager(string directory)
    {
        directory = Path.GetFullPath(directory);
        if (!File.Exists(Path.Combine(directory, "config.g"))) directory = Path.Combine(directory, "content", "game");
        if (!File.Exists(Path.Combine(directory, "config.g")) || !File.Exists(Path.Combine(directory, "_system.g"))) throw new DirectoryNotFoundException("Select Eastward or its content/game directory");
        GameDirectory = Path.TrimEndingDirectorySeparator(directory);
    }

    public LoaderSnapshot Inspect()
    {
        var configuration = ReadConfiguration();
        var conflicts = new List<string>();
        try { ValidateBootstrap(configuration); }
        catch (Exception error) when (error is IOException or InvalidDataException) { conflicts.Add(error.Message); }
        try { ReadPackages(configuration, validateAll: true); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException) { conflicts.Add(error.Message); }
        return new LoaderSnapshot(configuration.Bootstrap != null, configuration.Bootstrap?.Version, configuration.Mods.Select(mod => new LoaderModInfo(mod.Id, mod.Name, mod.File, mod.Enabled, mod.Kind) { Conflicts = conflicts.Where(error => error.Contains(mod.Id, StringComparison.Ordinal)).ToArray() }).ToArray(), conflicts);
    }

    public void Install()
    {
        using var gate = Lock();
        var configuration = ReadConfiguration();
        ValidateBootstrap(configuration);
        configuration.Deployment = Prepare(configuration);
        var oldHash = HashFile(SystemArchive);
        var original = configuration.Bootstrap == null ? SystemArchive : Resolve(configuration.Bootstrap.Backup);
        var originalHash = HashFile(original);
        var backup = ".eastward-loader/backups/" + originalHash + "/_system.g";
        var backupPath = Resolve(backup);
        if (!File.Exists(backupPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(original, backupPath, false);
        }
        if (HashFile(backupPath) != originalHash) throw new InvalidDataException("Loader backup was modified");
        var stage = Stage();
        try
        {
            var injected = Path.Combine(stage, "_system.g");
            ArchiveInjector.Inject(backupPath, LoaderBundle(), injected);
            configuration.Bootstrap = new LoaderBootstrap(originalHash, HashFile(injected), backup, LoaderVersion);
            ReplaceBootstrap(injected, configuration, oldHash, stage);
        }
        finally { DeleteStage(stage); }
    }

    public void Uninstall()
    {
        using var gate = Lock();
        var configuration = ReadConfiguration();
        ValidateBootstrap(configuration);
        if (configuration.Bootstrap == null) throw new InvalidOperationException("The managed loader is not installed");
        var oldHash = configuration.Bootstrap.InstalledArchiveHash;
        var original = Resolve(configuration.Bootstrap.Backup);
        var stage = Stage();
        try
        {
            var restored = Path.Combine(stage, "_system.g");
            File.Copy(original, restored);
            configuration.Bootstrap = null;
            ReplaceBootstrap(restored, configuration, oldHash, stage);
        }
        finally { DeleteStage(stage); }
    }

    public void Import(string source)
    {
        using var gate = Lock();
        var configuration = ReadConfiguration();
        ValidateBootstrap(configuration);
        source = Path.GetFullPath(source);
        var folder = Directory.Exists(source);
        var kind = !folder && Path.GetExtension(source).Equals(".ewmod", StringComparison.OrdinalIgnoreCase) ? "ewmod" : "lua";
        if (!folder && !File.Exists(source)) throw new FileNotFoundException("Mod source does not exist", source);
        if (kind == "lua" && !folder && !Path.GetExtension(source).Equals(".lua", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Import an .ewmod, Lua file or folder containing main.lua");
        var main = folder ? Path.Combine(source, "main.lua") : source;
        if (!File.Exists(main)) throw new FileNotFoundException("A Lua mod folder must contain main.lua", main);
        if (kind == "ewmod")
        {
            using var manager = Baseline();
            var package = ModPackage.Read(source);
            var conflicts = package.Validate(manager.Manager);
            if (conflicts.Count > 0) throw new InvalidDataException("Mod belongs to different game resources: " + string.Join(", ", conflicts));
            if (package.ResourcePaths.Any(path => Reserved(path))) throw new InvalidDataException("The system bootstrap is managed by the loader and cannot be replaced by an ewmod");
        }
        var contentHash = HashFile(main);
        var stem = Regex.Replace(Path.GetFileNameWithoutExtension(folder ? Path.GetFileName(source) : source), "[^A-Za-z0-9_]", "_").Trim('_');
        if (stem.Length == 0) stem = "mod";
        stem = stem[..Math.Min(stem.Length, 40)];
        var id = stem + "_" + contentHash[..12];
        if (configuration.Mods.Any(mod => mod.Id == id)) throw new InvalidOperationException("This mod is already imported");
        var relative = "packages/" + id + (kind == "ewmod" ? ".ewmod" : "/main.lua");
        var destination = Resolve(kind == "ewmod" ? relative : "packages/" + id);
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("The mod destination already exists; existing files will not be overwritten");
        var stage = Stage();
        var mod = new ManagedMod { Id = id, Name = kind == "ewmod" ? ModPackage.Read(source).Name : Path.GetFileNameWithoutExtension(folder ? Path.GetFileName(source) : source), File = relative, Kind = kind, Sha256 = contentHash };
        try
        {
            var payload = Path.Combine(stage, "payload");
            if (kind == "ewmod") File.Copy(source, payload);
            else
            {
                Directory.CreateDirectory(payload);
                if (folder)
                {
                    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                    {
                        RejectLinks(file, source);
                        var name = Path.GetRelativePath(source, file).Replace('\\', '/');
                        var target = SafePath.ResolveWithin(payload, name);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(file, target);
                        mod.Files[name] = HashFile(target);
                    }
                }
                else { File.Copy(source, Path.Combine(payload, "main.lua")); mod.Files["main.lua"] = contentHash; }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (kind == "ewmod") File.Move(payload, destination); else Directory.Move(payload, destination);
            configuration.Mods.Add(mod);
            try { WriteConfiguration(configuration); }
            catch { if (kind == "ewmod") File.Delete(destination); else Directory.Delete(destination, true); throw; }
        }
        finally { DeleteStage(stage); }
    }

    public void SetEnabled(IReadOnlyCollection<string> enabledIds)
    {
        using var gate = Lock();
        var configuration = ReadConfiguration();
        ValidateBootstrap(configuration);
        RequireCurrentLoader(configuration);
        var ids = enabledIds.ToHashSet(StringComparer.Ordinal);
        if (ids.Count != enabledIds.Count || ids.Any(id => configuration.Mods.All(mod => mod.Id != id))) throw new ArgumentException("Unknown or duplicate mod ID");
        foreach (var mod in configuration.Mods) mod.Enabled = ids.Contains(mod.Id);
        configuration.Deployment = Prepare(configuration);
        WriteConfiguration(configuration);
    }

    public void Remove(string id)
    {
        using var gate = Lock();
        var configuration = ReadConfiguration();
        ValidateBootstrap(configuration);
        RequireCurrentLoader(configuration);
        var mod = configuration.Mods.SingleOrDefault(mod => mod.Id == id) ?? throw new ArgumentException("Unknown mod ID");
        configuration.Mods.Remove(mod);
        configuration.Deployment = Prepare(configuration);
        // Retain imported files and backups; removing an entry must not delete user mod sources.
        WriteConfiguration(configuration);
    }

    private LoaderDeployment Prepare(LoaderConfiguration configuration)
    {
        var resources = ReadPackages(configuration, validateAll: false);
        return new LoaderDeployment
        {
            Resources = resources.Values.Select(resource => resource.Hash).ToList(),
            Packages = configuration.Mods.Where(mod => mod.Enabled && mod.Kind == "ewmod").Select(mod => new PackageHash(mod.Id, mod.Sha256)).ToList()
        };
    }
    private sealed record ModResource(ResourceHash Hash, byte[] Bytes, string ModId);
    private Dictionary<string, ModResource> ReadPackages(LoaderConfiguration configuration, bool validateAll)
    {
        using var baseline = Baseline();
        var result = new Dictionary<string, ModResource>(StringComparer.Ordinal);
        foreach (var mod in configuration.Mods.Where(mod => mod.Enabled || validateAll))
        {
            var path = Resolve(mod.File);
            if (!File.Exists(path) || HashFile(path) != mod.Sha256) throw new InvalidDataException("Mod file is missing or changed: " + mod.Id);
            if (mod.Kind == "lua")
            {
                var directory = Path.GetDirectoryName(path)!;
                foreach (var (file, hash) in mod.Files)
                {
                    var full = SafePath.ResolveWithin(directory, file);
                    RejectLinks(full, ModsDirectory);
                    if (!File.Exists(full) || HashFile(full) != hash) throw new InvalidDataException("Lua mod source is missing or changed: " + mod.Id + "/" + file);
                }
                continue;
            }
            var package = ModPackage.Read(path);
            var conflicts = package.Validate(baseline.Manager);
            if (conflicts.Count > 0) throw new InvalidDataException("Mod " + mod.Id + " belongs to different game resources: " + string.Join(", ", conflicts));
            if (!mod.Enabled) continue;
            using var zip = ZipFile.OpenRead(path);
            using var stream = zip.GetEntry("manifest.json")!.Open();
            var manifest = JsonNode.Parse(stream)!;
            foreach (var resource in package.ResourcePaths)
            {
                if (Reserved(resource)) throw new InvalidDataException("Mod cannot replace the loader bootstrap: " + mod.Id);
                var entry = manifest["resources"]![resource]!;
                var hash = new ResourceHash(resource, entry["originalHash"]?.GetValue<string>(), entry["hash"]!.GetValue<string>(), entry["size"]!.GetValue<int>());
                var bytes = package.GetResource(resource);
                if (result.TryGetValue(resource, out var existing) && !existing.Bytes.AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Mod conflict: " + existing.ModId + " and " + mod.Id + " replace " + resource);
                result[resource] = new ModResource(hash, bytes, mod.Id);
            }
        }
        return result;
    }

    private LoaderConfiguration ReadConfiguration()
    {
        if (!File.Exists(ConfigurationPath))
        {
            var fresh = new LoaderConfiguration();
            if (Directory.Exists(ModsDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(ModsDirectory, "*.lua", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
                {
                    var id = Path.GetFileNameWithoutExtension(file);
                    if (!Regex.IsMatch(id, "^[A-Za-z0-9_]+$")) continue;
                    RejectLinks(file, GameDirectory);
                    fresh.Mods.Add(new ManagedMod { Id = id, Name = id, File = Path.GetFileName(file), Kind = "lua", Enabled = true, Sha256 = HashFile(file), Files = new Dictionary<string, string> { [Path.GetFileName(file)] = HashFile(file) } });
                }
            }
            return fresh;
        }
        RejectLinks(ConfigurationPath, GameDirectory);
        var configuration = JsonSerializer.Deserialize<LoaderConfiguration>(File.ReadAllBytes(ConfigurationPath), JsonOptions) ?? throw new InvalidDataException("Empty loader configuration");
        if (configuration.Format != "eastward-loader" || configuration.Version != 1 || configuration.Mods.Select(mod => mod.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != configuration.Mods.Count) throw new InvalidDataException("Unsupported loader configuration or duplicate mod IDs");
        foreach (var mod in configuration.Mods)
        {
            if (!Regex.IsMatch(mod.Id, "^[A-Za-z0-9_]+$") || mod.Kind is not ("lua" or "ewmod")) throw new InvalidDataException("Invalid mod entry");
            Resolve(mod.File);
        }
        return configuration;
    }

    private static void RequireCurrentLoader(LoaderConfiguration configuration)
    {
        if (configuration.Bootstrap != null && configuration.Bootstrap.Version != LoaderVersion)
            throw new InvalidOperationException("Update the installed loader before changing enabled mods");
    }
    private void ValidateBootstrap(LoaderConfiguration configuration)
    {
        RejectLinks(SystemArchive, GameDirectory);
        if (configuration.Bootstrap == null) return;
        if (HashFile(SystemArchive) != configuration.Bootstrap.InstalledArchiveHash) throw new InvalidDataException("The installed system archive changed outside the mod manager; refusing to overwrite it");
        var backup = Resolve(configuration.Bootstrap.Backup);
        if (!File.Exists(backup) || HashFile(backup) != configuration.Bootstrap.OriginalArchiveHash) throw new InvalidDataException("The original system archive backup is missing or changed");
    }

    private void ReplaceBootstrap(string replacement, LoaderConfiguration configuration, string expectedHash, string stage)
    {
        if (HashFile(SystemArchive) != expectedHash) throw new InvalidDataException("System archive changed while preparing the loader");
        var previous = Path.Combine(stage, "previous.g");
        File.Copy(SystemArchive, previous);
        File.Move(replacement, SystemArchive, true);
        try { WriteConfiguration(configuration); }
        catch { File.Move(previous, SystemArchive, true); throw; }
    }

    private void WriteConfiguration(LoaderConfiguration configuration)
    {
        Directory.CreateDirectory(ModsDirectory);
        var temporary = Resolve(".loader-config-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(configuration, JsonOptions));
        try { File.Move(temporary, ConfigurationPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string Resolve(string relative)
    {
        var path = SafePath.ResolveWithin(ModsDirectory, relative);
        RejectLinks(path, GameDirectory);
        return path;
    }
    private static void RejectLinks(string path, string root)
    {
        for (var item = new FileInfo(path); item != null && !item.FullName.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal); item = item.Directory == null ? null : new FileInfo(item.Directory.FullName))
        {
            if ((File.Exists(item.FullName) || Directory.Exists(item.FullName)) && (File.GetAttributes(item.FullName) & FileAttributes.ReparsePoint) != 0) throw new IOException("Symbolic links are not supported in managed mod paths: " + item.FullName);
        }
    }
    private IDisposable Lock()
    {
        Directory.CreateDirectory(ModsDirectory);
        var path = Resolve(".eastward-loader.lock");
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private string Stage()
    {
        var path = Resolve(".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    private static void DeleteStage(string stage) { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    private static bool Reserved(string path) => path.StartsWith("_system/", StringComparison.Ordinal) || path.StartsWith("mods/", StringComparison.Ordinal) || path.StartsWith("lib/", StringComparison.Ordinal) || path.StartsWith("__eastward_modloader_packages/", StringComparison.Ordinal) || path is "config/game_config" or "config/game_config.json";
    private static string HashFile(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static Dictionary<string, byte[]> LoaderBundle()
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in new[] { "main.lua", "ewmod.lua", "overlay.lua" })
        {
            using var stream = typeof(LoaderModManager).Assembly.GetManifestResourceStream("EastwardModLoader." + name) ?? throw new InvalidOperationException("Missing bundled loader: " + name);
            using var buffer = new MemoryStream(); stream.CopyTo(buffer); result[name == "main.lua" ? "loader.lua" : name] = buffer.ToArray();
        }
        return result;
    }
    private BaselineSession Baseline()
    {
        var manager = AssetManager.Create(Path.Combine(GameDirectory, "config.g"));
        manager.RootDirectory = GameDirectory;
        foreach (var file in Directory.EnumerateFiles(GameDirectory, "*.g", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal)) manager.RegisterArchive(file);
        var dlc = Path.GetFullPath(Path.Combine(GameDirectory, "../..", "content_dlc"));
        if (Directory.Exists(dlc)) foreach (var file in Directory.EnumerateFiles(dlc, "*.g", SearchOption.AllDirectories).Order(StringComparer.Ordinal)) manager.RegisterArchive(file);
        return new BaselineSession(manager);
    }
    private sealed class BaselineSession(AssetManager manager) : IDisposable
    {
        public AssetManager Manager { get; } = manager;
        public void Dispose() => Manager.UnloadArchives();
    }
}
