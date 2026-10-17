using System;
using System.Collections.Generic;
using RimFront;

// The real Guard implementation uses this inert log shim and a fake backend.
// No game assembly, user32 call, window discovery, or live UI is used by these tests.
namespace Verse { internal static class Log { internal static void Message(string _) { } internal static void Warning(string _) { } } }
internal static class Program
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Main()
    {
        double now=0;var backend=new Backend();var logs=new List<string>();
        var guard=new NativeWindowIcon.Guard(backend,()=>now,logs.Add);
        guard.Tick();Check(backend.Resolves==0,"inactive lifecycle does not inspect windows");
        guard.Capture();Check(guard.OwnedIconCount==2&&backend.Copies==2,"capture owns two independent copies");
        for(int i=0;i<100;i++)guard.Capture();
        Check(backend.Copies==2,"repeated entry never grows icon ownership");
        guard.Restore();Check(backend.Writes==0,"unchanged originals are not rewritten");
        backend.Icons[1]=new IntPtr(77);backend.Icons[0]=IntPtr.Zero;
        guard.Restore();Check(backend.Writes==2&&logs.Count==1,"changed or missing icon slots restored and reported once");
        Check(backend.Icons[1]==new IntPtr(101)&&backend.Icons[0]==new IntPtr(102),"restored handles come from captured originals");
        guard.Restore();Check(backend.Writes==2,"already restored copies are not rewritten");
        backend.Window=new IntPtr(2);backend.Icons[1]=new IntPtr(88);backend.Icons[0]=new IntPtr(89);
        now=.1;guard.Tick();Check(backend.Writes==4&&logs.Count==1,"replacement owned window receives same preserved copies");
        int resolves=backend.Resolves;now=.11;guard.Tick();Check(backend.Resolves==resolves,"retry cadence avoids per-frame window lookup");
        backend.Owned=false;backend.Icons[1]=new IntPtr(90);now=.3;guard.Tick();
        Check(backend.Writes==4,"foreign window is never rewritten");
        now=3;guard.Tick();resolves=backend.Resolves;
        for(int i=0;i<100;i++){now+=.1;guard.Tick();}
        Check(guard.RemainingChecks==0&&backend.Resolves==resolves,"expired transition stops all window polling");
        Check(backend.Destroyed.Count==0,"published copies stay alive while windows may use them");

        var unavailable=new Backend{Available=false};var unavailableGuard=new NativeWindowIcon.Guard(unavailable,()=>0,_=>{});
        unavailableGuard.Capture();unavailableGuard.Restore();unavailableGuard.Tick();
        Check(unavailable.Resolves==0&&unavailable.Copies==0,"unsupported platform is inert");
        var foreign=new Backend{Owned=false};var foreignGuard=new NativeWindowIcon.Guard(foreign,()=>0,_=>{});foreignGuard.Capture();
        Check(foreign.Copies==0,"foreign capture cannot preserve another app's icon");
        var empty=new Backend();empty.Icons[0]=empty.Icons[1]=IntPtr.Zero;
        var emptyGuard=new NativeWindowIcon.Guard(empty,()=>0,_=>{});emptyGuard.Capture();emptyGuard.Restore();
        Check(empty.Copies==0&&empty.Writes==0,"no original icon means no invented replacement asset");
        var oneSize=new Backend();oneSize.Icons[0]=IntPtr.Zero;
        var oneGuard=new NativeWindowIcon.Guard(oneSize,()=>0,_=>{});oneGuard.Capture();
        Check(oneSize.CopySources.Count==2&&oneSize.CopySources[0]==oneSize.CopySources[1],"missing size copies actual available source");
        var failure=new Backend{FailCopy=2};var failedGuard=new NativeWindowIcon.Guard(failure,()=>0,_=>{});failedGuard.Capture();failedGuard.Restore();
        Check(failedGuard.OwnedIconCount==0&&failure.Destroyed.Count==1&&failure.Writes==0,"partial capture frees only unpublished copy");
        failure.FailCopy=0;failedGuard.Capture();Check(failedGuard.OwnedIconCount==2,"capture can retry after incomplete allocation");
        var disappearing=new Backend();var raceGuard=new NativeWindowIcon.Guard(disappearing,()=>0,_=>{});raceGuard.Capture();
        disappearing.Icons[1]=new IntPtr(99);disappearing.LoseOwnershipOnRead=true;raceGuard.Restore();
        Check(disappearing.Writes==0,"window ownership rechecked before write after icon read");
        now=0;var capped=new Backend();var cappedGuard=new NativeWindowIcon.Guard(capped,()=>now,_=>{});cappedGuard.Capture();cappedGuard.Restore();
        int start=capped.Resolves;
        for(int i=0;i<40;i++){now+=.051;cappedGuard.Tick();}
        Check(capped.Resolves-start<=12&&cappedGuard.RemainingChecks==0,"retry count and deadline jointly bound work");
        Console.WriteLine($"PASS: {checks} window-icon lifecycle checks using injected clock/backend. No native window APIs or live UI were called.");
    }
    sealed class Backend:NativeWindowIcon.IBackend
    {
        public bool Available{get;set;}=true;
        internal bool Owned=true,LoseOwnershipOnRead;
        internal IntPtr Window=new IntPtr(1);
        internal readonly Dictionary<int,IntPtr> Icons=new Dictionary<int,IntPtr>{{0,new IntPtr(10)},{1,new IntPtr(11)}};
        internal readonly List<IntPtr> CopySources=new List<IntPtr>(),Destroyed=new List<IntPtr>();
        internal int Resolves,Copies,Writes,FailCopy;
        public IntPtr ResolveWindow(){Resolves++;return Window;}
        public bool OwnsWindow(IntPtr window)=>Owned&&window==Window&&window!=IntPtr.Zero;
        public IntPtr ReadIcon(IntPtr window,int kind){if(LoseOwnershipOnRead)Owned=false;return Icons[kind];}
        public IntPtr Copy(IntPtr icon){CopySources.Add(icon);Copies++;return Copies==FailCopy?IntPtr.Zero:new IntPtr(100+Copies);}
        public bool WriteIcon(IntPtr window,int kind,IntPtr icon){Writes++;Icons[kind]=icon;return true;}
        public void DestroyCopy(IntPtr icon)=>Destroyed.Add(icon);
    }
}
