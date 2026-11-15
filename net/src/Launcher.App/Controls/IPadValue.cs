namespace Launcher.App.Controls;

/// <summary>A control that holds a value the pad's left and right step (the redesigned interface's option rows), as
/// a slider or a stepper does; <see cref="PadNav.IsValue"/> counts it, and <c>Views/PadActions</c> steps it.</summary>
public interface IPadValue
{
    /// <summary>One step that way (-1 left, 1 right). False: the value is at its end (a fresh press then moves the
    /// focus on).</summary>
    bool StepBy(int by);
}
