using System.Text;
using EastwardModLoader;

try
{
    if (args.Length == 0 || (args[0] == "inject" || args[0] == "update") && args.Length != 4
        || args[0] == "restore" && args.Length != 3
        || args[0] != "inject" && args[0] != "restore" && args[0] != "update")
    {
        Console.Error.WriteLine("usage: Injector inject <_system.g> <main.lua loader> <output/_system.g>");
        Console.Error.WriteLine("       Injector update <_system.g> <main.lua loader> <output/_system.g>");
        Console.Error.WriteLine("       Injector restore <_system.g> <output/_system.g>");
        return 2;
    }

    if (args[0] == "inject")
    {
        ArchiveInjector.Inject(args[1], args[2], args[3]);
    }
    else if (args[0] == "update")
    {
        ArchiveInjector.UpdateLoader(args[1], args[2], args[3]);
    }
    else
    {
        ArchiveInjector.Restore(args[1], args[2]);
    }
    Console.WriteLine("Archive written: " + Path.GetFullPath(args[^1]));
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or DecoderFallbackException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
