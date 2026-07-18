using UnityEngine;

/// <summary>Fast per-frame FOV test. Used to CULL visual-only work — particle bursts, tracers, muzzle
/// flashes — for anything the player camera can't see, so we don't even SPAWN off-screen effects
/// (huge GameObject-churn saver). Rendering itself is already frustum-culled by Unity. NEVER gate
/// gameplay (zombie movement, building logic) on this — off-screen things must keep simulating.</summary>
public static class View
{
    static readonly Plane[] planes = new Plane[6];
    static int frame = -1;
    static Camera cam;

    static void Refresh()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;
        if (cam == null) cam = Camera.main;                 // cached; re-fetched if it was destroyed
        if (cam != null) GeometryUtility.CalculateFrustumPlanes(cam, planes); // non-alloc overload
    }

    /// <summary>Is a sphere at 'pos' with the given radius inside the player camera's view frustum?
    /// Returns true when there's no camera yet (never cull in that case).</summary>
    public static bool Visible(Vector3 pos, float radius = 2.5f)
    {
        Refresh();
        if (cam == null) return true;
        return GeometryUtility.TestPlanesAABB(planes, new Bounds(pos, new Vector3(radius * 2f, radius * 2f, radius * 2f)));
    }
}
