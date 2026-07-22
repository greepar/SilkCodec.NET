using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace SilkCodec.NET.Tests;

public sealed class SilkEncoderTests
{
    [Fact]
    public void EncodeProducesSilkV3Packets()
    {
        const int sampleRate = 24_000;
        var pcm = CreateSineWave(sampleRate, 200);
        var encoded = new SilkEncoder().Encode(pcm);

        Assert.Equal("#!SILK_V3", Encoding.ASCII.GetString(encoded, 0, 9));

        var offset = 9;
        var packetCount = 0;
        while (true)
        {
            var packetLength = BinaryPrimitives.ReadInt16LittleEndian(encoded.AsSpan(offset, 2));
            offset += 2;
            if (packetLength == -1)
                break;

            Assert.InRange(packetLength, (short)1, short.MaxValue);
            offset += packetLength;
            packetCount++;
        }

        Assert.Equal(10, packetCount);
        Assert.Equal(encoded.Length, offset);
    }

    [Fact]
    public void TencentOutputHasPrefixAndNoTerminator()
    {
        var pcm = CreateSineWave(16_000, 20);
        var encoded = new SilkEncoder(new SilkEncoderOptions
        {
            SampleRate = 16_000,
            MaxInternalSampleRate = 16_000,
            Tencent = true
        }).Encode(pcm);

        Assert.Equal(0x02, encoded[0]);
        Assert.Equal("#!SILK_V3", Encoding.ASCII.GetString(encoded, 1, 9));
        var packetLength = BinaryPrimitives.ReadInt16LittleEndian(encoded.AsSpan(10, 2));
        Assert.Equal(encoded.Length, 12 + packetLength);
    }

    private static short[] CreateSineWave(int sampleRate, int durationMilliseconds)
    {
        var samples = new short[sampleRate * durationMilliseconds / 1000];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (short)(Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 8_000);
        return samples;
    }
}
