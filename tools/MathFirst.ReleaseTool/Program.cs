namespace MathFirst.ReleaseTool;

internal static class Program
{
    public static Task<int> Main(string[] args) => ReleaseCli.RunAsync(args);
}
