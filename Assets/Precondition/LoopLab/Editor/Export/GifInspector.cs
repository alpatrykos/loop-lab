using System;
using System.Collections.Generic;
using System.IO;

namespace Precondition.LoopLab.Editor.Export
{
    public readonly struct GifInspection
    {
        public GifInspection(string version, int width, int height, int frameCount, int[] frameDelays, bool isInfiniteLoop)
        {
            Version = version;
            Width = width;
            Height = height;
            FrameCount = frameCount;
            FrameDelays = frameDelays ?? Array.Empty<int>();
            IsInfiniteLoop = isInfiniteLoop;
        }

        public string Version { get; }
        public int Width { get; }
        public int Height { get; }
        public int FrameCount { get; }
        public int[] FrameDelays { get; }
        public bool IsInfiniteLoop { get; }
    }

    public static class GifInspector
    {
        public static GifInspection InspectFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("GIF inspection requires a file path.", nameof(path));
            }

            return Inspect(File.ReadAllBytes(path));
        }

        public static GifInspection Inspect(byte[] gifBytes)
        {
            if (gifBytes == null)
            {
                throw new ArgumentNullException(nameof(gifBytes));
            }

            if (gifBytes.Length < 13)
            {
                throw new InvalidDataException("GIF data is too short to contain a valid header.");
            }

            var index = 0;
            var version = ReadAscii(gifBytes, ref index, 6);
            if (!string.Equals(version, "GIF87a", StringComparison.Ordinal) &&
                !string.Equals(version, "GIF89a", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported GIF version {version}.");
            }

            var width = ReadUInt16(gifBytes, ref index);
            var height = ReadUInt16(gifBytes, ref index);
            var packedField = ReadByte(gifBytes, ref index);
            index += 2;

            if ((packedField & 0x80) != 0)
            {
                var globalColorTableEntryCount = 1 << ((packedField & 0x07) + 1);
                index += globalColorTableEntryCount * 3;
            }

            var frameDelays = new List<int>();
            var frameCount = 0;
            var isInfiniteLoop = false;

            while (index < gifBytes.Length)
            {
                var introducer = ReadByte(gifBytes, ref index);
                if (introducer == 0x3B)
                {
                    break;
                }

                switch (introducer)
                {
                    case 0x21:
                        ReadExtensionBlock(gifBytes, ref index, frameDelays, ref isInfiniteLoop);
                        break;
                    case 0x2C:
                        ReadImageBlock(gifBytes, ref index);
                        frameCount++;
                        break;
                    default:
                        throw new InvalidDataException($"Unexpected GIF block introducer 0x{introducer:X2}.");
                }
            }

            return new GifInspection(version, width, height, frameCount, frameDelays.ToArray(), isInfiniteLoop);
        }

        private static void ReadExtensionBlock(byte[] gifBytes, ref int index, List<int> frameDelays, ref bool isInfiniteLoop)
        {
            var extensionLabel = ReadByte(gifBytes, ref index);
            switch (extensionLabel)
            {
                case 0xF9:
                    ReadGraphicControlExtension(gifBytes, ref index, frameDelays);
                    break;
                case 0xFF:
                    ReadApplicationExtension(gifBytes, ref index, ref isInfiniteLoop);
                    break;
                default:
                    SkipSubBlocks(gifBytes, ref index, ReadByte(gifBytes, ref index));
                    break;
            }
        }

        private static void ReadGraphicControlExtension(byte[] gifBytes, ref int index, List<int> frameDelays)
        {
            var blockSize = ReadByte(gifBytes, ref index);
            if (blockSize != 4)
            {
                throw new InvalidDataException($"Unexpected Graphic Control Extension size {blockSize}.");
            }

            index++;
            frameDelays.Add(ReadUInt16(gifBytes, ref index));
            index++;

            var terminator = ReadByte(gifBytes, ref index);
            if (terminator != 0x00)
            {
                throw new InvalidDataException("Graphic Control Extension terminator is missing.");
            }
        }

        private static void ReadApplicationExtension(byte[] gifBytes, ref int index, ref bool isInfiniteLoop)
        {
            var blockSize = ReadByte(gifBytes, ref index);
            var applicationIdentifier = ReadAscii(gifBytes, ref index, blockSize);

            while (index < gifBytes.Length)
            {
                var subBlockLength = ReadByte(gifBytes, ref index);
                if (subBlockLength == 0)
                {
                    break;
                }

                if (string.Equals(applicationIdentifier, "NETSCAPE2.0", StringComparison.Ordinal) &&
                    subBlockLength == 3 &&
                    gifBytes[index] == 0x01)
                {
                    var loopCount = gifBytes[index + 1] | (gifBytes[index + 2] << 8);
                    isInfiniteLoop = loopCount == 0;
                }

                index += subBlockLength;
            }
        }

        private static void ReadImageBlock(byte[] gifBytes, ref int index)
        {
            index += 8;
            var packedField = ReadByte(gifBytes, ref index);
            if ((packedField & 0x80) != 0)
            {
                var localColorTableEntryCount = 1 << ((packedField & 0x07) + 1);
                index += localColorTableEntryCount * 3;
            }

            index++;
            SkipSubBlocks(gifBytes, ref index);
        }

        private static void SkipSubBlocks(byte[] gifBytes, ref int index)
        {
            while (index < gifBytes.Length)
            {
                var subBlockLength = ReadByte(gifBytes, ref index);
                if (subBlockLength == 0)
                {
                    break;
                }

                index += subBlockLength;
            }
        }

        private static void SkipSubBlocks(byte[] gifBytes, ref int index, byte firstBlockLength)
        {
            var subBlockLength = firstBlockLength;
            while (true)
            {
                index += subBlockLength;
                if (index >= gifBytes.Length)
                {
                    throw new InvalidDataException("GIF sub-block overruns the available data.");
                }

                subBlockLength = ReadByte(gifBytes, ref index);
                if (subBlockLength == 0)
                {
                    return;
                }
            }
        }

        private static string ReadAscii(byte[] gifBytes, ref int index, int length)
        {
            if (index + length > gifBytes.Length)
            {
                throw new InvalidDataException("GIF data ended unexpectedly while reading ASCII content.");
            }

            var chars = new char[length];
            for (var charIndex = 0; charIndex < length; charIndex++)
            {
                chars[charIndex] = (char)gifBytes[index + charIndex];
            }

            index += length;
            return new string(chars);
        }

        private static ushort ReadUInt16(byte[] gifBytes, ref int index)
        {
            var low = ReadByte(gifBytes, ref index);
            var high = ReadByte(gifBytes, ref index);
            return (ushort)(low | (high << 8));
        }

        private static byte ReadByte(byte[] gifBytes, ref int index)
        {
            if (index >= gifBytes.Length)
            {
                throw new InvalidDataException("GIF data ended unexpectedly.");
            }

            return gifBytes[index++];
        }
    }
}
