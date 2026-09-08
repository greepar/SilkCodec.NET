using System.Collections.Concurrent;

namespace SilkCodec.NET.Demo;

internal enum AudioProfile
{
    Flat,
    Music
}

internal static class AudioPreprocessor
{
    private const int ResamplerTapCount = 48;
    private const int ResamplerPhaseCount = 1_024;
    private const double CompressorThresholdDecibels = -12;
    private const double CompressorRatio = 1.75;
    private const double LimiterCeiling = 0.8912509381337456; // -1 dBFS
    private static readonly double CompressorThreshold = Math.Pow(10, CompressorThresholdDecibels / 20);
    private static readonly double CompressorExponent = -(1 - 1 / CompressorRatio);
    private static readonly ConcurrentDictionary<(int InputRate, int OutputRate), double[]> ResamplerKernels = new();

    internal static int SelectOutputSampleRate(int sourceSampleRate)
    {
        return sourceSampleRate switch
        {
            8_000 or 11_025 or 12_000 or 16_000 or 22_050 or 24_000 or 32_000 or 44_100 or 48_000 => 24_000,
            _ => throw new InvalidDataException($"不支持 {sourceSampleRate} Hz 的 MP3。")
        };
    }

    internal static float[] DownmixToMono(IReadOnlyList<float> interleavedSamples, int channels)
    {
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels));

        var frameCount = interleavedSamples.Count / channels;
        var mono = new float[frameCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            double sum = 0;
            var offset = frame * channels;
            for (var channel = 0; channel < channels; channel++)
                sum += interleavedSamples[offset + channel];

            mono[frame] = (float)(sum / channels);
        }

        return mono;
    }

    internal static float[] ProcessMono(
        IReadOnlyList<float> monoSamples,
        int inputSampleRate,
        int outputSampleRate,
        AudioProfile profile)
    {
        var output = inputSampleRate == outputSampleRate
            ? monoSamples.ToArray()
            : ResampleBandLimited(monoSamples, inputSampleRate, outputSampleRate);

        if (profile == AudioProfile.Music)
            ApplyMusicProfile(output, outputSampleRate);

        return output;
    }

    internal static short[] ToPcm16(IReadOnlyList<float> samples)
    {
        var pcm = new short[samples.Count];
        for (var i = 0; i < samples.Count; i++)
            pcm[i] = (short)Math.Round(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);

        return pcm;
    }

    private static float[] ResampleBandLimited(
        IReadOnlyList<float> input,
        int inputSampleRate,
        int outputSampleRate)
    {
        if (input.Count == 0)
            return [];
        if (inputSampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(inputSampleRate));
        if (outputSampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(outputSampleRate));

        var outputLength = (int)Math.Round(input.Count * (double)outputSampleRate / inputSampleRate);
        var output = new float[outputLength];
        var kernels = ResamplerKernels.GetOrAdd(
            (inputSampleRate, outputSampleRate),
            static rates => CreatePolyphaseKernels(rates.InputRate, rates.OutputRate));
        var sourceStep = inputSampleRate / (double)outputSampleRate;
        var firstTap = 1 - ResamplerTapCount / 2;

        for (var outputIndex = 0; outputIndex < output.Length; outputIndex++)
        {
            var sourcePosition = (outputIndex + 0.5) * sourceStep - 0.5;
            var sourceBase = (int)Math.Floor(sourcePosition);
            var fraction = sourcePosition - sourceBase;
            var phase = Math.Min((int)Math.Round(fraction * ResamplerPhaseCount), ResamplerPhaseCount);
            var kernelOffset = phase * ResamplerTapCount;
            double sum = 0;

            for (var tap = 0; tap < ResamplerTapCount; tap++)
            {
                var sourceIndex = Math.Clamp(sourceBase + firstTap + tap, 0, input.Count - 1);
                sum += input[sourceIndex] * kernels[kernelOffset + tap];
            }

            output[outputIndex] = (float)sum;
        }

        return output;
    }

    private static double[] CreatePolyphaseKernels(int inputSampleRate, int outputSampleRate)
    {
        var kernels = new double[(ResamplerPhaseCount + 1) * ResamplerTapCount];
        var cutoff = 0.475 * Math.Min(1, outputSampleRate / (double)inputSampleRate);
        var firstTap = 1 - ResamplerTapCount / 2;
        var windowRadius = ResamplerTapCount / 2.0;

        for (var phase = 0; phase <= ResamplerPhaseCount; phase++)
        {
            var fraction = phase / (double)ResamplerPhaseCount;
            var kernelOffset = phase * ResamplerTapCount;
            double kernelSum = 0;

            for (var tap = 0; tap < ResamplerTapCount; tap++)
            {
                var distance = firstTap + tap - fraction;
                var normalizedDistance = distance / windowRadius;
                var window = 0.42 + 0.5 * Math.Cos(Math.PI * normalizedDistance)
                                  + 0.08 * Math.Cos(2 * Math.PI * normalizedDistance);
                var argument = 2 * cutoff * distance;
                var sinc = Math.Abs(argument) < 1e-12
                    ? 1
                    : Math.Sin(Math.PI * argument) / (Math.PI * argument);
                var coefficient = 2 * cutoff * sinc * window;
                kernels[kernelOffset + tap] = coefficient;
                kernelSum += coefficient;
            }

            for (var tap = 0; tap < ResamplerTapCount; tap++)
                kernels[kernelOffset + tap] /= kernelSum;
        }

        return kernels;
    }

    private static void ApplyMusicProfile(float[] samples, int sampleRate)
    {
        ApplyBiquad(samples, CreateHighPass(sampleRate, 80, Math.Sqrt(0.5)));

        var lowPassCutoff = Math.Min(11_000, sampleRate * 0.46);
        ApplyBiquad(samples, CreateLowPass(sampleRate, lowPassCutoff, 0.5411961));
        ApplyBiquad(samples, CreateLowPass(sampleRate, lowPassCutoff, 1.306563));

        ApplyCompressor(samples, sampleRate);
        ApplySoftLimiter(samples);
    }

    private static void ApplyCompressor(float[] samples, int sampleRate)
    {
        var attack = Math.Exp(-1 / (sampleRate * 0.015));
        var release = Math.Exp(-1 / (sampleRate * 0.180));
        double envelopeSquared = 0;

        for (var i = 0; i < samples.Length; i++)
        {
            var sampleSquared = samples[i] * samples[i];
            var coefficient = sampleSquared > envelopeSquared ? attack : release;
            envelopeSquared = coefficient * envelopeSquared + (1 - coefficient) * sampleSquared;
            var envelope = Math.Sqrt(envelopeSquared);

            var gain = envelope > CompressorThreshold
                ? Math.Pow(envelope / CompressorThreshold, CompressorExponent)
                : 1;
            samples[i] = (float)(samples[i] * gain);
        }
    }

    private static void ApplySoftLimiter(float[] samples)
    {
        var knee = LimiterCeiling * 0.9;
        var kneeRange = LimiterCeiling - knee;

        for (var i = 0; i < samples.Length; i++)
        {
            var magnitude = Math.Abs(samples[i]);
            if (magnitude <= knee)
                continue;

            var limited = knee + kneeRange * Math.Tanh((magnitude - knee) / kneeRange);
            samples[i] = (float)Math.CopySign(limited, samples[i]);
        }
    }

    private static BiquadCoefficients CreateHighPass(int sampleRate, double frequency, double q)
    {
        var omega = 2 * Math.PI * frequency / sampleRate;
        var cosine = Math.Cos(omega);
        var alpha = Math.Sin(omega) / (2 * q);
        var a0 = 1 + alpha;
        return new BiquadCoefficients(
            (1 + cosine) / (2 * a0),
            -(1 + cosine) / a0,
            (1 + cosine) / (2 * a0),
            -2 * cosine / a0,
            (1 - alpha) / a0);
    }

    private static BiquadCoefficients CreateLowPass(int sampleRate, double frequency, double q)
    {
        var omega = 2 * Math.PI * frequency / sampleRate;
        var cosine = Math.Cos(omega);
        var alpha = Math.Sin(omega) / (2 * q);
        var a0 = 1 + alpha;
        return new BiquadCoefficients(
            (1 - cosine) / (2 * a0),
            (1 - cosine) / a0,
            (1 - cosine) / (2 * a0),
            -2 * cosine / a0,
            (1 - alpha) / a0);
    }

    private static void ApplyBiquad(float[] samples, BiquadCoefficients coefficients)
    {
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var x0 = samples[i];
            var y0 = coefficients.B0 * x0 + coefficients.B1 * x1 + coefficients.B2 * x2
                     - coefficients.A1 * y1 - coefficients.A2 * y2;
            samples[i] = (float)y0;
            x2 = x1;
            x1 = x0;
            y2 = y1;
            y1 = y0;
        }
    }

    private readonly record struct BiquadCoefficients(double B0, double B1, double B2, double A1, double A2);
}
