using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.ViewModels;

/// <summary>A screen's state and actions, without visuals: views bind to these and can be replaced.</summary>
public abstract class ViewModelBase : ObservableObject
{
    /// <summary>The thread the view model was made on: the window's (the launcher has one).</summary>
    private readonly Thread _ui = Thread.CurrentThread;

    /// <summary>Runs on the thread the view model was made on, the window's (core events arrive on network threads);
    /// nothing runs once that thread has no dispatcher any more. Not <see cref="Dispatcher.UIThread"/>, which is
    /// whichever application the process made last: in the launcher the same, but the tests make an application
    /// without a window for each test, and a page of a test that is over, still told of its state's changes, put its
    /// work on the next test's thread, where it touched its own controls from the wrong thread ("a different thread
    /// owns it").</summary>
    protected void OnUi(Action action) => Dispatcher.FromThread(_ui)?.Post(action);
}
