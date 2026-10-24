using MphRead;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

internal static class SceneProbeChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var key=new ReplayCameraKeyframe(10,new Vector3(1,2,3),Quaternion.FromEulerAngles(.2f,.4f,.1f),1.2f,Roll:.3f);
        var scene=new Scene();scene.FixtureCamera(key);check(scene.CurveAcceptanceMatches(key),"render probe accepts matching camera/view/projection");
        scene.FixtureProjection=scene.GetPerspectiveMatrix(1f);check(!scene.CurveAcceptanceMatches(key),"render probe rejects stale projection even when camera FOV scalar matches");
        scene.FixtureCamera(key);scene.FixtureView=new Matrix4(float.NaN,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1);
        check(!scene.CurveAcceptanceMatches(key),"render probe rejects NaN view matrix rather than silently passing tolerance comparisons");
        scene.FixtureCamera(key);scene.FixtureProjection=new Matrix4(float.PositiveInfinity,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1);
        check(!scene.CurveAcceptanceMatches(key),"render probe rejects nonfinite projection");
        scene.FixtureCamera(key);scene.FixturePosition=new(float.NaN,2,3);check(!scene.CurveAcceptanceMatches(key),"render probe rejects nonfinite camera position");
        scene.FixtureCamera(key);scene.FixtureFov=float.NaN;check(!scene.CurveAcceptanceMatches(key),"render probe rejects nonfinite camera lens");
        scene.FixtureCamera(key);check(!scene.CurveAcceptanceMatches(key with{Rotation=new(float.NaN,0,0,1)}),"render probe rejects nonfinite evaluator orientation");
    }
}
namespace MphRead
{
    public partial class Scene
    {
        private Vector3 _cameraPosition,_cameraFacing,_cameraUp;
        private float _cameraFov;
        private Matrix4 _viewMatrix,_perspectiveMatrix;
        internal Matrix4 FixtureProjection{set=>_perspectiveMatrix=value;}
        internal Matrix4 FixtureView{set=>_viewMatrix=value;}
        internal Vector3 FixturePosition{set=>_cameraPosition=value;}
        internal float FixtureFov{set=>_cameraFov=value;}
        public Matrix4 GetPerspectiveMatrix(float fov)=>Matrix4.CreatePerspectiveFieldOfView(fov,16f/9,.1f,1000);
        internal void FixtureCamera(ReplayCameraKeyframe key)
        {
            _cameraPosition=key.Position;_cameraFov=key.Fov;
            _cameraFacing=Vector3.Transform(-Vector3.UnitZ,key.Rotation).Normalized();
            _cameraUp=Vector3.Transform(Vector3.Transform(Vector3.UnitY,key.Rotation),Quaternion.FromAxisAngle(_cameraFacing,key.Roll)).Normalized();
            _viewMatrix=Matrix4.LookAt(_cameraPosition,_cameraPosition+_cameraFacing,_cameraUp);
            _perspectiveMatrix=GetPerspectiveMatrix(_cameraFov);
        }
    }
}
