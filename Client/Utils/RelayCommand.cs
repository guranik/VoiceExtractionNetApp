using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Client.Utils;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    protected void Set<T>(ref T field, T value,
        [CallerMemberName] string prop = null)
    {
        field = value;
        PropertyChanged?.Invoke(this,
            new PropertyChangedEventArgs(prop));
    }

    protected void OnPropertyChanged(string prop)
        => PropertyChanged?.Invoke(this,
            new PropertyChangedEventArgs(prop));
}

public class RelayCommand : ICommand
{
    private readonly Action _action;

    public RelayCommand(Action action)
        => _action = action;

    public event EventHandler CanExecuteChanged;

    public bool CanExecute(object parameter) => true;
    public void Execute(object parameter) => _action();
}
