using System.Text;
using System.Text.RegularExpressions;
using EastwardLib.Assets;

namespace EastwardModLoader;

public static class ArchiveInjector
{
    private const string Marker = "-- EastwardModLoader bootstrap v2";
    private const string BackupName = "_system/__eastward_modloader/original_main.lua";
    private const string LoaderName = "_system/__eastward_modloader/loader.lua";
    private const string MainName = "_system/main.lua";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static void Inject(string inputPath, string loaderPath, string outputPath)
    {
        ValidatePaths(inputPath, outputPath);
        var archive = GArchive.Read(inputPath);
        if (archive.ContainsKey(BackupName) || archive.ContainsKey(LoaderName))
        {
            throw new InvalidDataException("Already injected. Restore this archive before updating the loader.");
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
        if (source.Contains(Marker, StringComparison.Ordinal))
        {
            throw new InvalidDataException("An existing loader bootstrap was found.");
        }
        // This standalone startup call follows setupEnvironment and precedes game.init.
        // Reject ambiguous versions instead of replacing the game's entire startup script.
        var starts = Regex.Matches(source, @"(?m)^[ \t]*mock\.init[ \t]*\(");
        if (starts.Count != 1)
        {
            throw new InvalidDataException("Expected exactly one standalone mock.init startup call.");
        }
        var loader = File.ReadAllBytes(loaderPath);
        var loaderText = Utf8.GetString(loader);
        if (!loaderText.Contains("EastwardModLoader", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The loader source is not an EastwardModLoader script.");
        }
        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var hook = Marker + newline
            + "do" + newline
            + "    local ok, error = pcall(function()" + newline
            + "        local stream = MOAIFileStream.new()" + newline
            + "        stream:open('_system/__eastward_modloader/loader.lua', MOAIFileStream.READ)" + newline
            + "        local source = stream:read()" + newline
            + "        stream:close()" + newline
            + "        local chunk, reason = loadstring(assert(source, 'Loader source missing'), '@EastwardModLoader')" + newline
            + "        assert(chunk, reason)()" + newline
            + "    end)" + newline
            + "    if not ok then print('[EastwardModLoader] ' .. tostring(error)) end" + newline
            + "end" + newline;
        archive.Add(BackupName, original);
        archive.Add(LoaderName, loader);
        archive[MainName] = Utf8.GetBytes(source.Insert(starts[0].Index, hook));
        WriteNew(archive, outputPath);
    }

    public static void Restore(string inputPath, string outputPath)
    {
        ValidatePaths(inputPath, outputPath);
        var archive = GArchive.Read(inputPath);
        if (!archive.TryGetValue(BackupName, out var original))
        {
            throw new InvalidDataException("No original bootstrap is embedded in this archive.");
        }
        archive[MainName] = original;
        archive.Remove(BackupName);
        archive.Remove(LoaderName);
        WriteNew(archive, outputPath);
    }

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
