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
    : args.Length == 0 && string.Equals(ReadOptional("前置处理（flat/music；忠实还原选 flat）", "flat"), "music",
        StringComparison.OrdinalIgnoreCase);
var audioProfile = musicProfile ? SilkMp3AudioProfile.Music : SilkMp3AudioProfile.Flat;

try
{
    Console.WriteLine("正在解码 MP3、前处理并编码 SILK...");
    if (bitRate < 32_000)
        Console.WriteLine("警告: 低于 32000 bit/s 时编码器可能从 24 kHz SWB 降为 16 kHz WB，音乐高频会明显变差。");

    var encoder = new SilkEncoder(new SilkEncoderOptions
    {
        SampleRate = 24_000,
        MaxInternalSampleRate = 24_000,
        BitRate = bitRate,
        PacketLengthMilliseconds = 20,
        Complexity = 2,
        Tencent = !standardSilk
    });

    var outputDirectory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(outputDirectory))
        Directory.CreateDirectory(outputDirectory);

    encoder.EncodeMp3(inputPath, outputPath, audioProfile);
    var outputLength = new FileInfo(outputPath).Length;

    Console.WriteLine();
    Console.WriteLine($"导出完成: {outputPath}");
    Console.WriteLine($"格式: {(standardSilk ? "标准 SILK v3" : "Tencent SILK v3")}");
    Console.WriteLine(
        $"前置处理: {(musicProfile ? "music（80 Hz 高通、约 11 kHz 低通、轻柔压缩、-1 dBFS 软限幅）" : "flat（仅单声道混音、带限重采样和 PCM16 转换）")}");
    Console.WriteLine($"码率: {bitRate} bit/s");
    Console.WriteLine($"文件大小: {outputLength:N0} 字节");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"转换失败: {exception.Message}");
    return 1;
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
