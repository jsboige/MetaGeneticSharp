using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace GeneticSharp.Infrastructure.Framework.Images
{
    public static class ImageExtensions
    {

        public static DirectBitmap MakeGrayscaleImage(this Image original)
        {
            //create a blank bitmap the same size as original
            DirectBitmap newBitmap = new DirectBitmap(original.Width, original.Height);

            //get a graphics object from the new image
            using (Graphics g = Graphics.FromImage(newBitmap.Bitmap))
            {

                //create the grayscale ColorMatrix
                ColorMatrix colorMatrix = new ColorMatrix(
                    new float[][]
                    {
                        new float[] {.3f, .3f, .3f, 0, 0},
                        new float[] {.59f, .59f, .59f, 0, 0},
                        new float[] {.11f, .11f, .11f, 0, 0},
                        new float[] {0, 0, 0, 1, 0},
                        new float[] {0, 0, 0, 0, 1}
                    });

                //create some image attributes
                using (ImageAttributes attributes = new ImageAttributes())
                {

                    //set the color matrix attribute
                    attributes.SetColorMatrix(colorMatrix);

                    //draw the original image on the new image
                    //using the grayscale color matrix
                    g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                        0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            return newBitmap;
        }

        /// <summary>
        /// Platform-independent counterpart of <see cref="MakeGrayscaleImage(Image)"/>: applies the
        /// same grayscale <see cref="ColorMatrix"/> (luma weights .3, .59, .11 on every color
        /// channel, alpha unchanged) to the pixels of a <see cref="DirectBitmap"/>, in managed
        /// code and without GDI+. The result is a new, independent <see cref="DirectBitmap"/>;
        /// <paramref name="original"/> is left untouched and still belongs to the caller.
        ///
        /// <para>Pixels are read and written in the <see cref="PixelFormat.Format32bppPArgb"/>
        /// layout of <see cref="DirectBitmap.Bits"/>: a translucent pixel is un-premultiplied
        /// before the matrix and premultiplied again after it, since the matrix applies to
        /// straight colors and the canvas of the original is premultiplied. The weighted sum is
        /// computed in single precision and rounded to the nearest level, so a pixel that is
        /// already gray keeps its level. For opaque pixels the result is pinned against the GDI+
        /// conversion on Windows (<c>DirectBitmapGrayscaleTests</c>); translucent pixels are not
        /// compared with GDI+, whose premultiplication rounding may differ by one level.</para>
        /// </summary>
        public static DirectBitmap MakeGrayscaleImage(this DirectBitmap original)
        {
            ArgumentNullException.ThrowIfNull(original);
            var gray = new int[original.Bits.Length];
            for (int i = 0; i < gray.Length; i++)
            {
                gray[i] = ToGrayscalePArgb(original.Bits[i]);
            }

            return new DirectBitmap(original.Width, original.Height, gray);
        }

        private static int ToGrayscalePArgb(int pargb)
        {
            int a = (pargb >> 24) & 0xFF;
            if (a == 0)
            {
                return 0;
            }

            int r = (pargb >> 16) & 0xFF;
            int g = (pargb >> 8) & 0xFF;
            int b = pargb & 0xFF;
            if (a < 255)
            {
                r = Math.Min(255, (r * 255 + a / 2) / a);
                g = Math.Min(255, (g * 255 + a / 2) / a);
                b = Math.Min(255, (b * 255 + a / 2) / a);
            }

            float luma = r * .3f + g * .59f + b * .11f;
            int level = Math.Clamp((int)(luma + .5f), 0, 255);
            if (a < 255)
            {
                level = (level * a + 127) / 255;
            }

            return (a << 24) | (level << 16) | (level << 8) | level;
        }

        //todo: The following was to be used in DirectBitmap to create from bitmap/clone, but it has a memory leak, ImageConverter does not have the issue but it is not available in .Net core<3.0, and a move to SharpImage or other libs is recommended rather the compatibility System.Drawing

        //public static int[] ToIntArray(this Bitmap bmp)
        //{
        //    BitmapData bData = bmp.LockBits(new Rectangle(new Point(), bmp.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);

        //    // number of bytes in the bitmap
        //    var byteCount = bData.Stride * (bmp.Height);

        //    int[] ints = new int[byteCount / 4];

        //    Marshal.Copy(bData.Scan0, ints, 0, byteCount / 4);

        //    // don't forget to unlock the bitmap!!
        //    bmp.UnlockBits(bData);

        //    return ints;
        //}


        /// <summary>
        /// Helper to build a RGB color from HSV definition
        /// </summary>
        /// <param name="h">Hue</param>
        /// <param name="s">Saturation</param>
        /// <param name="v"Value></param>
        /// <param name="r">Red</param>
        /// <param name="g">Green</param>
        /// <param name="b">Blue</param>
        public static (double r, double g, double b) HsvToRgb(this (double h, double s, double v) hsv)
        {
            double r, g, b;
            if (Math.Abs(hsv.h - 1.0) <= double.Epsilon)
            {
                hsv.h = 0.0;
            }

            double step = 1.0 / 6.0;
            double vh = hsv.h / step;

            int i = (int)Math.Floor(vh);

            double f = vh - i;
            double p = hsv.v * (1.0 - hsv.s);
            double q = hsv.v * (1.0 - hsv.s * f);
            double t = hsv.v * (1.0 - hsv.s * (1.0 - f));

            switch (i)
            {
                case 0:
                {
                    r = hsv.v;
                    g = t;
                    b = p;
                    break;
                }
                case 1:
                {
                    r = q;
                    g = hsv.v;
                    b = p;
                    break;
                }
                case 2:
                {
                    r = p;
                    g = hsv.v;
                    b = t;
                    break;
                }
                case 3:
                {
                    r = p;
                    g = q;
                    b = hsv.v;
                    break;
                }
                case 4:
                {
                    r = t;
                    g = p;
                    b = hsv.v;
                    break;
                }
                case 5:
                {
                    r = hsv.v;
                    g = p;
                    b = q;
                    break;
                }
                default:
                {
                    // not possible - if we get here it is an internal error
                    throw new ArgumentException();
                }
            }

            return (r, g, b);
        }
    }
}