using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UartTool.Services
{
    public class SerialPortService : IDisposable
    {
        private readonly SerialPort _sp = new SerialPort();
        private CancellationTokenSource? _cts;

        public event Action<byte[]>? DataReceived;
        public bool IsOpen => _sp.IsOpen;

        public void Open(string port, int baud, int databits, Parity parity, StopBits stopBits)
        {
            if (IsOpen) Close();

            _sp.PortName = port;
            _sp.BaudRate = baud;
            _sp.DataBits = databits;
            _sp.Parity = parity;
            _sp.StopBits = stopBits;
            _sp.ReadTimeout = 500;
            _sp.WriteTimeout = 500;
            _sp.Open();

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ReadLoopAsync(_cts.Token));
        }

        private async Task ReadLoopAsync(CancellationToken token)
        {
            var buf = new byte[4096];
            while (!token.IsCancellationRequested && IsOpen)
            {
                try
                {
                    int n = await _sp.BaseStream.ReadAsync(buf.AsMemory(0, buf.Length), token);
                    if (n > 0)
                    {
                        var data = new byte[n];
                        Buffer.BlockCopy(buf, 0, data, 0, n);
                        DataReceived?.Invoke(data);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception)
                {
                    // 可在这里抛出状态事件，或尝试重连
                    await Task.Delay(50, token);
                }
            }
        }

        public async Task WriteAsync(byte[] data, CancellationToken token = default)
        {
            if (!IsOpen) throw new InvalidOperationException("串口未打开");
            await _sp.BaseStream.WriteAsync(data.AsMemory(0, data.Length), token);
            await _sp.BaseStream.FlushAsync(token);
        }

        public void Close()
        {
            try
            {
                _cts?.Cancel();
                if (_sp.IsOpen) _sp.Close();
            }
            catch { }
        }

        public void Dispose()
        {
            Close();
            _sp.Dispose();
        }
    }
}
