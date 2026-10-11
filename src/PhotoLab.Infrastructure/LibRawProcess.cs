using System.Diagnostics;

namespace PhotoLab.Infrastructure;

internal sealed record LibRawProcessResult(int ExitCode, string Output, string Error);

/// <summary>Drains both pipes and kills owned native process trees on cancellation or timeout.</summary>
internal static class LibRawProcess
{
    public static async Task<LibRawProcessResult> RunAsync(string executable, IEnumerable<string> arguments,
        TimeSpan timeout, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(timeout);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("LibRaw tool could not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        try
        {
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { throw new TimeoutException("LibRaw tool timed out: " + Path.GetFileName(executable)); }
            return new LibRawProcessResult(process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
                catch (InvalidOperationException) { } // The process can exit between HasExited and Kill.
            }
            // Observe pipe completion even on cancellation so no reader remains on a disposed process.
            await Task.WhenAll(output, error);
        }
    }
}
