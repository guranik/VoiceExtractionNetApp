using System.ComponentModel;
using Client.Utils;
using Xunit;

namespace Client.Tests.Utils;

class TestObservable : ObservableObject
{
    private int _value;
    public int Value
    {
        get => _value;
        set => Set(ref _value, value);
    }

    public void Raise(string prop) => OnPropertyChanged(prop);
}

public class ObservableObjectTests
{
    [Fact]
    public void Set_ShouldUpdateFieldAndRaisePropertyChanged()
    {
        var obj = new TestObservable();
        string raisedProp = null;

        obj.PropertyChanged += (_, e) => raisedProp = e.PropertyName;

        obj.Value = 42;

        Assert.Equal(42, obj.Value);
        Assert.Equal(nameof(obj.Value), raisedProp);
    }

    [Fact]
    public void OnPropertyChanged_ShouldRaiseEvent()
    {
        var obj = new TestObservable();
        string raisedProp = null;

        obj.PropertyChanged += (_, e) => raisedProp = e.PropertyName;

        obj.Raise("Test");

        Assert.Equal("Test", raisedProp);
    }
}
