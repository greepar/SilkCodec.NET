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

## Upstream Reference

The managed SILK codec implementation in this project is based on the SILK implementation from [Jitsi/libjitsi](https://github.com/jitsi/libjitsi), licensed under the Apache License 2.0.
