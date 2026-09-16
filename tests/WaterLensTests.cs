using System;
using System.Collections.Generic;
using System.Diagnostics;
using FreeIsland;

// Pure pixel/math fixtures. Compile with WaterLens.cs only; this executable never
// captures the screen, creates a window, or accesses app settings or OS controls.
internal static class WaterLensTests
{
    private static int passed, failed;
    private static readonly int[] Sizes = { 80, 80, 440, 102, 660, 153 };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Run(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS: " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL: " + name + " -> " + error.GetType().Name + ": " + error.Message); }
    }

    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, message + " must be rejected with an argument exception");
    }

    private static byte[] Solid(int width, int height, int stride, byte blue, byte green, byte red)
    {
        byte[] frame = new byte[stride * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * stride + x * 4;
                frame[index] = blue; frame[index + 1] = green; frame[index + 2] = red; frame[index + 3] = 255;
            }
        return frame;
    }

    private static byte[] Checker(int width, int height, int cell)
    {
        byte[] frame = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool blue = ((x / cell + y / cell) & 1) == 0;
                int index = (y * width + x) * 4;
                frame[index] = blue ? (byte)250 : (byte)20;
                frame[index + 1] = 12;
                frame[index + 2] = blue ? (byte)20 : (byte)250;
                frame[index + 3] = 255;
            }
        return frame;
    }

    private static void Premultiplied(WaterLens lens)
    {
        Check(lens.Pixels.Length == lens.Width * lens.Height * 4, "pixel storage must match dimensions");
        for (int i = 0; i < lens.Pixels.Length; i += 4)
        {
            int alpha = lens.Pixels[i + 3];
            Check(lens.Pixels[i] <= alpha && lens.Pixels[i + 1] <= alpha && lens.Pixels[i + 2] <= alpha,
                "premultiplied channels must not exceed coverage alpha");
            if (alpha == 0) Check(lens.Pixels[i] == 0 && lens.Pixels[i + 1] == 0 && lens.Pixels[i + 2] == 0,
                "transparent pixels must not retain RGB from a previous frame");
        }
        Check(!Double.IsNaN(lens.MeanBrightness) && lens.MeanBrightness >= 0 && lens.MeanBrightness <= 1,
            "mean brightness must remain finite and normalized");
    }

    private static void GeometricRefraction()
    {
        const int bw = 192, bh = 144, cell = 7, ox = 37, oy = 29;
        byte[] background = Checker(bw, bh, cell);
        var lens = new WaterLens(112, 76);
        lens.Shape(28, 0, 0, 0);
        lens.Refract(background, bw, bh, bw * 4, ox, oy, 1, 1);
        int hueFlips = 0, visible = 0;
        for (int y = 0; y < lens.Height; y++)
            for (int x = 0; x < lens.Width; x++)
            {
                int pixel = (y * lens.Width + x) * 4;
                if (lens.Pixels[pixel + 3] != 255) continue;
                visible++;
                int bx = ox + x, by = oy + y;
                // Exclude source boundaries: a direct sample at x+.5/y+.5 has
                // the same dominant hue here. Neutral lighting cannot flip B/R.
                if (bx % cell == cell - 1 || by % cell == cell - 1) continue;
                int source = (by * bw + bx) * 4;
                int oldHue = background[source] - background[source + 2];
                int newHue = lens.Pixels[pixel] - lens.Pixels[pixel + 2];
                if (Math.Abs(newHue) > 30 && oldHue * newHue < 0) hueFlips++;
            }
        Check(visible > 3000, "fixture must cover an actual lens interior");
        Check(hueFlips > 100, "the lens must sample displaced background cells, not merely tint the original pixels");
        Console.WriteLine("  displaced checker hues=" + hueFlips + ", opaque interior pixels=" + visible);
        Premultiplied(lens);
    }

    private static void ClearCenterAndAntialiasing()
    {
        var lens = new WaterLens(120, 80);
        lens.Shape(32, 0, 0, 0);
        lens.Refract(Solid(160, 120, 640, 39, 113, 181), 160, 120, 640, 20, 20, 1, 1);
        int center = ((lens.Height / 2) * lens.Width + lens.Width / 2) * 4;
        byte[] color = { 39, 113, 181 };
        for (int channel = 0; channel < 3; channel++)
            Check(Math.Abs(lens.Pixels[center + channel] - color[channel]) <= 4,
                "clear center must retain backdrop color rather than a milky or blue fill");
        Check(lens.Pixels[center + 3] == 255, "captured background must fully cover the center without double blending");
        var partialValues = new HashSet<byte>();
        int clear = 0, partial = 0, opaque = 0;
        for (int i = 3; i < lens.Pixels.Length; i += 4)
        {
            byte alpha = lens.Pixels[i];
            if (alpha == 0) clear++; else if (alpha == 255) opaque++; else { partial++; partialValues.Add(alpha); }
        }
        Check(clear > 0 && partial > 30 && partialValues.Count > 5 && opaque > clear,
            "rounded silhouette needs transparent exterior, graded antialiasing and a filled center");
        Check(lens.Pixels[3] == 0 && lens.Pixels[lens.Pixels.Length - 1] == 0, "outer corners remain transparent");
        Premultiplied(lens);
    }

    private static void StrideOffsetAndClampedSampling()
    {
        const int bw = 44, bh = 31, paddedStride = bw * 4 + 28;
        byte[] tight = Checker(bw, bh, 5), padded = new byte[paddedStride * bh];
        for (int i = 0; i < padded.Length; i++) padded[i] = 231;
        for (int y = 0; y < bh; y++) Buffer.BlockCopy(tight, y * bw * 4, padded, y * paddedStride, bw * 4);
        var a = new WaterLens(72, 48); var b = new WaterLens(72, 48);
        a.Shape(19, .6, -.7, .8); b.Shape(19, .6, -.7, .8);
        a.Refract(tight, bw, bh, bw * 4, -30, 12.25, 1.8, .75);
        b.Refract(padded, bw, bh, paddedStride, -30, 12.25, 1.8, .75);
        for (int i = 0; i < a.Pixels.Length; i++) Check(a.Pixels[i] == b.Pixels[i], "row padding must never enter the image");
        double[] offsets = { -1e12, 0, 1e12 };
        foreach (double offset in offsets)
        {
            a.Refract(tight, bw, bh, bw * 4, offset, -offset, 2, .5);
            Premultiplied(a);
        }
    }

    private static void FiniteShapeBoundaries()
    {
        int[] widths = { -4, 0, 2, 3, 4, 5, 17, 80 };
        byte[] frame = Checker(100, 96, 6);
        foreach (int width in widths)
        {
            var lens = new WaterLens(width, Math.Max(2, width / 2));
            Check(lens.Width >= 2 && lens.Height >= 2, "small dimensions are normalized");
            double[] radii = { -100, 0, .1, 3, 100000 };
            foreach (double radius in radii)
            {
                lens.Shape(radius, 100, -100, 100);
                lens.Refract(frame, 100, 96, 400, 0, 0, 1, 1);
                Premultiplied(lens);
                lens.Shape(radius, -100, 100, -100);
                lens.Refract(frame, 100, 96, 400, 0, 0, 1, 1);
                Premultiplied(lens);
            }
        }
    }

    private static void RejectInvalidFrames()
    {
        var lens = new WaterLens(40, 24); lens.Shape(10, 0, 0, 0);
        Reject(delegate { lens.Refract(null, 2, 2, 8, 0, 0, 1, 1); }, "null frame");
        Reject(delegate { lens.Refract(new byte[16], 1, 2, 8, 0, 0, 1, 1); }, "too narrow frame");
        Reject(delegate { lens.Refract(new byte[16], 2, 1, 8, 0, 0, 1, 1); }, "too short frame");
        Reject(delegate { lens.Refract(new byte[16], 2, 2, 7, 0, 0, 1, 1); }, "short row stride");
        Reject(delegate { lens.Refract(new byte[15], 2, 2, 8, 0, 0, 1, 1); }, "truncated frame");
    }

    private static void RejectOverflowFrames()
    {
        var lens = new WaterLens(40, 24); lens.Shape(10, 0, 0, 0);
        Reject(delegate { lens.Refract(new byte[16], 2, 2, Int32.MaxValue, 0, 0, 1, 1); }, "overflowing stride product");
        Reject(delegate { lens.Refract(new byte[16], Int32.MaxValue, 2, 8, 0, 0, 1, 1); }, "overflowing width product");
    }

    private static void RejectOverflowDimensions()
    {
        // Int32.MaxValue squared wraps to one: the unfixed implementation only
        // allocates a few bytes here, so this fixture cannot exhaust memory.
        Reject(delegate { new WaterLens(Int32.MaxValue, Int32.MaxValue); }, "overflowing lens dimensions");
    }

    private static void RejectNonFiniteShape()
    {
        double[] invalid = { Double.NaN, Double.PositiveInfinity, Double.NegativeInfinity };
        foreach (double number in invalid)
            for (int parameter = 0; parameter < 4; parameter++)
            {
                var lens = new WaterLens(40, 24); double[] values = { 10, 0, 0, 0 }; values[parameter] = number;
                Reject(delegate { lens.Shape(values[0], values[1], values[2], values[3]); }, "non-finite shape argument");
            }
    }

    private static void RejectNonFiniteSampling()
    {
        var lens = new WaterLens(40, 24); lens.Shape(10, 0, 0, 0); byte[] frame = Checker(48, 32, 4);
        double[] invalid = { Double.NaN, Double.PositiveInfinity, Double.NegativeInfinity };
        foreach (double number in invalid)
            for (int parameter = 0; parameter < 4; parameter++)
            {
                double[] values = { 0, 0, 1, 1 }; values[parameter] = number;
                Reject(delegate { lens.Refract(frame, 48, 32, 192, values[0], values[1], values[2], values[3]); }, "non-finite sampling transform");
            }
    }

    private static void SpringConvergence()
    {
        double[] rates = { 30, 60, 144 };
        foreach (double rate in rates)
        {
            var spring = new WaterSpring { Target = 1 };
            bool moving = true; double peak = 0; int steps = 0;
            while (moving && steps++ < rate * 3)
            {
                moving = spring.Advance(1 / rate); peak = Math.Max(peak, spring.Value);
                Check(!Double.IsNaN(spring.Value) && !Double.IsInfinity(spring.Value), "press must remain finite");
            }
            Check(!moving && spring.Value == 1 && spring.Velocity == 0 && peak < 1.05, "press converges with limited overshoot");
            spring.Target = 0; moving = true; steps = 0;
            while (moving && steps++ < rate * 3)
            {
                // A single delayed frame exercises the maximum time-step guard.
                moving = spring.Advance(steps == 3 ? .8 : 1 / rate);
                Check(spring.Value > -.05 && spring.Value < 1.05, "release must not jump or repeatedly oscillate");
            }
            Check(!moving && spring.Value == 0 && spring.Velocity == 0, "release stops at exact rest");
            Check(!spring.Advance(1 / rate), "settled spring must not request idle animation");
        }
    }

    private static void NonFiniteSpringTime()
    {
        Reject(delegate { new WaterSpring { Target = 1 }.Advance(Double.NaN); }, "NaN elapsed time");
    }

    private static void Performance()
    {
        byte[] frame = Checker(1100, 400, 13);
        for (int index = 0; index < Sizes.Length; index += 2)
        {
            var lens = new WaterLens(Sizes[index], Sizes[index + 1]);
            lens.Shape(Math.Min(lens.Width, lens.Height) * .42, 0, 0, 0);
            for (int warm = 0; warm < 4; warm++) lens.Refract(frame, 1100, 400, 4400, 20, 20, 1, 1);
            const int count = 20; var timer = Stopwatch.StartNew();
            for (int run = 0; run < count; run++) lens.Refract(frame, 1100, 400, 4400, 20 + run, 20, 1, 1);
            double sampleMs = timer.Elapsed.TotalMilliseconds / count;
            timer.Restart();
            for (int run = 0; run < count; run++)
            {
                lens.Shape(Math.Min(lens.Width, lens.Height) * .42, run / 20.0, .15, -.08);
                lens.Refract(frame, 1100, 400, 4400, 20 + run, 20, 1, 1);
            }
            double deformMs = timer.Elapsed.TotalMilliseconds / count;
            Premultiplied(lens);
            Console.WriteLine("PERF " + lens.Width + "x" + lens.Height + ": cached-shape refract=" + sampleMs.ToString("F2") +
                " ms/frame; deform+refract=" + deformMs.ToString("F2") + " ms/frame; 60Hz-budget=" + (deformMs <= 16.67 ? "within" : "exceeded"));
        }
    }

    private static void AdjustableOptics()
    {
        var lens = new WaterLens(112, 76);
        byte[] background = Checker(192, 144, 7);
        lens.Shape(28, 0, 0, 0, false, 0);
        lens.Refract(background, 192, 144, 768, 37, 29, 1, 1, 1, 0);
        byte[] flat = (byte[])lens.Pixels.Clone();
        lens.Shape(28, 0, 0, 0, false, 2);
        lens.Refract(background, 192, 144, 768, 37, 29, 1, 1, 1, 0);
        int displaced = 0;
        for (int i = 0; i < flat.Length; i += 4) if (Math.Abs(flat[i] - lens.Pixels[i]) > 30) displaced++;
        Check(displaced > 500, "refraction slider must change sampled pixels, not only lighting");
        lens.Shape(28, 0, 0, 0, false, 0);
        lens.Refract(background, 192, 144, 768, 37, 29, 1, 1, 1, 0);
        for (int i = 0; i < flat.Length; i++) Check(flat[i] == lens.Pixels[i], "returning to zero must invalidate cached optical shape");
        byte[] solid = Solid(192, 144, 768, 39, 113, 181);
        lens.Refract(solid, 192, 144, 768, 37, 29, 1, 1, 0, 0);
        int center = (38 * 112 + 56) * 4;
        Check(lens.Pixels[center] == 248 && lens.Pixels[center + 1] == 248 && lens.Pixels[center + 2] == 248, "zero transmission produces a dense neutral surface");
        lens.Refract(solid, 192, 144, 768, 37, 29, 1, 1, 1, 0);
        Check(lens.Pixels[center] == 39 && lens.Pixels[center + 1] == 113 && lens.Pixels[center + 2] == 181, "maximum transmission preserves the source colors");
        byte[] unlit = (byte[])lens.Pixels.Clone();
        lens.Refract(solid, 192, 144, 768, 37, 29, 1, 1, 1, 2);
        int lit = 0; for (int i = 0; i < flat.Length; i += 4) if (unlit[i] != lens.Pixels[i]) lit++;
        Check(lit > 100, "highlight slider must change the material rim");
        Premultiplied(lens);
        Reject(delegate { lens.Shape(28, 0, 0, 0, false, Double.NaN); }, "non-finite refraction");
        Reject(delegate { lens.Refract(solid, 192, 144, 768, 37, 29, 1, 1, Double.NaN, 1); }, "non-finite transparency");
        Reject(delegate { lens.Refract(solid, 192, 144, 768, 37, 29, 1, 1, 1, Double.PositiveInfinity); }, "non-finite highlight");
    }

    private static int Main()
    {
        Run("actual geometric displacement of fixed checker colors", GeometricRefraction);
        Run("clear center, rounded coverage and antialiased premultiplied edges", ClearCenterAndAntialiasing);
        Run("padded rows, source offsets and boundary-clamped sampling", StrideOffsetAndClampedSampling);
        Run("small dimensions and finite radius/pressure/pull extremes", FiniteShapeBoundaries);
        Run("incomplete backdrop arguments", RejectInvalidFrames);
        Run("overflow-safe backdrop dimensions and stride", RejectOverflowFrames);
        Run("overflow-safe lens dimensions", RejectOverflowDimensions);
        Run("non-finite shape arguments rejected at the boundary", RejectNonFiniteShape);
        Run("non-finite source transforms rejected at the boundary", RejectNonFiniteSampling);
        Run("press/release convergence at 30/60/144 Hz and delayed frame", SpringConvergence);
        Run("non-finite spring time rejected", NonFiniteSpringTime);
        Run("independent refraction, transmission and highlight controls", AdjustableOptics);
        Run("typical-size managed CPU performance", Performance);
        Console.WriteLine("RESULT " + passed + " passed, " + failed + " failed. Fixed synthetic pixels only; no desktop capture or OS actions.");
        return failed == 0 ? 0 : 1;
    }
}
