using System;
using System.Collections.Generic;

namespace UartTool.Models
{
    public class MessageFrame
    {
        public byte[] Header { get; set; } = Array.Empty<byte>();
        public byte[]? Tail { get; set; }
        public bool UseCrc16 { get; set; }
        public bool UseLength { get; set; }

        public byte[] Pack(byte[] payload)
        {
            int lengthValue = 0;
            if (UseLength)
            {
                lengthValue += Header.Length;
                lengthValue += 2;
                lengthValue += payload.Length;
                if (UseCrc16) lengthValue += 2;
                if (Tail != null) lengthValue += Tail.Length;
            }
            int capacity = Header.Length
                           + (UseLength ? 2 : 0)
                           + payload.Length
                           + (UseCrc16 ? 2 : 0)
                           + (Tail?.Length ?? 0);

            var list = new List<byte>(capacity);
            list.AddRange(Header);
            if (UseLength)
            {
                byte[] lenBytes = BitConverter.GetBytes((ushort)lengthValue); 
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(lenBytes);
                list.AddRange(lenBytes);
            }
            list.AddRange(payload);
            if (UseCrc16)
                list.AddRange(Crc16.ModbusBytesLE(payload));
            if (Tail is { Length: > 0 })
                list.AddRange(Tail);
            return list.ToArray();
        }
    }
}
