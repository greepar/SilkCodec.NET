using Xunit;

namespace SilkCodec.NET.Tests;

public sealed class Mp3EncodingTests
{
    private static readonly byte[] Mp3Fixture = Convert.FromBase64String(
        "SUQzBAAAAAAAIlRTU0UAAAAOAAADTGF2ZjYzLjEuMTAxAAAAAAAAAAAAAAD/84TAAAAAAAAAAAAASW5mbwAAAA8AAAAHAAADYABVVVVVVVVVVVVVVVVVVXFxcXFxcXFxcXFxcXFxjo6Ojo6Ojo6Ojo6Ojo6qqqqqqqqqqqqqqqqqqqrHx8fHx8fHx8fHx8fHx+Pj4+Pj4+Pj4+Pj4+Pj//////////////////8AAAAATGF2YzYzLjEuAAAAAAAAAAAAAAAAJAJAAAAAAAAAA2AfTHwuAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/80TEABJ4anw/WBgAC7ZJaAztnbD13qnWO47wF/DOc0nM5TGEtGimxOH5fLKSkpKSkpMATB8HwffUCHB8/lAxKcoH+Hvf0e/o9/8MA+frAgIaAf5T39BBCCDCCAAAAI3/80TECRTpPoRXm2gAxZ7dT7phakdsVDBaeGDA0qO+KzJQwts9vXz4E1BaQnv47hhhhib/jlHCZD2Ht/5iXS6ZF4vf/mJdLqSRkj/BURBUFf+WCoKiI8CtDtaccAVD7Q//80TECBMQWjR/3gAAS8tyj6WtMCkIIwehejA2CbMGsMw5s17DL0DJMHkHwwOwNDA/AYBgAqAVNVpT9T3Hder02fv3xtejTRZ9n/0/+z/V6PpVrww1tQAEgCYGA0YekCb/80TEDhEQVhwA7/RA9N8mRwCmBAg+Jo/C5GYUqCDn4mGnNmKDFm0VFTuXDls5/+9X9P/fv3K/7WMs3HPV1FfT/37qlU4EezAAwBEDACRgFACIYEQCFGCwAZhhHwQ4YJr/80TEHBZ4WhwBXxAABKxiCIayZsG2vGJWC2Rh7wO4YHMBOGAAAP5gQoCOYBOAQjwAWouXbWPPa//7Pd6qtHs/+uqnr/2NPf6/FtD//8bY0iBnErIDHAaRgDDv63shaf//80TEFRbycpQBmmgAhIBMB4f4mY9yXHJ/49CgOclzf/8ehcNCXL5v//koXDQvl83Ln//5cQL5fTLhogX////00y4ggbppmiCBv//4fBABh8EAGH4SQAP1AIF0sDURxOz/80TEDBVaqnwLlGgAteAANAGItwrgHUCvfjFC0heR3f4jQwxNGFHr/5kPYljEepr/+ZD2NjpdNVf/5edZdQpL///oqqXmSMxS////mS5ikkZIomKSRlVMQU1FNC4wVVU=");

    [Fact]
    public void StreamOverloadProducesTencentSilkAndLeavesStreamsOpen()
    {
        using var input = new MemoryStream(Mp3Fixture);
        using var output = new MemoryStream();

        CreateEncoder(tencent: true).EncodeMp3(input, output);

        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
        Assert.True(output.ToArray().AsSpan().StartsWith(new byte[]
            { 0x02, (byte)'#', (byte)'!', (byte)'S', (byte)'I', (byte)'L', (byte)'K' }));
        Assert.True(output.Length > 100);
    }

    [Fact]
    public void ByteArrayOverloadSupportsMusicProfileAndIsDeterministic()
    {
        var encoder = CreateEncoder(tencent: true);

        var first = encoder.EncodeMp3(new MemoryStream(Mp3Fixture), SilkMp3AudioProfile.Music);
        var second = encoder.EncodeMp3(new MemoryStream(Mp3Fixture), SilkMp3AudioProfile.Music);

        Assert.Equal(first, second);
        Assert.True(first.Length > 100);
    }

    [Fact]
    public void PathOverloadWritesStandardSilk()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"SilkCodec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var inputPath = Path.Combine(directory, "input.mp3");
        var outputPath = Path.Combine(directory, "output.silk");

        try
        {
            File.WriteAllBytes(inputPath, Mp3Fixture);
            CreateEncoder(tencent: false).EncodeMp3(inputPath, outputPath);

            var output = File.ReadAllBytes(outputPath);
            Assert.True(output.AsSpan().StartsWith("#!SILK_V3"u8));
            Assert.Equal(0xFF, output[^1]);
            Assert.Equal(0xFF, output[^2]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Mp3EncodingRejectsNonTwentyFourKilohertzEncoderConfiguration()
    {
        var encoder = new SilkEncoder(new SilkEncoderOptions
        {
            SampleRate = 16_000,
            MaxInternalSampleRate = 16_000
        });

        Assert.Throws<InvalidOperationException>(() => encoder.EncodeMp3(new MemoryStream(Mp3Fixture)));
    }

    [Fact]
    public void StreamOverloadRejectsUsingOneStreamForInputAndOutput()
    {
        using var stream = new MemoryStream(Mp3Fixture, writable: true);

        Assert.Throws<ArgumentException>(() => CreateEncoder(tencent: true).EncodeMp3(stream, stream));
        Assert.Equal(Mp3Fixture, stream.ToArray());
    }

    [Fact]
    public void PathOverloadPreservesExistingDestinationWhenMp3IsInvalid()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"SilkCodec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var inputPath = Path.Combine(directory, "invalid.mp3");
        var outputPath = Path.Combine(directory, "existing.silk");
        byte[] originalOutput = [1, 2, 3, 4];

        try
        {
            File.WriteAllBytes(inputPath, [1, 2, 3]);
            File.WriteAllBytes(outputPath, originalOutput);

            Assert.ThrowsAny<Exception>(() => CreateEncoder(tencent: true).EncodeMp3(inputPath, outputPath));
            Assert.Equal(originalOutput, File.ReadAllBytes(outputPath));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PathOverloadValidatesProfileBeforeReplacingDestination()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"SilkCodec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var inputPath = Path.Combine(directory, "input.mp3");
        var outputPath = Path.Combine(directory, "existing.silk");
        byte[] originalOutput = [5, 6, 7, 8];

        try
        {
            File.WriteAllBytes(inputPath, Mp3Fixture);
            File.WriteAllBytes(outputPath, originalOutput);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CreateEncoder(tencent: true).EncodeMp3(inputPath, outputPath, (SilkMp3AudioProfile)99));
            Assert.Equal(originalOutput, File.ReadAllBytes(outputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PathOverloadRejectsSameInputAndOutputPathBeforeTruncatingInput()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SilkCodec-{Guid.NewGuid():N}.mp3");
        File.WriteAllBytes(path, Mp3Fixture);

        try
        {
            Assert.Throws<ArgumentException>(() => CreateEncoder(tencent: true).EncodeMp3(path, path));
            Assert.Equal(Mp3Fixture, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static SilkEncoder CreateEncoder(bool tencent) => new(new SilkEncoderOptions
    {
        SampleRate = 24_000,
        MaxInternalSampleRate = 24_000,
        BitRate = 100_000,
        Complexity = 2,
        Tencent = tencent
    });
}
