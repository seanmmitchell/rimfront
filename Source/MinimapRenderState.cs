using System;

namespace RimFront
{
    // Shared by native camera/culling hooks for one synchronous render pass.
    // Never leave the colony camera override active after an exception.
    internal sealed class MinimapRenderState
    {
        internal bool Active { get; private set; }
        internal MinimapView View { get; private set; }
        internal void Run(MinimapView view, Action draw)
        {
            if (Active) throw new InvalidOperationException("Nested minimap rendering is unsupported.");
            View = view;
            Active = true;
            try { draw(); }
            finally { Active = false; View = default; }
        }
    }
}