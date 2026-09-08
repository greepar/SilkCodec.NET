using NLayer;
using SilkCodec.NET;

Console.WriteLine("SilkCodec.NET MP3 -> SILK Demo");
Console.WriteLine();

var inputPath = args.Length > 0 ? args[0] : ReadRequired("请输入 MP3 文件路径: ");
inputPath = Path.GetFullPath(inputPath.Trim().Trim('"'));

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"找不到文件: {inputPath}");
    return 1;
}

if (!string.Equals(Path.GetExtension(inputPath), ".mp3", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("请输入 .mp3 文件。");
    return 1;
}

var outputPath = args.Length > 1
    ? args[1]
    : ReadOptional("输出 SILK 路径", Path.ChangeExtension(inputPath, ".silk"));
outputPath = Path.GetFullPath(outputPath.Trim().Trim('"'));

var bitRateText = args.Length > 2 ? args[2] : ReadOptional("码率（bit/s；音乐建议 100000）", "100000");
if (!int.TryParse(bitRateText, out var bitRate) || bitRate is < 5_000 or > 100_000)
{
    Console.Error.WriteLine("码率必须是 5000 到 100000 之间的整数。");
    return 1;
}

var standardSilk = args.Length > 3
    ? string.Equals(args[3], "standard", StringComparison.OrdinalIgnoreCase)
    : string.Equals(ReadOptional("封装格式（tencent/standard）", "tencent"), "standard", StringComparison.OrdinalIgnoreCase);
var musicProfile = args.Length > 4
    ? string.Equals(args[4], "music", StringComparison.OrdinalIgnoreCase)
    : args.Length == 0 && string.Equals(ReadOptional("前置处理（flat/music；忠实还原选 flat）", "flat"), "music", StringComparison.OrdinalIgnoreCase);

try
{
    Console.WriteLine("正在解码 MP3...");
    using var mp3 = new MpegFile(inputPath);
    var sourceSampleRate = mp3.SampleRate;
    var sampleRate = GetSupportedSampleRate(sourceSampleRate);
    var pcm = DecodeMonoPcm16(mp3, sampleRate, musicProfile);

    var resampling = sourceSampleRate == sampleRate ? string.Empty : $" -> {sampleRate} Hz";
    Console.WriteLine($"PCM: {sourceSampleRate} Hz{resampling}, {mp3.Channels} 声道 -> 单声道, {pcm.Length / (double)sampleRate:F2} 秒");
    if (bitRate < 32_000 && sampleRate >= 24_000)
        Console.WriteLine("警告: 低于 32000 bit/s 时编码器可能从 24 kHz SWB 降为 16 kHz WB，音乐高频会明显变差。");
    Console.WriteLine("正在编码 SILK...");

    var encoder = new SilkEncoder(new SilkEncoderOptions
    {
        SampleRate = sampleRate,
        MaxInternalSampleRate = Math.Min(sampleRate, 24_000),
        BitRate = bitRate,
        PacketLengthMilliseconds = 20,
        Complexity = 2,
        Tencent = !standardSilk
    });

    var outputDirectory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(outputDirectory))
        Directory.CreateDirectory(outputDirectory);

    using var output = File.Create(outputPath);
    encoder.Encode(pcm, output);

    Console.WriteLine();
    Console.WriteLine($"导出完成: {outputPath}");
    Console.WriteLine($"格式: {(standardSilk ? "标准 SILK v3" : "Tencent SILK v3")}");
    Console.WriteLine($"前置处理: {(musicProfile ? "音乐优化" : "无")}");
    Console.WriteLine($"码率: {bitRate} bit/s");
    Console.WriteLine($"文件大小: {output.Length:N0} 字节");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"转换失败: {exception.Message}");
    return 1;
}

static int GetSupportedSampleRate(int sourceSampleRate)
{
    return sourceSampleRate switch
    {
        8_000 or 12_000 or 16_000 or 24_000 or 32_000 or 44_100 or 48_000 => sourceSampleRate,
        11_025 => 12_000,
        22_050 => 24_000,
        _ => throw new InvalidDataException($"不支持 {sourceSampleRate} Hz 的 MP3。")
    };
}

static short[] DecodeMonoPcm16(MpegFile mp3, int outputSampleRate, bool musicProfile)
{
    var channels = mp3.Channels;
    if (channels <= 0)
        throw new InvalidDataException("MP3 声道数无效。");

    var decoded = new float[1152 * channels];
    var monoSamples = new List<float>();

    int samplesRead;
    while ((samplesRead = mp3.ReadSamples(decoded, 0, decoded.Length)) > 0)
    {
        var completeSamples = samplesRead - samplesRead % channels;
        for (var offset = 0; offset < completeSamples; offset += channels)
        {
            double mono = 0;
            for (var channel = 0; channel < channels; channel++)
                mono += decoded[offset + channel];

            monoSamples.Add((float)(mono / channels));
        }
    }

    if (monoSamples.Count == 0)
        throw new InvalidDataException("MP3 中没有可解码的音频数据。");

    var resampled = mp3.SampleRate == outputSampleRate
        ? monoSamples.ToArray()
        : ResampleLinear(monoSamples, mp3.SampleRate, outputSampleRate);
    if (musicProfile)
        ApplyMusicProfile(resampled, outputSampleRate);

    var pcm = new short[resampled.Length];
    for (var i = 0; i < resampled.Length; i++)
        pcm[i] = (short)Math.Round(Math.Clamp(resampled[i], -1f, 1f) * short.MaxValue);

    return pcm.ToArray();
}

static float[] ResampleLinear(IReadOnlyList<float> input, int inputSampleRate, int outputSampleRate)
{
    var output = new float[(int)((long)input.Count * outputSampleRate / inputSampleRate)];
    var sourceStep = inputSampleRate / (double)outputSampleRate;

    for (var i = 0; i < output.Length; i++)
    {
        var sourcePosition = i * sourceStep;
        var left = Math.Min((int)sourcePosition, input.Count - 1);
        var right = Math.Min(left + 1, input.Count - 1);
        var fraction = sourcePosition - left;
        output[i] = (float)(input[left] + (input[right] - input[left]) * fraction);
    }

    return output;
}

static void ApplyMusicProfile(float[] samples, int sampleRate)
{
    ApplyBiquad(samples, CreateHighPass(sampleRate, 60, 0.707));
    ApplyBiquad(samples, CreatePeakingEq(sampleRate, 4_500, 0.8, 2.5));

    const float threshold = 0.95f;
    for (var i = 0; i < samples.Length; i++)
    {
        var magnitude = Math.Abs(samples[i]);
        if (magnitude <= threshold)
            continue;

        var excess = (magnitude - threshold) / (1 - threshold);
        var limited = threshold + (1 - threshold) * (1 - Math.Exp(-excess));
        samples[i] = samples[i] < 0 ? -(float)limited : (float)limited;
    }
}

static (double B0, double B1, double B2, double A1, double A2) CreateHighPass(
    int sampleRate, double frequency, double q)
{
    var omega = 2 * Math.PI * frequency / sampleRate;
    var cosine = Math.Cos(omega);
    var alpha = Math.Sin(omega) / (2 * q);
    var a0 = 1 + alpha;
    return (
        (1 + cosine) / (2 * a0),
        -(1 + cosine) / a0,
        (1 + cosine) / (2 * a0),
        -2 * cosine / a0,
        (1 - alpha) / a0);
}

static (double B0, double B1, double B2, double A1, double A2) CreatePeakingEq(
    int sampleRate, double frequency, double q, double gainDecibels)
{
    var amplitude = Math.Pow(10, gainDecibels / 40);
    var omega = 2 * Math.PI * frequency / sampleRate;
    var alpha = Math.Sin(omega) / (2 * q);
    var cosine = Math.Cos(omega);
    var a0 = 1 + alpha / amplitude;
    return (
        (1 + alpha * amplitude) / a0,
        -2 * cosine / a0,
        (1 - alpha * amplitude) / a0,
        -2 * cosine / a0,
        (1 - alpha / amplitude) / a0);
}

static void ApplyBiquad(
    float[] samples,
    (double B0, double B1, double B2, double A1, double A2) coefficients)
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

static string ReadRequired(string prompt)
{
    while (true)
    {
        Console.Write(prompt);
        var value = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(value))
            return value;
    }
}

static string ReadOptional(string prompt, string defaultValue)
{
    Console.Write($"{prompt} [{defaultValue}]: ");
    var value = Console.ReadLine()?.Trim();
    return string.IsNullOrEmpty(value) ? defaultValue : value;
}
