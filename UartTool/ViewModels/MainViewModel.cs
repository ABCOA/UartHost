using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UartTool.Models;
using UartTool.Services;
using UartTool.Utils;

namespace UartTool.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        
        public bool IsConnected => _serial.IsOpen;
        public bool LED0 = false;
        public bool LED1 = false;
        
        private bool _hideBrightnessLog;
        public bool HideBrightnessLog
        {
            get => _hideBrightnessLog;
            set
            {
                _hideBrightnessLog = value;
                OnPropertyChanged();
            }
        }
        
        private readonly object _rxLock = new();
        private readonly List<byte> _rxBuffer = new();

        void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        // 串口参数
        public ObservableCollection<string> PortNames { get; } =
            new ObservableCollection<string>(System.IO.Ports.SerialPort.GetPortNames().OrderBy(s => s));

        public ObservableCollection<int> BaudRates { get; } =
            new ObservableCollection<int>(new[] { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 });

        public ObservableCollection<int> DataBitsList { get; } =
            new ObservableCollection<int>(new[] { 7, 8 });

        public ObservableCollection<System.IO.Ports.Parity> ParityList { get; } =
            new ObservableCollection<System.IO.Ports.Parity>((System.IO.Ports.Parity[])Enum.GetValues(typeof(System.IO.Ports.Parity)));

        public ObservableCollection<System.IO.Ports.StopBits> StopBitsList { get; } =
            new ObservableCollection<System.IO.Ports.StopBits>((System.IO.Ports.StopBits[])Enum.GetValues(typeof(System.IO.Ports.StopBits)));

        private string? _selectedPort;
        public string? SelectedPort { get => _selectedPort; set { _selectedPort = value; OnPropertyChanged(); } }

        private int _selectedBaudRate = 115200;
        public int SelectedBaudRate { get => _selectedBaudRate; set { _selectedBaudRate = value; OnPropertyChanged(); } }

        private int _selectedDataBits = 8;
        public int SelectedDataBits { get => _selectedDataBits; set { _selectedDataBits = value; OnPropertyChanged(); } }

        private System.IO.Ports.Parity _selectedParity = System.IO.Ports.Parity.None;
        public System.IO.Ports.Parity SelectedParity { get => _selectedParity; set { _selectedParity = value; OnPropertyChanged(); } }

        private System.IO.Ports.StopBits _selectedStopBits = System.IO.Ports.StopBits.One;
        public System.IO.Ports.StopBits SelectedStopBits { get => _selectedStopBits; set { _selectedStopBits = value; OnPropertyChanged(); } }

        // UI 选项
        private bool _hexMode;
        public bool HexMode { get => _hexMode; set { _hexMode = value; OnPropertyChanged(); } }

        private bool _showTimestamp = true;
        public bool ShowTimestamp { get => _showTimestamp; set { _showTimestamp = value; OnPropertyChanged(); } }

        private bool _appendNewLine;
        public bool AppendNewLine { get => _appendNewLine; set { _appendNewLine = value; OnPropertyChanged(); } }

        // 发送/日志
        public ObservableCollection<string> LogLines { get; } = new ObservableCollection<string>();
        private string _sendText = "";
        public string SendText { get => _sendText; set { _sendText = value; OnPropertyChanged(); } }

        // 状态
        private string _statusText = "未连接";
        public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

        private long _rx, _tx;
        public string RxTxCounter => $"RX:{_rx}  TX:{_tx}";
        void IncRx(long n) { _rx += n; OnPropertyChanged(nameof(RxTxCounter)); }
        void IncTx(long n) { _tx += n; OnPropertyChanged(nameof(RxTxCounter)); }

        public string ConnectButtonText => _serial.IsOpen ? "断开" : "连接";
        public string LED0ButtonText => LED0 ? "LED0关" : "LED0开";
        public string LED1ButtonText => LED1 ? "LED1关" : "LED1开";
        
        // 亮度显示
        private int _brightnessRaw;
        public int BrightnessRaw
        {
            get => _brightnessRaw;
            set
            {
                _brightnessRaw = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BrightnessDisplay));
            }
        }

        private double _brightnessVoltage;
        public double BrightnessVoltage
        {
            get => _brightnessVoltage;
            set
            {
                _brightnessVoltage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BrightnessDisplay));
            }
        }
        
        public string BrightnessDisplay =>
            $"{BrightnessVoltage:F3} V  (ADC={BrightnessRaw})";


        // 帧配置
        public string FrameHeaderHex { get; set; } = "";
        public string FrameTailHex { get; set; } = "";
        public bool UseCrc16 { get; set; }
        public bool UseLength { get; set; }

        // 命令
        public AsyncRelayCommand RefreshPortsCommand { get; }
        public AsyncRelayCommand ConnectCommand { get; }
        public AsyncRelayCommand SendCommand { get; }
        public AsyncRelayCommand SendFramedCommand { get; }
        public AsyncRelayCommand ClearSendCommand { get; }
        public AsyncRelayCommand SendLED0ToggleCommand { get; }
        public AsyncRelayCommand SendLED1ToggleCommand { get; }
        
        public AsyncRelayCommand SaveLogCommand { get; }
        public AsyncRelayCommand ClearLogCommand { get; }

        private readonly SerialPortService _serial = new SerialPortService();

        public MainViewModel()
        {
            _serial.DataReceived += OnDataReceived;

            RefreshPortsCommand = new AsyncRelayCommand(_ => { RefreshPorts(); return Task.CompletedTask; });
            ConnectCommand      = new AsyncRelayCommand(async _ => await ToggleConnectAsync());
            SendCommand         = new AsyncRelayCommand(async _ => await SendAsync(),      _ => IsConnected);
            SendFramedCommand   = new AsyncRelayCommand(async _ => await SendFramedAsync(),_ => IsConnected);
            ClearSendCommand    = new AsyncRelayCommand(_ => { SendText = ""; return Task.CompletedTask; });
            SaveLogCommand      = new AsyncRelayCommand(_ => { SaveLog(); return Task.CompletedTask; });
            ClearLogCommand     = new AsyncRelayCommand(_ => { LogLines.Clear(); return Task.CompletedTask; });
            SendLED0ToggleCommand = new AsyncRelayCommand(async _ => await ToggleLed0Async(), _ => IsConnected);
            SendLED1ToggleCommand = new AsyncRelayCommand(async _ => await ToggleLed1Async(), _ => IsConnected);
        }

        private void RefreshPorts()
        {
            PortNames.Clear();
            foreach (var p in System.IO.Ports.SerialPort.GetPortNames().OrderBy(s => s))
                PortNames.Add(p);
            AppendLog("已刷新串口列表。");
        }

        private void RaiseCommandStates()
        {
            SendCommand.RaiseCanExecuteChanged();
            SendFramedCommand.RaiseCanExecuteChanged();
            SendLED0ToggleCommand.RaiseCanExecuteChanged();
            SendLED1ToggleCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(IsConnected));
        }

        private async Task ToggleConnectAsync()
        {
            if (_serial.IsOpen)
            {
                _serial.Close();
                StatusText = "已断开";
                AppendLog("串口已断开。");
                RaiseCommandStates();
                return;
            }

            try
            {
                if (SelectedPort == null) throw new InvalidOperationException("未选择端口");
                _serial.Open(SelectedPort, SelectedBaudRate, SelectedDataBits, SelectedParity, SelectedStopBits);
                StatusText = $"已连接 {SelectedPort} @ {SelectedBaudRate}";
                AppendLog(StatusText);
            }
            catch (Exception ex)
            {
                AppendLog("连接失败: " + ex.Message);
            }
            finally
            {
                RaiseCommandStates();
            }
            await Task.CompletedTask;
        }

        private async Task SendAsync()
        {
            if (!_serial.IsOpen) { AppendLog("未连接，无法发送。"); return; }
            try
            {
                byte[] toSend;
                if (HexMode)
                {
                    var hex = SendText.Replace(" ", "").Replace(",", "").Replace("\r", "").Replace("\n", "");
                    if (hex.Length % 2 != 0) throw new Exception("HEX 长度必须为偶数");
                    toSend = Enumerable.Range(0, hex.Length / 2)
                        .Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
                }
                else
                {
                    var text = AppendNewLine ? (SendText + "\r\n") : SendText;
                    toSend = Encoding.ASCII.GetBytes(text);
                }

                await _serial.WriteAsync(toSend);
                IncTx(toSend.LongLength);
                AppendLog("TX " + (HexMode ? BitConverter.ToString(toSend).Replace("-", " ") : Encoding.ASCII.GetString(toSend)));
            }
            catch (Exception ex)
            {
                AppendLog("发送失败: " + ex.Message);
            }
        }

        private async Task SendFramedAsync()
        {
            if (!_serial.IsOpen) { AppendLog("未连接，无法发送（帧）。"); return; }
            try
            {
                var frame = new MessageFrame
                {
                    Header = HexToBytes(FrameHeaderHex),
                    Tail = string.IsNullOrWhiteSpace(FrameTailHex) ? null : HexToBytes(FrameTailHex),
                    UseCrc16 = UseCrc16,
                    UseLength = UseLength
                };
                var payload = HexMode ? HexToBytes(SendText) : Encoding.ASCII.GetBytes(SendText);
                var bytes = frame.Pack(payload);
                await _serial.WriteAsync(bytes);
                IncTx(bytes.LongLength);
                AppendLog($"TX(FRAME) {BitConverter.ToString(bytes).Replace("-", " ")}");
            }
            catch (Exception ex)
            {
                AppendLog("帧发送失败: " + ex.Message);
            }
        }

        private async Task SendLedFrameAsync(byte cmd)
        {
            if (!_serial.IsOpen)
            {
                AppendLog("未连接，无法发送 LED 命令。");
                return;
            }

            var frame = new MessageFrame
            {
                Header = new byte[] { 0xEF, 0x01 }, // 固定头
                Tail = null, // 没有尾
                UseCrc16 = true, // MCU 需要 CRC16
                UseLength = true // MCU 需要长度
            };

            // payload: 00 00 00 XX
            var payload = new byte[] { 0x00, 0x00, 0x00, cmd };

            var bytes = frame.Pack(payload);

            await _serial.WriteAsync(bytes);
            IncTx(bytes.LongLength);
            AppendLog($"TX(LED) {BitConverter.ToString(bytes).Replace("-", " ")}");
        }

        private async Task ToggleLed0Async()
        {
            bool turnOn = !LED0;

            byte cmd = turnOn ? (byte)0x00 : (byte)0x01; // 0: ON, 1: OFF

            await SendLedFrameAsync(cmd);

            LED0 = turnOn;
            OnPropertyChanged(nameof(LED0ButtonText)); // 通知按钮文字刷新
        }

        private async Task ToggleLed1Async()
        {
            bool turnOn = !LED1;

            byte cmd = turnOn ? (byte)0x02 : (byte)0x03; // 2: ON, 3: OFF

            await SendLedFrameAsync(cmd);

            LED1 = turnOn;
            OnPropertyChanged(nameof(LED1ButtonText));
        }

        private void OnDataReceived(byte[] data)
        {
            IncRx(data.LongLength);

            lock (_rxLock)
            {
                _rxBuffer.AddRange(data);
                TryParseFramesFromMcu();
            }
            
            if (HideBrightnessLog && IsBrightnessFrame(data))
            {
                return;
            }

            var s = HexMode
                ? BitConverter.ToString(data).Replace("-", " ")
                : Encoding.ASCII.GetString(data);

            AppendLog("RX " + s);
        }
        
        private bool IsBrightnessFrame(byte[] data)
        {
            if (data == null) return false;
            if (data.Length != 10) return false;

            if (data[0] != 0xEF) return false;
            if (data[1] != 0x01) return false;
            if (data[2] != 0x00) return false;
            if (data[3] != 0x0A) return false;
            if (data[4] != 0x10) return false;

            return true;
        }

        private void AppendLog(string line)
        {
            var prefix = ShowTimestamp ? $"[{DateTime.Now:HH:mm:ss.fff}] " : "";
            System.Windows.Application.Current.Dispatcher.Invoke(() => LogLines.Add(prefix + line));
        }

        private void SaveLog()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                                    $"UartLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllLines(path, LogLines);
            AppendLog("日志已保存: " + path);
        }

        private static byte[] HexToBytes(string hex)
        {
            hex = new string(hex.Where(Uri.IsHexDigit).ToArray());
            if (hex.Length % 2 != 0) throw new Exception("HEX 长度必须为偶数");
            return Enumerable.Range(0, hex.Length / 2)
                    .Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
        }
        
        private void TryParseFramesFromMcu()
        {
            // 帧格式：EF 01 [LenH] [LenL] [payload...] [CRC16_L] [CRC16_H]
            while (_rxBuffer.Count >= 4)
            {
                int headerIndex = -1;
                for (int i = 0; i < _rxBuffer.Count - 1; i++)
                {
                    if (_rxBuffer[i] == 0xEF && _rxBuffer[i + 1] == 0x01)
                    {
                        headerIndex = i;
                        break;
                    }
                }

                if (headerIndex < 0)
                {
                    _rxBuffer.Clear();
                    return;
                }

                if (headerIndex > 0)
                    _rxBuffer.RemoveRange(0, headerIndex);

                if (_rxBuffer.Count < 4) return;

                int len = (_rxBuffer[2] << 8) | _rxBuffer[3];

                if (len < 6 || len > 1024)
                {
                    _rxBuffer.RemoveAt(0);
                    continue;
                }

                if (_rxBuffer.Count < len) return;

                byte[] frame = _rxBuffer.Take(len).ToArray();
                _rxBuffer.RemoveRange(0, len);

                int payloadLen = len - 6;
                if (payloadLen <= 0) continue;

                byte[] payload = new byte[payloadLen];
                Array.Copy(frame, 4, payload, 0, payloadLen);

                ushort crcRecv = (ushort)(frame[len - 2] | (frame[len - 1] << 8));
                ushort crcCalc = BitConverter.ToUInt16(Crc16.ModbusBytesLE(payload), 0);

                if (crcCalc != crcRecv)
                {
                    AppendLog("RX 帧 CRC 错误，已丢弃。");
                    continue;
                }

                HandleFrameFromMcu(payload);
            }
        }
        
        private void HandleFrameFromMcu(byte[] payload)
        {
            if (payload.Length == 0) return;

            byte cmd = payload[0];

            switch (cmd)
            {
                case 0x10:
                    if (payload.Length >= 4)
                    {
                        int adc = (payload[2] << 8) | payload[3];
                        double voltage = adc * 3.3 / 4095.0;

                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            BrightnessRaw = adc;
                            BrightnessVoltage = voltage;
                            if (!HideBrightnessLog)
                            {
                                AppendLog($"亮度更新: ADC={adc}, 电压={voltage:F3}V");
                            }
                        });
                    }
                    break;
            }
        }
    }
}
