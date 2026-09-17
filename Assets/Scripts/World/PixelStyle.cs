using System;
using UnityEngine;

namespace JurassicPark.World
{
    public static class PixelStyle
    {
        public static Color32[] Remap(Color32[] source, int width, int height, int step, Color32[] palette,
            out int outputWidth, out int outputHeight, int luminanceRampColors = 0)
        {
            if (source == null || width < 1 || height < 1 || source.Length != width * height)
                throw new ArgumentException("Pixel dimensions must match the source image.");
            if (step < 1 || palette == null || palette.Length == 0)
                throw new ArgumentException("A positive pixel step and a nonempty palette are required.");
            if (luminanceRampColors < 0 || luminanceRampColors > palette.Length)
                throw new ArgumentException("The luminance ramp must fit inside the shared palette.");
            outputWidth = (width + step - 1) / step;
            outputHeight = (height + step - 1) / step;
            var result = new Color32[outputWidth * outputHeight];
            for (int y = 0; y < outputHeight; y++)
            for (int x = 0; x < outputWidth; x++)
            {
                float red = 0, green = 0, blue = 0, alpha = 0;
                int samples = 0;
                for (int sy = y * step; sy < Mathf.Min(height, (y + 1) * step); sy++)
                for (int sx = x * step; sx < Mathf.Min(width, (x + 1) * step); sx++)
                {
                    Color32 pixel = source[sy * width + sx];
                    red += pixel.r * pixel.a;
                    green += pixel.g * pixel.a;
                    blue += pixel.b * pixel.a;
                    alpha += pixel.a;
                    samples++;
                }
                if (alpha == 0) continue;
                // Alpha-weighted reduction removes fine source dithering without bleeding transparent RGB.
                Color32 mapped;
                if (luminanceRampColors > 0)
                {
                    float luminance = (red * 0.2126f + green * 0.7152f + blue * 0.0722f) / (alpha * 255f);
                    mapped = palette[Mathf.Clamp(Mathf.RoundToInt(luminance * (luminanceRampColors - 1)), 0, luminanceRampColors - 1)];
                }
                else mapped = Nearest(red / alpha, green / alpha, blue / alpha, palette);
                mapped.a = (byte)Mathf.RoundToInt(alpha / samples);
                result[y * outputWidth + x] = mapped;
            }
            return result;
        }

        private static Color32 Nearest(float red, float green, float blue, Color32[] palette)
        {
            float best = float.PositiveInfinity;
            Color32 result = palette[0];
            foreach (Color32 color in palette)
            {
                float r = color.r - red, g = color.g - green, b = color.b - blue;
                float distance = r * r + g * g + b * b;
                if (distance >= best) continue;
                best = distance;
                result = color;
            }
            return result;
        }
    }
}
