using System.Buffers.Binary;

namespace FocusAI.Domain.Audio;

/// <summary>
/// Wraps raw PCM samples in the 44-byte RIFF header that makes them a .wav file.
/// </summary>
/// <remarks>
/// Gemini returns headerless PCM — 24 kHz, 16-bit, mono — which is the audio and
/// nothing else. No browser will play it: an &lt;audio&gt; element handed a bare
/// sample stream has no way to know the rate, the width or the channel count, so
/// it reports an unsupported source and stops. The header carries exactly those
/// three facts, and adding it is the whole difference between a file that plays
/// and one that does not.
///
/// Written out rather than taken from a library because it is forty-four bytes of
/// well-specified layout, and a dependency for it would be larger than the code.
/// </remarks>
public static class WavWriter
{
    private const int HeaderLength = 44;
    private const short PcmFormat = 1;
    private const short BitsPerSample = 16;

    /// <summary>
    /// Returns <paramref name="pcm"/> prefixed with a RIFF/WAVE header describing it.
    /// </summary>
    public static byte[] FromPcm(ReadOnlySpan<byte> pcm, int sampleRate = 24_000, short channels = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);

        var blockAlign = (short)(channels * BitsPerSample / 8);
        var byteRate = sampleRate * blockAlign;

        var file = new byte[HeaderLength + pcm.Length];
        var header = file.AsSpan(0, HeaderLength);

        // RIFF chunk. The declared size counts everything after this field, which
        // is the file minus the eight bytes of "RIFF" and the size itself.
        "RIFF"u8.CopyTo(header[..4]);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4, 4), (uint)(file.Length - 8));
        "WAVE"u8.CopyTo(header.Slice(8, 4));

        // fmt chunk: 16 bytes of PCM description.
        "fmt "u8.CopyTo(header.Slice(12, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(20, 2), PcmFormat);
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(22, 2), channels);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(24, 4), (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(28, 4), (uint)byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(32, 2), blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(34, 2), BitsPerSample);

        // data chunk: the samples, exactly as they arrived.
        "data"u8.CopyTo(header.Slice(36, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(40, 4), (uint)pcm.Length);

        pcm.CopyTo(file.AsSpan(HeaderLength));

        return file;
    }

    /// <summary>
    /// How long the reading runs, from the size of the samples. Exact rather than
    /// estimated — the player needs a duration before it has decoded anything.
    /// </summary>
    public static TimeSpan Duration(int pcmLength, int sampleRate = 24_000, short channels = 1)
    {
        var bytesPerSecond = sampleRate * channels * BitsPerSample / 8;

        return bytesPerSecond <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds((double)pcmLength / bytesPerSecond);
    }

    /// <summary>True when these bytes already carry a RIFF/WAVE header.</summary>
    public static bool IsWav(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 12 &&
        bytes[..4].SequenceEqual("RIFF"u8) &&
        bytes.Slice(8, 4).SequenceEqual("WAVE"u8);

    /// <summary>Kept next to the writer so callers do not spell the type themselves.</summary>
    public const string ContentType = "audio/wav";
}
