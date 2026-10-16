using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EastwardLib.Assets;

namespace EastwardModLoader;

public static class ArchiveInjector
{
    private const string Marker = "-- EastwardModLoader bootstrap v3";
    private const string Prefix = "_system/__eastward_modloader/";
    private const string BackupName = Prefix + "original_main.lua";
    private const string MetadataName = Prefix + "installation.json";
    private const string MainName = "_system/main.lua";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static void Inject(string inputPath, string loaderPath, string outputPath)
    {
        Inject(inputPath, ReadModules(loaderPath), outputPath);
    }

    public static void Inject(string inputPath, IReadOnlyDictionary<string, byte[]> modules, string outputPath)
    {
        ValidatePaths(inputPath, outputPath);
        var archive = GArchive.Read(inputPath);
        if (archive.Keys.Any(name => name.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Already injected or loader namespace is occupied. Use UpdateLoader or Restore.");
        }
        Install(archive, modules);
        WriteNew(archive, outputPath);
    }

    public static void UpdateLoader(string inputPath, string loaderPath, string outputPath)
    {
        UpdateLoader(inputPath, ReadModules(loaderPath), outputPath);
    }

    public static void UpdateLoader(string inputPath, IReadOnlyDictionary<string, byte[]> modules, string outputPath)
    {
        ValidatePaths(inputPath, outputPath);
        var archive = GArchive.Read(inputPath);
        RestoreOriginal(archive);
        Install(archive, modules);
        WriteNew(archive, outputPath);
    }

    public static void Restore(string inputPath, string outputPath)
    {
        ValidatePaths(inputPath, outputPath);
        var archive = GArchive.Read(inputPath);
        RestoreOriginal(archive);
        WriteNew(archive, outputPath);
    }

    private static IReadOnlyDictionary<string, byte[]> ReadModules(string loaderPath)
    {
        var modules = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["loader.lua"] = File.ReadAllBytes(loaderPath) };
        var packageModule = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(loaderPath))!, "ewmod.lua");
        if (File.Exists(packageModule))
        {
            modules.Add("ewmod.lua", File.ReadAllBytes(packageModule));
        }
        return modules;
    }

    private static void Install(GArchive archive, IReadOnlyDictionary<string, byte[]> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        if (!modules.TryGetValue("loader.lua", out var loader) || !Utf8.GetString(loader).Contains("EastwardModLoader", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Missing EastwardModLoader loader.lua module.");
        }
        if (!archive.TryGetValue(MainName, out var original))
        {
            throw new InvalidDataException("The archive does not contain _system/main.lua.");
        }
        if (original.Length > 0 && original[0] == 0x1b)
        {
            throw new InvalidDataException("Compiled bootstrap is unsupported; refusing to decompile or replace it.");
        }
        var source = Utf8.GetString(original);
        if (source.Contains("-- EastwardModLoader bootstrap", StringComparison.Ordinal))
        {
            throw new InvalidDataException("An existing loader bootstrap was found.");
        }
        var starts = Regex.Matches(source, @"(?m)^[ \t]*mock\.init[ \t]*\(");
        if (starts.Count != 1)
        {
            throw new InvalidDataException("Expected exactly one standalone mock.init startup call.");
        }
        var moduleHashes = new JsonObject();
        foreach (var (name, data) in modules)
        {
            if (!Regex.IsMatch(name, @"^[A-Za-z0-9_-]+\.lua$") || name == "original_main.lua")
            {
                throw new InvalidDataException("Invalid loader module name: " + name);
            }
            Utf8.GetString(data);
            moduleHashes.Add(name, Hash(data));
        }
        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var hook = Marker + newline
            + "do" + newline
            + "    local ok, reason = pcall(function()" + newline
            + "        local stream = MOAIFileStream.new()" + newline
            + "        stream:open('_system/__eastward_modloader/loader.lua', MOAIFileStream.READ)" + newline
            + "        local source = stream:read()" + newline
            + "        stream:close()" + newline
            + "        local chunk, error = loadstring(assert(source, 'Loader source missing'), '@EastwardModLoader')" + newline
            + "        assert(chunk, error)()" + newline
            + "    end)" + newline
            + "    if not ok then print('[EastwardModLoader] ' .. tostring(reason)) end" + newline
            + "end" + newline;
        archive.Add(BackupName, original);
        foreach (var (name, data) in modules)
        {
            archive.Add(Prefix + name, (byte[])data.Clone());
        }
        archive[MainName] = Utf8.GetBytes(source.Insert(starts[0].Index, hook));
        archive.Add(MetadataName, Utf8.GetBytes(new JsonObject
        {
            ["format"] = "eastward-loader-installation",
            ["version"] = 3,
            ["originalHash"] = Hash(original),
            ["installedMainHash"] = Hash(archive[MainName]),
            ["modules"] = moduleHashes
        }.ToJsonString()));
    }

    private static void RestoreOriginal(GArchive archive)
    {
        if (!archive.TryGetValue(BackupName, out var original) || !archive.TryGetValue(MainName, out var main))
        {
            throw new InvalidDataException("No original bootstrap is embedded in this archive.");
        }
        if (archive.TryGetValue(MetadataName, out var metadata))
        {
            var info = JsonNode.Parse(metadata) ?? throw new InvalidDataException("Invalid loader installation metadata.");
            if (info["format"]?.ToString() != "eastward-loader-installation" || info["version"]?.GetValue<int>() != 3
                || info["originalHash"]?.ToString() != Hash(original) || info["installedMainHash"]?.ToString() != Hash(main))
            {
                throw new InvalidDataException("Loader bootstrap or original backup changed after installation.");
            }
            var owned = new HashSet<string>(StringComparer.Ordinal) { BackupName, MetadataName };
            foreach (var (name, checksum) in info["modules"]!.AsObject())
            {
                owned.Add(Prefix + name);
            }
            if (archive.Keys.Any(name => name.StartsWith(Prefix, StringComparison.Ordinal) && !owned.Contains(name)))
            {
                throw new InvalidDataException("Loader namespace contains unrecognized files.");
            }
            foreach (var (name, checksum) in info["modules"]!.AsObject())
            {
                if (!archive.TryGetValue(Prefix + name, out var data) || checksum?.ToString() != Hash(data))
                {
                    throw new InvalidDataException("Installed loader module changed: " + name);
                }
            }
        }
        else if (!Utf8.GetString(main).Contains("-- EastwardModLoader bootstrap v2", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unknown loader bootstrap version.");
        }
        archive[MainName] = original;
        foreach (var name in archive.Keys.Where(name => name.StartsWith(Prefix, StringComparison.Ordinal)).ToArray())
        {
            archive.Remove(name);
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void ValidatePaths(string inputPath, string outputPath)
    {
        if (Path.GetFileName(inputPath) != "_system.g" || Path.GetFileName(outputPath) != "_system.g")
        {
            throw new ArgumentException("Both archive filenames must be _system.g to preserve the mounted entry prefix.");
        }
        if (Path.GetFullPath(inputPath).Equals(Path.GetFullPath(outputPath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ArgumentException("Output must be a separate path; the input is never overwritten.");
        }
    }

    private static void WriteNew(GArchive archive, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        try
        {
            archive.Write(output, true);
        }
        catch
        {
            output.Dispose();
            File.Delete(path);
            throw;
        }
    }
}
