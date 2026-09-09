using System.Buffers.Binary;
using NLayer;

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

    /// <summary>Decodes an MP3 file, preprocesses it as mono 24 kHz audio, and encodes it to SILK.</summary>
    public byte[] EncodeMp3(string mp3Path, SilkMp3AudioProfile profile = SilkMp3AudioProfile.Flat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mp3Path);
        using var input = File.OpenRead(mp3Path);
        return EncodeMp3(input, profile);
    }

    /// <summary>Decodes an MP3 stream, preprocesses it as mono 24 kHz audio, and encodes it to SILK.</summary>
    /// <remarks>The input stream remains open. The complete decoded audio is buffered in memory.</remarks>
    public byte[] EncodeMp3(Stream mp3, SilkMp3AudioProfile profile = SilkMp3AudioProfile.Flat)
    {
        using var output = new MemoryStream();
        EncodeMp3(mp3, output, profile);
        return output.ToArray();
    }

    /// <summary>Converts an MP3 file directly to a SILK file.</summary>
    public void EncodeMp3(
        string mp3Path,
        string silkPath,
        SilkMp3AudioProfile profile = SilkMp3AudioProfile.Flat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mp3Path);
        ArgumentException.ThrowIfNullOrWhiteSpace(silkPath);
        ValidateMp3Options(profile);

        var fullInputPath = Path.GetFullPath(mp3Path);
        var fullOutputPath = Path.GetFullPath(silkPath);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(fullInputPath, fullOutputPath, pathComparison))
            throw new ArgumentException("The MP3 input and SILK output paths must be different.", nameof(silkPath));

        var outputDirectory = Path.GetDirectoryName(fullOutputPath)!;
        var temporaryPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(fullOutputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var input = File.OpenRead(fullInputPath))
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                EncodeMp3(input, output, profile);

            File.Move(temporaryPath, fullOutputPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    /// <summary>Decodes an MP3 stream and writes the encoded SILK stream.</summary>
    /// <remarks>
    /// Both streams remain open. Input is consumed and output is written from their current positions.
    /// The complete decoded audio is buffered in memory.
    /// </remarks>
    public void EncodeMp3(
        Stream mp3,
        Stream output,
        SilkMp3AudioProfile profile = SilkMp3AudioProfile.Flat)
    {
        ArgumentNullException.ThrowIfNull(mp3);
        ArgumentNullException.ThrowIfNull(output);
        if (ReferenceEquals(mp3, output))
            throw new ArgumentException("The MP3 input and SILK output streams must be different.", nameof(output));
        if (!mp3.CanRead)
            throw new ArgumentException("The MP3 stream must be readable.", nameof(mp3));
        if (!output.CanWrite)
            throw new ArgumentException("The output stream must be writable.", nameof(output));
        ValidateMp3Options(profile);

        using var decoder = new MpegFile(mp3);
        var pcm = DecodeMp3(decoder, profile);
        Encode(pcm, output);
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

    private static short[] DecodeMp3(MpegFile decoder, SilkMp3AudioProfile profile)
    {
        var channels = decoder.Channels;
        if (channels <= 0)
            throw new InvalidDataException("The MP3 channel count is invalid.");

        var decoded = new float[1152 * channels];
        var monoSamples = new List<float>();
        int samplesRead;
        while ((samplesRead = decoder.ReadSamples(decoded, 0, decoded.Length)) > 0)
        {
            var completeSamples = samplesRead - samplesRead % channels;
            for (var offset = 0; offset < completeSamples; offset += channels)
            {
                double sum = 0;
                for (var channel = 0; channel < channels; channel++)
                    sum += decoded[offset + channel];

                monoSamples.Add((float)(sum / channels));
            }
        }

        if (monoSamples.Count == 0)
            throw new InvalidDataException("The MP3 stream contains no decodable audio.");

        var processed = AudioPreprocessor.ProcessMono(monoSamples, decoder.SampleRate, 24_000, profile);
        return AudioPreprocessor.ToPcm16(processed);
    }

    private void ValidateMp3Options(SilkMp3AudioProfile profile)
    {
        if (!Enum.IsDefined(profile))
            throw new ArgumentOutOfRangeException(nameof(profile));
        if (_options.SampleRate != 24_000 || _options.MaxInternalSampleRate != 24_000)
            throw new InvalidOperationException("MP3 encoding requires SampleRate and MaxInternalSampleRate to be 24000 Hz.");
    }
}
