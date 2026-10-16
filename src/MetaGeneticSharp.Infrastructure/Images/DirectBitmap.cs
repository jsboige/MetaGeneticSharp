using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GeneticSharp.Infrastructure.Framework.Images
{
    public class DirectBitmap : IDisposable, ICloneable
    {
        private Bitmap? _bitmap;

        /// <summary>
        /// GDI+ view over <see cref="Bits"/>, created on first access. Pixel reads and writes
        /// (<see cref="GetPixel"/>, <see cref="SetPixel"/>, <see cref="Bits"/>) never touch GDI+,
        /// so a <see cref="DirectBitmap"/> that is only filled and read works on every platform;
        /// GDI+ (System.Drawing.Common, Windows-only since .NET 7) is needed only when a caller asks
        /// for this property, e.g. to draw on it with <see cref="Graphics"/>. jsboige's original
        /// (@ d05826fd) built the view eagerly in the constructor, which made the type unusable
        /// outside Windows; the view itself is unchanged: same pinned array, same stride, same
        /// <see cref="PixelFormat.Format32bppPArgb"/> layout.
        /// </summary>
        public Bitmap Bitmap => _bitmap ??= CreateBitmap();
        public int[] Bits { get; private set; }
        public int Height { get; private set; }
        public int Width { get; private set; }

        protected GCHandle BitsHandle { get; private set; }

        public DirectBitmap(int width, int height): this(width, height, new Int32[width * height])
        {
        }


        public DirectBitmap(int width, int height, int[] existing)
        {
            Width = width;
            Height = height;
            // L3 micro-opt (additive perf, byte-identical output): jsboige's original copied
            // `existing` twice -- `new List<int>(existing)` allocates+fills a List's backing
            // array, then `.ToArray()` allocates+copies a second time. `Array.Clone()` does a
            // single allocation + one block copy, yielding a byte-identical int[]. The explicit
            // null check preserves the original ArgumentNullException-on-null contract (the
            // List<int> ctor threw it too). Credit jsboige @ d05826fd (MyIntelligenceAgency/
            // GeneticSharp, branch Metaheuristics) for the original DirectBitmap.
            Bits = existing is null
                ? throw new ArgumentNullException(nameof(existing))
                : (int[])existing.Clone();
        }

        private Bitmap CreateBitmap()
        {
            BitsHandle = GCHandle.Alloc(Bits, GCHandleType.Pinned);
            return new Bitmap(Width, Height, Width * 4, PixelFormat.Format32bppPArgb,
                BitsHandle.AddrOfPinnedObject());
        }

        public void SetPixel(int x, int y, Color colour)
        {
            int index = x + (y * Width);
            int col = colour.ToArgb();

            Bits[index] = col;
        }

        public Color GetPixel(int x, int y)
        {
            int index = x + (y * Width);
            int col = Bits[index];
            Color result = Color.FromArgb(col);

            return result;
        }
      
       

        object ICloneable.Clone()
        {
            return Clone();
        }


        public DirectBitmap Clone()
        {
            return new DirectBitmap(Width, Height, Bits);
        }

        public void Dispose()
        {
            _bitmap?.Dispose();
            _bitmap = null;
            if (BitsHandle.IsAllocated)
            {
                BitsHandle.Free();
            }
        }
    }
}