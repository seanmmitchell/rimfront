using System;
using RimFront;

static class Program
{
    static int checks;
    static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    static bool[] Disk(int width, int radius)
    {
        var cells = new bool[width * width]; int c = width / 2;
        for (int z = 0; z < width; z++) for (int x = 0; x < width; x++)
            cells[z * width + x] = (x - c) * (x - c) + (z - c) * (z - c) <= radius * radius;
        return cells;
    }
    static void Main()
    {
        const int w = 81, c = 40, mid = c * w + c;
        var empty = MountainProfile.Build(new bool[w * w], w, w);
        Check(Array.TrueForAll(empty, h => h == 0), "Open terrain has no mountain");
        var small = MountainProfile.Build(Disk(w, 2), w, w);
        var largeRock = Disk(w, 16);
        var large = MountainProfile.Build(largeRock, w, w);
        Check(large[mid] > small[mid], "A wider rock formation has a taller summit");
        Check(large[mid] > large[mid + 15], "A mountain slopes down toward its exposed edge");
        Check(MountainProfile.Height(5, 1) > MountainProfile.Height(5, .25f), "Relative density changes height at the same radius");
        Check(MountainProfile.Height(10, .5f) > MountainProfile.Height(3, .5f), "Radius changes height at the same density");
        var isolated = new bool[w * w]; isolated[mid] = true;
        Check(MountainProfile.Build(isolated, w, w)[mid] < 3, "An isolated stone stays a low outcrop");
        // Mining changes both the silhouette and its dependency beyond one chunk.
        largeRock[mid] = false;
        var mined = MountainProfile.Build(largeRock, w, w);
        Check(mined[mid] == 0 && mined[mid + 1] < large[mid + 1], "Mining removes rock and lowers the neighboring profile");
        Check(mined[mid + 8] < large[mid + 8], "Profile changes propagate beyond the nearest chunk");
        var again = MountainProfile.Build(largeRock, w, w);
        for (int i = 0; i < again.Length; i++)
        {
            Check(again[i] == mined[i], "The same map produces the same silhouette");
            Check(float.IsFinite(again[i]) && again[i] <= MountainProfile.MaximumHeight, "Heights stay finite and bounded");
        }
        var solid = new bool[w * w]; Array.Fill(solid, true);
        var full = MountainProfile.Build(solid, w, w);
        Check(full[0] < full[mid] && full[mid] == MountainProfile.MaximumHeight, "Map edges are exposed and large peaks are capped");
        Check(MountainProfile.Corner(full, w, w, 0, 0) >= MountainProfile.BaseHeight, "Boundary corners remain valid");
        foreach (int step in new[] { 0, 1, 2 })
        {
            float distance = ViewRange.DistanceFor(step);
            Check(distance == 60 + step * 30, "Slider stops are 60, 90 and 120 tiles");
            int radius = ViewRange.ChunkRadiusFor(step);
            Check(radius * 12 >= distance, "Chunk coverage reaches the far plane even at a chunk edge");
            Check((radius * 2 + 1) * (radius * 2 + 1) <= ViewRange.MaximumChunks, "Every slider stop fits the cache budget");
        }
        Check(ViewRange.DistanceFor(-1) == 60 && ViewRange.DistanceFor(int.MaxValue) == 120, "Invalid saved settings clamp safely");
        Console.WriteLine($"PASS: {checks} mountain profile and view-range checks. No GPU or game session started.");
    }
}
