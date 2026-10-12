using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MousePilot
{
    public sealed class RuleAction
    {
        public string kind;
        public string appId;
        public string url;
    }

    public sealed class GestureRule
    {
        public string id;
        public string name;
        public List<string> sequence = new List<string>();
        public int gapMs = 700;
        public bool enabled;
        public RuleAction action;
        public string lastRun;
        public int runCount;
    }

    /// <summary>
    /// Matches consecutive input tokens using a monotonic clock supplied by the host.
    /// This class is intentionally independent of Windows hooks and action execution.
    /// The caller must serialize Feed and Reset calls.
    /// </summary>
    public sealed class GestureMatcher
    {
        private sealed class Progress
        {
            internal string Signature;
            internal readonly List<string> Tokens = new List<string>();
            internal long LastInput;
        }

        private readonly Dictionary<string, Progress> progress = new Dictionary<string, Progress>();
        private long cooldownUntil;
        private long lastTimestamp;
        private bool hasTimestamp;
        public const int CooldownMs = 1500;
        public const int MaximumSequenceLength = 8;

        public void Reset()
        {
            progress.Clear();
            cooldownUntil = 0;
            lastTimestamp = 0;
            hasTimestamp = false;
        }

        public string Feed(string input, long nowMs, List<GestureRule> rules)
        {
            // A changed clock cannot carry a partial gesture into another timeline.
            if (hasTimestamp && nowMs < lastTimestamp)
                Reset();
            lastTimestamp = nowMs;
            hasTimestamp = true;

            if (nowMs < cooldownUntil)
                return null;
            if (rules == null || String.IsNullOrEmpty(input))
            {
                progress.Clear();
                return null;
            }

            HashSet<string> activeIds = new HashSet<string>();
            string matchedId = null;
            int matchedLength = 0;
            foreach (GestureRule rule in rules)
            {
                if (rule == null || !rule.enabled || String.IsNullOrEmpty(rule.id)
                    || rule.sequence == null || rule.sequence.Count == 0
                    || rule.sequence.Count > MaximumSequenceLength || rule.gapMs <= 0)
                    continue;
                if (!activeIds.Add(rule.id))
                    continue;

                string signature = rule.gapMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "\0" + String.Join("\0", rule.sequence.ToArray());
                Progress current;
                if (!progress.TryGetValue(rule.id, out current) || current.Signature != signature)
                {
                    current = new Progress();
                    current.Signature = signature;
                    progress[rule.id] = current;
                }

                if (current.Tokens.Count > 0 && nowMs - current.LastInput > rule.gapMs)
                    current.Tokens.Clear();
                current.LastInput = nowMs;
                current.Tokens.Add(input);
                if (current.Tokens.Count > rule.sequence.Count)
                    current.Tokens.RemoveAt(0);

                bool complete = current.Tokens.Count == rule.sequence.Count;
                for (int i = 0; complete && i < rule.sequence.Count; i++)
                    complete = String.Equals(current.Tokens[i], rule.sequence[i], StringComparison.Ordinal);
                if (complete && rule.sequence.Count > matchedLength)
                {
                    matchedId = rule.id;
                    matchedLength = rule.sequence.Count;
                }
            }

            // A disabled or removed rule must not retain a partial gesture.
            List<string> inactiveIds = new List<string>();
            foreach (string id in progress.Keys)
                if (!activeIds.Contains(id))
                    inactiveIds.Add(id);
            foreach (string id in inactiveIds)
                progress.Remove(id);

            if (matchedId != null)
            {
                progress.Clear();
                cooldownUntil = nowMs > Int64.MaxValue - CooldownMs ? Int64.MaxValue : nowMs + CooldownMs;
            }
            return matchedId;
        }
    }

    /// <summary>
    /// Observe physical mouse input without suppressing or modifying any click.
    /// Start on a thread running a Windows message loop. Input subscribers should
    /// enqueue work and return immediately; a low-level hook must stay responsive.
    /// </summary>
    public sealed class NativeMouseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_XBUTTONUP = 0x020C;
        private const uint LLMHF_INJECTED = 0x00000001;
        private readonly HookProc callback;
        private IntPtr hook;
        private int wheelRemainder;
        private bool disposed;

        public event Action<string, long> Input;

        public NativeMouseHook()
        {
            callback = OnMouse;
        }

        public bool Active { get { return hook != IntPtr.Zero; } }

        public void Start()
        {
            if (disposed)
                throw new ObjectDisposedException("NativeMouseHook");
            if (Active)
                return;
            hook = SetWindowsHookEx(WH_MOUSE_LL, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not start the mouse listener.");
        }

        private IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int message = unchecked((int)wParam.ToInt64());
                    if (message == WM_LBUTTONUP || message == WM_RBUTTONUP || message == WM_MBUTTONUP
                        || message == WM_MOUSEWHEEL || message == WM_XBUTTONUP)
                    {
                        MouseData data = (MouseData)Marshal.PtrToStructure(lParam, typeof(MouseData));
                        if ((data.flags & LLMHF_INJECTED) == 0)
                        {
                            if (message == WM_LBUTTONUP) Emit("left");
                            else if (message == WM_RBUTTONUP) Emit("right");
                            else if (message == WM_MBUTTONUP) Emit("middle");
                            else if (message == WM_XBUTTONUP)
                            {
                                uint button = (data.mouseData >> 16) & 0xFFFF;
                                if (button == 1) Emit("back");
                                else if (button == 2) Emit("forward");
                            }
                            else
                            {
                                int delta = unchecked((short)((data.mouseData >> 16) & 0xFFFF));
                                if ((wheelRemainder > 0 && delta < 0) || (wheelRemainder < 0 && delta > 0))
                                    wheelRemainder = 0;
                                wheelRemainder += delta;
                                while (wheelRemainder >= 120)
                                {
                                    wheelRemainder -= 120;
                                    Emit("wheelUp");
                                }
                                while (wheelRemainder <= -120)
                                {
                                    wheelRemainder += 120;
                                    Emit("wheelDown");
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Never let subscriber or marshalling errors escape into Windows.
            }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }

        private void Emit(string token)
        {
            Action<string, long> handler = Input;
            if (handler == null)
                return;
            long ticks = Stopwatch.GetTimestamp();
            long frequency = Stopwatch.Frequency;
            long nowMs = (ticks / frequency) * 1000 + (ticks % frequency) * 1000 / frequency;
            handler(token, nowMs);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
            Input = null;
            GC.SuppressFinalize(this);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseData
        {
            public Point pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
