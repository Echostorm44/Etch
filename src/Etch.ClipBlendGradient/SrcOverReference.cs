namespace Etch.ClipBlendGradient;

public static class SrcOverReference
{
    public static Rgba16f Composite(Rgba16f src, Rgba16f dst)
    {
        float srcR = (float)src.R;
        float srcG = (float)src.G;
        float srcB = (float)src.B;
        float srcA = (float)src.A;

        float dstR = (float)dst.R;
        float dstG = (float)dst.G;
        float dstB = (float)dst.B;
        float dstA = (float)dst.A;

        float resultA = srcA + dstA * (1.0f - srcA);
        if (resultA < 0.0001f)
            return Rgba16f.Zero;

        float invSrcA = 1.0f - srcA;
        float resultR = (srcR * srcA + dstR * dstA * invSrcA) / resultA;
        float resultG = (srcG * srcA + dstG * dstA * invSrcA) / resultA;
        float resultB = (srcB * srcA + dstB * dstA * invSrcA) / resultA;

        return Rgba16f.From(resultR, resultG, resultB, resultA);
    }

    public static Rgba16f CompositePremultiplied(Rgba16f srcPremul, Rgba16f dstPremul)
    {
        float srcR = (float)srcPremul.R;
        float srcG = (float)srcPremul.G;
        float srcB = (float)srcPremul.B;
        float srcA = (float)srcPremul.A;

        float dstR = (float)dstPremul.R;
        float dstG = (float)dstPremul.G;
        float dstB = (float)dstPremul.B;
        float dstA = (float)dstPremul.A;

        float resultA = srcA + dstA * (1.0f - srcA);
        if (resultA < 0.0001f)
            return Rgba16f.Zero;

        float invSrcA = 1.0f - srcA;
        float resultR = srcR + dstR * invSrcA;
        float resultG = srcG + dstG * invSrcA;
        float resultB = srcB + dstB * invSrcA;

        return Rgba16f.From(resultR, resultG, resultB, resultA);
    }
}
