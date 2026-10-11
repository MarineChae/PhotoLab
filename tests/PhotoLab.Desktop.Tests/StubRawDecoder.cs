using PhotoLab.Imaging.Decoding;

/// <summary>Exercises decoder replacement, failures and non-cooperative completion without native processes.</summary>
sealed class StubRawDecoder : IRawDecoder
{
    private readonly Func<string, CancellationToken, Task<RawImage>> _decode;

    public StubRawDecoder(Func<string, CancellationToken, Task<RawImage>> decode) => _decode = decode;

    public Task<RawImage> DecodeAsync(string sourcePath, CancellationToken cancellation = default)
        => _decode(sourcePath, cancellation);
}
