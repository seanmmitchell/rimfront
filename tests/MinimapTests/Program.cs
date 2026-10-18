using System;
using RimFront;

static class Program
{
    static int checks;
    static void Main()
    {
        MinimapView local = MinimapView.Frame(100, 100, false, 0, 0);
        Check(local.Size == 50 && local.X(100) == .5f && local.Y(100) == .5f, "idle view centres the pawn in 50 tiles");
        Check(local.Y(110) < local.Y(100) && local.X(110) > local.X(100), "north is up and east is right");
        Check(MinimapView.Frame(100, 100, true, 110, 105).Size == 50, "near aim keeps local scale");
        Check(!default(MinimapView).CoversLocalArea(0, 0), "uninitialized view must refresh");
        Check(local.CoversLocalArea(104.9f, 95.1f), "ordinary movement retains at least 20 tiles on every side");
        Check(!local.CoversLocalArea(105.1f, 100), "travel past the safety margin forces refresh before losing local coverage");
        Check(!local.CoversLocalArea(200, 200), "teleport forces immediate reframing");
        foreach (float origin in new[] { 0f, .125f, 100f, 499f })
            foreach (float distance in new[] { 26f, 30f, 60f, 120f })
                for (int angle = 0; angle < 360; angle += 15)
                {
                    float radians = angle * (float)Math.PI / 180;
                    float x = origin + distance * (float)Math.Sin(radians), z = origin + distance * (float)Math.Cos(radians);
                    MinimapView view = MinimapView.Frame(origin, origin, true, x, z);
                    foreach (float dx in new[] { -25f, 25f })
                        foreach (float dz in new[] { -25f, 25f })
                            Inside(view, origin + dx, origin + dz, "all local corners survive distant aim");
                    foreach (float dx in new[] { -4f, 4f })
                        foreach (float dz in new[] { -4f, 4f })
                            Inside(view, x + dx, z + dz, "distant target has context on every side");
                    Check(view.Size >= 50, "aim never zooms in past the local radius");
                }
        MinimapView east = MinimapView.Frame(100, 100, true, 220, 100);
        Check(east.X(100) < .5f && east.X(220) > .5f, "distant aim pans toward the target");
        Check(east.Size > 120, "framing is not hard-coded to current weapon or camera range");
        MinimapView edge = MinimapView.Frame(.5f, .5f, false, 0, 0);
        Check(edge.MinX < 0 && edge.MinZ < 0 && edge.X(.5f) == .5f, "map edge retains local framing with outside-map padding");
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Check(MinimapView.Frame(100, 100, true, bad, 100).Size == 50, "invalid aim X falls back locally");
            Check(MinimapView.Frame(100, 100, true, 100, bad).Size == 50, "invalid aim Z falls back locally");
        }
        Check(MinimapView.Frame(100, 100, false, 220, 100).Size == 50, "lost aim returns to local view without retaining a stale target");
        var state = new MinimapRenderState();
        Check(!state.Active && state.View.Size == 0, "no native camera override outside a render pass");
        for (int i = 0; i < 3; i++)
        {
            int draws = 0;
            state.Run(east, () =>
            {
                draws++;
                Check(state.Active && state.View.Size == east.Size && state.View.MinX == east.MinX,
                    "all native camera/culling consumers see the same minimap frame");
                bool rejected = false;
                try { state.Run(local, () => draws += 100); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected && state.Active && state.View.MinX == east.MinX,
                    "nested rendering cannot steal or reset the active camera scope");
            });
            Check(draws == 1 && !state.Active && state.View.Size == 0, "one native pass restores camera ownership on completion");
            var expected = new Exception("injected renderer failure");
            try { state.Run(local, () => throw expected); }
            catch (Exception actual) { Check(ReferenceEquals(actual, expected), "native render exception is preserved"); }
            Check(!state.Active && state.View.Size == 0, "native render failure always clears the camera/culling override");
        }
        Console.WriteLine("PASS: " + checks + " minimap framing/projection and native render-scope checks (no GPU)");
    }
    static void Inside(MinimapView view, float x, float z, string scenario)
        => Check(view.X(x) >= -.00001f && view.X(x) <= 1.00001f && view.Y(z) >= -.00001f && view.Y(z) <= 1.00001f, scenario);
    static void Check(bool condition, string scenario)
    {
        if (!condition) throw new Exception("FAIL: " + scenario);
        checks++;
    }
}
