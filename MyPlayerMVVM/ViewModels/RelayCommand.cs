using System;
using System.Windows.Input;

namespace MyPlayer_NET9.ViewModels
{
    /// <summary>
    /// Стандартная реализация ICommand для привязки методов ViewModel
    /// к элементам управления (Button.Command и т.д.) без code-behind.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        /// <summary>
        /// Единственный конструктор. Параметр в execute/canExecute можно игнорировать
        /// через "_ =>", если команде не нужны данные — так исчезает неоднозначность
        /// перегрузок, которая раньше возникала при передаче методов без параметров.
        /// </summary>
        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);
    }
}
