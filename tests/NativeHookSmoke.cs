using System;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows.Forms;
using MousePilot;

internal static class NativeHookSmoke
{
    [STAThread]
    public static int Main()
    {
        try
        {
            // A failed timer assertion must fail the process, not show a dialog.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            return Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
    }

    private static int Run()
    {
        using (NativeMouseHook hook = new NativeMouseHook())
        using (ApplicationContext context = new ApplicationContext())
        using (Timer timer = new Timer())
        {
            if (hook.Active) throw new Exception("Hook is unexpectedly active before Start.");
            Type mouseData = typeof(NativeMouseHook).GetNestedType("MouseData", BindingFlags.NonPublic);
            int expectedSize = IntPtr.Size == 8 ? 32 : 24;
            if (Marshal.SizeOf(mouseData) != expectedSize) throw new Exception("Incorrect native mouse data size.");
            hook.Start();
            if (!hook.Active) throw new Exception("Hook was not active after Start.");
            hook.Start(); // Starting twice must not install a second hook.
            timer.Interval = 250;
            timer.Tick += delegate
            {
                timer.Stop();
                hook.Dispose();
                if (hook.Active) throw new Exception("Hook remained active after Dispose.");
                hook.Dispose(); // Idempotent cleanup.
                context.ExitThread();
            };
            timer.Start();
            Application.Run(context);
            bool rejectedDisposedStart = false;
            try { hook.Start(); }
            catch (ObjectDisposedException) { rejectedDisposedStart = true; }
            if (!rejectedDisposedStart) throw new Exception("Starting a disposed hook should fail.");
            Console.WriteLine("PASS: native hook starts and stops on WinForms message loop (" + (IntPtr.Size * 8) + "-bit, struct " + expectedSize + " bytes). No inputs synthesized; no actions launched.");
        }
        return 0;
    }
}
