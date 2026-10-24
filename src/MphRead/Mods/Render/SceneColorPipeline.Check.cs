#if !ANDROID && !MPHREAD_SERVER
using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using NativeGL = OpenTK.Graphics.OpenGL.GL;

namespace MphRead;

public partial class Scene
{
    internal static void RunInventedSceneColorAcceptance(Action<bool,string> check)
    {
        int draw=NativeGL.GetInteger(GetPName.DrawFramebufferBinding),read=NativeGL.GetInteger(GetPName.ReadFramebufferBinding);
        int program=NativeGL.GetInteger(GetPName.CurrentProgram),unit=NativeGL.GetInteger(GetPName.ActiveTexture);
        bool setting=Mods.RenderOptions.LinearColor; var shadows=Mods.RenderOptions.Shadows; byte slot=Menu.SaveSlot; SaveWhen save=Menu.NeededSave;
        var outerCallback=Mods.Render.OpenGlFailureInjection.Callback;
        Scene? scene=null, nativeScene=null;
        try
        {
            Mods.RenderOptions.LinearColor=true; Mods.RenderOptions.Shadows=Mods.ShadowQuality.Off; Menu.SaveSlot=0; Menu.NeededSave=SaveWhen.Never;
            scene=new Scene(new(64,64),null!,null!,_=>{},()=>{},new SourceGxAcceptanceServices(),initializeRuntime:false){SideScene=true};
            scene.InitShaders();
            void CameraIs(Scene owner, Vector3 expected, string label)
            {
                check(owner._shaderLocations.MaterialCameraPosition>=0,label+": real Scene material camera uniform is active");
                float[] camera=new float[3];
                NativeGL.GetUniform(owner._shaderProgramId,owner._shaderLocations.MaterialCameraPosition,camera);
                check((new Vector3(camera[0],camera[1],camera[2])-expected).LengthSquared<.000001f,label);
            }
            check(!scene.SceneLinearColorActive,"R22 camera regression starts before first world admission with shadows Off");
            Matrix4 initialView=Matrix4.CreateTranslation(-4,-3,-9);
            NativeGL.UniformMatrix4(scene._shaderLocations.ViewMatrix,false,ref initialView);
            scene.SetMaterialCamera(ref initialView);
            CameraIs(scene,new(4,3,9),"nonorigin camera uploads before the first linear world pass");
            scene._viewMatrix=Matrix4.CreateTranslation(-7,2,-11);
            scene.BeginWorldPass();
            CameraIs(scene,new(7,-2,11),"world boundary refreshes the actual eye without an available shadow receiver");
            ColorRgba[] pooled = { new(128, 128, 128, 255), new(0, 0, 0, 0) };
            int worldTexture = scene.BindGetTexture(pooled, 1, 1);
            check(scene._sceneColorTextures.ContainsKey(worldTexture) && !scene.SceneColorTexturesFailed,
                "scene color upload reads only the declared image from a larger pooled buffer");
            int hudTexture = scene.BindGetTexture(pooled, 1, 1, sceneColor: false);
            check(!scene._sceneColorTextures.ContainsKey(hudTexture), "native HUD storage excludes scene color variants");
            check(scene.SceneLinearColorActive,"actual Scene publishes linear mode only after complete encoded target/program initialization");
            int originalTexture=scene._sceneEncodedTexture,originalFramebuffer=scene._sceneEncodedFramebuffer;
            int originalDepth=scene._sceneEncodedDepth,originalProgram=scene._sceneEncodeProgram;
            long originalBytes=scene.SceneColorPipelineBytes;
            scene.BeginWorldPass();
            check(scene._sceneEncodedTexture==originalTexture&&scene.SceneColorPipelineBytes==originalBytes,
                "warmed Scene frames reuse encoded target and program without allocation");
            scene._sourceBodyItems.Add(new RenderItem());
            scene.BeginWorldPass();
            check(!scene.SceneLinearColorActive,
                "encoded SourceGX body collection keeps the entire world frame in the native domain");
            NativeGL.ClearColor(.5f,.5f,.5f,1); NativeGL.Clear(ClearBufferMask.ColorBufferBit);
            check(scene.EncodeSceneColorBeforeOverlays(),"native SourceGX frame requires no transfer pass");
            byte[] nativeBody=Mods.Render.CaptureReadback.ReadRgbaRegion(
                new(scene.SceneReadFramebuffer,64,64,"invented-encoded-body",scene.SceneCaptureEncoding),0,0,64,64);
            check(Math.Abs(nativeBody[(32*64+32)*4]-128)<=2&&scene.SceneCaptureEncoding=="native-encoded-rgba8",
                "encoded SourceGX frame readback preserves native gray without a second sRGB encode");
            scene._sourceBodyItems.Clear();
            scene.BeginWorldPass();
            check(scene.SceneLinearColorActive&&scene._sceneEncodedTexture==originalTexture
                &&scene.SceneColorPipelineBytes==originalBytes,
                "the next body-free frame resumes linear color with its existing target owners");
            NativeGL.ClearColor(.5f,.5f,.5f,1); NativeGL.Clear(ClearBufferMask.ColorBufferBit);
            check(scene.EncodeSceneColorBeforeOverlays(),"actual Scene encode draw completes");
            byte[] encoded=Mods.Render.CaptureReadback.ReadRgbaRegion(new(scene.SceneReadFramebuffer,64,64,"invented-linear",scene.SceneCaptureEncoding),0,0,64,64);
            check(Math.Abs(encoded[(32*64+32)*4]-188)<=2&&!scene.SceneLinearColorActive,
                "actual Scene encodes linear working gray and disables transfer before native overlays");
            check(scene.SceneCaptureSize==new Vector2i(64,64)&&scene.SceneCaptureEncoding=="srgb-encoded-rgba8",
                "Scene capture describes encoded presentation pixels");
            Matrix4 movedView=Matrix4.CreateTranslation(2,-5,-13);
            Mods.RenderOptions.LinearColor=false; // Existing scene request stays frozen.
            NativeGL.UniformMatrix4(scene._shaderLocations.ViewMatrix,false,ref movedView);
            scene.SetMaterialCamera(ref movedView);
            CameraIs(scene,new(-2,5,13),"between-frame eye movement uploads with linear drawing inactive and a changed saved setting");
            Matrix4 previewView=Matrix4.LookAt(new Vector3(8,6,4),new Vector3(1,2,-3),Vector3.UnitY);
            NativeGL.UniformMatrix4(scene._shaderLocations.ViewMatrix,false,ref previewView);
            scene.SetMaterialCamera(ref previewView);
            CameraIs(scene,new(8,6,4),"temporary preview view uploads its own nonorigin camera outside linear drawing");
            NativeGL.UniformMatrix4(scene._shaderLocations.ViewMatrix,false,ref scene._viewMatrix);
            scene.SetMaterialCamera(ref scene._viewMatrix);
            CameraIs(scene,new(7,-2,11),"restoring a preview view restores the gameplay eye");
            Mods.RenderOptions.LinearColor=true;
            var callback=Mods.Render.OpenGlFailureInjection.Callback;
            try
            {
                Mods.Render.OpenGlFailureInjection.Callback=stage=>{if(stage=="scene-color-before-publish")throw new Mods.Render.OpenGlInjectedFailureException(stage);};
                bool failed=false;
                try{scene.EnsureSceneColorPipeline(new(80,48));}catch(Mods.Render.OpenGlInjectedFailureException){failed=true;}
                check(failed&&scene._sceneEncodedTexture==originalTexture&&scene._sceneEncodedFramebuffer==originalFramebuffer
                    &&scene._sceneEncodedDepth==originalDepth&&scene._sceneEncodeProgram==originalProgram,
                    "failed encoded target transaction preserves every previously published owner");
                check(NativeGL.IsTexture(originalTexture)&&NativeGL.IsFramebuffer(originalFramebuffer),"failed target transaction leaves prior scene drawable");
            }
            finally{Mods.Render.OpenGlFailureInjection.Callback=callback;}
            int sourceTexture=scene.AllocateTexture();
            try
            {
                NativeGL.ActiveTexture(TextureUnit.Texture0);
                NativeGL.BindTexture(TextureTarget.Texture2D,sourceTexture);
                var asset=new Mods.Render.RgbaMipTextureAsset("source-stage-fixture",
                    Mods.Render.TextureAssetClass.World,Mods.Render.TextureAssetChannel.Albedo,
                    new[] { new Mods.Render.CompressedTextureMip(1,1,new byte[] {128,64,32,255}) });
                check(scene.TryUploadWorldMaterialPreparedBound(asset,sourceTexture,true,out _,out _,registerSceneColor:false),
                    "source-stage encoded texture uploads with ordinary residency admission");
                check(!scene._sceneColorTextures.ContainsKey(sourceTexture),
                    "source-stage final-color decoding does not allocate an unused linear texture copy");
            }
            finally {scene.ReleaseTexture(sourceTexture);}

            scene.Size=new(80,48); scene.BeginWorldPass();
            check(scene._sceneEncodedSize==new Vector2i(80,48)&&scene.SceneLinearColorActive,
                "size transition retries and publishes a complete native-size output");
            check(!NativeGL.IsTexture(originalTexture)&&!NativeGL.IsFramebuffer(originalFramebuffer)&&!NativeGL.IsRenderbuffer(originalDepth),
                "successful encoded target resize retires previous color/depth/framebuffer ownership");
            int resizedTexture=scene._sceneEncodedTexture,resizedFramebuffer=scene._sceneEncodedFramebuffer;
            scene.DisposeSceneColorPipeline();
            check(!NativeGL.IsTexture(resizedTexture)&&!NativeGL.IsFramebuffer(resizedFramebuffer)
                &&!NativeGL.IsProgram(originalProgram)&&scene.SceneColorPipelineBytes==0,
                "scene color disposal releases the full program/color/depth bundle");
            Mods.RenderOptions.LinearColor=false;
            nativeScene=new Scene(new(32,32),null!,null!,_=>{},()=>{},new SourceGxAcceptanceServices(),initializeRuntime:false){SideScene=true};
            nativeScene.InitShaders();
            NativeGL.UseProgram(nativeScene._shaderProgramId);
            Vector3 sentinel=new(21,22,23);
            NativeGL.Uniform3(nativeScene._shaderLocations.MaterialCameraPosition,sentinel);
            nativeScene.SetMaterialCamera(ref movedView);
            CameraIs(nativeScene,ModernMaterialCorrectionsEnabled?new Vector3(-2,5,13):sentinel,
                "Original scene camera remains untouched unless the independent material correction experiment is enabled");
            check(NativeGL.GetError()==ErrorCode.NoError,"actual Scene color lifecycle and camera uploads are GL-clean");
        }
        finally
        {
            Mods.Render.OpenGlFailureInjection.Callback=outerCallback;
            nativeScene?.UnloadGl();
            scene?.UnloadGl();
            NativeGL.BindFramebuffer(FramebufferTarget.DrawFramebuffer,draw);NativeGL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,read);
            Mods.Render.GraphicsApi.UseProgram(program);Mods.Render.GraphicsApi.ActiveTexture((TextureUnit)unit);
            Mods.RenderOptions.LinearColor=setting;Mods.RenderOptions.Shadows=shadows;Menu.SaveSlot=slot;Menu.NeededSave=save;
        }
    }
}
#endif
