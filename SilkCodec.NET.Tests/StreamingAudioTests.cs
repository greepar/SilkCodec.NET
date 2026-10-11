using System.Buffers.Binary;
using Xunit;

namespace SilkCodec.NET.Tests;

public sealed class StreamingAudioTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var rate in new[] { 8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000 })
        foreach (var profile in new[] { SilkMp3AudioProfile.Flat, SilkMp3AudioProfile.Music })
        foreach (var count in new[] { 1, 17, 12001 })
            yield return [rate, profile, count];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void StreamingSamplesMatchBatchAcrossRatesProfilesAndEndpoints(int rate, SilkMp3AudioProfile profile, int count)
    {
        var input = Enumerable.Range(0, count).Select(i => (float)(0.95 * Math.Sin(i * 0.17) + 0.2)).ToArray();
        var expected = AudioPreprocessor.ToPcm16(AudioPreprocessor.ProcessMono(input, rate, 24000, profile));
        var position = 0;
        var processor = new StreamingAudioProcessor(buffer =>
        {
            var take = Math.Min(17, input.Length - position);
            input.AsSpan(position, take).CopyTo(buffer);
            position += take;
            return take;
        }, rate, profile);
        var actual = new List<short>();
        var block = new short[479];
        int read;
        while ((read = processor.Read(block)) > 0) actual.AddRange(block.Take(read));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void StreamingPreprocessingAllocationDoesNotGrowWithDuration()
    {
        Run(4800);
        var before = GC.GetAllocatedBytesForCurrentThread();
        Run(4800);
        var shortBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        Run(480000);
        var longBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(longBytes, shortBytes, shortBytes + 1024);
        Assert.True(longBytes < 100000, $"Allocated {longBytes} bytes.");

        static void Run(int samples)
        {
            var remaining = samples;
            var processor = new StreamingAudioProcessor(buffer =>
            {
                var count = Math.Min(remaining, buffer.Length);
                Array.Fill(buffer, 0.25f, 0, count);
                remaining -= count;
                return count;
            }, 48000, SilkMp3AudioProfile.Music);
            Span<short> output = stackalloc short[480];
            var total = 0;
            int read;
            while ((read = processor.Read(output)) != 0) total += read;
            Assert.Equal(samples / 2, total);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PcmStreamMatchesSpanEncodingWithShortReadsAndPartialFinalFrame(bool tencent)
    {
        var samples = Enumerable.Range(0, 2401).Select(i => (short)(12000 * Math.Sin(i * 0.13))).ToArray();
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), samples[i]);
        var encoder = new SilkEncoder(new SilkEncoderOptions { Tencent = tencent, Complexity = 2 });
        var expected = encoder.Encode(samples);
        using var input = new ShortReadStream(bytes);
        using var output = new MemoryStream();
        encoder.EncodePcm16LittleEndian(input, output);
        Assert.Equal(expected, output.ToArray());
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public void PcmStreamRejectsOddByteCountAndObservesCancellation()
    {
        var encoder = new SilkEncoder();
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => encoder.EncodePcm16LittleEndian(new MemoryStream(new byte[3]), output));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => encoder.EncodePcm16LittleEndian(new MemoryStream(new byte[960]), output, cancellation.Token));
    }

    private sealed class ShortReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 7));
    }
}
