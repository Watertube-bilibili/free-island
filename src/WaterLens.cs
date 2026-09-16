using System;

namespace FreeIsland
{
    // A shallow convex lens. The same distance field defines the silhouette,
    // refracted coordinates and rim lighting, including during deformation.
    internal sealed class WaterLens
    {
        internal readonly int Width, Height;
        internal readonly byte[] Pixels;
        private readonly float[] sampleX, sampleY, light;
        private readonly byte[] coverage;
        private double previousRadius = -1, previousPressure, previousX, previousY, previousRefraction = -1;
        private bool previousCompact;
        internal double MeanBrightness { get; private set; }

        internal WaterLens(int width, int height)
        {
            Width = Math.Max(2, width); Height = Math.Max(2, height);
            if ((long)Width * Height > 4 * 1024 * 1024) throw new ArgumentOutOfRangeException("width", "Lens dimensions exceed the pixel budget.");
            int count = Width * Height;
            Pixels = new byte[count * 4]; sampleX = new float[count]; sampleY = new float[count];
            light = new float[count]; coverage = new byte[count];
        }

        internal void Shape(double radius, double pressure, double pullX, double pullY, bool compact = false, double refraction = 1)
        {
            if (!Finite(radius) || !Finite(pressure) || !Finite(pullX) || !Finite(pullY) || !Finite(refraction)) throw new ArgumentException("Non-finite lens shape.");
            refraction = Clamp(refraction, 0, 2);
            radius = Math.Max(2, Math.Min(radius, Math.Min(Width, Height) * .5));
            pressure = Clamp(pressure, -.25, 1.1); pullX = Clamp(pullX, -1, 1); pullY = Clamp(pullY, -1, 1);
            if (refraction == previousRefraction && compact == previousCompact && Math.Abs(radius - previousRadius) < .01 && Math.Abs(pressure - previousPressure) < .003 &&
                Math.Abs(pullX - previousX) < .005 && Math.Abs(pullY - previousY) < .005) return;
            previousRadius = radius; previousPressure = pressure; previousX = pullX; previousY = pullY;
            previousCompact = compact;
            previousRefraction = refraction;
            double halfW = Width * .5, halfH = Height * .5;
            double sx = 1 + pressure * .025 + Math.Abs(pullX) * .025 - Math.Abs(pullY) * .009;
            double sy = 1 - pressure * .037 + Math.Abs(pullY) * .025 - Math.Abs(pullX) * .009;
            double inset = compact ? .5 : Math.Max(2, Math.Min(Width, Height) * .045);
            double edgeWidth = Math.Max(4, Math.Min(16, Math.Min(Width, Height) * .20));
            double strength = edgeWidth * .68 * (1 - pressure * .15) * refraction;
            double r = Math.Min(radius, Math.Min(halfW - inset, halfH - inset));
            double straightX = halfW - inset - r, straightY = halfH - inset - r;
            for (int y = 0, i = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++, i++)
                {
                    double px = (x + .5 - halfW - pullX * inset * .22) / sx;
                    double py = (y + .5 - halfH - pullY * inset * .22) / sy;
                    double qx = Math.Abs(px) - straightX, qy = Math.Abs(py) - straightY;
                    double ax = Math.Max(qx, 0), ay = Math.Max(qy, 0);
                    double corner = Math.Sqrt(ax * ax + ay * ay);
                    double distance = corner + Math.Min(Math.Max(qx, qy), 0) - r;
                    double alpha = Clamp(.6 - distance, 0, 1);
                    coverage[i] = (byte)(alpha * 255 + .5);
                    if (alpha == 0) { light[i] = 0; continue; }
                    double nx, ny;
                    if (corner > .0001) { nx = ax / corner * Math.Sign(px); ny = ay / corner * Math.Sign(py); }
                    else if (qx > qy) { nx = Math.Sign(px); ny = 0; }
                    else { nx = 0; ny = Math.Sign(py); }
                    double depth = Math.Max(0, -distance);
                    double bevel = Clamp(1 - depth / edgeWidth, 0, 1);
                    // A clear interior, mild magnification and a stronger curved lip.
                    double bend = strength * bevel * bevel * (3 - 2 * bevel);
                    sampleX[i] = (float)(x + .5 + (halfW + px / (1 + .028 * refraction) - nx * bend - x - .5) * Math.Min(1, refraction));
                    sampleY[i] = (float)(y + .5 + (halfH + py / (1 + .028 * refraction) - ny * bend - y - .5) * Math.Min(1, refraction));
                    double topLight = Math.Max(0, -nx * .55 - ny * .83);
                    double bottomLight = Math.Max(0, nx * .68 + ny * .74);
                    double rim = Math.Exp(-depth * depth / 1.35);
                    double insideRim = Math.Exp(-(depth - 2.2) * (depth - 2.2) / 3.2);
                    // Neutral water, not a blue or milky panel. Opposing reflections
                    // identify the curved surface without covering the whole lens.
                    light[i] = (float)(.012 + rim * (.66 * topLight + .30 * bottomLight) -
                        insideRim * (.10 + .12 * (1 - topLight)));
                }
            }
        }

        internal void Refract(byte[] backdrop, int backgroundWidth, int backgroundHeight, int backgroundStride,
            double sourceX, double sourceY, double sourceScaleX, double sourceScaleY, double transparency = 1, double highlight = 1)
        {
            if (backdrop == null || backgroundWidth < 2 || backgroundHeight < 2 || backgroundStride < (long)backgroundWidth * 4 ||
                backdrop.LongLength < (long)backgroundStride * backgroundHeight) throw new ArgumentException("Incomplete backdrop frame.");
            if (!Finite(sourceX) || !Finite(sourceY) || !Finite(sourceScaleX) || !Finite(sourceScaleY) || !Finite(transparency) || !Finite(highlight)) throw new ArgumentException("Non-finite sampling transform.");
            transparency = Clamp(transparency, 0, 1); highlight = Clamp(highlight, 0, 2);
            // Transmission changes material density, never label opacity. Refracted
            // backdrop stays opaque to avoid mixing two displaced copies of a slide.
            double density = Math.Pow(1 - transparency, 3);
            double brightness = 0; int samples = 0;
            for (int i = 0; i < coverage.Length; i++)
            {
                int destination = i * 4, alpha = coverage[i];
                if (alpha == 0) { Pixels[destination] = Pixels[destination + 1] = Pixels[destination + 2] = Pixels[destination + 3] = 0; continue; }
                double bx = Clamp(sourceX + sampleX[i] * sourceScaleX, 0, backgroundWidth - 1.001);
                double by = Clamp(sourceY + sampleY[i] * sourceScaleY, 0, backgroundHeight - 1.001);
                int ix = (int)bx, iy = (int)by, wx = (int)((bx - ix) * 256), wy = (int)((by - iy) * 256);
                int a = iy * backgroundStride + ix * 4, b = a + 4, c = a + backgroundStride, d = c + 4;
                double illumination = Clamp(light[i] * highlight, -.8, .95);
                for (int channel = 0; channel < 3; channel++)
                {
                    int top = backdrop[a + channel] * (256 - wx) + backdrop[b + channel] * wx;
                    int bottom = backdrop[c + channel] * (256 - wx) + backdrop[d + channel] * wx;
                    double value = ((top * (256 - wy) + bottom * wy) >> 16);
                    value += (248 - value) * density;
                    if (i % 41 == 0) { brightness += value / 255; samples++; }
                    value = illumination >= 0 ? value + (255 - value) * illumination : value * (1 + illumination);
                    Pixels[destination + channel] = (byte)(Clamp(value, 0, 255) * alpha / 255);
                }
                Pixels[destination + 3] = (byte)alpha;
            }
            MeanBrightness = samples == 0 ? .5 : brightness / samples;
        }

        internal static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        internal static double Clamp(double value, double minimum, double maximum) { return Math.Max(minimum, Math.Min(maximum, value)); }
    }

    internal sealed class WaterSpring
    {
        internal double Value, Velocity, Target;
        internal bool Advance(double seconds)
        {
            if (!WaterLens.Finite(seconds)) throw new ArgumentException("Non-finite elapsed time.");
            seconds = WaterLens.Clamp(seconds, .001, .04);
            // Substeps keep a suspended UI thread from producing a large jump.
            int steps = Math.Max(1, (int)Math.Ceiling(seconds / .008)); double dt = seconds / steps;
            for (int i = 0; i < steps; i++) { Velocity += ((Target - Value) * 235 - Velocity * 25) * dt; Value += Velocity * dt; }
            if (Math.Abs(Target - Value) < .001 && Math.Abs(Velocity) < .01) { Value = Target; Velocity = 0; return false; }
            return true;
        }
    }
}
