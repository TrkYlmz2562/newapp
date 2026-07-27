using System.Buffers.Binary;
using System.Text;
using FocusAI.Domain.Audio;
using Xunit;

namespace FocusAI.UnitTests.Audio;

/// <summary>
/// The forty-four bytes that decide whether the browser plays the file or refuses it.
/// </summary>
/// <remarks>
/// There is no partial credit here: a header that is wrong in one field does not
/// produce distorted audio, it produces an &lt;audio&gt; element reporting an
/// unsupported source. And the failure only shows up on a device, after a
/// rendering has already been paid for — so the layout is pinned field by field.
/// </remarks>
public class WavWriterTests
{
    private static byte[] Pcm(int samples) => new byte[samples * 2];

    private static string Ascii(ReadOnlySpan<byte> bytes) => Encoding.ASCII.GetString(bytes);

    [Fact]
    public void The_header_declares_riff_wave_and_a_pcm_fmt_chunk()
    {
        var wav = WavWriter.FromPcm(Pcm(100));

        Assert.Equal("RIFF", Ascii(wav.AsSpan(0, 4)));
        Assert.Equal("WAVE", Ascii(wav.AsSpan(8, 4)));
        Assert.Equal("fmt ", Ascii(wav.AsSpan(12, 4)));
        Assert.Equal("data", Ascii(wav.AsSpan(36, 4)));

        // 16-byte fmt chunk, format 1 = uncompressed PCM.
        Assert.Equal(16u, BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(16, 4)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(20, 2)));
    }

    [Fact]
    public void The_two_declared_sizes_describe_the_actual_file()
    {
        // Both are off-by-something magnets: the RIFF size excludes its own eight
        // bytes, and the data size counts samples only.
        var pcm = Pcm(1_000);
        var wav = WavWriter.FromPcm(pcm);

        Assert.Equal((uint)(wav.Length - 8), BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(4, 4)));
        Assert.Equal((uint)pcm.Length, BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(40, 4)));
        Assert.Equal(44 + pcm.Length, wav.Length);
    }

    [Fact]
    public void The_rate_block_align_and_byte_rate_agree_with_each_other()
    {
        // Gemini's format: 24 kHz, 16-bit, mono. A player that trusts byteRate and
        // a player that computes it from rate × blockAlign must reach the same
        // answer, or the timeline drifts against the audio.
        var wav = WavWriter.FromPcm(Pcm(10), sampleRate: 24_000, channels: 1);

        var channels = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22, 2));
        var rate = BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(24, 4));
        var byteRate = BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(28, 4));
        var blockAlign = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(32, 2));
        var bits = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34, 2));

        Assert.Equal(1, channels);
        Assert.Equal(24_000u, rate);
        Assert.Equal(16, bits);
        Assert.Equal(2, blockAlign);
        Assert.Equal(rate * (uint)blockAlign, byteRate);
    }

    [Fact]
    public void The_samples_survive_the_wrapping_byte_for_byte()
    {
        var pcm = new byte[] { 1, 2, 3, 4, 250, 251, 252, 253 };
        var wav = WavWriter.FromPcm(pcm);

        Assert.Equal(pcm, wav.AsSpan(44).ToArray());
    }

    [Fact]
    public void Empty_input_still_produces_a_valid_and_empty_file()
    {
        var wav = WavWriter.FromPcm([]);

        Assert.Equal(44, wav.Length);
        Assert.True(WavWriter.IsWav(wav));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(40, 4)));
    }

    [Theory]
    [InlineData(48_000, 2)]
    [InlineData(16_000, 1)]
    public void Other_formats_are_described_correctly_too(int sampleRate, short channels)
    {
        var wav = WavWriter.FromPcm(Pcm(64), sampleRate, channels);

        Assert.Equal((uint)sampleRate, BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(24, 4)));
        Assert.Equal(channels, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22, 2)));
        Assert.Equal((short)(channels * 2), BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(32, 2)));
    }

    [Fact]
    public void Already_wrapped_audio_is_recognised_so_it_is_not_wrapped_twice()
    {
        // The synthesizer checks this before adding a header. Wrapping a wav in a
        // wav yields a file whose first chunk is nonsense.
        Assert.True(WavWriter.IsWav(WavWriter.FromPcm(Pcm(8))));
        Assert.False(WavWriter.IsWav(Pcm(8)));
        Assert.False(WavWriter.IsWav([1, 2, 3]));
    }

    [Fact]
    public void Duration_is_derived_from_the_sample_count()
    {
        // One second of 24 kHz 16-bit mono is 48,000 bytes.
        Assert.Equal(1d, WavWriter.Duration(48_000).TotalSeconds, 3);
        Assert.Equal(0d, WavWriter.Duration(0).TotalSeconds, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_nonsensical_rate_is_refused_rather_than_written(int sampleRate) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WavWriter.FromPcm(Pcm(4), sampleRate));
}
