using SilkCodec.NET.Managed;

namespace SilkCodec.NET;

internal sealed class JitsiSilkEncoder : IDisposable
{
    private const int MaxPacketBytes = 1_250;

    private readonly SKP_Silk_encoder_state_FLP _state = new();
    private readonly SKP_SILK_SDK_EncControlStruct _control = new();
    private readonly short[] _input;
    private readonly byte[] _output = new byte[MaxPacketBytes];
    private readonly short[] _outputLength = new short[1];
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
        _input = new short[_control.packetSize];

        ThrowOnError(EncAPI.SKP_Silk_SDK_InitEncoder(_state, new SKP_SILK_SDK_EncControlStruct()));
    }

    public ReadOnlySpan<byte> EncodeFrame(ReadOnlySpan<short> samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (samples.Length > _input.Length)
            throw new ArgumentException("The frame contains more samples than the configured packet size.", nameof(samples));

        samples.CopyTo(_input);
        _outputLength[0] = MaxPacketBytes;
        ThrowOnError(EncAPI.SKP_Silk_SDK_Encode(
            _state, _control, _input, 0, samples.Length, _output, 0, _outputLength));

        return _output.AsSpan(0, _outputLength[0]);
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
