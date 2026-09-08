using SilkCodec.NET.Managed;
using Xunit;

namespace SilkCodec.NET.Tests;

public sealed class WarpedNoiseShapingTests
{
    [Fact]
    public void WarpedDelayedDecisionNsqMatchesOfficialUnvoicedGoldenVector()
    {
        AssertDelayedDecisionGoldenVector(
            voiced: false,
            expectedQHash: 0xc0f3df17e10fd92fUL,
            expectedSeed: 2,
            expectedLagPrev: 76,
            expectedInvGain: 24970,
            expectedLfAr: 172444,
            expectedAr:
            [
                -341696, 1672472, -1053043, 49793, -466508, -479560, 1043267, 179906, 110332, -693102, -41929, -1379279,
                1301758, -1211286, 278333, -360083
            ],
            expectedLpc:
            [
                606736, -609616, 538032, -557856, -1550928, 975424, -777856, 681744, -620112, 583904, -555824, -1526272,
                967088, -759200, 686800, -609168, 593312, 1415136, -970240, 786032, -662288, 631584, -594896, -1509168,
                966096, -745568, 684000, -603696, 580320, 1522496, -944592, 747712
            ],
            expectedXqFirst: [-145, -185, -171, -179, -172, -173, -81, 144, 191, 175, 181, 176, -112, -197, -166, -181],
            expectedXqLast: [95, 227, -155, 126, -106, 101, -95, -242, 155, -119, 110, -97, 93, 244, -151, 120],
            expectedLtpFirst:
            [
                -57900, -18560, -32467, -26425, -28609, -26752, 6842, 63254, 18984, 32236, 27683, 28582, -85293, -10742,
                -35364, -24798
            ],
            expectedLtpLast:
            [
                158120, -43738, -52455, 95074, -118808, 138197, -154526, 36825, 59454, -98256, 121191, -137830, 153768,
                -34743, -59851, 97872
            ]);
    }

    [Fact]
    public void WarpedDelayedDecisionNsqMatchesOfficialVoicedRewhiteningGoldenVector()
    {
        AssertDelayedDecisionGoldenVector(
            voiced: true,
            expectedQHash: 0xab10baa4fa2b340fUL,
            expectedSeed: 2,
            expectedLagPrev: 84,
            expectedInvGain: 25414,
            expectedLfAr: 154368,
            expectedAr:
            [
                -457044, 1608777, -958738, -321966, -444694, -1191668, 385538, 130669, 510937, -878062, 530377,
                -2207109, 339944, -470766, -291626, -260515
            ],
            expectedLpc:
            [
                435968, -670928, 446480, -544240, -1647088, 890784, -956400, 543440, -814752, 472704, -696816, -1693312,
                743232, -995520, 569056, -757008, 498096, 1305136, -1079600, 552528, -803424, 422096, -729248, -1710656,
                802880, -919840, 576368, -733792, 559072, 1453088, -926080, 708992
            ],
            expectedXqFirst:
            [-1046, -1327, -940, -898, -882, -1149, -933, -820, -845, -812, -810, -787, -1054, -1114, -1077, -1077],
            expectedXqLast: [78, 205, -170, 87, -126, 66, -115, -269, 126, -145, 91, -115, 88, 229, -146, 112],
            expectedLtpFirst:
            [
                -420459, -139136, -122171, -147117, -146025, -246313, -72330, -156523, -126847, -138320, -122610,
                -137403, -226338, -165132, -183503, -171568
            ],
            expectedLtpLast:
            [
                148405, -41238, -59239, 86810, -111437, 116986, -137549, 12634, 71113, -111653, 125557, -140925, 151562,
                -35543, -56138, 90640
            ]);
    }

    [Fact]
    public void WarpedAutocorrelationMatchesIndependentReference()
    {
        const int order = 8;
        const float warping = 0.27f;
        var input = Enumerable.Range(0, 77)
            .Select(i => (float)(Math.Sin(i * 0.31) * 1200.0 + Math.Cos(i * 0.07) * 400.0))
            .ToArray();
        var actual = Enumerable.Repeat(float.NaN, order + 5).ToArray();

        WarpedAutocorrelationFLP.SKP_Silk_warped_autocorrelation_FLP(
            actual, 2, input, 3, warping, 73, order);
        var expected = ReferenceWarpedAutocorrelation(input[3..76], warping, order);

        for (var i = 0; i <= order; i++)
            Assert.Equal(expected[i], actual[i + 2]);
        Assert.True(float.IsNaN(actual[1]));
        Assert.True(float.IsNaN(actual[^1]));
    }

    [Fact]
    public void WarpedPrefilterStateIsContinuousAcrossCalls()
    {
        const int order = 8;
        const float warping = 0.31f;
        var input = Enumerable.Range(0, 96)
            .Select(i => (float)(Math.Sin(i * 0.19) * 2000.0 + i * 0.5))
            .ToArray();
        var coefficients = new[] { 0.22f, -0.15f, 0.1f, -0.07f, 0.05f, -0.03f, 0.02f, -0.01f };

        var onePassState = new float[order + 1];
        var onePass = new float[input.Length];
        PrefilterFLP.SKP_Silk_warped_LPC_analysis_filter_FLP(
            onePassState, onePass, 0, coefficients, 0, input, 0, warping, input.Length, order);

        var splitState = new float[order + 1];
        var split = new float[input.Length];
        PrefilterFLP.SKP_Silk_warped_LPC_analysis_filter_FLP(
            splitState, split, 0, coefficients, 0, input, 0, warping, 41, order);
        PrefilterFLP.SKP_Silk_warped_LPC_analysis_filter_FLP(
            splitState, split, 41, coefficients, 0, input, 41, warping, input.Length - 41, order);

        Assert.Equal(onePass, split);
        Assert.Equal(onePassState, splitState);
        Assert.Contains(splitState, value => value != 0.0f);
    }

    [Fact]
    public void SixthOrderLpcAnalysisFilterMatchesDirectCalculation()
    {
        var input = Enumerable.Range(0, 48)
            .Select(i => (float)(Math.Sin(i * 0.23) * 700.0 + i * 1.25))
            .ToArray();
        var coefficients = new[] { 0.31f, -0.19f, 0.13f, -0.08f, 0.04f, -0.015f };
        var actual = Enumerable.Repeat(float.NaN, input.Length).ToArray();

        LPCAnalysisFilterFLP.SKP_Silk_LPC_analysis_filter_FLP(
            actual, coefficients, input, 0, input.Length, 6);

        Assert.Equal(new float[6], actual[..6]);
        for (var i = 6; i < input.Length; i++)
        {
            var prediction = 0.0f;
            for (var coefficient = 0; coefficient < coefficients.Length; coefficient++)
                prediction += input[i - coefficient - 1] * coefficients[coefficient];

            Assert.Equal(input[i] - prediction, actual[i]);
        }
    }

    [Theory]
    [InlineData(24_000, 0, 8, 72, 264, 0)]
    [InlineData(24_000, 1, 12, 120, 360, 23592)]
    [InlineData(24_000, 2, 16, 120, 360, 23592)]
    [InlineData(16_000, 1, 12, 80, 240, 15728)]
    public void ComplexityConfiguresOfficialWarpingPipeline(
        int sampleRate, int complexity, int shapingOrder, int lookahead, int windowLength, int warpingQ16)
    {
        var state = new SKP_Silk_encoder_state_FLP();
        Assert.Equal(0, InitEncoderFLP.SKP_Silk_init_encoder_FLP(state));

        int result = ControlCodecFLP.SKP_Silk_control_encoder_FLP(
            state, sampleRate, sampleRate / 1000, 20, 25_000, 0, 0, 0, 20, complexity);

        Assert.Equal(0, result);
        Assert.Equal(complexity, state.sCmn.Complexity);
        Assert.Equal(shapingOrder, state.sCmn.shapingLPCOrder);
        Assert.Equal(lookahead, state.sCmn.la_shape);
        Assert.Equal(windowLength, state.sCmn.shapeWinLength);
        Assert.Equal(warpingQ16, state.sCmn.warping_Q16);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ComplexityEncodingIsDeterministic(int complexity)
    {
        var pcm = Enumerable.Range(0, 24_000 / 5)
            .Select(i => (short)(Math.Sin(2.0 * Math.PI * 440.0 * i / 24_000.0) * 8_000.0))
            .ToArray();
        var options = new SilkEncoderOptions { Complexity = complexity, Tencent = true };

        var first = new SilkEncoder(options).Encode(pcm);
        var second = new SilkEncoder(options).Encode(pcm);

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ComplexityTransitionPreservesMaximumLookaheadHistory()
    {
        const int sampleRate = 24_000;
        var state = new SKP_Silk_encoder_state_FLP();
        Assert.Equal(0, EncAPI.SKP_Silk_SDK_InitEncoder(state, new SKP_SILK_SDK_EncControlStruct()));
        var control = new SKP_SILK_SDK_EncControlStruct
        {
            API_sampleRate = sampleRate,
            maxInternalSampleRate = sampleRate,
            packetSize = sampleRate / 50,
            bitRate = 25_000,
            complexity = 0
        };
        var samples = Enumerable.Range(0, sampleRate / 50)
            .Select(i => (short)(4_000 + Math.Sin(i * 0.17) * 3_000))
            .ToArray();
        var output = new byte[1_250];
        var outputLength = new short[] { (short)output.Length };

        Assert.Equal(0, EncAPI.SKP_Silk_SDK_Encode(
            state, control, samples, 0, samples.Length, output, 0, outputLength));
        Assert.Contains(state.x_buf[552..600], value => value != 0.0f);

        control.complexity = 2;
        outputLength[0] = (short)output.Length;
        Assert.Equal(0, EncAPI.SKP_Silk_SDK_Encode(
            state, control, samples, 0, samples.Length, output, 0, outputLength));
        Assert.True(outputLength[0] > 0);
        Assert.Equal(2, state.sCmn.Complexity);
        Assert.True(state.sCmn.warping_Q16 > 0);
    }

    [Fact]
    public void NsqCloneDeepCopiesAllArrayState()
    {
        var original = new SKP_Silk_nsq_state();
        original.xq[0] = 1;
        original.sLTP_shp_Q10[0] = 2;
        original.sLPC_Q14[0] = 3;
        original.sAR2_Q14[0] = 4;

        var clone = (SKP_Silk_nsq_state)original.clone();
        clone.xq[0] = 11;
        clone.sLTP_shp_Q10[0] = 12;
        clone.sLPC_Q14[0] = 13;
        clone.sAR2_Q14[0] = 14;

        Assert.Equal((short)1, original.xq[0]);
        Assert.Equal(2, original.sLTP_shp_Q10[0]);
        Assert.Equal(3, original.sLPC_Q14[0]);
        Assert.Equal(4, original.sAR2_Q14[0]);
        Assert.NotSame(original.xq, clone.xq);
        Assert.NotSame(original.sLTP_shp_Q10, clone.sLTP_shp_Q10);
        Assert.NotSame(original.sLPC_Q14, clone.sLPC_Q14);
        Assert.NotSame(original.sAR2_Q14, clone.sAR2_Q14);
    }

    [Theory]
    [InlineData(1, 0, false)]
    [InlineData(2, 0, true)]
    [InlineData(1, 1, true)]
    public void WrapperDispatchesDelayedDecisionForMultipleStatesOrWarping(
        int delayedDecisionStates, int warpingQ16, bool expected)
    {
        var state = new SKP_Silk_encoder_state
        {
            nStatesDelayedDecision = delayedDecisionStates,
            warping_Q16 = warpingQ16
        };

        Assert.Equal(expected, WrappersFLP.UsesDelayedDecision(state));
    }

    [Fact]
    public void WarpedLbrrEncodingIsDeterministic()
    {
        var pcm = Enumerable.Range(0, 24_000 / 5)
            .Select(i => (short)(Math.Sin(2.0 * Math.PI * 220.0 * i / 24_000.0) * 10_000.0))
            .ToArray();
        var options = new SilkEncoderOptions
        {
            BitRate = 32_000,
            Complexity = 1,
            PacketLossPercentage = 10,
            UseInBandFec = true,
            Tencent = true
        };

        var first = new SilkEncoder(options).Encode(pcm);
        var second = new SilkEncoder(options).Encode(pcm);

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    private static void AssertDelayedDecisionGoldenVector(
        bool voiced,
        ulong expectedQHash,
        int expectedSeed,
        int expectedLagPrev,
        int expectedInvGain,
        int expectedLfAr,
        int[] expectedAr,
        int[] expectedLpc,
        short[] expectedXqFirst,
        short[] expectedXqLast,
        int[] expectedLtpFirst,
        int[] expectedLtpLast)
    {
        var encoder = new SKP_Silk_encoder_state
        {
            frame_length = 480,
            subfr_length = 120,
            shapingLPCOrder = 16,
            predictLPCOrder = 16,
            warping_Q16 = voiced ? 19661 : 23592,
            nStatesDelayedDecision = 4
        };
        var control = new SKP_Silk_encoder_control
        {
            Seed = voiced ? 1 : 2,
            sigtype = voiced ? Define.SIG_TYPE_VOICED : Define.SIG_TYPE_UNVOICED,
            QuantOffsetType = voiced ? 1 : 0
        };
        control.pitchL = voiced ? [80, 82, 78, 84] : [73, 74, 75, 76];

        var nsq = new SKP_Silk_nsq_state
        {
            sLF_AR_shp_Q12 = voiced ? -1700 : 2300,
            lagPrev = voiced ? 79 : 73,
            rand_seed = voiced ? -123456789 : 987654321,
            prev_inv_gain_Q16 = voiced ? 24500 : 27000
        };
        for (var i = 0; i < nsq.xq.Length; i++)
        {
            nsq.xq[i] = (short)(((i * 37 + (voiced ? 113 : 29)) % 12001) - 6000);
            nsq.sLTP_shp_Q10[i] = ((i * 7919 + (voiced ? 17011 : 9011)) % 400001) - 200000;
        }

        for (var i = 0; i < Define.NSQ_LPC_BUF_LENGTH(); i++)
            nsq.sLPC_Q14[i] = ((i * 4567 + (voiced ? 321 : 654)) % 60001) - 30000;
        for (var i = 0; i < nsq.sAR2_Q14.Length; i++)
            nsq.sAR2_Q14[i] = ((i * 2345 + (voiced ? 777 : 333)) % 20001) - 10000;

        var input = new short[480];
        for (var i = 0; i < input.Length; i++)
        {
            int value = ((i * 1877 + i * i * 13 + (voiced ? 991 : 313)) % 24001) - 12000;
            input[i] = (short)(voiced && i % 80 < 5 ? value + 7000 : value);
        }

        short[] predictionBase = [1150, -720, 510, -390, 300, -240, 190, -150, 120, -95, 75, -58, 44, -32, 22, -12];
        var prediction = new short[32];
        for (var i = 0; i < prediction.Length; i++)
            prediction[i] = (short)(predictionBase[i & 15] + (i >= 16 ? voiced ? 17 : -13 : 0));

        short[] ltpBase = [900, 1800, 2600, 1700, 800];
        var ltp = new short[20];
        for (var i = 0; i < ltp.Length; i++)
            ltp[i] = (short)(ltpBase[i % 5] + i / 5 * 31);

        var ar = new short[64];
        for (var i = 0; i < ar.Length; i++)
        {
            int magnitude = 500 - i % 16 * 21 + i / 16 * 9;
            ar[i] = (short)((i & 1) != 0 ? -magnitude : magnitude);
        }

        var harm = new int[4];
        var tilt = new int[4];
        var lf = new int[4];
        var gains = new int[4];
        for (var i = 0; i < 4; i++)
        {
            harm[i] = voiced ? 4200 + i * 240 : 1600 + i * 170;
            tilt[i] = -900 + i * 110;
            lf[i] = (8500 + i * 330) | ((6200 + i * 270) << 16);
            gains[i] = (voiced ? 142000 : 151000) + i * (voiced ? 9000 : 7000);
        }

        var q = new byte[480];
        NSQDelDec.SKP_Silk_NSQ_del_dec(
            encoder, control, nsq, input, q, voiced ? 2 : 4, prediction, ltp, ar,
            harm, tilt, lf, gains, voiced ? 1180 : 1040, voiced ? 12288 : 16384);

        Assert.Equal(expectedQHash, Fnv1A64(q));
        Assert.Equal(expectedSeed, control.Seed);
        Assert.Equal(voiced ? -123456789 : 987654321, nsq.rand_seed);
        Assert.Equal(expectedLagPrev, nsq.lagPrev);
        Assert.Equal(expectedInvGain, nsq.prev_inv_gain_Q16);
        Assert.Equal(expectedLfAr, nsq.sLF_AR_shp_Q12);
        Assert.Equal(expectedAr, nsq.sAR2_Q14);
        Assert.Equal(expectedLpc, nsq.sLPC_Q14[..Define.NSQ_LPC_BUF_LENGTH()]);
        Assert.Equal(expectedXqFirst, nsq.xq[..16]);
        Assert.Equal(expectedXqLast, nsq.xq[464..480]);
        Assert.Equal(expectedLtpFirst, nsq.sLTP_shp_Q10[..16]);
        Assert.Equal(expectedLtpLast, nsq.sLTP_shp_Q10[464..480]);
    }

    private static ulong Fnv1A64(byte[] values)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte value in values)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        return hash;
    }

    private static float[] ReferenceWarpedAutocorrelation(float[] input, float warping, int order)
    {
        var state = new double[order + 1];
        var correlation = new double[order + 1];
        foreach (float sample in input)
        {
            double current = sample;
            for (var i = 0; i < order; i += 2)
            {
                double even = state[i] + warping * (state[i + 1] - current);
                state[i] = current;
                correlation[i] += state[0] * current;

                current = state[i + 1] + warping * (state[i + 2] - even);
                state[i + 1] = even;
                correlation[i + 1] += state[0] * even;
            }

            state[order] = current;
            correlation[order] += state[0] * current;
        }

        return correlation.Select(value => (float)value).ToArray();
    }
}
