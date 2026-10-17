using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Verse;

namespace RimFront
{
    // A preservation guard, not a diagnosis of which engine/driver changed an icon.
    // Never changes the title, class icon, application identity, or another process's window.
    internal static class NativeWindowIcon
    {
        static readonly Guard guard = new Guard(new WindowsBackend(),
            () => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency,
            message => Log.Message("[RimFront] " + message));
        static readonly Action capture = guard.Capture, restore = guard.Restore, tick = guard.Tick;
        static bool disabled;

        internal static void Capture() => Run(capture);
        internal static void Restore() => Run(restore);
        internal static void Tick() => Run(tick);

        static void Run(Action action)
        {
            if (disabled) return;
            try { action(); }
            catch (Exception error)
            {
                disabled = true;
                // Captured copies may already be displayed; retaining at most two until process
                // exit is safer than destroying a handle that a live HWND still references.
                Log.Warning("[RimFront] Window icon preservation unavailable: " + error.Message);
            }
        }

        // Injectable so lifecycle checks can run offline without calling the real desktop APIs.
        internal interface IBackend
        {
            bool Available { get; }
            IntPtr ResolveWindow();
            bool OwnsWindow(IntPtr window);
            IntPtr ReadIcon(IntPtr window, int kind);
            IntPtr Copy(IntPtr icon);
            bool WriteIcon(IntPtr window, int kind, IntPtr icon);
            void DestroyCopy(IntPtr icon);
        }

        internal sealed class Guard
        {
            const int Small = 0, Big = 1, MaximumChecks = 12;
            readonly IBackend backend;
            readonly Func<double> clock;
            readonly Action<string> log;
            IntPtr originalBig, originalSmall, copiedBig, copiedSmall;
            double nextCheck, deadline;
            bool reportedRestore;
            internal int RemainingChecks { get; private set; }
            internal int OwnedIconCount => (copiedBig != IntPtr.Zero ? 1 : 0) + (copiedSmall != IntPtr.Zero ? 1 : 0);

            internal Guard(IBackend backend, Func<double> clock, Action<string> log)
            { this.backend = backend; this.clock = clock; this.log = log; }

            internal void Capture()
            {
                if (!backend.Available || copiedBig != IntPtr.Zero) return;
                IntPtr window = backend.ResolveWindow();
                if (!backend.OwnsWindow(window)) return;
                IntPtr observedBig = backend.ReadIcon(window, Big), observedSmall = backend.ReadIcon(window, Small);
                IntPtr big = observedBig, small = observedSmall;
                // Windows normally synthesizes the missing size from the available icon. Preserve
                // that actual source, rather than loading an executable/default/replacement asset.
                if (big == IntPtr.Zero) big = small;
                if (small == IntPtr.Zero) small = big;
                if (big == IntPtr.Zero || small == IntPtr.Zero) return;
                IntPtr newBig = IntPtr.Zero, newSmall = IntPtr.Zero;
                try
                {
                    newBig = backend.Copy(big);
                    if (newBig == IntPtr.Zero) return;
                    newSmall = backend.Copy(small);
                    if (newSmall == IntPtr.Zero) return;
                    originalBig = observedBig; originalSmall = observedSmall;
                    copiedBig = newBig; copiedSmall = newSmall;
                    newBig = newSmall = IntPtr.Zero;
                    // Exactly two independent HICONs are retained for this game process's lifetime.
                    // All later transitions reuse them, including replacement HWNDs. Windows frees
                    // them at process exit. There is deliberately no unload/dispose DestroyIcon:
                    // WM_SETICON borrows its handle and a live window may still be displaying it.
                }
                finally
                {
                    // Only unpublished copies from an incomplete capture are ours to destroy now.
                    if (newBig != IntPtr.Zero) backend.DestroyCopy(newBig);
                    if (newSmall != IntPtr.Zero) backend.DestroyCopy(newSmall);
                }
            }

            internal void Restore()
            {
                if (!backend.Available || copiedBig == IntPtr.Zero) return;
                RestoreNow();
                double now = clock();
                RemainingChecks = MaximumChecks;
                nextCheck = now + .05;
                deadline = now + 2.0;
            }

            internal void Tick()
            {
                // No window enumeration, process lookup, or P/Invoke outside this bounded interval.
                if (RemainingChecks == 0) return;
                double now = clock();
                if (now > deadline) { RemainingChecks = 0; return; }
                if (now < nextCheck) return;
                RemainingChecks--;
                nextCheck = now + .15;
                RestoreNow();
            }

            void RestoreNow()
            {
                IntPtr window = backend.ResolveWindow();
                if (!backend.OwnsWindow(window)) return;
                bool changed = RestoreSlot(window, Big, originalBig, copiedBig);
                changed |= RestoreSlot(window, Small, originalSmall, copiedSmall);
                if (changed && !reportedRestore)
                {
                    reportedRestore = true;
                    log("A game-window icon handle changed after a native-view transition; restored the captured icon.");
                }
            }

            bool RestoreSlot(IntPtr window, int kind, IntPtr original, IntPtr copy)
            {
                IntPtr current = backend.ReadIcon(window, kind);
                // A different copy handle is not evidence of a different image. Avoid touching the
                // unchanged original and only report when replacing a genuinely different handle.
                if (current == original || current == copy) return false;
                return backend.OwnsWindow(window) && backend.WriteIcon(window, kind, copy);
            }
        }

        sealed class WindowsBackend : IBackend
        {
            const uint GetIcon = 0x007f, SetIcon = 0x0080, AbortIfHung = 0x0002;
            const int ClassBigIcon = -14, ClassSmallIcon = -34;
            public bool Available => Environment.OSVersion.Platform == PlatformID.Win32NT;

            public IntPtr ResolveWindow()
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    process.Refresh();
                    IntPtr window = process.MainWindowHandle;
                    if (OwnsWindow(window)) return window;
                }
                // This is the calling thread's active window, never another app's foreground HWND.
                IntPtr active = GetActiveWindow();
                return OwnsWindow(active) && GetWindow(active, 4) == IntPtr.Zero ? active : IntPtr.Zero;
            }

            public bool OwnsWindow(IntPtr window)
            {
                if (window == IntPtr.Zero || !IsWindow(window)) return false;
                GetWindowThreadProcessId(window, out uint owner);
                return owner == GetCurrentProcessId();
            }

            public IntPtr ReadIcon(IntPtr window, int kind)
            {
                if (!OwnsWindow(window)) return IntPtr.Zero;
                if (!Message(window, GetIcon, new IntPtr(kind), IntPtr.Zero, out IntPtr icon)) return IntPtr.Zero;
                if (icon != IntPtr.Zero) return icon;
                if (kind == 0 && Message(window, GetIcon, new IntPtr(2), IntPtr.Zero, out icon) && icon != IntPtr.Zero) return icon;
                // WM_GETICON returns zero for a window using its registered class's icon.
                int index = kind == 1 ? ClassBigIcon : ClassSmallIcon;
                return IntPtr.Size == 8 ? GetClassLongPtr64(window, index) : new IntPtr(unchecked((int)GetClassLong32(window, index)));
            }

            public IntPtr Copy(IntPtr icon) => CopyIcon(icon);
            public void DestroyCopy(IntPtr icon) { DestroyIcon(icon); }
            public bool WriteIcon(IntPtr window, int kind, IntPtr icon) => OwnsWindow(window)
                && Message(window, SetIcon, new IntPtr(kind), icon, out _);

            static bool Message(IntPtr window, uint message, IntPtr kind, IntPtr value, out IntPtr result) =>
                SendMessageTimeout(window, message, kind, value, AbortIfHung, 50, out result) != IntPtr.Zero;

            [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
            static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
            [DllImport("user32.dll")] static extern IntPtr CopyIcon(IntPtr icon);
            [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool DestroyIcon(IntPtr icon);
            [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool IsWindow(IntPtr window);
            [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
            [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();
            [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
            [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
            [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] static extern IntPtr GetClassLongPtr64(IntPtr window, int index);
            [DllImport("user32.dll", EntryPoint = "GetClassLongW")] static extern uint GetClassLong32(IntPtr window, int index);
        }
    }
}
