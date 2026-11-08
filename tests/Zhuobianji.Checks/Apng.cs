using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Zhuobianji.Checks;

// README 动图：APNG（浏览器和 GitHub 都会动，颜色不像 GIF 只有 256 色）。每帧只记和上一帧不同的那块，
// 和上一帧一样的帧合并成更长的停留，文件小。帧是不透明的 RGB，第一帧是整张。
internal sealed class ApngWriter(int width, int height)
{
    private readonly List<(Int32Rect Region, byte[] Data, int Delay)> frames = [];
    private int[]? previous;
    private double previousTime;

    // pixels 是 Bgr32 / Pbgra32 的整张画面（每帧一个新数组，写入器留着它和下一帧比），time 是这帧出现的时刻（毫秒）。
    internal void Add(int[] pixels, double time)
    {
        if (previous is not null)
        {
            var delay = (int)Math.Round(time - previousTime);
            var region = Changed(previous, pixels);
            if (region.IsEmpty) return;
            Stretch(delay);
            previousTime = time;
            frames.Add((region, Encode(pixels, region), 0));
        }
        else
        {
            previousTime = time;
            frames.Add((new Int32Rect(0, 0, width, height), Encode(pixels, new Int32Rect(0, 0, width, height)), 0));
        }
        previous = pixels;
    }

    // 最后一帧停多久（毫秒），然后从头再放。
    internal void Save(string path, double endTime)
    {
        Stretch((int)Math.Round(endTime - previousTime));
        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BigEndian(header, 0, width);
        BigEndian(header, 4, height);
        header[8] = 8;   // 每个颜色 8 位
        header[9] = 2;   // RGB
        Chunk(file, "IHDR", header);
        var control = new byte[8];
        BigEndian(control, 0, frames.Count);
        BigEndian(control, 4, 0);   // 一直循环
        Chunk(file, "acTL", control);
        var sequence = 0;
        for (var index = 0; index < frames.Count; index++)
        {
            var (region, data, delay) = frames[index];
            var frame = new byte[26];
            BigEndian(frame, 0, sequence++);
            BigEndian(frame, 4, region.Width);
            BigEndian(frame, 8, region.Height);
            BigEndian(frame, 12, region.X);
            BigEndian(frame, 16, region.Y);
            frame[20] = (byte)(Math.Min(delay, 65535) >> 8);
            frame[21] = (byte)Math.Min(delay, 65535);
            frame[22] = 1000 >> 8;
            frame[23] = 1000 & 0xFF;
            frame[24] = 0;   // 画完不清除
            frame[25] = 0;   // 直接覆盖
            Chunk(file, "fcTL", frame);
            foreach (var idat in ImageData(data))
            {
                if (index == 0) { Chunk(file, "IDAT", idat); continue; }
                var payload = new byte[idat.Length + 4];
                BigEndian(payload, 0, sequence++);
                idat.CopyTo(payload, 4);
                Chunk(file, "fdAT", payload);
            }
        }
        Chunk(file, "IEND", []);
    }

    internal int FrameCount => frames.Count;

    private void Stretch(int delay)
    {
        var last = frames[^1];
        frames[^1] = last with { Delay = last.Delay + Math.Max(delay, 1) };
    }

    private Int32Rect Changed(int[] before, int[] after)
    {
        int left = width, top = height, right = -1, bottom = -1;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                if (((before[row + x] ^ after[row + x]) & 0xFFFFFF) == 0) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }
        return right < 0 ? Int32Rect.Empty : new Int32Rect(left, top, right - left + 1, bottom - top + 1);
    }

    // 用 WPF 的 PNG 编码器压一块，再从里面取出压好的图像数据。
    private byte[] Encode(int[] pixels, Int32Rect region)
    {
        var block = new int[region.Width * region.Height];
        for (var y = 0; y < region.Height; y++) Array.Copy(pixels, (region.Y + y) * width + region.X, block, y * region.Width, region.Width);
        var part = new FormatConvertedBitmap(BitmapSource.Create(region.Width, region.Height, 96, 96, PixelFormats.Bgr32, null, block, region.Width * 4),
            PixelFormats.Bgr24, null, 0);
        var encoder = new PngBitmapEncoder { Interlace = PngInterlaceOption.Off };
        encoder.Frames.Add(BitmapFrame.Create(part));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static IEnumerable<byte[]> ImageData(byte[] png)
    {
        for (var offset = 8; offset < png.Length;)
        {
            var length = png[offset] << 24 | png[offset + 1] << 16 | png[offset + 2] << 8 | png[offset + 3];
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IHDR" && (png[offset + 8 + 8] != 8 || png[offset + 8 + 9] != 2)) throw new InvalidDataException("PNG 编码器没按 8 位 RGB 写。");
            if (type == "IDAT") yield return png.AsSpan(offset + 8, length).ToArray();
            offset += 12 + length;
        }
    }

    private static void Chunk(Stream file, string type, byte[] data)
    {
        var head = new byte[8];
        BigEndian(head, 0, data.Length);
        System.Text.Encoding.ASCII.GetBytes(type, 0, 4, head, 4);
        file.Write(head);
        file.Write(data);
        var crc = Crc(Crc(0xFFFFFFFF, head.AsSpan(4, 4)), data) ^ 0xFFFFFFFF;
        var tail = new byte[4];
        BigEndian(tail, 0, (int)crc);
        file.Write(tail);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data) crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static void BigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }
}
