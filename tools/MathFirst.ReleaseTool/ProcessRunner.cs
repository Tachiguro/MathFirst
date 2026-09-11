namespace MathFirst.ReleaseTool;

using System.ComponentModel;
using System.Diagnostics;

public sealed class ProcessRunner : IProcessRunner
{
    public ProcessResult Run(ProcessInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.FileName,
            WorkingDirectory = invocation.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new ReleaseToolException($"Could not start required command '{invocation.FileName}'.");
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            return new ProcessResult(
                process.ExitCode,
                standardOutput.GetAwaiter().GetResult(),
                standardError.GetAwaiter().GetResult());
        }
        catch (Win32Exception exception)
        {
            throw new ReleaseToolException(
                $"Could not start required command '{invocation.FileName}': {exception.Message}");
        }
    }
}

public static class ProcessResultExtensions
{
    public static ProcessResult EnsureSuccess(this ProcessResult result, string commandName)
    {
        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? string.Empty
                : $" {result.StandardError.Trim()}";
            throw new ReleaseToolException(
                $"Required command '{commandName}' failed with exit code {result.ExitCode}.{detail}");
        }

        return result;
    }
}
