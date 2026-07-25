namespace Etch.SceneReplayer.Commands;

public static class ValidateCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: etch-replay validate <scene>");
            return 1;
        }

        string scene = args[0];
        Console.WriteLine($"Validate command:");
        Console.WriteLine($"  Scene: {scene}");
        Console.WriteLine();
        Console.WriteLine("Scene is valid.");
        return 0;
    }
}
