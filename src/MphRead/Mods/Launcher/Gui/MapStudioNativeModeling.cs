using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    internal sealed partial class NativeStudioEditorSession : INativeStudioEditorModeling, INativeStudioEditorModelingViewport
    {
        private long _modelConfiguration=1;
        private bool _modelHooked;
        private readonly NativeStudioGestureFence _modelGesture=new();
        private NativeStudioModelingSnapshot? _modelSnapshot;
        private MapViewportLayout _modelDrawLayout;
        private int _modelFrameWidth,_modelFrameHeight;
        private Vector3 _modelDrawCamera,_modelDrawTarget;
        private bool _modelDrawPerspective,_modelDrawValid;
        private void RecordModelingDraw(bool drawn,int frameWidth,int frameHeight)
        {
            _modelDrawValid=drawn;
            if(!drawn){CancelModeling();return;}
            _modelDrawLayout=_layout;_modelFrameWidth=frameWidth;_modelFrameHeight=frameHeight;
            _modelDrawCamera=_viewport.CameraPosition;_modelDrawTarget=_viewport.CameraTarget;_modelDrawPerspective=_viewport.View=="Perspective";
        }
        private MapMesh? _topologyMesh;
        private ulong _topologyState;
        private string _topology="";
        private void EnsureModeling()
        {
            if(_modelHooked||!Alive)return;
            _modelHooked=true;_viewport.SelectionChanged+=ModelSelectionChanged;
        }
        private void ModelSelectionChanged(){_viewport.NativeOverlaySelectionChanged();_revision++;_snapshot=null;_resources=null;_modelSnapshot=null;}
        private void ReleaseModeling()
        {
            CancelModeling();
            if(_modelHooked){_viewport.SelectionChanged-=ModelSelectionChanged;_modelHooked=false;}
        }
        private MapMesh? ModelMesh=>MapObjects.Find(_document.Project.Definition,_document.ActiveObjectId)?.Value as MapMesh;
        private string ModelTopology(MapMesh? mesh)
        {
            if(mesh==null)return "";
            if(_topologyMesh!=mesh||_topologyState!=_document.CurrentStateId.Value)
            {_topologyMesh=mesh;_topologyState=_document.CurrentStateId.Value;_topology=MapStudioModelingCommands.TopologySignature(mesh);}
            return _topology;
        }
        private string Elements=>string.Join(",",_viewport.SubSelection.Faces.Order())+"|"+
            string.Join(",",_viewport.SubSelection.Edges.OrderBy(e=>e.A).ThenBy(e=>e.B).Select(e=>e.A+":"+e.B))+"|"+
            string.Join(",",_viewport.SubSelection.Vertices.Order())+"|"+_viewport.SubSelection.ActiveFace+"|"+
            _viewport.SubSelection.ActiveVertex+"|"+_viewport.SubSelection.ActiveEdge;
        private NativeStudioModelingTicket ModelingTicket=>new(Ticket,_modelConfiguration,ModelMesh?.Id??Guid.Empty,ModelTopology(ModelMesh),Elements);
        private static T ModelEnum<T>(string value,T fallback) where T:struct,Enum=>Enum.TryParse<T>(value,out var parsed)&&Enum.IsDefined(parsed)?parsed:fallback;
        public NativeStudioModelingSnapshot CaptureModeling()
        {
            CheckThread();EnsureModeling();var ticket=ModelingTicket;
            if(_modelSnapshot is {} cached&&cached.Ticket==ticket&&cached.Alive==Alive&&cached.GestureActive==(_modelGesture.Active!=null))return cached;
            var mesh=ModelMesh;var rows=new List<NativeStudioComponentRow>();var sub=_viewport.SubSelection;
            if(mesh!=null&&_viewport.ElementMode=="Face")rows.AddRange(mesh.Faces.Select((f,i)=>new NativeStudioComponentRow(i,-1,-1,"FACE "+i+" // "+f.Length+" CORNERS",sub.ObjectId==mesh.Id&&sub.Faces.Contains(i))));
            else if(mesh!=null&&_viewport.ElementMode=="Edge")rows.AddRange(new MapMeshTopology(mesh).Edges.OrderBy(e=>e.A).ThenBy(e=>e.B).Select((e,i)=>new NativeStudioComponentRow(i,e.A,e.B,"EDGE "+e.A+" - "+e.B,sub.ObjectId==mesh.Id&&sub.Edges.Contains(e))));
            else if(mesh!=null&&_viewport.ElementMode=="Vertex")rows.AddRange(mesh.Vertices.Select((v,i)=>new NativeStudioComponentRow(i,-1,-1,"VERTEX "+i,sub.ObjectId==mesh.Id&&sub.Vertices.Contains(i))));
            return _modelSnapshot=new(_revision,ticket,Alive,ModelEnum(_viewport.ElementMode,NativeStudioElementMode.Object),ModelEnum(_viewport.Tool,NativeStudioTransformTool.Move),
                ModelEnum(_viewport.Axes,NativeStudioAxes.Free),_nativeSelection,ModelEnum(_viewport.PivotMode,NativeStudioPivot.Individual),_viewport.LocalAxes,
                _viewport.Snap,_viewport.AngleSnap,_viewport.ScaleSnap,_viewport.CursorPivot,Array.AsReadOnly(rows.ToArray()),
                rows.Count(r=>r.Selected)+" / "+rows.Count+" COMPONENTS SELECTED",_modelGesture.Active!=null,_viewport.UvChecker,
                Alive?"NATIVE MODELING // SAME DOCUMENT, PREVIEW AND UNDO OWNER":"EDITOR LEASE RETIRED");
        }
        private NativeStudioSelectionMode _nativeSelection;
        private bool ModelCurrent(NativeStudioModelingTicket ticket)=>Current(ticket.Owner)&&ticket==ModelingTicket
            &&(ModelMesh is not {} mesh||MapStudioModelingCommands.TopologySignature(mesh)==ticket.Topology);
        private void ModelingChanged()
        {
            _modelConfiguration++;_modelSnapshot=null;_snapshot=null;_resources=null;
        }
        public NativeStudioResult ConfigureModeling(NativeStudioModelingTicket ticket,NativeStudioElementMode? mode=null,
            NativeStudioTransformTool? tool=null,NativeStudioAxes? axes=null,bool? local=null,NativeStudioSelectionMode? selection=null,
            NativeStudioPivot? pivot=null,float? gridSnap=null,float? angleSnap=null,float? scaleSnap=null,Vector3? cursor=null)
        {
            CheckThread();if(!ModelCurrent(ticket))return Stale();
            if(mode is {} m&&!Enum.IsDefined(m)||tool is {} t&&!Enum.IsDefined(t)||axes is {} a&&!Enum.IsDefined(a)
                ||selection is {} s&&!Enum.IsDefined(s)||pivot is {} p&&!Enum.IsDefined(p)
                ||gridSnap is {} g&&(!float.IsFinite(g)||g<0||g>100000)||angleSnap is {} n&&(!float.IsFinite(n)||n<0||n>180)
                ||scaleSnap is {} sc&&(!float.IsFinite(sc)||sc<0||sc>1000)||cursor is {} c&&(!ModelFinite(c)||Math.Abs(c.X)>1000000||Math.Abs(c.Y)>1000000||Math.Abs(c.Z)>1000000))
                return new(false,"MODEL SETTINGS REQUIRE FINITE VALUES WITHIN THE SHOWN BOUNDS");
            CancelModeling();
            if(mode is {} newMode&&_viewport.ElementMode!=newMode.ToString()){_viewport.ElementMode=newMode.ToString();_viewport.ClearSubSelection();}
            if(tool is {} newTool)_viewport.Tool=newTool.ToString();if(axes is {} newAxes)_viewport.Axes=newAxes.ToString();
            if(local is {} newLocal)_viewport.LocalAxes=newLocal;if(selection is {} newSelection)_nativeSelection=newSelection;
            if(pivot is {} newPivot)_viewport.PivotMode=newPivot.ToString();if(gridSnap is {} newGrid)_viewport.Snap=newGrid;
            if(angleSnap is {} newAngle)_viewport.AngleSnap=newAngle;if(scaleSnap is {} newScale)_viewport.ScaleSnap=newScale;
            if(cursor is {} newCursor)_viewport.CursorPivot=newCursor;ModelingChanged();return new(true,"MODELING SETTINGS APPLIED // NO DOCUMENT HISTORY CHANGE");
        }
        public NativeStudioResult SelectComponent(NativeStudioModelingTicket ticket,NativeStudioComponentRow component)
        {
            CheckThread();if(!ModelCurrent(ticket)||ModelMesh is not {Locked:false} mesh)return Stale();
            var row=CaptureModeling().Components.FirstOrDefault(r=>r.Index==component.Index);
            if(row!=component)return Stale();CancelModeling();var sub=_viewport.SubSelection;
            if(_nativeSelection==NativeStudioSelectionMode.Replace)sub.Clear();sub.Bind(mesh.Id);
            bool toggle=_nativeSelection==NativeStudioSelectionMode.Toggle;
            if(_viewport.ElementMode=="Face"){if(!toggle||!sub.Faces.Remove(row.Index))sub.Faces.Add(row.Index);sub.ActiveFace=row.Index;}
            else if(_viewport.ElementMode=="Vertex"){if(!toggle||!sub.Vertices.Remove(row.Index))sub.Vertices.Add(row.Index);sub.ActiveVertex=row.Index;}
            else if(_viewport.ElementMode=="Edge"){var edge=new MapEdge(row.A,row.B);if(!toggle||!sub.Edges.Remove(edge))sub.Edges.Add(edge);sub.ActiveEdge=edge;}
            else return new(false,"CHOOSE FACE, EDGE OR VERTEX MODE FIRST");
            sub.Validate(mesh);ModelSelectionChanged();return new(true,"COMPONENT SELECTION UPDATED");
        }
        public NativeStudioResult ExecuteModeling(NativeStudioModelingTicket ticket,NativeStudioModelingOperation operation,NativeStudioModelingValues values)
        {
            CheckThread();if(!ModelCurrent(ticket)||!Enum.IsDefined(operation))return Stale();CancelModeling();
            try
            {
                var mesh=ModelMesh;var sub=_viewport.SubSelection;string mode=_viewport.ElementMode;
                if(operation==NativeStudioModelingOperation.ConvertMesh){MapStudioModelingCommands.ConvertSelection(_document);_topologyMesh=null;ModelingChanged();return new(true,"AUTHORED PRIMITIVE CONVERTED // UNDO RETAINS THE ORIGINAL");}
                if(operation==NativeStudioModelingOperation.ToggleChecker){_viewport.UvChecker=!_viewport.UvChecker;_viewport.RefreshMaterialPreview();ModelingChanged();return new(true,"UV CHECKER PREVIEW UPDATED");}
                if(operation==NativeStudioModelingOperation.ApplyTransform)
                {
                    if(mode=="Object")
                    {
                        if(!ModelFinite(new(values.X,values.Y,values.Z))||Math.Abs(values.X)>1000000||Math.Abs(values.Y)>1000000||Math.Abs(values.Z)>1000000
                            ||!float.IsFinite(values.Angle)||Math.Abs(values.Angle)>3600||!float.IsFinite(values.Scale)||values.Scale is <.001f or >1000)
                            return new(false,"TRANSFORM VALUES ARE OUTSIDE SUPPORTED FINITE BOUNDS");
                        var move=new Vector3(values.X,values.Y,values.Z)*ModelScaleAxes;var ids=_document.Selection.ToArray();if(ids.Length==0||ids.Any(id=>MapObjects.Find(_document.Project.Definition,id)?.Value is MapGeometry{Locked:true}))return new(false,"SELECT UNLOCKED OBJECTS FIRST");
                        _document.TransformSelection(ids,_viewport.Tool,_viewport.Tool=="Move"?move:Vector3.Zero,
                            _viewport.Tool=="Rotate"?values.Angle:0,_viewport.Tool=="Scale"?values.Scale:1,_viewport.LocalAxes,
                            rotationAxis:ModelTurnAxis,scaleAxes:ModelScaleAxes,pivot:ModelPivot);
                    }
                    else
                    {
                        if(mesh is not {Locked:false}||sub.ObjectId!=mesh.Id||sub.VertexIndices(mesh,mode).Length==0)
                            return new(false,"SELECT COMPONENTS ON THE CURRENT UNLOCKED MESH FIRST");
                        MapStudioModelingCommands.TransformElements(_document,sub,mode,_viewport.Tool,new Vector3(values.X,values.Y,values.Z)*ModelScaleAxes,values.Angle,values.Scale,
                            _viewport.LocalAxes,ModelTurnAxis,ModelScaleAxes,_viewport.PivotMode,_viewport.CursorPivot);
                    }
                }
                else
                {
                    if(mesh is not {Locked:false}||mode=="Object")return new(false,"SELECT AN UNLOCKED MESH AND COMPONENT MODE FIRST");
                    sub.Bind(mesh.Id);sub.Validate(mesh);
                    switch(operation)
                    {
                        case NativeStudioModelingOperation.SelectAll:sub.SelectAll(mesh,mode);break;
                        case NativeStudioModelingOperation.Clear:sub.Clear();break;
                        case NativeStudioModelingOperation.Linked:sub.Linked(mesh,mode);break;
                        case NativeStudioModelingOperation.Grow:sub.Resize(mesh,mode,true);break;
                        case NativeStudioModelingOperation.Shrink:sub.Resize(mesh,mode,false);break;
                        case NativeStudioModelingOperation.Boundary:sub.Edges.UnionWith(new MapMeshTopology(mesh).BoundaryEdges());_viewport.ElementMode="Edge";break;
                        case NativeStudioModelingOperation.CopyUv:RequireFaces();_owner._copiedFaceUv=MapStudioModelingCommands.CopyUv(_document,mesh.Id,sub.Faces);break;
                        default:
                            RequireFaces();MapStudioUvOperation uv=operation switch{
                                NativeStudioModelingOperation.ApplyUv=>MapStudioUvOperation.Transform,NativeStudioModelingOperation.FitUv=>MapStudioUvOperation.Fit,
                                NativeStudioModelingOperation.MatchDensity=>MapStudioUvOperation.Density,NativeStudioModelingOperation.PasteUv=>MapStudioUvOperation.Paste,
                                NativeStudioModelingOperation.RotateUvPositive=>MapStudioUvOperation.RotatePositive,NativeStudioModelingOperation.RotateUvNegative=>MapStudioUvOperation.RotateNegative,
                                NativeStudioModelingOperation.FlipU=>MapStudioUvOperation.FlipU,NativeStudioModelingOperation.FlipV=>MapStudioUvOperation.FlipV,
                                NativeStudioModelingOperation.ExplicitUv=>MapStudioUvOperation.Explicit,NativeStudioModelingOperation.ProjectedUv=>MapStudioUvOperation.Projected,
                                _=>throw new InvalidOperationException("UNKNOWN MODELING COMMAND")};
                            MapStudioModelingCommands.EditUv(_document,mesh.Id,sub.Faces,uv,new(values.Scale,values.ScaleV,values.OffsetU,values.OffsetV,values.Angle,values.Density),_owner._copiedFaceUv);break;
                    }
                    void RequireFaces(){if(mode!="Face"||sub.Faces.Count==0)throw new InvalidOperationException("SELECT MESH FACES BEFORE EDITING UVS");}
                }
                ModelSelectionChanged();return new(true,"MODELING COMMAND COMPLETE // EXISTING HISTORY RETAINED");
            }
            catch(Exception error) when(error is not OutOfMemoryException and not StackOverflowException)
            {return new(false,error is InvalidOperationException or ArgumentException?error.Message:"MODELING COMMAND FAILED // CURRENT DOCUMENT RETAINED");}
        }
        private Vector3 ModelTurnAxis=>_viewport.Axes=="X"?Vector3.UnitX:_viewport.Axes=="Z"?Vector3.UnitZ:Vector3.UnitY;
        private Vector3 ModelScaleAxes=>_viewport.Axes=="Free"?Vector3.One:new(_viewport.Axes.Contains('X')?1:0,_viewport.Axes.Contains('Y')?1:0,_viewport.Axes.Contains('Z')?1:0);
        private Vector3? ModelPivot=>_viewport.PivotMode switch{
            "World"=>Vector3.Zero,"Cursor"=>_viewport.CursorPivot,"Active"=>MapObjects.Find(_document.Project.Definition,_document.ActiveObjectId) is {} active?new(active.Position[0],active.Position[1],active.Position[2]):null,
            "Center"=>_document.Selection.Select(id=>MapObjects.Find(_document.Project.Definition,id)).Where(o=>o!=null).Select(o=>new Vector3(o!.Position[0],o.Position[1],o.Position[2])).DefaultIfEmpty().Aggregate(Vector3.Zero,(a,b)=>a+b)/Math.Max(1,_document.Selection.Count),_=>null};
        private static bool ModelFinite(Vector3 value)=>float.IsFinite(value.X)&&float.IsFinite(value.Y)&&float.IsFinite(value.Z);
        public NativeStudioViewportBasis? CaptureModelingBasis(int frameWidth,int frameHeight,int clientWidth,int clientHeight)
        {
            CheckThread();if(!Alive||!_modelDrawValid||_renderUnavailable||!_modelDrawLayout.IsValid||frameWidth!=_modelFrameWidth||frameHeight!=_modelFrameHeight
                ||_viewport.CameraPosition!=_modelDrawCamera||_viewport.CameraTarget!=_modelDrawTarget||(_viewport.View=="Perspective")!=_modelDrawPerspective)return null;
            var basis=new NativeStudioViewportBasis(frameWidth,frameHeight,clientWidth,clientHeight,_modelDrawLayout.PixelX,_modelDrawLayout.PixelY,_modelDrawLayout.PixelWidth,_modelDrawLayout.PixelHeight,_modelDrawCamera,_modelDrawTarget,_modelDrawPerspective);
            return basis.IsValid?basis:null;
        }
        private bool ModelBasisCurrent(NativeStudioViewportBasis basis)=>CaptureModelingBasis(basis.FrameWidth,basis.FrameHeight,basis.ClientWidth,basis.ClientHeight)==basis;
        public bool BeginModelingPointer(NativeStudioModelingTicket ticket,NativeStudioViewportBasis basis,double x,double y)
        {
            CheckThread();if(!ModelCurrent(ticket)||!ModelBasisCurrent(basis)||!basis.Contains(x,y)||_modelGesture.Active!=null)return false;
            var local=basis.Local(x,y);
            if(!_viewport.NativeBeginTransform(_layout,local.X,local.Y,basis.FrameWidth/(double)basis.ClientWidth,basis.FrameHeight/(double)basis.ClientHeight))return false;
            if(_modelGesture.Begin(ticket,basis,x,y,out _)){_modelSnapshot=null;return true;}_viewport.CancelInteraction();return false;
        }
        public bool PickModelingPointer(NativeStudioModelingTicket ticket,NativeStudioViewportBasis basis,double x,double y)
        {
            CheckThread();if(!ModelCurrent(ticket)||!ModelBasisCurrent(basis)||!basis.Contains(x,y)||_viewport.ElementMode=="Object")return false;
            var local=basis.Local(x,y);return _viewport.NativePickComponent(_layout,local.X,local.Y,_nativeSelection==NativeStudioSelectionMode.Add,_nativeSelection==NativeStudioSelectionMode.Toggle,basis.FrameWidth/(double)basis.ClientWidth,basis.FrameHeight/(double)basis.ClientHeight);
        }
        public bool MoveModelingPointer(NativeStudioViewportBasis basis,double x,double y)
        {
            CheckThread();if(!Alive)return false;
            if(_modelGesture.Active is not {} active)return false;
            if(!ModelCurrent(active.Ticket)||!_viewport.NativeTransformActive||!ModelBasisCurrent(basis)||!_modelGesture.Move(active.Id,ModelingTicket,basis,x,y,out var delta)){CancelModeling();return false;}
            _viewport.NativeUpdateTransform(delta.X,delta.Y);return true;
        }
        public NativeStudioResult EndModelingPointer(NativeStudioViewportBasis basis,double x,double y)
        {
            CheckThread();if(!Alive||_modelGesture.Active is not {} active)return Stale();
            if(!ModelCurrent(active.Ticket)||!_viewport.NativeTransformActive||!ModelBasisCurrent(basis)||_modelGesture.Finish(active.Id,ModelingTicket,basis,x,y)==null){CancelModeling();return Stale();}
            try{_viewport.NativeUpdateTransform(x-active.StartX,y-active.StartY);_viewport.NativeCommitTransform();_modelSnapshot=null;return new(true,"TRANSFORM COMMITTED // ONE UNDO COMMAND");}
            catch(Exception error) when(error is not OutOfMemoryException and not StackOverflowException){return new(false,error.Message);}
            finally{CancelModeling();}
        }
        public void ValidateModelingInput(NativeStudioViewportBasis? basis)
        {
            CheckThread();if(!Alive)return;
            if(_modelGesture.Active is {} active&&(!Current(active.Ticket.Owner)||!_viewport.NativeTransformActive||basis is not {} current
                ||!ModelBasisCurrent(current)||!_modelGesture.Validate(active.Id,ModelingTicket,current)))CancelModeling();
        }
        private void CancelModeling()
        {
            if(_modelGesture.Active is {} active)_modelGesture.Cancel(active.Id);
            if(Alive)_viewport.CancelInteraction();_modelSnapshot=null;
        }
    }
}
