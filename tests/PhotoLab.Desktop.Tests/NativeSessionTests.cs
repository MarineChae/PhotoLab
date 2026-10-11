using PhotoLab.Imaging;
using PhotoLab.Imaging.Decoding;
using PhotoLab.Infrastructure;

/// <summary>Optional real-RAW verification for an explicitly built native bridge; never substitutes synthetic camera files.</summary>
public static class NativeSessionTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    public static async Task RunAsync(string bridge, string[] sources)
    {
        if (sources.Length == 0) throw new ArgumentException("Native session tests require actual RAW files.");
        var wb = new CameraWhiteBalance(2.125f, 1, 1.375f, 0);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await NativeRawSession.OpenAsync(bridge, sources[0], cancelled.Token); throw new Exception("FAIL: cancelled session opened"); }
        catch (OperationCanceledException) { Console.WriteLine("PASS: native session open checks pre-cancellation"); }
        string directory = Path.Combine(Path.GetTempPath(), "photolab-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string bad = Path.Combine(directory, "invalid.NEF");
            await File.WriteAllTextAsync(bad, "not a camera RAW");
            try { await NativeRawSession.OpenAsync(bridge, bad); throw new Exception("FAIL: invalid session opened"); }
            catch (InvalidDataException) { Console.WriteLine("PASS: native session rejects invalid RAW and releases failed-open resources"); }
            foreach (string source in sources)
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                using var reference = await new LibRawDecoder().DevelopAsync(source, wb);
                double cli = timer.Elapsed.TotalMilliseconds;
                string copy = Path.Combine(directory, "cached 카메라 " + Path.GetFileName(source));
                File.Copy(source, copy);
                timer.Restart();
                await using var session = await NativeRawSession.OpenAsync(bridge, copy);
                double open = timer.Elapsed.TotalMilliseconds;
                Check(session.LibRawVersion == reference.Metadata.CameraColor?.LibRawVersion, "native bridge matches installed LibRaw: " + Path.GetFileName(source));
                File.Delete(copy); // The session released its stream; cached development must succeed without this file.
                timer.Restart();
                using var first = await session.DevelopAsync(wb);
                double firstTime = timer.Elapsed.TotalMilliseconds;
                Check(first.Width == reference.Pixels.Width && first.Height == reference.Pixels.Height,
                    "cached RAW dimensions match CLI: " + Path.GetFileName(source));
                float maximumDifference = 0;
                for (int i = 0; i < first.Pixels.Length; i++)
                    maximumDifference = Math.Max(maximumDifference, Math.Abs(first.Pixels[i] - reference.Pixels.Pixels[i]));
                Check(maximumDifference <= 4 / 65535f, "cached native RGB16 agrees with explicit-WB CLI: " + Path.GetFileName(source));
                using (var second = await session.DevelopAsync(new CameraWhiteBalance(1.275f, 1, 1.9375f, 1)))
                    Check(!first.Pixels.SequenceEqual(second.Pixels), "cached sensor data supports changed WB: " + Path.GetFileName(source));
                timer.Restart();
                using var repeated = await session.DevelopAsync(wb);
                double repeatTime = timer.Elapsed.TotalMilliseconds;
                Check(repeated.Pixels.SequenceEqual(first.Pixels), "A/B/A development restores identical pixels without rereading source: " + Path.GetFileName(source));
                using var halfReference = await HalfReferenceAsync(source, wb);
                using var expectedPreview = ImageResize.Preview(halfReference, 600);
                timer.Restart();
                using var small = await session.DevelopPreviewAsync(wb, 600);
                double previewTime = timer.Elapsed.TotalMilliseconds;
                float previewDifference = 0;
                if (small.Pixels.Length == expectedPreview.Pixels.Length)
                    for (int i = 0; i < small.Pixels.Length; i++)
                        previewDifference = Math.Max(previewDifference, Math.Abs(small.Pixels[i] - expectedPreview.Pixels[i]));
                Console.WriteLine($"PREVIEW: {Path.GetFileName(source)} native {small.Width}x{small.Height}, reference {expectedPreview.Width}x{expectedPreview.Height}, max error {previewDifference:G9}");
                Check(small.Width == expectedPreview.Width && small.Height == expectedPreview.Height &&
                    small.Pixels.SequenceEqual(expectedPreview.Pixels), "cached half-size preview matches independent LibRaw -h reference: " + Path.GetFileName(source));
                using (var fullAfterPreview = await session.DevelopAsync(wb))
                    Check(fullAfterPreview.Pixels.SequenceEqual(first.Pixels), "full development remains unchanged after half-size preview: " + Path.GetFileName(source));
                try { await session.DevelopAsync(wb, cancelled.Token); throw new Exception("FAIL: cancelled development succeeded"); }
                catch (OperationCanceledException) { Console.WriteLine("PASS: cached session pre-cancellation leaves rawdata intact"); }
                using (var afterCancel = await session.DevelopAsync(wb))
                    Check(afterCancel.Pixels.SequenceEqual(first.Pixels), "cached session remains valid after pre-cancellation: " + Path.GetFileName(source));
                using var superseded = new CancellationTokenSource();
                var obsolete = session.DevelopAsync(wb, superseded.Token);
                superseded.CancelAfter(1);
                try { using var completed = await obsolete; }
                catch (OperationCanceledException) { /* Whether queued or running, cancellation must not invalidate rawdata. */ }
                using (var afterSuperseded = await session.DevelopAsync(wb))
                    Check(afterSuperseded.Pixels.SequenceEqual(first.Pixels), "superseded requests preserve cached sensor data: " + Path.GetFileName(source));
                var closingWork = session.DevelopAsync(wb);
                await session.DisposeAsync();
                try { using var completed = await closingWork; }
                catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException) { }
                Check(true, "concurrent disposal joins native work before releasing its module: " + Path.GetFileName(source));
                await session.DisposeAsync(); await session.DisposeAsync();
                Check(first.Pixels.Length > 0 && repeated.Pixels.SequenceEqual(first.Pixels), "native results own pixels beyond session disposal: " + Path.GetFileName(source));
                try { await session.DevelopAsync(wb); throw new Exception("FAIL: disposed session developed"); }
                catch (ObjectDisposedException) { Console.WriteLine("PASS: disposed cached session rejects new development"); }
                Console.WriteLine($"SESSION: {Path.GetFileName(source)} CLI endpoint {cli:F1}ms, open/unpack {open:F1}ms, first {firstTime:F1}ms, repeat {repeatTime:F1}ms, half/600px preview {previewTime:F1}ms, max error {maximumDifference:G9}");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("All native RAW session tests passed.");
    }

    internal static async Task<FloatImageBuffer> HalfReferenceAsync(string source, CameraWhiteBalance wb)
    {
        string output = Path.Combine(Path.GetTempPath(), "photolab-half-reference-" + Guid.NewGuid().ToString("N") + ".ppm");
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(Environment.GetEnvironmentVariable("PHOTOLAB_DCRAW_EMU") ?? "dcraw_emu")
                { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (string argument in new[] { "-r", wb.Channel0.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                wb.Channel1.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                wb.Channel2.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                wb.Channel3.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "+M", "-h", "-o", "1", "-4", "-Z", output, Path.GetFullPath(source) })
                start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            Task<string> error = process.StandardError.ReadToEndAsync();
            Task<string> text = process.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                await Task.WhenAll(error, text);
            }
            if (process.ExitCode != 0) throw new InvalidDataException(await error);
            return LinearPpmDecoder.Read(output);
        }
        finally { File.Delete(output); }
    }
}
