using System;
using System.Reflection;
using System.Runtime.Serialization;
using FasterGameLoading;

namespace FasterGameLoading.Tests
{
    /// <summary>
    /// 建立不需要 Unity GameObject 的測試 fixture。
    /// </summary>
    internal static class TestFixtures
    {
        public static byte[] CreatePngHeader(int width, int height)
        {
            var header = new byte[24];
            header[0] = 0x89;
            header[1] = 0x50;
            header[2] = 0x4E;
            header[3] = 0x47;
            WriteBigEndian(header, 16, width);
            WriteBigEndian(header, 20, height);
            return header;
        }

        private static void WriteBigEndian(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }
    }
}
