using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Precondition.LoopLab.Editor.Export
{
    public static class GifEncoder
    {
        private const int MaxPaletteEntries = 256;
        private const int HistogramChannelBits = 5;
        private const int HistogramEntryCount = 1 << (HistogramChannelBits * 3);
        private const int HistogramShift = 8 - HistogramChannelBits;
        private const int MaxHistogramSamplesPerFrame = 8192;

        public static byte[] Encode(
            IReadOnlyList<Color32[]> frames,
            int width,
            int height,
            int framesPerSecond,
            GifExportOptions options)
        {
            if (frames == null)
            {
                throw new ArgumentNullException(nameof(frames));
            }

            return Encode(
                frames.Count,
                width,
                height,
                framesPerSecond,
                frameIndex => frames[frameIndex],
                options);
        }

        public static byte[] Encode(
            int frameCount,
            int width,
            int height,
            int framesPerSecond,
            Func<int, Color32[]> frameProvider,
            GifExportOptions options)
        {
            if (frameProvider == null)
            {
                throw new ArgumentNullException(nameof(frameProvider));
            }

            if (frameCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameCount), "GIF export requires at least one frame.");
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "GIF export requires positive frame dimensions.");
            }

            var validatedFramesPerSecond = LoopLabRenderSettings.ValidateFramesPerSecond(framesPerSecond);
            var palette = BuildPalette(frameCount, frameProvider, width, height);
            var frameDelays = BuildFrameDelays(frameCount, validatedFramesPerSecond);
            var nearestPaletteCache = CreateNearestPaletteCache();
            var indexedPixels = new byte[width * height];

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            WriteHeader(writer, width, height, palette);
            WriteLoopExtension(writer);

            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var framePixels = GetValidatedFrame(frameProvider(frameIndex), width, height, frameIndex);
                QuantizeFrame(framePixels, width, height, palette, options.Dithering, indexedPixels, nearestPaletteCache);
                WriteGraphicControlExtension(writer, frameDelays[frameIndex]);
                WriteImageDescriptor(writer, width, height);
                WriteImageData(writer, indexedPixels, palette.MinimumCodeSize);
            }

            writer.Write((byte)0x3B);
            writer.Flush();
            return stream.ToArray();
        }

        public static int[] BuildFrameDelays(int frameCount, int framesPerSecond)
        {
            var safeFrameCount = Mathf.Max(1, frameCount);
            var safeFramesPerSecond = LoopLabRenderSettings.ValidateFramesPerSecond(framesPerSecond);
            var frameDelays = new int[safeFrameCount];
            var previousCumulativeDelay = 0;

            for (var frameIndex = 0; frameIndex < safeFrameCount; frameIndex++)
            {
                var nextCumulativeDelay = Mathf.RoundToInt(((frameIndex + 1) * 100f) / safeFramesPerSecond);
                frameDelays[frameIndex] = Mathf.Max(1, nextCumulativeDelay - previousCumulativeDelay);
                previousCumulativeDelay = nextCumulativeDelay;
            }

            return frameDelays;
        }

        private static GifPalette BuildPalette(
            int frameCount,
            Func<int, Color32[]> frameProvider,
            int width,
            int height)
        {
            var histogramCounts = new int[HistogramEntryCount];
            var redSums = new long[HistogramEntryCount];
            var greenSums = new long[HistogramEntryCount];
            var blueSums = new long[HistogramEntryCount];

            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var framePixels = GetValidatedFrame(frameProvider(frameIndex), width, height, frameIndex);
                SampleFrame(framePixels, frameIndex, histogramCounts, redSums, greenSums, blueSums);
            }

            var histogramEntries = new List<HistogramEntry>();
            for (var entryIndex = 0; entryIndex < HistogramEntryCount; entryIndex++)
            {
                var count = histogramCounts[entryIndex];
                if (count <= 0)
                {
                    continue;
                }

                histogramEntries.Add(new HistogramEntry(
                    new Color32(
                        (byte)(redSums[entryIndex] / count),
                        (byte)(greenSums[entryIndex] / count),
                        (byte)(blueSums[entryIndex] / count),
                        255),
                    count,
                    entryIndex));
            }

            if (histogramEntries.Count == 0)
            {
                return GifPalette.Create(new[]
                {
                    new Color32(0, 0, 0, 255),
                    new Color32(255, 255, 255, 255)
                });
            }

            if (histogramEntries.Count <= MaxPaletteEntries)
            {
                histogramEntries.Sort(CompareHistogramEntriesByFrequency);
                var directPalette = new List<Color32>(histogramEntries.Count);
                for (var index = 0; index < histogramEntries.Count; index++)
                {
                    directPalette.Add(histogramEntries[index].Color);
                }

                return GifPalette.Create(directPalette);
            }

            return GifPalette.Create(BuildMedianCutPalette(histogramEntries));
        }

        private static void SampleFrame(
            Color32[] framePixels,
            int frameIndex,
            int[] histogramCounts,
            long[] redSums,
            long[] greenSums,
            long[] blueSums)
        {
            var stride = Mathf.Max(1, framePixels.Length / MaxHistogramSamplesPerFrame);
            var offset = frameIndex % stride;

            for (var pixelIndex = offset; pixelIndex < framePixels.Length; pixelIndex += stride)
            {
                var pixel = framePixels[pixelIndex];
                var histogramIndex = GetHistogramIndex(pixel.r, pixel.g, pixel.b);
                histogramCounts[histogramIndex]++;
                redSums[histogramIndex] += pixel.r;
                greenSums[histogramIndex] += pixel.g;
                blueSums[histogramIndex] += pixel.b;
            }
        }

        private static List<Color32> BuildMedianCutPalette(List<HistogramEntry> histogramEntries)
        {
            var boxes = new List<PaletteBox>
            {
                new(histogramEntries)
            };

            while (boxes.Count < MaxPaletteEntries)
            {
                boxes.Sort((left, right) =>
                {
                    var scoreComparison = right.SplitScore.CompareTo(left.SplitScore);
                    if (scoreComparison != 0)
                    {
                        return scoreComparison;
                    }

                    return left.FirstKey.CompareTo(right.FirstKey);
                });

                var splitIndex = -1;
                for (var index = 0; index < boxes.Count; index++)
                {
                    if (boxes[index].CanSplit)
                    {
                        splitIndex = index;
                        break;
                    }
                }

                if (splitIndex < 0)
                {
                    break;
                }

                var box = boxes[splitIndex];
                boxes.RemoveAt(splitIndex);
                var (leftBox, rightBox) = box.Split();
                boxes.Add(leftBox);
                boxes.Add(rightBox);
            }

            boxes.Sort((left, right) => left.FirstKey.CompareTo(right.FirstKey));

            var palette = new List<Color32>(boxes.Count);
            for (var index = 0; index < boxes.Count; index++)
            {
                palette.Add(boxes[index].AverageColor);
            }

            return palette;
        }

        private static void QuantizeFrame(
            Color32[] framePixels,
            int width,
            int height,
            GifPalette palette,
            GifDitheringMode dithering,
            byte[] indexedPixels,
            int[] nearestPaletteCache)
        {
            if (dithering == GifDitheringMode.FloydSteinberg)
            {
                QuantizeFrameWithDithering(framePixels, width, height, palette, indexedPixels, nearestPaletteCache);
                return;
            }

            for (var pixelIndex = 0; pixelIndex < framePixels.Length; pixelIndex++)
            {
                var pixel = framePixels[pixelIndex];
                indexedPixels[pixelIndex] = (byte)GetNearestPaletteIndex(pixel.r, pixel.g, pixel.b, palette, nearestPaletteCache);
            }
        }

        private static void QuantizeFrameWithDithering(
            Color32[] framePixels,
            int width,
            int height,
            GifPalette palette,
            byte[] indexedPixels,
            int[] nearestPaletteCache)
        {
            var currentRedError = new float[width + 2];
            var currentGreenError = new float[width + 2];
            var currentBlueError = new float[width + 2];
            var nextRedError = new float[width + 2];
            var nextGreenError = new float[width + 2];
            var nextBlueError = new float[width + 2];

            for (var y = 0; y < height; y++)
            {
                Array.Clear(nextRedError, 0, nextRedError.Length);
                Array.Clear(nextGreenError, 0, nextGreenError.Length);
                Array.Clear(nextBlueError, 0, nextBlueError.Length);

                for (var x = 0; x < width; x++)
                {
                    var pixelIndex = (y * width) + x;
                    var pixel = framePixels[pixelIndex];

                    var red = Mathf.Clamp(pixel.r + currentRedError[x + 1], 0f, 255f);
                    var green = Mathf.Clamp(pixel.g + currentGreenError[x + 1], 0f, 255f);
                    var blue = Mathf.Clamp(pixel.b + currentBlueError[x + 1], 0f, 255f);

                    var paletteIndex = GetNearestPaletteIndex(
                        (byte)Mathf.RoundToInt(red),
                        (byte)Mathf.RoundToInt(green),
                        (byte)Mathf.RoundToInt(blue),
                        palette,
                        nearestPaletteCache);

                    indexedPixels[pixelIndex] = (byte)paletteIndex;
                    var paletteColor = palette.Colors[paletteIndex];

                    var redError = red - paletteColor.r;
                    var greenError = green - paletteColor.g;
                    var blueError = blue - paletteColor.b;

                    currentRedError[x + 2] += redError * (7f / 16f);
                    currentGreenError[x + 2] += greenError * (7f / 16f);
                    currentBlueError[x + 2] += blueError * (7f / 16f);

                    nextRedError[x] += redError * (3f / 16f);
                    nextGreenError[x] += greenError * (3f / 16f);
                    nextBlueError[x] += blueError * (3f / 16f);

                    nextRedError[x + 1] += redError * (5f / 16f);
                    nextGreenError[x + 1] += greenError * (5f / 16f);
                    nextBlueError[x + 1] += blueError * (5f / 16f);

                    nextRedError[x + 2] += redError * (1f / 16f);
                    nextGreenError[x + 2] += greenError * (1f / 16f);
                    nextBlueError[x + 2] += blueError * (1f / 16f);
                }

                Swap(ref currentRedError, ref nextRedError);
                Swap(ref currentGreenError, ref nextGreenError);
                Swap(ref currentBlueError, ref nextBlueError);
            }
        }

        private static int GetNearestPaletteIndex(
            byte red,
            byte green,
            byte blue,
            GifPalette palette,
            int[] nearestPaletteCache)
        {
            var histogramIndex = GetHistogramIndex(red, green, blue);
            var cachedPaletteIndex = nearestPaletteCache[histogramIndex];
            if (cachedPaletteIndex >= 0)
            {
                return cachedPaletteIndex;
            }

            var closestPaletteIndex = 0;
            var closestDistance = int.MaxValue;

            for (var paletteIndex = 0; paletteIndex < palette.Colors.Length; paletteIndex++)
            {
                var paletteColor = palette.Colors[paletteIndex];
                var redDelta = red - paletteColor.r;
                var greenDelta = green - paletteColor.g;
                var blueDelta = blue - paletteColor.b;
                var distance = (redDelta * redDelta) + (greenDelta * greenDelta) + (blueDelta * blueDelta);

                if (distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = distance;
                closestPaletteIndex = paletteIndex;

                if (distance == 0)
                {
                    break;
                }
            }

            nearestPaletteCache[histogramIndex] = closestPaletteIndex;
            return closestPaletteIndex;
        }

        private static int[] CreateNearestPaletteCache()
        {
            var nearestPaletteCache = new int[HistogramEntryCount];
            Array.Fill(nearestPaletteCache, -1);
            return nearestPaletteCache;
        }

        private static int GetHistogramIndex(byte red, byte green, byte blue)
        {
            var redIndex = red >> HistogramShift;
            var greenIndex = green >> HistogramShift;
            var blueIndex = blue >> HistogramShift;
            return (redIndex << (HistogramChannelBits * 2)) | (greenIndex << HistogramChannelBits) | blueIndex;
        }

        private static int CompareHistogramEntriesByFrequency(HistogramEntry left, HistogramEntry right)
        {
            var countComparison = right.Count.CompareTo(left.Count);
            if (countComparison != 0)
            {
                return countComparison;
            }

            return left.Key.CompareTo(right.Key);
        }

        private static Color32[] GetValidatedFrame(Color32[] framePixels, int width, int height, int frameIndex)
        {
            if (framePixels == null)
            {
                throw new InvalidOperationException($"GIF frame provider returned null for frame {frameIndex}.");
            }

            if (framePixels.Length != width * height)
            {
                throw new InvalidOperationException(
                    $"GIF frame {frameIndex} has {framePixels.Length} pixels but expected {width * height} ({width}x{height}).");
            }

            return framePixels;
        }

        private static void WriteHeader(BinaryWriter writer, int width, int height, GifPalette palette)
        {
            WriteAscii(writer, "GIF89a");
            writer.Write((ushort)width);
            writer.Write((ushort)height);

            var packedField = (byte)(0x80 | (7 << 4) | (palette.ColorTableBits - 1));
            writer.Write(packedField);
            writer.Write((byte)0);
            writer.Write((byte)0);

            for (var colorIndex = 0; colorIndex < palette.ColorTableEntryCount; colorIndex++)
            {
                var paletteIndex = colorIndex < palette.Colors.Length
                    ? colorIndex
                    : palette.Colors.Length - 1;
                var color = palette.Colors[paletteIndex];
                writer.Write(color.r);
                writer.Write(color.g);
                writer.Write(color.b);
            }
        }

        private static void WriteLoopExtension(BinaryWriter writer)
        {
            writer.Write((byte)0x21);
            writer.Write((byte)0xFF);
            writer.Write((byte)0x0B);
            WriteAscii(writer, "NETSCAPE2.0");
            writer.Write((byte)0x03);
            writer.Write((byte)0x01);
            writer.Write((ushort)0);
            writer.Write((byte)0x00);
        }

        private static void WriteGraphicControlExtension(BinaryWriter writer, int frameDelay)
        {
            writer.Write((byte)0x21);
            writer.Write((byte)0xF9);
            writer.Write((byte)0x04);
            writer.Write((byte)0x00);
            writer.Write((ushort)frameDelay);
            writer.Write((byte)0x00);
            writer.Write((byte)0x00);
        }

        private static void WriteImageDescriptor(BinaryWriter writer, int width, int height)
        {
            writer.Write((byte)0x2C);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)width);
            writer.Write((ushort)height);
            writer.Write((byte)0x00);
        }

        private static void WriteImageData(BinaryWriter writer, byte[] indexedPixels, int minimumCodeSize)
        {
            writer.Write((byte)minimumCodeSize);
            var compressedImageData = GifLzwEncoder.Encode(indexedPixels, minimumCodeSize);
            WriteSubBlocks(writer, compressedImageData);
        }

        private static void WriteSubBlocks(BinaryWriter writer, byte[] data)
        {
            var offset = 0;
            while (offset < data.Length)
            {
                var blockLength = Mathf.Min(255, data.Length - offset);
                writer.Write((byte)blockLength);
                writer.Write(data, offset, blockLength);
                offset += blockLength;
            }

            writer.Write((byte)0x00);
        }

        private static void WriteAscii(BinaryWriter writer, string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                writer.Write((byte)value[index]);
            }
        }

        private static void Swap(ref float[] left, ref float[] right)
        {
            var buffer = left;
            left = right;
            right = buffer;
        }

        private readonly struct HistogramEntry
        {
            public HistogramEntry(Color32 color, int count, int key)
            {
                Color = color;
                Count = count;
                Key = key;
            }

            public Color32 Color { get; }
            public int Count { get; }
            public int Key { get; }
        }

        private sealed class PaletteBox
        {
            private readonly List<HistogramEntry> entries;

            public PaletteBox(List<HistogramEntry> entries)
            {
                this.entries = entries;
                RecalculateStatistics();
            }

            public Color32 AverageColor { get; private set; }
            public bool CanSplit => entries.Count > 1;
            public int FirstKey { get; private set; }
            public int SplitScore { get; private set; }

            public (PaletteBox left, PaletteBox right) Split()
            {
                if (!CanSplit)
                {
                    throw new InvalidOperationException("Cannot split a palette box with a single histogram entry.");
                }

                entries.Sort(GetEntryComparer(GetDominantChannel()));

                var targetCount = totalCount / 2;
                var runningCount = 0;
                var splitIndex = 1;
                for (; splitIndex < entries.Count; splitIndex++)
                {
                    runningCount += entries[splitIndex - 1].Count;
                    if (runningCount >= targetCount)
                    {
                        break;
                    }
                }

                splitIndex = Mathf.Clamp(splitIndex, 1, entries.Count - 1);

                return (
                    new PaletteBox(entries.GetRange(0, splitIndex)),
                    new PaletteBox(entries.GetRange(splitIndex, entries.Count - splitIndex)));
            }

            private int totalCount;
            private int redRange;
            private int greenRange;
            private int blueRange;

            private void RecalculateStatistics()
            {
                totalCount = 0;
                FirstKey = int.MaxValue;

                var minRed = byte.MaxValue;
                var minGreen = byte.MaxValue;
                var minBlue = byte.MaxValue;
                var maxRed = byte.MinValue;
                var maxGreen = byte.MinValue;
                var maxBlue = byte.MinValue;
                long redSum = 0;
                long greenSum = 0;
                long blueSum = 0;

                for (var index = 0; index < entries.Count; index++)
                {
                    var entry = entries[index];
                    totalCount += entry.Count;
                    FirstKey = Mathf.Min(FirstKey, entry.Key);
                    redSum += (long)entry.Color.r * entry.Count;
                    greenSum += (long)entry.Color.g * entry.Count;
                    blueSum += (long)entry.Color.b * entry.Count;

                    minRed = entry.Color.r < minRed ? entry.Color.r : minRed;
                    minGreen = entry.Color.g < minGreen ? entry.Color.g : minGreen;
                    minBlue = entry.Color.b < minBlue ? entry.Color.b : minBlue;
                    maxRed = entry.Color.r > maxRed ? entry.Color.r : maxRed;
                    maxGreen = entry.Color.g > maxGreen ? entry.Color.g : maxGreen;
                    maxBlue = entry.Color.b > maxBlue ? entry.Color.b : maxBlue;
                }

                AverageColor = new Color32(
                    (byte)(redSum / totalCount),
                    (byte)(greenSum / totalCount),
                    (byte)(blueSum / totalCount),
                    255);
                redRange = maxRed - minRed;
                greenRange = maxGreen - minGreen;
                blueRange = maxBlue - minBlue;
                SplitScore = Mathf.Max(redRange, Mathf.Max(greenRange, blueRange)) * totalCount;
            }

            private int GetDominantChannel()
            {
                if (redRange >= greenRange && redRange >= blueRange)
                {
                    return 0;
                }

                return greenRange >= blueRange
                    ? 1
                    : 2;
            }

            private static Comparison<HistogramEntry> GetEntryComparer(int dominantChannel)
            {
                return (left, right) =>
                {
                    var primaryComparison = CompareByChannel(left, right, dominantChannel);
                    if (primaryComparison != 0)
                    {
                        return primaryComparison;
                    }

                    var secondaryChannel = (dominantChannel + 1) % 3;
                    var secondaryComparison = CompareByChannel(left, right, secondaryChannel);
                    if (secondaryComparison != 0)
                    {
                        return secondaryComparison;
                    }

                    var tertiaryChannel = (dominantChannel + 2) % 3;
                    var tertiaryComparison = CompareByChannel(left, right, tertiaryChannel);
                    if (tertiaryComparison != 0)
                    {
                        return tertiaryComparison;
                    }

                    return left.Key.CompareTo(right.Key);
                };
            }

            private static int CompareByChannel(HistogramEntry left, HistogramEntry right, int channel)
            {
                return channel switch
                {
                    0 => left.Color.r.CompareTo(right.Color.r),
                    1 => left.Color.g.CompareTo(right.Color.g),
                    _ => left.Color.b.CompareTo(right.Color.b)
                };
            }
        }

        private sealed class GifPalette
        {
            private GifPalette(Color32[] colors, int colorTableBits)
            {
                Colors = colors;
                ColorTableBits = colorTableBits;
                ColorTableEntryCount = 1 << colorTableBits;
                MinimumCodeSize = Mathf.Max(2, colorTableBits);
            }

            public Color32[] Colors { get; }
            public int ColorTableBits { get; }
            public int ColorTableEntryCount { get; }
            public int MinimumCodeSize { get; }

            public static GifPalette Create(IReadOnlyList<Color32> sourceColors)
            {
                var uniqueColors = new List<Color32>(sourceColors.Count);
                var seenColors = new HashSet<int>();

                for (var index = 0; index < sourceColors.Count; index++)
                {
                    var color = sourceColors[index];
                    var key = (color.r << 16) | (color.g << 8) | color.b;
                    if (seenColors.Add(key))
                    {
                        uniqueColors.Add(new Color32(color.r, color.g, color.b, 255));
                    }
                }

                if (uniqueColors.Count == 0)
                {
                    uniqueColors.Add(new Color32(0, 0, 0, 255));
                }

                if (uniqueColors.Count == 1)
                {
                    uniqueColors.Add(uniqueColors[0]);
                }

                var colorTableBits = 1;
                while ((1 << colorTableBits) < uniqueColors.Count)
                {
                    colorTableBits++;
                }

                return new GifPalette(uniqueColors.ToArray(), colorTableBits);
            }
        }

        private static class GifLzwEncoder
        {
            public static byte[] Encode(byte[] indexedPixels, int minimumCodeSize)
            {
                if (indexedPixels.Length == 0)
                {
                    return Array.Empty<byte>();
                }

                var clearCode = 1 << minimumCodeSize;
                var endCode = clearCode + 1;
                var nextCode = endCode + 1;
                var codeSize = minimumCodeSize + 1;
                var dictionary = new Dictionary<int, int>();
                var writer = new GifBitWriter();

                WriteClearCode(writer, clearCode, ref dictionary, ref nextCode, ref codeSize, minimumCodeSize);

                int prefix = indexedPixels[0];
                for (var index = 1; index < indexedPixels.Length; index++)
                {
                    var symbol = indexedPixels[index];
                    var dictionaryKey = (prefix << 8) | symbol;

                    if (dictionary.TryGetValue(dictionaryKey, out var dictionaryCode))
                    {
                        prefix = dictionaryCode;
                        continue;
                    }

                    writer.Write(prefix, codeSize);

                    if (nextCode < 4096)
                    {
                        dictionary[dictionaryKey] = nextCode;
                        nextCode++;

                        if (nextCode == (1 << codeSize) && codeSize < 12)
                        {
                            codeSize++;
                        }
                    }
                    else
                    {
                        WriteClearCode(writer, clearCode, ref dictionary, ref nextCode, ref codeSize, minimumCodeSize);
                    }

                    prefix = symbol;
                }

                writer.Write(prefix, codeSize);
                writer.Write(endCode, codeSize);
                return writer.ToArray();
            }

            private static void WriteClearCode(
                GifBitWriter writer,
                int clearCode,
                ref Dictionary<int, int> dictionary,
                ref int nextCode,
                ref int codeSize,
                int minimumCodeSize)
            {
                writer.Write(clearCode, codeSize);
                dictionary = new Dictionary<int, int>();
                nextCode = clearCode + 2;
                codeSize = minimumCodeSize + 1;
            }
        }

        private sealed class GifBitWriter
        {
            private readonly List<byte> bytes = new();
            private int bitBuffer;
            private int bitCount;

            public void Write(int code, int codeSize)
            {
                bitBuffer |= code << bitCount;
                bitCount += codeSize;

                while (bitCount >= 8)
                {
                    bytes.Add((byte)(bitBuffer & 0xFF));
                    bitBuffer >>= 8;
                    bitCount -= 8;
                }
            }

            public byte[] ToArray()
            {
                if (bitCount > 0)
                {
                    bytes.Add((byte)(bitBuffer & 0xFF));
                    bitBuffer = 0;
                    bitCount = 0;
                }

                return bytes.ToArray();
            }
        }
    }
}
