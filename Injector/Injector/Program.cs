using EastwardLib.Assets;

if (args.Length < 2)
{
    Console.WriteLine("usage: arg0 for _system.g path, arg1 for main.lua path");
    return;
}

if (!File.Exists(args[0]))
{
    Console.WriteLine("arg0 file not exist");
    return;
}

if (!File.Exists(args[1]))
{
    Console.WriteLine("arg1 file not exist");
    return;
}

var g = GArchive.Read(args[0]);
g[$"{Path.GetFileNameWithoutExtension(args[0])}/main.lua"] = new TextAsset(File.ReadAllText(args[1])).Encode();
File.Delete($"{args[0]}.bk");
File.Move(args[0], $"{args[0]}.bk");
g.Write(args[0]);