#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Formats;
using MphRead.Mods.Import.Retro;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using NativeGL = OpenTK.Graphics.OpenGL.GL;
using NVector4 = System.Numerics.Vector4;

namespace MphRead.Mods.Render
{
    internal static partial class OpenGlVisualCheck
    {
        private static void CheckCharacterSourceGx()
        {
            ResetPass();
            int before = _assertions;
            Scene.RunInventedSourceGxAcceptance(Require);
            ResetPass();
            Console.WriteLine($"[glvisualcheck] source GX PASS {_assertions - before} invented GPU assertions; production generated GLSL and Scene draw transaction; no owned media");
        }
    }
}

namespace MphRead
{
    public partial class Scene
    {
        // Acceptance only. Real constructor/Scene program ownership and exact
        // production draw are used; invented programs cannot mint a source lease.
        internal static void RunInventedSourceGxAcceptance(Action<bool, string> check)
        {
            const int size = 64;
            int outerDraw = NativeGL.GetInteger(GetPName.DrawFramebufferBinding);
            int outerRead = NativeGL.GetInteger(GetPName.ReadFramebufferBinding);
            int outerProgram = NativeGL.GetInteger(GetPName.CurrentProgram);
            int outerUnit = NativeGL.GetInteger(GetPName.ActiveTexture);
            int outerClient = NativeGL.GetInteger(GetPName.ClientActiveTexture);
            byte oldSlot = Menu.SaveSlot; SaveWhen oldSave = Menu.NeededSave;
            Menu.SaveSlot = 0; Menu.NeededSave = SaveWhen.Never;
            var scene = new Scene(new(size, size), null!, null!, _ => { }, () => { },
                new SourceGxAcceptanceServices(), initializeRuntime: false) { SideScene = true };
            var textures = new List<int>(); var programs = new List<int>();
            int weightedList = 0, nativeList = 0;
            try
            {
                Menu.SaveSlot = 0; Menu.NeededSave = SaveWhen.Never;
                // Initialize the actual ordinary Scene shader as the state and
                // pixel sentinel, not a second renderer or a mock native shader.
                scene.InitShaders();
                NativeGL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, outerDraw);
                NativeGL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, outerRead);
                scene._sourceGxPreviewView = scene._sourceGxPreviewProjection = Matrix4.Identity;
                scene._viewMatrix = scene._perspectiveMatrix = Matrix4.Identity;
                int nativeProgram = scene._shaderProgramId;
                check(nativeProgram != 0 && NativeGL.IsProgram(nativeProgram), "source GX fixture owns an actual native Scene program");

                // Row-major bottom row then top row; all channels deliberately
                // differ, including alpha. Sample texel centers to avoid a
                // platform-dependent filtering tolerance in the math oracles.
                byte[] texels = { 64,128,192,64, 32,96,160,128, 192,64,32,192, 16,48,80,255 };
                int image = Texture(2, 2, texels);
                int second = Texture(2, 2, new byte[] { 128,64,32,192, 16,32,64,255, 96,160,224,128, 255,128,64,0 });
                int[] sentinels = Enumerable.Range(0, 4).Select(i => Texture(1, 1, new byte[] { (byte)(11+i), (byte)(27+i), (byte)(41+i), 255 })).ToArray();
                weightedList = List(weighted: true, .25f, .25f, new Vector3(0, 0, 1));
                nativeList = List(weighted: false, 0, 0, Vector3.UnitZ);
                var item = new RenderItem { Type = RenderItemType.Mesh, ListId = weightedList,
                    WeightedSkinning = true, MatrixStackCount = 2, Transform = Matrix4.Identity };
                // Normalized .125/.375 weights over translations -.3/+.1 cancel.
                // Omitting normalization or a packed second joint moves edges.
                SetPalette(Matrix4.CreateTranslation(-.3f,0,0), Matrix4.CreateTranslation(.1f,0,0));
                var nativeItem = new RenderItem { Type = RenderItemType.Mesh, ListId = nativeList,
                    Transform = Matrix4.Identity, Alpha = 1, Diffuse = new(.12f,.64f,.24f),
                    PolygonMode = PolygonMode.Modulate, RenderMode = RenderMode.Normal,
                    CullingMode = CullingMode.Neither, TexcoordMatrix = Matrix4.Identity };

                var opaque = Material(Stage(new[]{15,12,8,15}, new[]{7,7,7,4}));
                byte[] a = Picture(opaque);
                Pixel(a,32,32,64,128,192,255,"opaque source ignores texture alpha");
                Pixel(a,7,32,0,0,0,255,"weighted quad left boundary proves normalized packed-joint skinning");
                Pixel(a,9,32,64,128,192,255,"weighted quad remains in expected source domain");
                Pixel(a,56,32,0,0,0,255,"weighted quad right boundary is bounded");

                var clamp = Material(Stage(new[]{8,8,15,8}, new[]{7,7,7,6}));
                Pixel(Picture(clamp),32,32,128,255,255,255,"TEV D plus A clamps independently per color channel");
                var multi = Material(Stage(new[]{15,12,8,15},new[]{7,7,7,4},0x700),
                    Stage(new[]{6,12,8,15},new[]{0,7,7,7},texture:1));
                multi.TexIds = new uint[]{1,2};
                Pixel(Picture(multi),32,32,160,160,200,255,"ordered TEV register3 write and later mix use independent numeric oracle",2);

                // Invented profile state is compiled into each plan. Alternating
                // zero/yellow/zero must not borrow another cached program's C1.
                var yellow = new NVector4(253/255f,239/255f,86/255f,0);
                var c1 = Material(Stage(new[]{15,4,12,15},new[]{7,7,7,6}));
                byte[] c1Zero = Picture(c1);
                Pixel(c1Zero,32,32,0,0,0,255,"default source C1 remains zero on the GPU");
                Pixel(Picture(c1,initialRegister1:yellow),32,32,253,239,86,255,"profile C1 uses explicit register2 RGB on the GPU");
                check(c1Zero.SequenceEqual(Picture(c1)),"zero/yellow/zero profile draws preserve zero-register cache isolation");
                var c1Mix = Material(Stage(new[]{15,8,4,0},new[]{7,7,7,6}));
                Pixel(Picture(c1Mix,initialRegister1:yellow),32,32,63,120,65,255,"source-like texture times C1 has independent byte oracle");
                var c1Write = Material(Stage(new[]{15,4,12,15},new[]{7,7,7,6},0x700),
                    Stage(new[]{15,6,12,15},new[]{7,7,7,6}));
                Pixel(Picture(c1Write,initialRegister1:yellow),32,32,253,239,86,255,"C1 propagated through committed C2 reaches later GPU stage");
                var c1Overwrite = Material(Stage(new[]{15,4,12,15},new[]{7,7,7,6},0x700),
                    Stage(new[]{15,8,12,15},new[]{7,7,7,6},0x700,texture:1),
                    Stage(new[]{15,6,12,15},new[]{7,7,7,6}));
                c1Overwrite.TexIds = new uint[]{1,2};
                Pixel(Picture(c1Overwrite,initialRegister1:yellow),32,32,128,64,32,255,"later C2 write replaces propagated C1 before its GPU read");

                var alpha = Material(Stage(new[]{15,12,8,15},new[]{7,7,7,4}));
                alpha.BlendSrc = 4; alpha.BlendDst = 5; alpha.Flags |= Cmdl.DepthSort;
                Pixel(Picture(alpha),32,32,16,32,48,207,"source alpha framebuffer policy blends straight RGB exactly once",2);
                var additive = Material(Stage(new[]{15,12,8,15},new[]{7,7,7,4}));
                additive.BlendSrc = additive.BlendDst = 1; additive.Flags |= Cmdl.DepthSort;
                Pixel(Picture(additive),32,32,64,128,192,255,"source additive policy uses ONE ONE despite fractional source alpha");

                // A real scroll changes raw primary UVs at caller time and
                // repeats after the bounded clock period, without a scene tick.
                var scroll = Material(Stage(new[]{15,12,8,15},new[]{7,7,7,6}));
                scroll.TexGens[0].Mtx = 0;
                scroll.UvAnims = new[]{new Cmdl.UvAnim{Mode=2,Params=new double[]{0,0,.25,0}}};
                Pixel(Picture(scroll,2),32,32,32,96,160,255,"primary UV scroll selects the independently authored second texel");
                check(Picture(scroll,0).SequenceEqual(Picture(scroll,900)), "source bounded clock repeats exact GPU pixels at900 seconds");
                check(Picture(scroll,0).SequenceEqual(Picture(opaque)), "zero-time UV and source opaque math preserve original sample");

                // Raw object normals drive environment UVs; the PN palette can
                // rotate the rendered geometry without rotating that input twice.
                GraphicsApi.DeleteLists(weightedList,1);
                weightedList = List(true,.25f,.25f,new Vector3(-.5f,.5f,-1)); item.ListId = weightedList;
                SetPalette(Matrix4.CreateRotationY(MathF.PI),Matrix4.CreateRotationY(MathF.PI));
                var environment = Material(Stage(new[]{15,12,8,15},new[]{7,7,7,6}));
                environment.TexGens[0] = new Cmdl.TexGen{Type=0,Src=1,Mtx=0,Post=0};
                environment.UvAnims = new[]{new Cmdl.UvAnim{Mode=0}};
                Pixel(Picture(environment),32,32,64,128,192,255,"environment domain uses raw normal and post STQ q1 with negative Z");
                var post1 = Material(Stage(new[]{15,12,8,15},new[]{7,7,7,6}));
                post1.TexGens[0] = new Cmdl.TexGen{Type=0,Src=1,Mtx=3,Post=3};
                post1.UvAnims = new[]{new Cmdl.UvAnim{Mode=2,Params=new double[]{.125,.25,.5,.75}},new Cmdl.UvAnim{Mode=0}};
                Pixel(Picture(post1,2),32,32,64,128,192,255,"packed post3 selects slot1 environment rows rather than slot0 scroll or identity");
                check(Picture(post1,0).SequenceEqual(Picture(post1,2)),"slot1 environment sampling remains independent of animated slot0");

                // Named preview lighting has its own numeric expectation.
                GraphicsApi.DeleteLists(weightedList,1);
                weightedList=List(true,.25f,.25f,Vector3.UnitZ); item.ListId=weightedList; SetPalette(Matrix4.Identity,Matrix4.Identity);
                var lit=Material(Stage(new[]{15,12,10,15},new[]{7,7,7,6},raster:4)); lit.Channels[0]=0x3001;
                float nd=90f/MathF.Sqrt(50*50+50*50+90*90);
                Pixel(Picture(lit),32,32,(int)MathF.Round(102+102*nd),(int)MathF.Round(100+102*nd),(int)MathF.Round(100+102*nd),255,"named preview light is PN-normal based and independent of native lighting",2);
                var zero=Material(Stage(new[]{15,12,10,15},new[]{7,7,7,6},raster:6));
                Pixel(Picture(zero),32,32,0,0,0,255,"unmapped raster channel reads zero rather than stale native light");

                byte[] NativePicture()
                {
                    Clear(); GraphicsApi.Disable(EnableCap.Blend); GraphicsApi.Disable(EnableCap.CullFace);
                    GraphicsApi.Enable(EnableCap.DepthTest); GraphicsApi.DepthFunc(DepthFunction.Less);
                    GraphicsApi.UseProgram(nativeProgram);
                    GraphicsApi.UniformMatrix4(scene._shaderLocations.ViewMatrix,false,ref scene._viewMatrix);
                    GraphicsApi.UniformMatrix4(scene._shaderLocations.ProjectionMatrix,false,ref scene._perspectiveMatrix);
                    GraphicsApi.Uniform1(scene._shaderLocations.ShowColors,1); GraphicsApi.Uniform1(scene._shaderLocations.UseFog,0);
                    scene.RenderItem(nativeItem); NativeGL.Finish(); return Read();
                }
                byte[] nativeBefore=NativePicture();
                check(nativeBefore.Where((_,i)=>i%4!=3).Any(v=>v!=0),"native sentinel actually renders before source draw");
                // Distinct caller units and raster state must survive the exact
                // production transaction before any outer picker scope restores.
                for(int i=0;i<4;i++){GraphicsApi.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0+i));GraphicsApi.BindTexture(TextureTarget.Texture2D,sentinels[i]);}
                NativeGL.ClientActiveTexture(TextureUnit.Texture1); GraphicsApi.ActiveTexture(TextureUnit.Texture3);
                GraphicsApi.UseProgram(nativeProgram); GraphicsApi.Enable(EnableCap.Blend);
                NativeGL.BlendFuncSeparate(BlendingFactorSrc.One,BlendingFactorDest.Zero,BlendingFactorSrc.Zero,BlendingFactorDest.One);
                GraphicsApi.DepthFunc(DepthFunction.Lequal); GraphicsApi.DepthMask(false);
                GraphicsApi.Enable(EnableCap.CullFace); GraphicsApi.CullFace(TriangleFace.Front);
                string state=State();
                Clear(preserve:true); Draw(opaque,0); NativeGL.Finish();
                check(State()==state,"source Scene transaction restores native program, four texture units, client unit and raster state");
                byte[] nativeAfter=NativePicture();
                check(nativeBefore.SequenceEqual(nativeAfter),"native A/source/native A reproduces exact ordinary Scene pixels");
                check(scene._shaderProgramId==nativeProgram,"source program never retargets native Scene program/location owner");
                foreach(int id in programs)check(NativeGL.IsProgram(id),"source generated program remains owned until Scene retirement");
                scene.ReleaseSourceGxPrograms();
                foreach(int id in programs)check(!NativeGL.IsProgram(id),"Scene retirement deletes source programs");
                check(NativeGL.IsProgram(nativeProgram)&&sentinels.All(NativeGL.IsTexture),"source retirement preserves native program and caller-owned sentinel textures");
                check(NativeGL.GetError()==ErrorCode.NoError,"invented source GX production draw leaves no GL errors");

                int Texture(int w,int h,byte[] bytes)
                {
                    int id=GraphicsApi.GenTexture();textures.Add(id);GraphicsApi.ActiveTexture(TextureUnit.Texture0);GraphicsApi.BindTexture(TextureTarget.Texture2D,id);
                    GraphicsApi.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Rgba8,w,h,0,PixelFormat.Rgba,PixelType.UnsignedByte,bytes);
                    GraphicsApi.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.Linear);
                    GraphicsApi.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)TextureMagFilter.Linear);
                    GraphicsApi.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapS,(int)TextureWrapMode.Repeat);
                    GraphicsApi.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapT,(int)TextureWrapMode.Repeat);return id;
                }
                int List(bool weighted,float u,float v,Vector3 normal)
                {
                    int id=GraphicsApi.GenLists(1);GraphicsApi.NewList(id,ListMode.Compile);GraphicsApi.Begin(PrimitiveType.Triangles);
                    foreach(var p in new[]{new Vector2(-.75f,-.75f),new Vector2(.75f,-.75f),new Vector2(.75f,.75f),new Vector2(-.75f,-.75f),new Vector2(.75f,.75f),new Vector2(-.75f,.75f)})
                    {if(weighted)GraphicsApi.Color4(.125f,.375f,0,0);GraphicsApi.Normal3(normal.X,normal.Y,normal.Z);GraphicsApi.TexCoord3(u,v,weighted?32:0);GraphicsApi.Vertex3(p.X,p.Y,0);}
                    GraphicsApi.End();GraphicsApi.EndList();return id;
                }
                void SetPalette(Matrix4 first,Matrix4 next)
                {
                    foreach(var pair in new[]{(Matrix:first,Offset:0),(Matrix:next,Offset:16)})for(int r=0;r<4;r++)for(int c=0;c<4;c++)item.MatrixStack[pair.Offset+r*4+c]=pair.Matrix[r,c];
                }
                void Clear(bool preserve=false)
                {
                    GraphicsApi.Viewport(0,0,size,size);GraphicsApi.Disable(EnableCap.ScissorTest);GraphicsApi.ColorMask(true,true,true,true);
                    bool mask=NativeGL.GetInteger(GetPName.DepthWritemask)!=0;GraphicsApi.DepthMask(true);GraphicsApi.ClearColor(0,0,0,1);
                    GraphicsApi.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);if(preserve)GraphicsApi.DepthMask(mask);
                }
                byte[] Read(){var b=new byte[size*size*4];GraphicsApi.ReadPixels(0,0,size,size,PixelFormat.Rgba,PixelType.UnsignedByte,b);return b;}
                CharacterSourceGxBinding Binding(Cmdl.Material material,NVector4 initialRegister1=default)
                {
                    var plan=CharacterSourceGxProgram.Compile(material,initialRegister1);string key=plan.Vertex120+plan.Fragment120;
                    if(!scene._sourceGxPrograms.TryGetValue(key,out var gpu))
                    {
                        int vs=0,fs=0,p=0;
                        try{vs=CompileSceneShader(ShaderType.VertexShader,plan.Vertex120,"invented-source-gx-vertex");fs=CompileSceneShader(ShaderType.FragmentShader,plan.Fragment120,"invented-source-gx-fragment");p=LinkSceneProgram(vs,fs,"invented-source-gx-program");gpu=new SourceGxGpuProgram(p);scene._sourceGxPrograms.Add(key,gpu);programs.Add(p);p=0;}
                        finally{if(p!=0)GraphicsApi.DeleteProgram(p);if(fs!=0)GraphicsApi.DeleteShader(fs);if(vs!=0)GraphicsApi.DeleteShader(vs);}
                    }
                    return new(scene,plan,gpu.Id,Array.AsReadOnly(material.TexIds.Select(t=>t==1?image:second).ToArray()));
                }
                void Draw(Cmdl.Material m,double seconds,NVector4 initialRegister1=default){item.SourceGx=new(Binding(m,initialRegister1),seconds);scene.RenderItem(item);}
                byte[] Picture(Cmdl.Material m,double seconds=0,NVector4 initialRegister1=default){Clear();Draw(m,seconds,initialRegister1);NativeGL.Finish();check(NativeGL.GetError()==ErrorCode.NoError,"source generated shader compiles/executes GL-clean");return Read();}
                void Pixel(byte[] p,int x,int y,int r,int g,int b,int alphaByte,string why,int tolerance=1)
                {int o=(y*size+x)*4;check(Math.Abs(p[o]-r)<=tolerance&&Math.Abs(p[o+1]-g)<=tolerance&&Math.Abs(p[o+2]-b)<=tolerance&&Math.Abs(p[o+3]-alphaByte)<=tolerance,why+$"; actual={p[o]},{p[o+1]},{p[o+2]},{p[o+3]} expected={r},{g},{b},{alphaByte}");}
                string State()
                {
                    int active=NativeGL.GetInteger(GetPName.ActiveTexture);var bindings=new List<int>();for(int i=0;i<4;i++){NativeGL.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0+i));bindings.Add(NativeGL.GetInteger(GetPName.TextureBinding2D));}NativeGL.ActiveTexture((TextureUnit)active);
                    return string.Join(",",bindings)+":"+string.Join(",",new[]{active,NativeGL.GetInteger(GetPName.ClientActiveTexture),NativeGL.GetInteger(GetPName.CurrentProgram),NativeGL.GetInteger(GetPName.DepthWritemask),NativeGL.GetInteger(GetPName.DepthFunc),NativeGL.GetInteger(GetPName.BlendSrcRgb),NativeGL.GetInteger(GetPName.BlendDstRgb),NativeGL.GetInteger(GetPName.BlendSrcAlpha),NativeGL.GetInteger(GetPName.BlendDstAlpha),NativeGL.GetInteger(GetPName.CullFaceMode),NativeGL.IsEnabled(EnableCap.Blend)?1:0,NativeGL.IsEnabled(EnableCap.CullFace)?1:0});
                }
            }
            finally
            {
                GraphicsApi.UseProgram(0);NativeGL.ClientActiveTexture((TextureUnit)outerClient);
                if(weightedList!=0)GraphicsApi.DeleteLists(weightedList,1);if(nativeList!=0)GraphicsApi.DeleteLists(nativeList,1);
                scene.UnloadGl();foreach(int t in textures)GraphicsApi.DeleteTexture(t);
                NativeGL.BindFramebuffer(FramebufferTarget.DrawFramebuffer,outerDraw);NativeGL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,outerRead);
                GraphicsApi.UseProgram(outerProgram);GraphicsApi.ActiveTexture((TextureUnit)outerUnit);
                Menu.SaveSlot=oldSlot;Menu.NeededSave=oldSave;
            }
        }
        private static Cmdl.Material Material(params Cmdl.TevStage[] stages)=>new(){Flags=Cmdl.DepthWrite,BlendSrc=1,BlendDst=0,TexIds=new uint[]{1},Channels=new uint[]{0x3000},Stages=stages,TexGens=new[]{new Cmdl.TexGen{Type=0,Src=4,Mtx=30,Post=61}}};
        private static Cmdl.TevStage Stage(int[] rgb,int[] alpha,uint color=0x100,uint a=0x100,int texture=0,int coordinate=0,int raster=255)=>new(){ColorIn=rgb,AlphaIn=alpha,RawColorOp=color,RawAlphaOp=a,ColorOp=Cmdl.TevOp.From(color),AlphaOp=Cmdl.TevOp.From(a),Tex=texture,TexCoord=coordinate,Ras=raster};
        private sealed class SourceGxAcceptanceServices:ISceneServices
        {
            public bool IsReplica=>true;public bool MayEndOnScore=>false;public bool ShouldLeaveAfterMatch=>false;public bool SuppressDamage=>true;public bool AllowsPresentationSideEffects=>false;public bool DisablePowerups=>true;public bool ReplicatesHealthSpawns=>false;
            public IPlayerReplicationHost PlayerReplication{get;}=new ReplayPlayerReplicationHost(new ReplayReplicaState());
            public MphRead.Mods.Multiplayer.MatchWorldProfile? NetworkWorldProfile=>null;
            public bool TryGetHealthSpawn(short id,out HealthSpawnState state){state=default;return false;}
        }
    }
}
#endif
