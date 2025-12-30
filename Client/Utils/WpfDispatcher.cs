using Client.Interfaces;
using Client;
namespace Client.Utils;
public class WpfDispatcher : IDispatcher
{
    public void Invoke(Action action)
        => App.Current.Dispatcher.Invoke(action);
}