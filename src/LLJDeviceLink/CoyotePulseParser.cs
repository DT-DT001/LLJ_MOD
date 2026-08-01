using System.Globalization;
using System.Text.RegularExpressions;

namespace LLJDeviceLink;

public static class CoyotePulseParser
{
    private const string DungeonlabMarker = "Dungeonlab+pulse:";
    public const int MaxFrames = 3000;

    private static readonly int[] FrequencyPeriodsMs =
    {
        10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25,
        26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41,
        42, 43, 44, 45, 46, 47, 48, 49, 50, 52, 54, 56, 58, 60, 62, 64,
        66, 68, 70, 72, 74, 76, 78, 80, 85, 90, 95, 100, 110, 120, 130,
        140, 150, 160, 170, 180, 190, 200, 233, 266, 300, 333, 366, 400,
        450, 500, 550, 600, 700, 800, 900, 1000
    };

    private static readonly double[] SectionSeconds =
    {
        0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1, 1.1, 1.2, 1.3,
        1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 2, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6,
        2.7, 2.8, 2.9, 3, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9,
        4, 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9, 5, 5.2, 5.4,
        5.6, 5.8, 6, 6.2, 6.4, 6.6, 6.8, 7, 7.2, 7.4, 7.6, 7.8, 8,
        8.5, 9, 9.5, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 23.4,
        26.6, 30, 33.4, 36.6, 40, 45, 50, 55, 60, 70, 80, 90, 100, 120,
        140, 160, 180, 200, 250, 300
    };

    private static readonly Regex HexToken = new(
        @"(?i)(?<![0-9a-f])(?:0x)?([0-9a-f]{40}|[0-9a-f]{16}|[0-9a-f]{6})(?![0-9a-f])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NumberToken = new(
        @"-?\d+(?:\.\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static List<byte[]> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("PULSE 文件为空");

        text = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (text.StartsWith(DungeonlabMarker, StringComparison.OrdinalIgnoreCase))
            return ParseDungeonlab(text);

        var frames = ParseHexFrames(text);
        if (frames.Count > 0) return frames;

        frames = ParseNumericPairs(text);
        if (frames.Count > 0) return frames;
        throw new InvalidDataException("未找到支持的郊狼 V2/V3 或 Dungeonlab PULSE 波形数据");
    }

    public static void ValidateFrame(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != 8)
            throw new InvalidDataException("郊狼 V3 单通道波形帧必须为 8 字节");
        for (var index = 0; index < 4; index++)
            if (frame[index] is < 10 or > 240)
                throw new InvalidDataException($"郊狼波形频率编码必须在 10–240，当前为 {frame[index]}");
        for (var index = 4; index < 8; index++)
            if (frame[index] > 100)
                throw new InvalidDataException($"郊狼波形强度必须在 0–100，当前为 {frame[index]}");
    }

    private static List<byte[]> ParseDungeonlab(string text)
    {
        var sections = text[DungeonlabMarker.Length..].Trim().Split("+section+", StringSplitOptions.None);
        if (sections.Length == 0) throw new InvalidDataException("Dungeonlab PULSE 不包含小节");

        var restDurationMs = 0d;
        var speedRate = 1d;
        var equals = sections[0].IndexOf('=');
        if (equals >= 0)
        {
            var metadata = sections[0][..equals].Split(',');
            sections[0] = sections[0][(equals + 1)..];
            if (metadata.Length < 2 ||
                !TryDouble(metadata[0], out restDurationMs) ||
                !TryDouble(metadata[1], out speedRate))
                throw new InvalidDataException("Dungeonlab PULSE 元数据无效");
            restDurationMs = Math.Max(0d, restDurationMs);
            speedRate = Math.Max(0.01d, speedRate);
        }

        var frames = new List<byte[]>();
        AppendRepeated(frames, BuildFrame(10, 0), (int)Math.Ceiling(restDurationMs / 100d));

        foreach (var rawSection in sections)
        {
            if (frames.Count >= MaxFrames) break;
            var slash = rawSection.IndexOf('/');
            if (slash < 0) continue;
            var header = rawSection[..slash].Split(',');
            if (header.Length < 5 ||
                !int.TryParse(header[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minIndex) ||
                !int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxIndex) ||
                !int.TryParse(header[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var durationIndex) ||
                !int.TryParse(header[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mode) ||
                !int.TryParse(header[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var enabled))
                throw new InvalidDataException("Dungeonlab PULSE 小节参数无效");
            if (enabled != 1) continue;

            var strengths = new List<double>();
            foreach (var rawPoint in rawSection[(slash + 1)..].Split(','))
            {
                if (string.IsNullOrWhiteSpace(rawPoint)) continue;
                var dash = rawPoint.IndexOf('-');
                var value = dash >= 0 ? rawPoint[..dash] : rawPoint;
                if (!TryDouble(value, out var strength))
                    throw new InvalidDataException("Dungeonlab PULSE 波形点无效");
                strengths.Add(Math.Clamp(strength, 0d, 100d));
            }
            if (strengths.Count == 0) continue;

            durationIndex = Math.Clamp(durationIndex, 0, SectionSeconds.Length - 1);
            var repeatCount = Math.Max(1, (int)Math.Ceiling(SectionSeconds[durationIndex] / (strengths.Count * 0.1d)));
            var totalPoints = repeatCount * strengths.Count;
            for (var repeatIndex = 0; repeatIndex < repeatCount && frames.Count < MaxFrames; repeatIndex++)
            {
                for (var pointIndex = 0; pointIndex < strengths.Count && frames.Count < MaxFrames; pointIndex++)
                {
                    var current = repeatIndex * strengths.Count + pointIndex;
                    var frequencyIndex = mode switch
                    {
                        2 => minIndex + (maxIndex - minIndex) * current / (double)totalPoints,
                        3 => minIndex + (maxIndex - minIndex) * pointIndex / (double)strengths.Count,
                        4 => minIndex + (maxIndex - minIndex) * repeatIndex / (double)repeatCount,
                        _ => minIndex
                    };
                    var encodedFrequency = EncodePeriod(PeriodForIndex(frequencyIndex) / speedRate);
                    frames.Add(BuildFrame(encodedFrequency, ToByte(strengths[pointIndex], 0, 100)));
                }
            }
        }

        if (frames.Count == 0) throw new InvalidDataException("Dungeonlab PULSE 没有已启用的有效小节");
        return frames;
    }

    private static List<byte[]> ParseHexFrames(string text)
    {
        var frames = new List<byte[]>();
        foreach (Match match in HexToken.Matches(text))
        {
            var token = match.Groups[1].Value;
            byte[] frame;
            if (token.Length == 6)
            {
                frame = ConvertV2(Convert.FromHexString(token));
            }
            else if (token.Length == 16)
            {
                frame = Convert.FromHexString(token);
                ValidateFrame(frame);
            }
            else
            {
                frame = ExtractFromB0(Convert.FromHexString(token));
            }
            frames.Add(frame);
            if (frames.Count >= MaxFrames) break;
        }
        return frames;
    }

    private static List<byte[]> ParseNumericPairs(string text)
    {
        var values = NumberToken.Matches(text)
            .Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToList();
        var frames = new List<byte[]>();
        for (var index = 0; index + 1 < values.Count && frames.Count < MaxFrames; index += 2)
        {
            var frequency = ToByte(values[index], 10, 240);
            var strength = ToByte(values[index + 1], 0, 100);
            frames.Add(BuildFrame(frequency, strength));
        }
        return frames;
    }

    private static byte[] ConvertV2(byte[] data)
    {
        if (data.Length != 3) throw new InvalidDataException("郊狼 V2 波形点必须为 3 字节");
        var packed = data[0] | data[1] << 8 | data[2] << 16;
        var x = packed & 0x1F;
        var y = packed >> 5 & 0x3FF;
        var z = packed >> 15 & 0x1F;
        var periodMs = x + y;
        if (x <= 0 || z <= 0 || periodMs < 10) return BuildFrame(10, 0);
        return BuildFrame(EncodePeriod(periodMs), Math.Clamp(z * 5, 0, 100));
    }

    private static byte[] ExtractFromB0(byte[] packet)
    {
        if (packet.Length != 20 || packet[0] != 0xB0)
            throw new InvalidDataException("40 位十六进制数据不是郊狼 V3 B0 指令");
        var a = packet.AsSpan(4, 8).ToArray();
        var b = packet.AsSpan(12, 8).ToArray();
        var aValid = IsValid(a);
        var bValid = IsValid(b);
        if (!aValid && !bValid) throw new InvalidDataException("B0 指令的 A/B 通道均不含有效波形");
        if (!aValid) return b;
        if (!bValid) return a;
        return b.Skip(4).Any(value => value > 0) && !a.Skip(4).Any(value => value > 0) ? b : a;
    }

    private static bool IsValid(byte[] frame)
    {
        try
        {
            ValidateFrame(frame);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static double PeriodForIndex(double index)
    {
        var safeIndex = Math.Clamp((int)Math.Floor(index), 0, FrequencyPeriodsMs.Length - 1);
        return FrequencyPeriodsMs[safeIndex];
    }

    private static byte EncodePeriod(double periodMs)
    {
        periodMs = Math.Clamp(periodMs, 10d, 1000d);
        var encoded = periodMs switch
        {
            <= 100d => periodMs,
            <= 600d => (periodMs - 100d) / 5d + 100d,
            _ => (periodMs - 600d) / 10d + 200d
        };
        return ToByte(encoded, 10, 240);
    }

    private static byte[] BuildFrame(int frequency, int strength)
    {
        var encodedFrequency = (byte)Math.Clamp(frequency, 10, 240);
        var encodedStrength = (byte)Math.Clamp(strength, 0, 100);
        return new[]
        {
            encodedFrequency, encodedFrequency, encodedFrequency, encodedFrequency,
            encodedStrength, encodedStrength, encodedStrength, encodedStrength
        };
    }

    private static void AppendRepeated(List<byte[]> frames, byte[] frame, int count)
    {
        for (var index = 0; index < count && frames.Count < MaxFrames; index++)
            frames.Add(frame.ToArray());
    }

    private static byte ToByte(double value, int min, int max)
        => (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), min, max);

    private static bool TryDouble(string value, out double result)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result);
}
