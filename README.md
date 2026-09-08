# SilkCodec.NET

Pure C# legacy SILK v3 encoding for .NET 8 and .NET 10.

[![NuGet](https://img.shields.io/nuget/v/SilkCodec.NET.svg)](https://www.nuget.org/packages/SilkCodec.NET)

## Usage

```csharp
using SilkCodec.NET;

short[] monoPcm = GetMonoPcm16();

var encoder = new SilkEncoder(new SilkEncoderOptions
{
    SampleRate = 24_000,
    MaxInternalSampleRate = 24_000,
    BitRate = 25_000,
    PacketLengthMilliseconds = 20,
    Complexity = 2,
    Tencent = true
});

byte[] silk = encoder.Encode(monoPcm);
File.WriteAllBytes("voice.silk", silk);
```

`EncodePcm16LittleEndian` accepts raw mono signed 16-bit little-endian PCM bytes. Incomplete final 20 ms frames are discarded, matching the reference `silk-codec/test/Encoder.c` behavior.

Standard output starts with `#!SILK_V3`, stores each packet as a little-endian signed 16-bit length followed by payload, and ends with `-1`. Tencent output adds a leading `0x02` and omits the terminator.

## MP3 Conversion Demo

Run the console demo and enter an MP3 path when prompted:

```shell
dotnet run --project SilkCodec.NET.Demo
```

The demo decodes MP3 with the managed `NLayer` decoder, downmixes it to mono PCM16, and exports Tencent SILK v3 by default. It also prompts for the output path, bitrate, and container format so different quality settings can be compared. The default bitrate is 100 kbps for maximum quality; music should use at least 32 kbps to avoid the encoder switching from 24 kHz super-wideband mode to 16 kHz wideband mode.

It can also run non-interactively:

```shell
dotnet run --project SilkCodec.NET.Demo -- input.mp3 output.silk 100000 tencent flat
```

Use `standard` as the final argument for a standard `#!SILK_V3` stream. Use `tencent` for the common WeChat/QQ-compatible stream with the leading `0x02` byte.

The optional fifth argument is `flat` or `music`. `flat` is the default and preserves the source spectrum as closely as the SILK codec permits. The optional `music` profile applies a gentle 60 Hz high-pass filter and a 2.5 dB presence boost around 4.5 kHz; it sounds brighter but can also emphasize SILK quantization noise, so it is not recommended when faithful reproduction is the priority.

Legacy SILK is a mono speech codec with a maximum 24 kHz internal sample rate (approximately 12 kHz audio bandwidth). Even at high bitrates it cannot preserve stereo or CD-quality music bandwidth; use Opus/AAC when the playback system permits it.

## Upstream Reference

The managed SILK codec implementation in this project is based on the SILK implementation from [Jitsi/libjitsi](https://github.com/jitsi/libjitsi), licensed under the Apache License 2.0. Warped noise shaping and related encoder behavior were adapted from the SILK SDK 1.0.9 reference implementation, licensed under the BSD 3-Clause Clear License. See `THIRD-PARTY-NOTICES` for details.
