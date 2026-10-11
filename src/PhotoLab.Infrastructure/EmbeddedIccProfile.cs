using System.Buffers.Binary;
using System.IO.Compression;

namespace PhotoLab.Infrastructure;

/// <summary>Read original PNG iCCP/JPEG APP2 ICC bytes, including LUT tags Skia may not retain.</summary>
public static class EmbeddedIccProfile
{
    private const int MAX_PROFILE_BYTES = 16 * 1024 * 1024;

    public static byte[] Read(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[8];
        stream.ReadExactly(header);
        if (header.SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) return ReadPng(stream);
        if (header[0] == 255 && header[1] == 216) { stream.Position = 2;return ReadJpeg(stream); }
        throw new InvalidDataException("ICC extraction supports JPEG and PNG only.");
    }

    private static byte[] ReadPng(Stream stream)
    {
        Span<byte> header = stackalloc byte[8];
        while (stream.Position < stream.Length)
        {
            stream.ReadExactly(header);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (length > stream.Length - stream.Position - 4) throw new InvalidDataException("Truncated PNG chunk.");
            if (header[4..].SequenceEqual("iCCP"u8))
            {
                if (length > MAX_PROFILE_BYTES) throw new InvalidDataException("ICC chunk is too large.");
                var data = new byte[(int)length];stream.ReadExactly(data);
                int end = Array.IndexOf(data,(byte)0);
                if (end < 1 || end > 79 || end + 2 >= data.Length || data[end+1] != 0)
                    throw new InvalidDataException("Invalid PNG ICC compression header.");
                using var compressed = new MemoryStream(data,end+2,data.Length-end-2,writable:false);
                using var zlib = new ZLibStream(compressed,CompressionMode.Decompress);
                using var profile = new MemoryStream();
                var block = new byte[8192];
                int count;
                while ((count=zlib.Read(block)) != 0)
                {
                    if (profile.Length + count > MAX_PROFILE_BYTES) throw new InvalidDataException("ICC profile is too large.");
                    profile.Write(block,0,count);
                }
                var result=profile.ToArray();
                if (result.Length==0) throw new InvalidDataException("Empty embedded ICC profile.");
                return result;
            }
            if (header[4..].SequenceEqual("IDAT"u8) || header[4..].SequenceEqual("IEND"u8)) return [];
            stream.Seek(length+4L,SeekOrigin.Current);
        }
        return [];
    }

    private static byte[] ReadJpeg(Stream stream)
    {
        var parts = new SortedDictionary<int,byte[]>();
        int expected = 0, total = 0;
        Span<byte> size = stackalloc byte[2];
        while (stream.Position < stream.Length)
        {
            if (stream.ReadByte()!=255) throw new InvalidDataException("Invalid JPEG marker.");
            int marker;do { marker=stream.ReadByte(); } while (marker==255);
            if (marker<0) throw new EndOfStreamException();
            if (marker==0xDA || marker==0xD9) break;
            if (marker==0x01 || marker is >=0xD0 and <=0xD7) continue;
            stream.ReadExactly(size);
            int length=BinaryPrimitives.ReadUInt16BigEndian(size)-2;
            if (length<0 || length>stream.Length-stream.Position) throw new InvalidDataException("Truncated JPEG segment.");
            if (marker!=0xE2) { stream.Seek(length,SeekOrigin.Current);continue; }
            var data=new byte[length];stream.ReadExactly(data);
            if (data.Length<12 || !data.AsSpan(0,12).SequenceEqual("ICC_PROFILE\0"u8)) continue;
            if (data.Length<15 || data[12]==0 || data[13]==0 || data[12]>data[13] ||
                (expected!=0 && expected!=data[13]) || parts.ContainsKey(data[12]))
                throw new InvalidDataException("Invalid JPEG ICC segment sequence.");
            expected=data[13];total=checked(total+data.Length-14);
            if (total>MAX_PROFILE_BYTES) throw new InvalidDataException("ICC profile is too large.");
            parts.Add(data[12],data[14..]);
        }
        if (expected==0) return [];
        if (parts.Count!=expected) throw new InvalidDataException("Missing JPEG ICC segments.");
        var profile=new byte[total];int offset=0;
        foreach (var part in parts.Values) { part.CopyTo(profile,offset);offset+=part.Length; }
        return profile;
    }
}
