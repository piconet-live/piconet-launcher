using System.Diagnostics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Launcher.App.Controls;

/// <summary>
/// The launcher's one motion helper: timed moves of transform and opacity only, never of layout, so a control is
/// always laid out where it ends and a click can land at any moment. Timings and easings are the theme's
/// (Theme.axaml, MOTION). The clock follows the window's frames, with a slow timer behind it for a window that draws
/// none (minimised, or headless), so nothing can stay part way. Under <see cref="Reduced"/> everything stands at once.
/// </summary>
public static class Motion
{
    private const int MostStaggered = 6;
    private const double RowRise = 8;
    private static readonly TimeSpan ListWindow = TimeSpan.FromMilliseconds(350), BurstLength = TimeSpan.FromMilliseconds(150),
        RefillGap = TimeSpan.FromSeconds(2);

    private static bool _reduced;

    [ThreadStatic]
    private static Clock? _clock;

    [ThreadStatic]
    private static TimeSpan _listWindowEnds;

    static Motion()
    {
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup, e) =>
        {
            if (e.NewValue is true && popup.Child is { } child)
            {
                Enter(child, 0, -6, 0.98, Quick);
            }
        });
        Install();
    }

    /// <summary>The Settings page's "Reduced motion".</summary>
    public static bool Reduced
    {
        get => _reduced;
        set
        {
            if (_reduced != value)
            {
                _reduced = value;
                ReducedChanged?.Invoke();
            }
        }
    }

    public static event Action? ReducedChanged;

    public static TimeSpan Quick => Asked("Quick", TimeSpan.FromMilliseconds(120));

    public static TimeSpan Standard => Asked("Standard", TimeSpan.FromMilliseconds(200));

    public static TimeSpan Emphasis => Asked("Emphasis", TimeSpan.FromMilliseconds(320));

    public static TimeSpan StaggerStep => Asked("StaggerStep", TimeSpan.FromMilliseconds(20));

    public static Easing EaseOut => Asked<Easing>("EaseOut", new CubicEaseOut());

    private static T Asked<T>(string key, T fallback) =>
        Application.Current is { } app && app.TryFindResource(key, out var value) && value is T found ? found : fallback;

    private static Clock Time => _clock ??= new Clock();

    /// <summary>Runs <paramref name="step"/> with 0 to 1 until the time is up; a new move of the same target and
    /// channel takes over from it. Under <see cref="Reduced"/> it ends at once.</summary>
    public static void Play(Visual target, string channel, TimeSpan duration, Easing? easing, Action<double> step,
        Action? done = null, TimeSpan delay = default)
    {
        if (Reduced || duration <= TimeSpan.Zero)
        {
            Time.Cancel(target, channel);
            step(1);
            done?.Invoke();
            return;
        }
        Time.Add(new Run(target, channel, duration, easing, step, done, delay));
    }

    /// <summary>The control arrives from an offset and a scale, fading in, and is left with no transform of its own.
    /// Only for what has no transform or opacity from a style (a page's host, a list's container, a panel).</summary>
    public static void Enter(Visual target, double dx, double dy, double scale = 1, TimeSpan? duration = null, TimeSpan delay = default)
    {
        if (Reduced)
        {
            return;
        }
        var transform = new MatrixTransform(Offset(dx, dy, scale, 0));
        target.RenderTransformOrigin = RelativePoint.Center;
        target.RenderTransform = transform;
        target.Opacity = 0;
        Play(target, nameof(Enter), duration ?? Standard, EaseOut,
            p =>
            {
                target.Opacity = p;
                transform.Matrix = Offset(dx, dy, scale, p);
            },
            () =>
            {
                target.ClearValue(Visual.RenderTransformProperty);
                target.ClearValue(Visual.RenderTransformOriginProperty);
                target.ClearValue(Visual.OpacityProperty);
            },
            delay);
    }

    private static Matrix Offset(double dx, double dy, double scale, double p)
    {
        var s = scale + ((1 - scale) * p);
        return Matrix.CreateScale(s, s) * Matrix.CreateTranslation(dx * (1 - p), dy * (1 - p));
    }

    /// <summary>A new page: it slides in, and its lists may arrive row by row.</summary>
    public static void EnterPage(Visual page, double dx, double dy)
    {
        OpenListWindow();
        Enter(page, dx, dy);
    }

    /// <summary>Lists attached in the next moment arrive row by row. A list attached at any other time (a seat changed,
    /// so its column was made again) just stands.</summary>
    public static void OpenListWindow() => _listWindowEnds = Time.Now + ListWindow;

    /// <summary>A short wash over a control that rests at opacity 0.</summary>
    public static void Flash(Visual target, double peak = 0.6)
    {
        if (!Reduced)
        {
            Play(target, nameof(Flash), Standard, null, p => target.Opacity = peak * Math.Sin(Math.PI * p),
                () => target.ClearValue(Visual.OpacityProperty));
        }
    }

    /// <summary>A short swell and back, nobody waits on it.</summary>
    public static void Pulse(Visual target, double by = 0.03)
    {
        if (Reduced)
        {
            return;
        }
        var transform = new ScaleTransform();
        target.RenderTransformOrigin = RelativePoint.Center;
        target.RenderTransform = transform;
        Play(target, nameof(Pulse), Emphasis, null,
            p => transform.ScaleX = transform.ScaleY = 1 + (by * Math.Sin(Math.PI * p)),
            () =>
            {
                target.ClearValue(Visual.RenderTransformProperty);
                target.ClearValue(Visual.RenderTransformOriginProperty);
            });
    }

    /// <summary>An ItemsControl's rows arrive one after another, up to a few steps, so a long list never waits.</summary>
    public static readonly AttachedProperty<bool> StaggerProperty = AvaloniaProperty.RegisterAttached<ItemsControl, ItemsControl, bool>("Stagger");

    /// <summary>A panel eases in each time it is shown.</summary>
    public static readonly AttachedProperty<bool> RevealProperty = AvaloniaProperty.RegisterAttached<Control, Control, bool>("Reveal");

    /// <summary>A big moment's entrance each time it is shown (the vote, the machine choice).</summary>
    public static readonly AttachedProperty<bool> RevealBigProperty = AvaloniaProperty.RegisterAttached<Control, Control, bool>("RevealBig");

    /// <summary>Set by a style: the control swells once when it comes on (the course drawn).</summary>
    public static readonly AttachedProperty<bool> PulseOnProperty = AvaloniaProperty.RegisterAttached<Control, Control, bool>("PulseOn");

    /// <summary>Set by a style: the control washes once when it comes on.</summary>
    public static readonly AttachedProperty<bool> FlashOnProperty = AvaloniaProperty.RegisterAttached<Control, Control, bool>("FlashOn");

    public static bool GetStagger(ItemsControl c) => c.GetValue(StaggerProperty);

    public static void SetStagger(ItemsControl c, bool on) => c.SetValue(StaggerProperty, on);

    public static bool GetReveal(Control c) => c.GetValue(RevealProperty);

    public static void SetReveal(Control c, bool on) => c.SetValue(RevealProperty, on);

    public static bool GetRevealBig(Control c) => c.GetValue(RevealBigProperty);

    public static void SetRevealBig(Control c, bool on) => c.SetValue(RevealBigProperty, on);

    public static bool GetPulseOn(Control c) => c.GetValue(PulseOnProperty);

    public static void SetPulseOn(Control c, bool on) => c.SetValue(PulseOnProperty, on);

    public static bool GetFlashOn(Control c) => c.GetValue(FlashOnProperty);

    public static void SetFlashOn(Control c, bool on) => c.SetValue(FlashOnProperty, on);

    private static void Install()
    {
        StaggerProperty.Changed.AddClassHandler<ItemsControl>((list, e) =>
        {
            if (e.NewValue is true)
            {
                Staggered(list);
            }
        });
        RevealProperty.Changed.AddClassHandler<Control>((c, e) => OnShown(c, e, () => Enter(c, 0, -6, 0.985)));
        RevealBigProperty.Changed.AddClassHandler<Control>((c, e) => OnShown(c, e, () =>
        {
            OpenListWindow();
            Enter(c, 0, 24, 0.98);
        }));
        PulseOnProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            if (e.NewValue is true)
            {
                Pulse(c);
            }
        });
        FlashOnProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            if (e.NewValue is true)
            {
                Flash(c, 0.35);
            }
        });
    }

    private static void OnShown(Control control, AvaloniaPropertyChangedEventArgs e, Action enter)
    {
        if (e.NewValue is true)
        {
            control.PropertyChanged += (_, change) =>
            {
                if (change.Property == Visual.IsVisibleProperty && change.NewValue is true)
                {
                    enter();
                }
            };
        }
    }

    private static void Staggered(ItemsControl list)
    {
        var fresh = true;
        var burstEnds = TimeSpan.Zero;
        var lastRefill = TimeSpan.MinValue;
        list.AttachedToVisualTree += (_, _) => fresh = true;
        list.PropertyChanged += (_, change) =>
        {
            if (change.Property == ItemsControl.ItemCountProperty && change.OldValue is 0 && change.NewValue is > 0
                && Time.Now - lastRefill > RefillGap)
            {
                lastRefill = Time.Now;
                burstEnds = Time.Now + BurstLength;
            }
        };
        list.ContainerPrepared += (_, e) =>
        {
            if (fresh)
            {
                fresh = false;
                if (Time.Now < _listWindowEnds)
                {
                    burstEnds = Time.Now + BurstLength;
                }
            }
            if (Time.Now < burstEnds)
            {
                Enter(e.Container, 0, RowRise, 1, Quick, StaggerStep * Math.Min(e.Index, MostStaggered));
            }
        };
    }

    private sealed record Run(Visual Target, string Channel, TimeSpan Duration, Easing? Easing, Action<double> Step, Action? Done, TimeSpan Delay)
    {
        /// <summary>From its first frame, so a page that takes long to build does not lose its move to that.</summary>
        public TimeSpan? Start { get; set; }
    }

    /// <summary>The moves under way on this thread: one list, ticked by the window's frames.</summary>
    private sealed class Clock
    {
        private static readonly TimeSpan Late = TimeSpan.FromMilliseconds(50);
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private readonly List<Run> _runs = [];
        private readonly DispatcherTimer _behind;
        private TimeSpan _lastFrame;
        private TimeSpan? _asked;

        public Clock()
        {
            _behind = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _behind.Tick += (_, _) =>
            {
                if (Now - _lastFrame > Late)
                {
                    Tick();
                }
                Ask();
            };
        }

        public TimeSpan Now => _watch.Elapsed;

        public void Cancel(Visual target, string channel) => _runs.RemoveAll(r => r.Target == target && r.Channel == channel);

        public void Add(Run run)
        {
            Cancel(run.Target, run.Channel);
            _runs.Add(run);
            _behind.Start();
            Ask();
        }

        /// <summary>The next frame, from whichever window a move is in. A frame asked of a window that has gone never
        /// comes: after a moment it is asked again.</summary>
        private void Ask()
        {
            if (_runs.Count == 0 || (_asked is { } at && Now - at < TimeSpan.FromMilliseconds(100)))
            {
                return;
            }
            foreach (var run in _runs)
            {
                if (TopLevel.GetTopLevel(run.Target) is { } top)
                {
                    _asked = Now;
                    top.RequestAnimationFrame(_ =>
                    {
                        _asked = null;
                        _lastFrame = Now;
                        Tick();
                        Ask();
                    });
                    return;
                }
            }
        }

        private void Tick()
        {
            var now = Now;
            foreach (var run in _runs.ToArray())
            {
                run.Start ??= now;
                var gone = now - run.Start.Value - run.Delay;
                if (gone < TimeSpan.Zero)
                {
                    continue;
                }
                var part = Math.Clamp(gone / run.Duration, 0, 1);
                run.Step(part >= 1 || run.Easing is null ? part : run.Easing.Ease(part));
                if (part >= 1)
                {
                    _runs.Remove(run);
                    run.Done?.Invoke();
                }
            }
            if (_runs.Count == 0)
            {
                _behind.Stop();
            }
        }
    }
}
