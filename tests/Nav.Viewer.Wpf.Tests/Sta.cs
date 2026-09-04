using System.Runtime.ExceptionServices;

namespace Nav.Viewer.Wpf.Tests;

/// <summary>
/// Runs a test body on a single-threaded-apartment thread.
/// </summary>
/// <remarks>
/// Every WPF element is a <c>DispatcherObject</c>, and constructing one on the
/// MTA thread a test runner hands out throws before the test has said anything.
/// The thread is created and joined per call rather than pooled, so no test can
/// see an element another test left behind.
/// <para>
/// The failure is captured and rethrown on the calling thread with its stack
/// intact -- an exception left to escape the thread would take the runner down
/// instead of failing one test.
/// </para>
/// </remarks>
internal static class Sta
{
    internal static void Run(Action body)
    {
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
