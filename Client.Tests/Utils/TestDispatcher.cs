using Client.Interfaces;

public class TestDispatcher : IDispatcher
{
    public void Invoke(Action action) => action();
}