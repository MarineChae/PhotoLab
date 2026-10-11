using PhotoLab.Imaging.Decoding;

/// <summary>Tests explicit development capability, forwarding and late cancellation independently of LibRaw.</summary>
sealed class StubRawDeveloper : IRawDeveloper
{
    private readonly Func<string, CameraWhiteBalance, CancellationToken, Task<RawImage>> _develop;

    public StubRawDeveloper(Func<string, CameraWhiteBalance, CancellationToken, Task<RawImage>> develop)
        => _develop = develop;

    public Task<RawImage> DecodeAsync(string sourcePath, CancellationToken cancellation = default)
        => throw new InvalidOperationException("Explicit development was expected.");

    public Task<RawImage> DevelopAsync(string sourcePath, CameraWhiteBalance whiteBalance,
        CancellationToken cancellation = default) => _develop(sourcePath, whiteBalance, cancellation);
}
