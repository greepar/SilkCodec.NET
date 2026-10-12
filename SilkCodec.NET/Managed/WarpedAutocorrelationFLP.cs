/*
 * Portions adapted from the SILK SDK 1.0.9 reference implementation.
 * Copyright (c) 2006-2012, Skype Limited. All rights reserved.
 * See THIRD-PARTY-NOTICES for the BSD 3-Clause license and disclaimer.
 */

namespace SilkCodec.NET.Managed;

internal static class WarpedAutocorrelationFLP
{
    internal static void SKP_Silk_warped_autocorrelation_FLP(
        float[] corr,
        int corrOffset,
        float[] input,
        int inputOffset,
        float warping,
        int length,
        int order)
    {
        SKP_Silk_warped_autocorrelation_FLP(corr, corrOffset, input, inputOffset, warping, length, order,
            new double[Define.SHAPE_LPC_ORDER_MAX + 1], new double[Define.SHAPE_LPC_ORDER_MAX + 1]);
    }

    internal static void SKP_Silk_warped_autocorrelation_FLP(
        float[] corr,
        int corrOffset,
        float[] input,
        int inputOffset,
        float warping,
        int length,
        int order,
        double[] state,
        double[] correlations)
    {
        EncoderCompat.Assert((order & 1) == 0);

        var stateSpan = state.AsSpan(0, order + 1);
        var correlationSpan = correlations.AsSpan(0, order + 1);
        var samples = input.AsSpan(inputOffset, length);
        stateSpan.Clear();
        correlationSpan.Clear();
        foreach (var sample in samples)
        {
            double tmp1 = sample;
            // The first allpass stage stores this input in state[0]. Keep it
            // in a local instead of loading that array element at every tap.
            double first = sample;
            for (var i = 0; i < order; i += 2)
            {
                var tmp2 = stateSpan[i] + warping * (stateSpan[i + 1] - tmp1);
                stateSpan[i] = tmp1;
                correlationSpan[i] += first * tmp1;

                tmp1 = stateSpan[i + 1] + warping * (stateSpan[i + 2] - tmp2);
                stateSpan[i + 1] = tmp2;
                correlationSpan[i + 1] += first * tmp2;
            }
            stateSpan[order] = tmp1;
            correlationSpan[order] += first * tmp1;
        }

        for (var i = 0; i <= order; i++)
            corr[corrOffset + i] = (float)correlations[i];
    }
}
