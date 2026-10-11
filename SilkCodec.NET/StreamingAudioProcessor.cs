namespace SilkCodec.NET;

/// <summary>Pulls mono samples with bounded lookahead, preserving the batch resampler's phase and endpoint rules.</summary>
internal sealed class StreamingAudioProcessor
{
    private const int Taps = 48;
    private const int Phases = 1024;
    private readonly float[] _ring = new float[4096];
    private readonly float[] _input = new float[1152];
    private readonly Func<float[], int> _read;
    private readonly int _inputRate;
    private readonly double[]? _kernels;
    private readonly MusicFilter? _music;
    private long _inputCount, _outputIndex;
    private bool _eof;

    internal StreamingAudioProcessor(Func<float[], int> read, int inputRate, SilkMp3AudioProfile profile)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputRate);
        _read = read;
        _inputRate = inputRate;
        _kernels = inputRate == 24000 ? null : AudioPreprocessor.GetResamplerKernels(inputRate, 24000);
        _music = profile == SilkMp3AudioProfile.Music ? new MusicFilter() : null;
    }

    internal int Read(Span<short> output)
    {
        var written = 0;
        while (written < output.Length)
        {
            var sourcePosition = (_outputIndex + 0.5) * (_inputRate / 24000d) - 0.5;
            var sourceBase = (long)Math.Floor(sourcePosition);
            var required = _kernels is null ? _outputIndex : Math.Max(0, sourceBase + 24);
            while (!_eof && _inputCount <= required)
            {
                var count = _read(_input);
                if (count == 0) { _eof = true; break; }
                if ((uint)count > _input.Length) throw new InvalidDataException("Invalid decoded sample count.");
                for (var i = 0; i < count; i++) _ring[(int)(_inputCount++ & 4095)] = _input[i];
            }
            if (_eof && _outputIndex >= (long)Math.Round(_inputCount * 24000d / _inputRate)) break;
            float sample;
            if (_kernels is null) sample = _ring[(int)(_outputIndex & 4095)];
            else
            {
                var phase = Math.Min((int)Math.Round((sourcePosition - sourceBase) * Phases), Phases);
                double sum = 0;
                for (var tap = 0; tap < Taps; tap++)
                {
                    var index = Math.Clamp(sourceBase - 23 + tap, 0, _inputCount - 1);
                    sum += _ring[(int)(index & 4095)] * _kernels[phase * Taps + tap];
                }
                sample = (float)sum;
            }
            if (_music is not null) sample = _music.Process(sample);
            output[written++] = (short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
            _outputIndex++;
        }
        return written;
    }

    private sealed class MusicFilter
    {
        private readonly Biquad _high = new(AudioPreprocessor.CreateHighPass(24000, 80, Math.Sqrt(0.5)));
        private readonly Biquad _low1 = new(AudioPreprocessor.CreateLowPass(24000, 11000, 0.5411961));
        private readonly Biquad _low2 = new(AudioPreprocessor.CreateLowPass(24000, 11000, 1.306563));
        private readonly double _attack = Math.Exp(-1 / (24000 * 0.015));
        private readonly double _release = Math.Exp(-1 / (24000 * 0.180));
        private readonly double _threshold = Math.Pow(10, -12d / 20);
        private double _envelopeSquared;

        internal float Process(float sample)
        {
            sample = _low2.Process(_low1.Process(_high.Process(sample)));
            var squared = sample * sample;
            var coefficient = squared > _envelopeSquared ? _attack : _release;
            _envelopeSquared = coefficient * _envelopeSquared + (1 - coefficient) * squared;
            var envelope = Math.Sqrt(_envelopeSquared);
            var gain = envelope > _threshold ? Math.Pow(envelope / _threshold, -(1 - 1 / 1.75)) : 1;
            sample = (float)(sample * gain);
            const double ceiling = 0.8912509381337456;
            var knee = ceiling * 0.9;
            var range = ceiling - knee;
            var magnitude = Math.Abs(sample);
            if (magnitude > knee)
                sample = (float)Math.CopySign(knee + range * Math.Tanh((magnitude - knee) / range), sample);
            return sample;
        }
    }

    private sealed class Biquad(AudioPreprocessor.BiquadCoefficients c)
    {
        private double _x1, _x2, _y1, _y2;
        internal float Process(float input)
        {
            var output = c.B0 * input + c.B1 * _x1 + c.B2 * _x2 - c.A1 * _y1 - c.A2 * _y2;
            _x2 = _x1; _x1 = input; _y2 = _y1; _y1 = output;
            return (float)output;
        }
    }
}
