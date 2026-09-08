using SilkCodec.NET.Managed;
using Xunit;

namespace SilkCodec.NET.Tests;

public sealed class WarpedNoiseShapingTests
{
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
