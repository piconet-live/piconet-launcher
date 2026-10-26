using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Xunit.v3;

[assembly: TestSupport.TestTimeLimit]

namespace TestSupport;

/// <summary>
/// A time limit for every test of the assembly (this file is compiled into each test project; nothing is written on a
/// test). The suites had none, so a test that hung kept a commit's check running (31 minutes once) and nothing said
/// which test it was.
/// <para>A test still running after <see cref="Seconds"/> is named on the error output and cancelled through
/// <see cref="TestContext.CancellationToken"/> (the token the tests give their waits), and fails whatever it then
/// does. A test that is still running <see cref="GraceSeconds"/> after that does not listen to its token: the test
/// program then ends at once with exit status <see cref="ExitStatus"/>, naming it, because nothing else can end a
/// thread that hangs.</para>
/// <para>The limit is many times what the slowest test takes on a busy computer. It
/// bounds a hang; no assertion rests on it, and it is not to be lowered to make a slow test "fail fast".
/// <c>FZGX_TEST_TIME_LIMIT_SECONDS</c> sets another limit, to see the mechanism at work.</para>
/// <para>Not covered: a hang outside a test's own run (a test class's constructor or Dispose). scripts/check.sh
/// gives each test program a time limit of its own for that.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class TestTimeLimitAttribute : BeforeAfterTestAttribute
{
    /// <summary>A test's time limit.</summary>
    public const int DefaultSeconds = 180;

    /// <summary>How long a cancelled test has to end.</summary>
    public const int GraceSeconds = 20;

    /// <summary>The test program's exit status when a test did not end.</summary>
    public const int ExitStatus = 3;

    private static readonly ConcurrentDictionary<IXunitTest, Running> _running = new();
    private static int _watching;

    /// <summary>The limit in use.</summary>
    public static int Seconds { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("FZGX_TEST_TIME_LIMIT_SECONDS"), NumberStyles.None,
            CultureInfo.InvariantCulture, out var seconds) && seconds > 0 ? seconds : DefaultSeconds;

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        ArgumentNullException.ThrowIfNull(test);
        _running[test] = new Running(test.TestDisplayName, Stopwatch.GetTimestamp(), TestContext.Current);
        if (Interlocked.Exchange(ref _watching, 1) == 0)
        {
            // a thread of its own: the watch must go on when the tests have used up the thread pool
            new Thread(Watch) { IsBackground = true, Name = "test time limit" }.Start();
        }
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        ArgumentNullException.ThrowIfNull(test);
        if (_running.TryRemove(test, out var running) && running.Cancelled)
        {
            throw new TimeoutException(
                $"TIME LIMIT: {running.Name} ran longer than a test's time limit ({Seconds} s) and was cancelled.");
        }
    }

    private static void Watch()
    {
        while (true)
        {
            Thread.Sleep(250);
            foreach (var running in _running.Values)
            {
                var seconds = Stopwatch.GetElapsedTime(running.Started).TotalSeconds;
                if (running.Cancelled && seconds > Seconds + GraceSeconds)
                {
                    Console.Error.WriteLine($"TIME LIMIT: {running.Name} was still running {GraceSeconds} s after it " +
                        $"was cancelled at a test's time limit ({Seconds} s). The test program ends here.");
                    Console.Error.Flush();
                    Environment.Exit(ExitStatus);
                }
                if (!running.Cancelled && seconds > Seconds)
                {
                    running.Cancelled = true;
                    Console.Error.WriteLine(
                        $"TIME LIMIT: {running.Name} is over a test's time limit ({Seconds} s): cancelled.");
                    running.Context.CancelCurrentTest();
                }
            }
        }
    }

    private sealed class Running(string name, long started, ITestContext context)
    {
        public string Name { get; } = name;

        public long Started { get; } = started;

        public ITestContext Context { get; } = context;

        public volatile bool Cancelled;
    }
}

/// <summary>The limit's own test. It is explicit, so no ordinary run takes it; <c>scripts/check.sh</c> runs it alone in
/// each test program (<c>dotnet test --explicit only</c>) with a limit of one second: it waits for ever on its token, and passes the check only by
/// being ended by the limit, named.</summary>
public sealed class TestTimeLimitSelfTest
{
    [Fact(Explicit = true)]
    public Task ATestThatWaitsForEverIsEndedByItsLimit() =>
        Task.Delay(Timeout.Infinite, TestContext.Current.CancellationToken);
}
