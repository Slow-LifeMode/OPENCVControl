using System;
using System.Windows.Input;

namespace OpenCvWindowToolWpfDemo.Infrastructure
{
    /// <summary>
    /// 提供WPF绑定使用的通用命令实现。
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object> execute;
        private readonly Predicate<object> canExecute;

        /// <summary>
        /// 初始化命令实例。
        /// </summary>
        /// <param name="execute">命令执行逻辑。</param>
        /// <param name="canExecute">命令是否可执行的判断逻辑。</param>
        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this.canExecute = canExecute;
        }

        /// <summary>
        /// 当命令可执行状态可能变化时触发。
        /// </summary>
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>
        /// 判断命令当前是否可以执行。
        /// </summary>
        /// <param name="parameter">命令参数。</param>
        /// <returns>可以执行时返回true。</returns>
        public bool CanExecute(object parameter)
        {
            return canExecute == null || canExecute(parameter);
        }

        /// <summary>
        /// 执行命令。
        /// </summary>
        /// <param name="parameter">命令参数。</param>
        public void Execute(object parameter)
        {
            execute(parameter);
        }
    }
}
