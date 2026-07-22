using SilkCodec.NET.Managed;

namespace SilkCodec.NET;

internal sealed class JitsiSilkEncoder : IDisposable
{
    private const int MaxPacketBytes = 1_250;

    private readonly SKP_Silk_encoder_state_FLP _state = new();
    private readonly SKP_SILK_SDK_EncControlStruct _control = new();
    private bool _disposed;

    public JitsiSilkEncoder(SilkEncoderOptions options)
    {
        _control.API_sampleRate = options.SampleRate;
        _control.maxInternalSampleRate = options.MaxInternalSampleRate;
        _control.packetSize = options.PacketLengthMilliseconds * options.SampleRate / 1000;
        _control.bitRate = options.BitRate;
        _control.packetLossPercentage = options.PacketLossPercentage;
        _control.complexity = options.Complexity;
        _control.useInBandFEC = options.UseInBandFec ? 1 : 0;
        _control.useDTX = options.UseDtx ? 1 : 0;

        ThrowOnError(EncAPI.SKP_Silk_SDK_InitEncoder(_state, new SKP_SILK_SDK_EncControlStruct()));
    }

    public byte[] EncodeFrame(ReadOnlySpan<short> samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var input = samples.ToArray();
        var output = new byte[MaxPacketBytes];
        var outputLength = new short[] { MaxPacketBytes };
        ThrowOnError(EncAPI.SKP_Silk_SDK_Encode(
            _state, _control, input, 0, input.Length, output, 0, outputLength));

        var packet = new byte[outputLength[0]];
        output.AsSpan(0, packet.Length).CopyTo(packet);
        return packet;
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private static void ThrowOnError(int result)
    {
        if (result != 0)
            throw new InvalidOperationException($"The managed SILK encoder returned error {result}.");
    }
}
