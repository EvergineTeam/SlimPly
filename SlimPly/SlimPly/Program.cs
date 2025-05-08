using System.Diagnostics;

namespace SlimPly;

internal static class Program
{
    /// <summary>
    /// The entry point of the SlimPly application.
    /// </summary>
    /// <param name="args">Command-line arguments. Expects two arguments: 
    /// the input PLY file path and the output compressed PLY file path.</param>
    /// <returns>An integer representing the exit code. Returns 0 on success, 1 on error.</returns>
    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("SlimPly 1.0");

        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: SlimPly <input.ply> <output.compressed.ply>");
            return 1;
        }

        var input = args[0];
        var output = args[1];

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var ply = await PlyReader.ReadAsync(input);
            var splat = new Splat(ply);

            await CompressedPlyWriter.WriteAsync(output, splat);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"error: {e}");
            return 1;
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine($"Execution time: {stopwatch.ElapsedMilliseconds} ms");
        }

        Console.WriteLine("done.");
        return 0;
    }
}