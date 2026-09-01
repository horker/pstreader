using System;

namespace XstReader
{
    static class Integrity
    {
        public static uint ComputeCrc(byte[] buffer, int offset, int count)
        {
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            uint crc = 0;

            for (int i = 0; i < count; i++)
            {
                crc ^= buffer[offset + i];

                for (int bit = 0; bit < 8; bit++)
                {
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ 0xEDB88320;
                    else
                        crc >>= 1;
                }
            }

            return crc;
        }

        public static ushort ComputeSignature(ulong fileOffset, ulong bid)
        {
            uint value = (uint)(fileOffset ^ bid);
            return (ushort)(((value >> 16) & 0xFFFF) ^ (value & 0xFFFF));
        }

        public static int AlignTo64(int value)
        {
            return (value + 63) & ~63;
        }
    }
}
