namespace SilkCodec.NET;

public sealed class SilkEncoderOptions
{
    public int SampleRate { get; init; } = 24_000;

    public int MaxInternalSampleRate { get; init; } = 24_000;

    public int BitRate { get; init; } = 25_000;

    public int PacketLengthMilliseconds { get; init; } = 20;

    public int PacketLossPercentage { get; init; }

    public int Complexity { get; init; } = 2;

    public bool UseInBandFec { get; init; }

    public bool UseDtx { get; init; }

    public bool Tencent { get; init; }

    internal void Validate()
    {
        if (SampleRate is not (8_000 or 12_000 or 16_000 or 24_000 or 32_000 or 44_100 or 48_000))
            throw new ArgumentOutOfRangeException(nameof(SampleRate), "Supported sample rates are 8000, 12000, 16000, 24000, 32000, 44100 and 48000 Hz.");

        if (MaxInternalSampleRate is not (8_000 or 12_000 or 16_000 or 24_000))
            throw new ArgumentOutOfRangeException(nameof(MaxInternalSampleRate), "The internal sample rate must be 8000, 12000, 16000 or 24000 Hz.");

        if (MaxInternalSampleRate > SampleRate)
            throw new ArgumentOutOfRangeException(nameof(MaxInternalSampleRate), "The internal sample rate cannot exceed the input sample rate.");

        if (PacketLengthMilliseconds is not (20 or 40 or 60 or 80 or 100))
            throw new ArgumentOutOfRangeException(nameof(PacketLengthMilliseconds), "Packet length must be 20, 40, 60, 80 or 100 ms.");

        if (BitRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(BitRate));

        if (PacketLossPercentage is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(PacketLossPercentage));

        if (Complexity is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(Complexity));
    }
}
