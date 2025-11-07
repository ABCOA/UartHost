using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace UartTool.Utils
{
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, Task> _executeAsync;
        private readonly Func<object?, bool>? _canExecute;

        public AsyncRelayCommand(Func<object?, Task> executeAsync, Func<object?, bool>? canExecute = null)
        {
            _executeAsync = executeAsync;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public async void Execute(object? parameter) // 只有 ICommand 接口是 void，我们内部把异常吃掉，转为日志
        {
            try
            {
                await _executeAsync(parameter);
            }
            catch (Exception ex)
            {
                // 这里不要再 throw，避免进程崩溃；改为记录日志或弹提示（由外层 VM 统一处理更好）
                System.Diagnostics.Debug.WriteLine("AsyncRelayCommand Error: " + ex);
            }
        }

        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}