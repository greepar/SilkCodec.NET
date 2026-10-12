using SilkCodec.NET.Managed;
using Xunit;

namespace SilkCodec.NET.Tests;

public sealed class FixedPointMultiplyTests
{
    [Fact]
    public void WidenedMultiplicationMatchesLegacyWordArithmeticIncludingOverflow()
    {
        int[] edges = [int.MinValue, int.MaxValue, -65536, -32768, -1, 0, 1, 32767, 32768, 65535, 65536];
        foreach (var a in edges)
        foreach (var b in edges)
        foreach (var c in edges) Check(a, b, c);
        var random = new Random(417);
        for (var i = 0; i < 100000; i++)
            Check((int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1),
                (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1),
                (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1));

        static void Check(int a, int b, int c)
        {
            unchecked
            {
                var low = (a >> 16) * (short)b + (((a & 65535) * (short)b) >> 16);
                var high = (a >> 16) * (b >> 16) + (((a & 65535) * (b >> 16)) >> 16);
                Assert.Equal(low, Macros.SKP_SMULWB(a, b));
                Assert.Equal(high, Macros.SKP_SMULWT(a, b));
                Assert.Equal(c + low, Macros.SKP_SMLAWB(c, a, b));
                Assert.Equal(c + high, Macros.SKP_SMLAWT(c, a, b));
            }
        }
    }
}
