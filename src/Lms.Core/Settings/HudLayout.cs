using System;
using Lms.Core.Diagnostics;

namespace Lms.Core.Settings
{
    /// <summary>
    ///     Resolution-independent placement of the HUD panels: minimap top left, zone readout top right.
    /// </summary>
    /// <remarks>
    ///     Panel sizes are authored in pixels at <see cref="ReferenceHeight"/> and multiplied by
    ///     <see cref="Scale"/>, so the HUD covers the same fraction of the screen at any resolution.
    /// </remarks>
    public readonly struct HudLayout
    {
        /// <summary>The screen height the pixel sizes below were authored against.</summary>
        public const float ReferenceHeight = 1080f;

        private const float MapPixels = 232f;
        private const float MarginPixels = 12f;
        private const float ReadoutWidthPixels = 460f;
        private const float ReadoutHeightPixels = 114f;

        /// <summary>
        ///     Minimum scale, so text on small screens (e.g. 720p) stays legible.
        /// </summary>
        private const float SmallestScale = 0.67f;

        /// <summary>
        ///     Maximum scale, so the HUD does not cover the game on very large screens.
        /// </summary>
        private const float LargestScale = 2f;

        public HudLayout(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), height,
                    "A screen with no pixels has no corners to put anything in.");
            }

            Width = width;
            Height = height;
            Scale = Clamped(height / ReferenceHeight);
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Multiplier applied to every authored pixel size.</summary>
        public float Scale { get; }

        /// <summary>The gap from the screen edge.</summary>
        public float Margin => MarginPixels * Scale;

        /// <summary>The minimap, square, in the top-left corner.</summary>
        public HudRect Minimap => new HudRect(Margin, Margin, MapPixels * Scale, MapPixels * Scale);

        /// <summary>The zone readout, right-aligned in the top-right corner.</summary>
        public HudRect Readout =>
            new HudRect(Width - ReadoutWidthPixels * Scale - Margin, Margin,
                ReadoutWidthPixels * Scale, ReadoutHeightPixels * Scale);

        /// <summary>
        ///     Scales a font size the same way as the panels.
        /// </summary>
        /// <remarks>
        ///     <paramref name="authored"/> is the size the game's skin uses, so a scale of 1 reproduces the
        ///     unscaled look exactly. Rounded to avoid blurry text and floored at 9.
        /// </remarks>
        public int Font(int authored) =>
            Math.Max(9, (int)Math.Round(Math.Max(1, authored) * Scale, MidpointRounding.AwayFromZero));

        /// <summary>
        ///     Whether the two panels overlap, which happens on narrow windows. The caller decides what to
        ///     drop (the HUD hides the readout).
        /// </summary>
        public bool PanelsCollide => Minimap.Right > Readout.Left;

        public override string ToString() =>
            Readable.Line($"{Width}x{Height} at {Scale:0.00}x: map {Minimap}, readout {Readout}");

        private static float Clamped(float scale) =>
            scale < SmallestScale ? SmallestScale : scale > LargestScale ? LargestScale : scale;
    }

    /// <summary>A rectangle in screen pixels, origin top left. Core has no Unity types, so not <c>Rect</c>.</summary>
    public readonly struct HudRect
    {
        public HudRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public float X { get; }

        public float Y { get; }

        public float Width { get; }

        public float Height { get; }

        public float Left => X;

        public float Right => X + Width;

        public float Top => Y;

        public float Bottom => Y + Height;

        public override string ToString() => Readable.Line($"({X:0}, {Y:0}) {Width:0}x{Height:0}");
    }
}
