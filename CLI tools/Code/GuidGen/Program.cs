// Hand-rolled arg parsing replaces ConsoleAppFramework to eliminate the only
// external dependency and reduce DLL size (~31 KB -> ~5-8 KB managed,
// and a smaller native binary under AOT thanks to less BCL surface area).

var format = "D";
var upper = false;
var count = 1;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-f":
        case "--format":
            if (++i < args.Length)
                format = args[i];
            else
            {
                Console.Error.WriteLine("Error: --format requires a value (N, D, B, P, X).");
                return 1;
            }
            break;
        case "-u":
        case "--upper":
            upper = true;
            break;
        case "-c":
        case "--count":
            if (++i < args.Length && int.TryParse(args[i], out var n) && n > 0)
                count = n;
            else
            {
                Console.Error.WriteLine("Error: --count requires a positive integer.");
                return 1;
            }
            break;
        case "-h":
        case "--help":
            PrintHelp();
            return 0;
        case "--version":
            Console.WriteLine("1.0.0");
            return 0;
        default:
            Console.Error.WriteLine($"Error: unknown option '{args[i]}'. Use --help for usage.");
            return 1;
    }
}

for (int i = 0; i < count; i++)
{
    var guid = Guid.NewGuid().ToString(format);
    Console.WriteLine(upper ? guid.ToUpperInvariant() : guid);
}

return 0;

static void PrintHelp()
{
    Console.WriteLine("""
        GuidGen — Generate GUIDs in various formats.

        Usage: guidgen [options]

        Options:
          -f, --format <FORMAT>  GUID format: N (digits only), D (hyphens),
                                 B (braces), P (parentheses), X (hex).  [default: D]
          -u, --upper            Output in uppercase.
          -c, --count <COUNT>    Number of GUIDs to generate.            [default: 1]
          -h, --help             Show this help message.
          --version              Show version information.
        """);
}
