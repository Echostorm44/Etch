namespace Etch.SceneReplayer;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Etch scene replayer");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  etch-replay render <scene> --backend=cpu|gpu --out=<png> [--width W --height H] [--scale S]");
            Console.WriteLine("  etch-replay dump <scene>");
            Console.WriteLine("  etch-replay validate <scene>");
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        string[] commandArgs = args.Length > 1 ? args[1..] : Array.Empty<string>();

        switch (command)
        {
            case "render":
                return Commands.RenderCommand.Run(commandArgs);
            case "dump":
                return Commands.DumpCommand.Run(commandArgs);
            case "validate":
                return Commands.ValidateCommand.Run(commandArgs);
            default:
                Console.WriteLine($"Unknown command: {command}");
                return 1;
        }
    }
}
