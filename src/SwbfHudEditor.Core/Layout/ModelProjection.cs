using System.Numerics;

namespace SwbfHudEditor.Core.Layout;

/// <summary>
/// How the game draws a <c>Model3D</c>'s mesh (<c>Red3DModelElementLite::RenderUsingContext</c>, M
/// <c>00839F80</c>): through the interface camera, in true perspective, from the element's world matrix
/// with <c>LocalToWorld</c>'s changes. One mesh unit comes out as <c>W / (640 × depth)</c> HUD pixels
/// in the element's own frame (300 at 1920 wide with the default depth), y up, and a vertex nearer the
/// camera is drawn larger, about the middle of the screen.
/// </summary>
public static class ModelProjection
{
    /// <summary>The drawable's depth when nothing sets one (<c>Red3DModelElementLite::Init</c>, P <c>008DECA0</c>).</summary>
    public const float DefaultDepth = 0.01f;

    /// <summary>The interface camera's near plane (<c>RedCamera::SetFrustum</c>, M <c>007FEEF0</c>); nearer is clipped.</summary>
    public const float NearPlane = 1f;

    /// <summary>
    /// What <c>RedModel::Render</c> is handed, from the element's world matrix in interface units:
    /// <c>LocalToWorld</c> (M <c>00839D80</c>) turns the up row over and multiplies only the translation by
    /// <c>depth × 640 / W</c> (<c>GetDepthMultiplier</c>, M <c>00839D60</c>, while the HUD draws), which
    /// pulls the model from the HUD's plane toward the camera.
    /// </summary>
    public static Matrix4x4 MeshToView(Matrix4x4 world, float depth, HudScreen screen)
    {
        var m = world;
        m.M21 = -m.M21;
        m.M22 = -m.M22;
        m.M23 = -m.M23;
        m.M24 = 0f;
        var k = depth * EngineMath.InterfaceWidth / screen.WidthF;
        m.M41 *= k;
        m.M42 *= k;
        m.M43 *= k;
        m.M44 = 1f;
        return m;
    }

    /// <summary>
    /// A point in the camera's space to screen pixels (<c>internalUpdateSceneConstants</c>, M
    /// <c>0080ACB0</c>): the camera looks down -z with a horizontal field of 60° and the same focal length
    /// <c>W / t</c> on both axes. Z of the result is the distance from the camera; null when the point is
    /// nearer than the near plane.
    /// </summary>
    public static Vector3? ToScreen(Vector3 view, HudScreen screen)
    {
        var distance = -view.Z;
        if (!(distance >= NearPlane)) return null;
        var f = screen.WidthF / EngineMath.FrustumWidth;
        return new Vector3(0.5f * screen.WidthF + f * view.X / distance, 0.5f * screen.HeightF - f * view.Y / distance, distance);
    }

    /// <summary>A mesh vertex to screen pixels, for a matrix from <see cref="MeshToView"/>.</summary>
    public static Vector3? Project(Vector3 vertex, Matrix4x4 meshToView, HudScreen screen) =>
        ToScreen(Vector3.Transform(vertex, meshToView), screen);
}
