using System.Text.Json;

namespace EastwardModLoader;

public sealed record LoaderModInfo(string Id, string Name, string File, bool Enabled, string Kind)
{
    public IReadOnlyList<string> Conflicts { get; init; } = [];
}
public sealed record LoaderSnapshot(bool Installed, string? Version, IReadOnlyList<LoaderModInfo> Mods, IReadOnlyList<string> Conflicts);

internal sealed class LoaderConfiguration
{
    public string Format { get; set; } = "eastward-loader";
    public int Version { get; set; } = 1;
    public List<ManagedMod> Mods { get; set; } = [];
    public LoaderBootstrap? Bootstrap { get; set; }
    public LoaderDeployment Deployment { get; set; } = new();
}

internal sealed class ManagedMod
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public bool Enabled { get; set; }
    public string Kind { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.Ordinal);
}

internal sealed record LoaderBootstrap(string OriginalArchiveHash, string InstalledArchiveHash, string Backup, string Version);
internal sealed record CachedArchive(string Name, string File, string Sha256);
internal sealed record PackageHash(string Id, string Sha256);
internal sealed record ResourceHash(string Path, string? OriginalHash, string Hash, int Size);
internal sealed class LoaderDeployment
{
    public List<CachedArchive> Archives { get; set; } = [];
    public List<PackageHash> Packages { get; set; } = [];
    public List<ResourceHash> Resources { get; set; } = [];
}
