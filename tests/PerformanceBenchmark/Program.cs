using System;
using System.Diagnostics;
using RimFront;

internal static class Program
{
    static void Main()
    {
        Console.WriteLine("Legacy snapshot serialization only; these are not game frame times.");
        foreach (int size in new[] { 250, 500 })
        {
            var state = new WorldSnapshot { width = size, height = size, cells = new int[size * size], pawns = new PawnState[100] };
            for (int i = 0; i < state.cells.Length; i++) state.cells[i] = i % 6;
            for (int i = 0; i < state.pawns.Length; i++) state.pawns[i] = new PawnState { id = i, name = "Benchmark pawn", x = i, z = i, health = 1 };
            for (int i = 0; i < 10; i++) WireJson.Encode(state);
            long before = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            int bytes = 0;
            for (int i = 0; i < 100; i++) bytes = WireJson.Encode(state).Length;
            timer.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine($"{size}x{size}, 100 pawns: {timer.Elapsed.TotalMilliseconds / 100:F3} ms/encode; {allocated / 100} allocated bytes/encode; {bytes} payload bytes; {bytes * 10} bytes/s at 10 Hz");
        }
    }
}
