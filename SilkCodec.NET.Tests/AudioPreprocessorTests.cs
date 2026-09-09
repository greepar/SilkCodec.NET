using Xunit;

namespace SilkCodec.NET.Tests;

public sealed class AudioPreprocessorTests
{
    [Theory]
    [InlineData(44_100)]
    [InlineData(48_000)]
    public void SourcesAboveTwentyFourKilohertzBecomeMonoTwentyFourKilohertz(int sourceSampleRate)
    {
        var stereo = CreateInterleavedStereo(sourceSampleRate, 1_000);
        var mono = AudioPreprocessor.DownmixToMono(stereo, 2);
        var outputRate = AudioPreprocessor.SelectOutputSampleRate(sourceSampleRate);
        var output = AudioPreprocessor.ProcessMono(mono, sourceSampleRate, outputRate, SilkMp3AudioProfile.Flat);

        Assert.Equal(24_000, outputRate);
        Assert.Equal(24_000, output.Length);
    }

    [Theory]
    [InlineData(8_000)]
    [InlineData(11_025)]
    [InlineData(12_000)]
    [InlineData(16_000)]
    [InlineData(22_050)]
    [InlineData(24_000)]
    [InlineData(32_000)]
    [InlineData(44_100)]
    [InlineData(48_000)]
    public void SupportedSourceRatesUseTwentyFourKilohertz(int sourceRate)
    {
        Assert.Equal(24_000, AudioPreprocessor.SelectOutputSampleRate(sourceRate));
    }

    [Theory]
    [InlineData(8_000)]
    [InlineData(11_025)]
    [InlineData(16_000)]
    [InlineData(22_050)]
    public void LowerSourceRatesAreResampledToTwentyFourKilohertz(int sourceRate)
    {
        var input = CreateTone(sourceRate, 1_000, 1_000, 0.25);

        var output = AudioPreprocessor.ProcessMono(input, sourceRate, 24_000, SilkMp3AudioProfile.Flat);

        Assert.Equal(24_000, output.Length);
        Assert.InRange(RootMeanSquare(output, 2_000), 0.17, 0.18);
    }

    [Fact]
    public void ResamplerPreservesDcAndHandlesShortInput()
    {
        var input = Enumerable.Repeat(0.25f, 17).ToArray();

        var output = AudioPreprocessor.ProcessMono(input, 44_100, 24_000, SilkMp3AudioProfile.Flat);

        Assert.Equal(9, output.Length);
        Assert.All(output, sample => Assert.InRange(sample, 0.2499f, 0.2501f));
    }

    [Fact]
    public void BandLimitedDownsamplingAttenuatesContentAboveOutputNyquist()
    {
        var passband = ProcessTone(48_000, 5_000, SilkMp3AudioProfile.Flat);
        var stopband = ProcessTone(48_000, 18_000, SilkMp3AudioProfile.Flat);

        Assert.True(RootMeanSquare(stopband, 2_000) < RootMeanSquare(passband, 2_000) * 0.05);
    }

    [Fact]
    public void BandLimitedDownsamplingPreservesTenKilohertzPassband()
    {
        var reference = ProcessTone(48_000, 5_000, SilkMp3AudioProfile.Flat);
        var upperPassband = ProcessTone(48_000, 10_000, SilkMp3AudioProfile.Flat);

        Assert.True(RootMeanSquare(upperPassband, 2_000) > RootMeanSquare(reference, 2_000) * 0.9);
    }

    [Fact]
    public void MusicHighPassAttenuatesDcAndTwentyHertz()
    {
        var dc = Enumerable.Repeat(0.5f, 48_000).ToArray();
        var twentyHertz = CreateTone(24_000, 20, 2_000, 0.5);
        var dcOutput = AudioPreprocessor.ProcessMono(dc, 24_000, 24_000, SilkMp3AudioProfile.Music);
        var toneOutput = AudioPreprocessor.ProcessMono(twentyHertz, 24_000, 24_000, SilkMp3AudioProfile.Music);

        Assert.True(RootMeanSquare(dcOutput, 24_000) < 0.01);
        Assert.True(RootMeanSquare(toneOutput, 24_000) < RootMeanSquare(twentyHertz, 24_000) * 0.2);
    }

    [Fact]
    public void MusicLowPassAttenuatesElevenPointEightKilohertzRelativeToFiveKilohertz()
    {
        var fiveKilohertz = ProcessTone(24_000, 5_000, SilkMp3AudioProfile.Music);
        var elevenPointEightKilohertz = ProcessTone(24_000, 11_800, SilkMp3AudioProfile.Music);

        Assert.True(RootMeanSquare(elevenPointEightKilohertz, 6_000)
            < RootMeanSquare(fiveKilohertz, 6_000) * 0.35);
    }

    [Fact]
    public void MusicCompressorReducesPeakAndDynamicRangeWithoutClipping()
    {
        const int sampleRate = 24_000;
        var input = new float[sampleRate * 2];
        FillTone(input, 0, sampleRate, sampleRate, 1_000, 0.12);
        FillTone(input, sampleRate, sampleRate, sampleRate, 1_000, 0.95);

        var output = AudioPreprocessor.ProcessMono(input, sampleRate, sampleRate, SilkMp3AudioProfile.Music);
        var inputRatio = RootMeanSquare(input, sampleRate, sampleRate) / RootMeanSquare(input, 0, sampleRate);
        var outputRatio = RootMeanSquare(output, sampleRate + 6_000, 18_000) / RootMeanSquare(output, 6_000, 18_000);

        Assert.True(outputRatio < inputRatio * 0.75);
        Assert.True(output.Max(Math.Abs) < input.Max(Math.Abs));
        Assert.True(output.Max(Math.Abs) <= 0.892);
    }

    [Fact]
    public void MusicCompressorDoesNotApplyFixedMakeupBelowThreshold()
    {
        const int sampleRate = 24_000;
        var input = CreateTone(sampleRate, 1_000, 2_000, 0.05);

        var output = AudioPreprocessor.ProcessMono(input, sampleRate, sampleRate, SilkMp3AudioProfile.Music);
        var gain = RootMeanSquare(output, sampleRate) / RootMeanSquare(input, sampleRate);

        Assert.InRange(gain, 0.98, 1.02);
    }

    [Fact]
    public void ProcessingIsDeterministic()
    {
        var input = CreateTone(44_100, 997, 750, 0.73);

        var first = AudioPreprocessor.ProcessMono(input, 44_100, 24_000, SilkMp3AudioProfile.Music);
        var second = AudioPreprocessor.ProcessMono(input, 44_100, 24_000, SilkMp3AudioProfile.Music);

        Assert.Equal(first, second);
    }

    [Fact]
    public void FlatProfileOnlyCopiesSamplesWhenRateAlreadyMatches()
    {
        float[] input = [-0.75f, -0.25f, 0, 0.25f, 0.75f];

        var output = AudioPreprocessor.ProcessMono(input, 24_000, 24_000, SilkMp3AudioProfile.Flat);

        Assert.Equal(input, output);
        Assert.NotSame(input, output);
    }

    private static float[] ProcessTone(int sourceRate, double frequency, SilkMp3AudioProfile profile)
    {
        var input = CreateTone(sourceRate, frequency, 1_000, 0.25);
        return AudioPreprocessor.ProcessMono(
            input,
            sourceRate,
            AudioPreprocessor.SelectOutputSampleRate(sourceRate),
            profile);
    }

    private static float[] CreateTone(int sampleRate, double frequency, int durationMilliseconds, double amplitude)
    {
        var samples = new float[sampleRate * durationMilliseconds / 1_000];
        FillTone(samples, 0, samples.Length, sampleRate, frequency, amplitude);
        return samples;
    }

    private static float[] CreateInterleavedStereo(int sampleRate, int durationMilliseconds)
    {
        var mono = CreateTone(sampleRate, 440, durationMilliseconds, 0.5);
        var stereo = new float[mono.Length * 2];
        for (var i = 0; i < mono.Length; i++)
        {
            stereo[i * 2] = mono[i];
            stereo[i * 2 + 1] = mono[i] * 0.5f;
        }

        return stereo;
    }

    private static void FillTone(
        float[] samples,
        int offset,
        int count,
        int sampleRate,
        double frequency,
        double amplitude)
    {
        for (var i = 0; i < count; i++)
            samples[offset + i] = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / sampleRate));
    }

    private static double RootMeanSquare(IReadOnlyList<float> samples, int skip) =>
        RootMeanSquare(samples, skip, samples.Count - skip);

    private static double RootMeanSquare(IReadOnlyList<float> samples, int offset, int count)
    {
        double sum = 0;
        for (var i = offset; i < offset + count; i++)
            sum += samples[i] * samples[i];

        return Math.Sqrt(sum / count);
    }
}
