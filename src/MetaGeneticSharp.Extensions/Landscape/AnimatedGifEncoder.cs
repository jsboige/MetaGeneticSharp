using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

namespace MetaGeneticSharp;

/// <summary>
/// Animated-GIF assembly for the convergence "flipbook": turns the per-generation PNG heatmaps
/// produced by the <c>RenderHeatmapPng</c> overloads into a single looping GIF89a, so a notebook
/// can show the GA walking down the relief as ONE animation instead of N static frames.
/// <para>
/// Additive and self-contained: SkiaSharp 3.x exposes no animated-GIF <em>encoder</em>
/// (only <see cref="SKCodec"/> decoding), so the GIF89a container, the median-cut 256-color
/// quantizer and the GIF-flavoured LZW are authored here. The byte-exact static renderers
/// (<c>RenderHeatmapPng</c> and the verbatim <c>DirectBitmap</c>) are untouched — no pendulum.
/// Credit for the heatmap pixels themselves: jsboige @ d05826fd.
/// </para>
/// </summary>
public static partial class SkiaLandscapeRenderer
{
    /// <summary>
    /// Assembles a list of equally-sized PNG frames into one looping animated GIF89a.
    /// All frames must share the same dimensions (use the same <c>width</c>/<c>height</c> when
    /// rendering each generation). A single 256-color global palette is built by median-cut over
    /// every frame, so the grayscale relief ramp and the saturated population/best markers all
    /// survive quantization.
    /// </summary>
    /// <param name="pngFrames">The per-frame PNG byte arrays, in playback order. Must be non-empty.</param>
    /// <param name="delayCentiseconds">Per-frame delay in 1/100 s (GIF unit). Default 25 = 0.25 s.</param>
    /// <param name="loopCount">Netscape loop count; 0 = loop forever (default).</param>
    /// <returns>The bytes of a GIF89a animation.</returns>
    public static byte[] EncodeAnimatedGif(
        IReadOnlyList<byte[]> pngFrames,
        int delayCentiseconds = 25,
        int loopCount = 0)
    {
        ArgumentNullException.ThrowIfNull(pngFrames);
        if (pngFrames.Count == 0)
        {
            throw new ArgumentException("At least one frame is required.", nameof(pngFrames));
        }
        if (delayCentiseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delayCentiseconds), "Delay must be non-negative.");
        }
        if (loopCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(loopCount), "Loop count must be non-negative.");
        }

        // Decode every PNG to a flat RGB buffer; all frames must agree on dimensions.
        int width = 0, height = 0;
        var rgbFrames = new List<byte[]>(pngFrames.Count);
        foreach (byte[] png in pngFrames)
        {
            ArgumentNullException.ThrowIfNull(png);
            using SKBitmap bmp = SKBitmap.Decode(png)
                ?? throw new ArgumentException("A frame could not be decoded as an image.", nameof(pngFrames));
            if (rgbFrames.Count == 0)
            {
                width = bmp.Width;
                height = bmp.Height;
            }
            else if (bmp.Width != width || bmp.Height != height)
            {
                throw new ArgumentException(
                    $"All frames must share the same dimensions ({width}x{height}); got {bmp.Width}x{bmp.Height}.",
                    nameof(pngFrames));
            }

            var rgb = new byte[width * height * 3];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    SKColor c = bmp.GetPixel(x, y);
                    int o = (y * width + x) * 3;
                    rgb[o] = c.Red;
                    rgb[o + 1] = c.Green;
                    rgb[o + 2] = c.Blue;
                }
            }
            rgbFrames.Add(rgb);
        }

        // One shared 256-color global palette across all frames (median-cut).
        (byte[] palette, int usedColors) = GifPalette.BuildGlobalPalette(rgbFrames, 256);

        // Map each pixel to its nearest palette index, caching by packed RGB (frames share colors).
        var nearestCache = new Dictionary<int, byte>();
        var indexedFrames = new List<byte[]>(rgbFrames.Count);
        foreach (byte[] rgb in rgbFrames)
        {
            int pixels = width * height;
            var idx = new byte[pixels];
            for (int i = 0; i < pixels; i++)
            {
                int key = (rgb[i * 3] << 16) | (rgb[i * 3 + 1] << 8) | rgb[i * 3 + 2];
                if (!nearestCache.TryGetValue(key, out byte pi))
                {
                    pi = GifPalette.NearestIndex(palette, usedColors, rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);
                    nearestCache[key] = pi;
                }
                idx[i] = pi;
            }
            indexedFrames.Add(idx);
        }

        using var ms = new MemoryStream();
        WriteGifHeader(ms, width, height, palette);
        WriteNetscapeLoop(ms, loopCount);
        foreach (byte[] idx in indexedFrames)
        {
            WriteGraphicControlExtension(ms, delayCentiseconds);
            WriteImageDescriptor(ms, width, height);
            WriteLzwImageData(ms, idx, minCodeSize: 8);
        }
        ms.WriteByte(0x3B); // GIF trailer
        return ms.ToArray();
    }

    private static void WriteGifHeader(Stream s, int width, int height, byte[] palette)
    {
        // "GIF89a" signature + version.
        s.Write(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, 0, 6);

        // Logical Screen Descriptor.
        WriteUInt16(s, (ushort)width);
        WriteUInt16(s, (ushort)height);
        // Packed: global color table present (0x80) | color resolution (7<<4) | GCT size 7 => 2^(7+1)=256.
        s.WriteByte(0x80 | 0x70 | 0x07);
        s.WriteByte(0x00); // background color index
        s.WriteByte(0x00); // pixel aspect ratio
        // Global Color Table: 256 RGB triples.
        s.Write(palette, 0, 256 * 3);
    }

    private static void WriteNetscapeLoop(Stream s, int loopCount)
    {
        s.WriteByte(0x21); // extension introducer
        s.WriteByte(0xFF); // application extension label
        s.WriteByte(0x0B); // block size (11)
        byte[] app = { (byte)'N', (byte)'E', (byte)'T', (byte)'S', (byte)'C', (byte)'A', (byte)'P', (byte)'E', (byte)'2', (byte)'.', (byte)'0' };
        s.Write(app, 0, app.Length);
        s.WriteByte(0x03); // sub-block size
        s.WriteByte(0x01); // loop sub-block id
        WriteUInt16(s, (ushort)loopCount); // 0 = infinite
        s.WriteByte(0x00); // block terminator
    }

    private static void WriteGraphicControlExtension(Stream s, int delayCentiseconds)
    {
        s.WriteByte(0x21); // extension introducer
        s.WriteByte(0xF9); // graphic control label
        s.WriteByte(0x04); // block size
        s.WriteByte(0x00); // packed (no transparency, no disposal)
        WriteUInt16(s, (ushort)delayCentiseconds);
        s.WriteByte(0x00); // transparent color index (unused)
        s.WriteByte(0x00); // block terminator
    }

    private static void WriteImageDescriptor(Stream s, int width, int height)
    {
        s.WriteByte(0x2C); // image separator
        WriteUInt16(s, 0); // left
        WriteUInt16(s, 0); // top
        WriteUInt16(s, (ushort)width);
        WriteUInt16(s, (ushort)height);
        s.WriteByte(0x00); // no local color table, not interlaced
    }

    /// <summary>
    /// GIF-flavoured variable-length LZW (giflib code-size schedule), emitted as 255-byte sub-blocks.
    /// </summary>
    private static void WriteLzwImageData(Stream s, byte[] indices, int minCodeSize)
    {
        s.WriteByte((byte)minCodeSize);

        const int maxCodeLimit = 4095; // giflib LZ_MAX_CODE: clear before a 13th-bit code is needed.
        const int maxBits = 12;        // giflib LZ_BITS.
        int clearCode = 1 << minCodeSize;   // 256
        int endCode = clearCode + 1;        // 257
        int runningBits = minCodeSize + 1;  // 9
        int maxCode1 = 1 << runningBits;    // 512
        int runningCode = endCode + 1;      // 258

        var dict = new Dictionary<int, int>();
        var bits = new BitPacker();

        // giflib code-size schedule: write each code at the CURRENT width, then grow the width
        // once the running code has passed the current ceiling. The bump is deferred by one
        // output relative to the naive "increment-then-test" rule; that deferral is exactly what
        // standard decoders (including SKCodec) expect, and getting it wrong yields ErrorInInput
        // the moment the dictionary crosses the 512-code boundary.
        void Output(int code)
        {
            bits.Write(code, runningBits);
            if (runningCode >= maxCode1 && runningBits < maxBits)
            {
                maxCode1 = 1 << ++runningBits;
            }
        }

        Output(clearCode);

        if (indices.Length > 0)
        {
            int prefix = indices[0];
            for (int i = 1; i < indices.Length; i++)
            {
                int k = indices[i];
                int key = (prefix << 8) | k;
                if (dict.TryGetValue(key, out int code))
                {
                    prefix = code;
                }
                else
                {
                    Output(prefix);
                    prefix = k;
                    if (runningCode >= maxCodeLimit)
                    {
                        Output(clearCode);
                        dict.Clear();
                        runningBits = minCodeSize + 1;
                        maxCode1 = 1 << runningBits;
                        runningCode = endCode + 1;
                    }
                    else
                    {
                        dict[key] = runningCode++;
                    }
                }
            }
            Output(prefix);
        }

        Output(endCode);
        byte[] payload = bits.ToArray();

        // Emit as sub-blocks of at most 255 bytes.
        int offset = 0;
        while (offset < payload.Length)
        {
            int chunk = Math.Min(255, payload.Length - offset);
            s.WriteByte((byte)chunk);
            s.Write(payload, offset, chunk);
            offset += chunk;
        }
        s.WriteByte(0x00); // block terminator
    }

    private static void WriteUInt16(Stream s, ushort value)
    {
        s.WriteByte((byte)(value & 0xFF));
        s.WriteByte((byte)((value >> 8) & 0xFF));
    }

    /// <summary>LSB-first bit accumulator for the LZW code stream.</summary>
    private sealed class BitPacker
    {
        private readonly List<byte> _bytes = new();
        private uint _acc;
        private int _bits;

        public void Write(int code, int codeSize)
        {
            _acc |= (uint)code << _bits;
            _bits += codeSize;
            while (_bits >= 8)
            {
                _bytes.Add((byte)(_acc & 0xFF));
                _acc >>= 8;
                _bits -= 8;
            }
        }

        public byte[] ToArray()
        {
            if (_bits > 0)
            {
                _bytes.Add((byte)(_acc & 0xFF));
                _acc = 0;
                _bits = 0;
            }
            return _bytes.ToArray();
        }
    }
}

/// <summary>Median-cut quantization to a shared 256-color palette for the animated GIF.</summary>
internal static class GifPalette
{
    private struct ColorCount
    {
        public byte R;
        public byte G;
        public byte B;
        public int Count;
    }

    /// <summary>
    /// Builds a global palette (median-cut) over every frame. Returns a 256*3 byte table (padded
    /// with zeros when fewer than 256 distinct boxes are produced) and the count of meaningful
    /// entries, so nearest-color search never picks a padded slot.
    /// </summary>
    public static (byte[] palette, int usedColors) BuildGlobalPalette(IReadOnlyList<byte[]> rgbFrames, int maxColors)
    {
        var hist = new Dictionary<int, int>();
        foreach (byte[] rgb in rgbFrames)
        {
            for (int i = 0; i < rgb.Length; i += 3)
            {
                int key = (rgb[i] << 16) | (rgb[i + 1] << 8) | rgb[i + 2];
                hist.TryGetValue(key, out int c);
                hist[key] = c + 1;
            }
        }

        var initial = new List<ColorCount>(hist.Count);
        foreach (KeyValuePair<int, int> kv in hist)
        {
            initial.Add(new ColorCount
            {
                R = (byte)((kv.Key >> 16) & 0xFF),
                G = (byte)((kv.Key >> 8) & 0xFF),
                B = (byte)(kv.Key & 0xFF),
                Count = kv.Value,
            });
        }

        var boxes = new List<List<ColorCount>> { initial };
        while (boxes.Count < maxColors)
        {
            int best = -1;
            long bestScore = -1;
            for (int b = 0; b < boxes.Count; b++)
            {
                if (boxes[b].Count < 2)
                {
                    continue;
                }
                long score = BoxScore(boxes[b]);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = b;
                }
            }
            if (best < 0)
            {
                break; // every box is a single color: cannot split further.
            }
            (List<ColorCount> lo, List<ColorCount> hi) = SplitBox(boxes[best]);
            boxes[best] = lo;
            boxes.Add(hi);
        }

        var palette = new byte[maxColors * 3];
        for (int b = 0; b < boxes.Count; b++)
        {
            long sr = 0, sg = 0, sb = 0, sc = 0;
            foreach (ColorCount c in boxes[b])
            {
                sr += (long)c.R * c.Count;
                sg += (long)c.G * c.Count;
                sb += (long)c.B * c.Count;
                sc += c.Count;
            }
            if (sc == 0)
            {
                sc = 1;
            }
            palette[b * 3] = (byte)(sr / sc);
            palette[b * 3 + 1] = (byte)(sg / sc);
            palette[b * 3 + 2] = (byte)(sb / sc);
        }
        return (palette, boxes.Count);
    }

    public static byte NearestIndex(byte[] palette, int usedColors, byte r, byte g, byte b)
    {
        int best = 0;
        long bestDist = long.MaxValue;
        for (int i = 0; i < usedColors; i++)
        {
            int dr = palette[i * 3] - r;
            int dg = palette[i * 3 + 1] - g;
            int db = palette[i * 3 + 2] - b;
            long dist = (long)dr * dr + (long)dg * dg + (long)db * db;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
                if (dist == 0)
                {
                    break;
                }
            }
        }
        return (byte)best;
    }

    private static long BoxScore(List<ColorCount> box)
    {
        (int dr, int dg, int db) = Extent(box);
        long cnt = 0;
        foreach (ColorCount c in box)
        {
            cnt += c.Count;
        }
        int range = Math.Max(dr, Math.Max(dg, db));
        return (long)range * cnt;
    }

    private static (int dr, int dg, int db) Extent(List<ColorCount> box)
    {
        byte rmn = 255, gmn = 255, bmn = 255, rmx = 0, gmx = 0, bmx = 0;
        foreach (ColorCount c in box)
        {
            if (c.R < rmn) { rmn = c.R; }
            if (c.R > rmx) { rmx = c.R; }
            if (c.G < gmn) { gmn = c.G; }
            if (c.G > gmx) { gmx = c.G; }
            if (c.B < bmn) { bmn = c.B; }
            if (c.B > bmx) { bmx = c.B; }
        }
        return (rmx - rmn, gmx - gmn, bmx - bmn);
    }

    private static (List<ColorCount> lo, List<ColorCount> hi) SplitBox(List<ColorCount> box)
    {
        (int dr, int dg, int db) = Extent(box);
        int channel = dr >= dg && dr >= db ? 0 : (dg >= db ? 1 : 2);
        box.Sort((a, c) => channel switch
        {
            0 => a.R - c.R,
            1 => a.G - c.G,
            _ => a.B - c.B,
        });

        long total = 0;
        foreach (ColorCount c in box)
        {
            total += c.Count;
        }

        long half = total / 2;
        long acc = 0;
        int splitIndex = 1;
        for (int i = 0; i < box.Count; i++)
        {
            acc += box[i].Count;
            if (acc >= half)
            {
                splitIndex = Math.Clamp(i + 1, 1, box.Count - 1);
                break;
            }
        }

        List<ColorCount> lo = box.GetRange(0, splitIndex);
        List<ColorCount> hi = box.GetRange(splitIndex, box.Count - splitIndex);
        return (lo, hi);
    }
}
