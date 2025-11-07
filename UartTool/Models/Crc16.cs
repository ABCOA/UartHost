using System;

namespace UartTool.Models
{
    public static class Crc16
    {
        // 常用 MODBUS 多项式 0xA001
        public static ushort Modbus(byte[] data, int offset = 0, int? count = null)
        {
            int len = count ?? (data.Length - offset);
            ushort crc = 0xFFFF;
            for (int i = 0; i < len; i++)
            {
                crc ^= data[offset + i];
                for (int b = 0; b < 8; b++)
                    crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : (crc >> 1));
            }
            return crc;
        }

        public static byte[] ModbusBytesLE(byte[] data, int offset = 0, int? count = null)
        {
            var v = Modbus(data, offset, count);
            return new[] { (byte)(v & 0xFF), (byte)(v >> 8) }; // 低字节在前
        }
    }
}