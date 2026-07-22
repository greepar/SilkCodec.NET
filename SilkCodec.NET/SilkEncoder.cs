using System.Buffers.Binary;

namespace SilkCodec.NET;

/// <summary>Encodes mono signed 16-bit PCM to a legacy SILK v3 stream.</summary>
public sealed class SilkEncoder
{
    private static ReadOnlySpan<byte> Header => "#!SILK_V3"u8;

    private readonly SilkEncoderOptions _options;

    public SilkEncoder(SilkEncoderOptions? options = null)
    {
        _options = options ?? new SilkEncoderOptions();
        _options.Validate();
    }

    public byte[] Encode(ReadOnlySpan<short> pcm)
    {
        using var output = new MemoryStream();
        Encode(pcm, output);
        return output.ToArray();
    }

    public byte[] EncodePcm16LittleEndian(ReadOnlySpan<byte> pcm)
    {
        if ((pcm.Length & 1) != 0)
            throw new ArgumentException("PCM byte length must be even.", nameof(pcm));

        var samples = new short[pcm.Length / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i * 2, 2));

        return Encode(samples);
    }

    public void Encode(ReadOnlySpan<short> pcm, Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite)
            throw new ArgumentException("The output stream must be writable.", nameof(output));

        if (_options.Tencent)
            output.WriteByte(0x02);
        output.Write(Header);

        var frameSampleCount = _options.SampleRate / 50;
        var completeSampleCount = pcm.Length - pcm.Length % frameSampleCount;
        Span<byte> packetLength = stackalloc byte[2];
        using var encoder = new JitsiSilkEncoder(_options);

        for (var offset = 0; offset < completeSampleCount; offset += frameSampleCount)
        {
            var packet = encoder.EncodeFrame(pcm.Slice(offset, frameSampleCount));
            if (packet.Length == 0)
                continue;

            if (packet.Length > short.MaxValue)
                throw new InvalidDataException("SILK packet is too large.");

            BinaryPrimitives.WriteInt16LittleEndian(packetLength, (short)packet.Length);
            output.Write(packetLength);
            output.Write(packet);
        }

        if (!_options.Tencent)
        {
            Span<byte> terminator = stackalloc byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(terminator, -1);
            output.Write(terminator);
        }
    }
}
