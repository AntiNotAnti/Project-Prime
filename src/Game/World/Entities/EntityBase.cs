using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using MphRead.Mods.Network;
using MphRead.Sound;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public abstract class EntityBase
    {
        public int Id { get; protected set; } = -1; // todo: use init for Id
        public virtual int Recolor { get; set; }
        public EntityType Type { get; }
        public bool ShouldDraw { get; protected set; } = true;
        public bool Initialized { get; set; } = true;
        public bool Active { get; protected set; } = true;
        public bool Hidden { get; set; }
        public float Alpha { get; set; } = 1.0f;

        protected internal Scene _scene;
        protected readonly string? _nodeName;
        public NodeRef NodeRef { get; set; } = NodeRef.None;
        protected int _scanId = 0;
        protected internal float _drawScale = 1;
        protected internal Matrix4 _transform = Matrix4.Identity;
        protected internal Vector3 _scale = new Vector3(1, 1, 1);
        protected Vector3 _rotation = Vector3.Zero;
        protected internal Vector3 _position = Vector3.Zero;

        protected Node? _colAttachNode = null;
        private bool _drawColUpdated = true;
        public EntityCollision?[] EntityCollision { get; } = new EntityCollision?[2];
        // todo: look into getting rid of this in favor of EntityCollision
        public Matrix4 CollisionTransform => _colAttachNode == null ? _transform : _colAttachNode.Animation;

        protected internal readonly SoundSource _soundSource;

        public Matrix4 Transform
        {
            get
            {
                return _transform;
            }
            set
            {
                if (_transform != value)
                {
                    _scale = value.ExtractScale();
                    value.ExtractRotation().ToEulerAngles(out _rotation);
                    _position = value.Row3.Xyz;
                    _transform = value;
                    _drawColUpdated = false;
                }
            }
        }

        public Vector3 Scale
        {
            get
            {
                return _scale;
            }
            set
            {
                if (_scale != value)
                {
                    _transform = Matrix4.CreateScale(value) * Matrix4.CreateRotationZ(Rotation.Z)
                        * Matrix4.CreateRotationY(Rotation.Y) * Matrix4.CreateRotationX(Rotation.X);
                    _transform.Row3.Xyz = Position;
                    _scale = value;
                    _drawColUpdated = false;
                }
            }
        }

        public Vector3 Rotation
        {
            get
            {
                return _rotation;
            }
            set
            {
                if (_rotation != value)
                {
                    _transform = Matrix4.CreateScale(Scale) * Matrix4.CreateRotationZ(value.Z)
                        * Matrix4.CreateRotationY(value.Y) * Matrix4.CreateRotationX(value.X);
                    _transform.Row3.Xyz = Position;
                    _rotation = value;
                    _drawColUpdated = false;
                }
            }
        }

        public Vector3 Position
        {
            get
            {
                return _position;
            }
            set
            {
                if (_position != value)
                {
                    _transform.Row3.Xyz = value;
                    _position = value;
                    _drawColUpdated = false;
                }
            }
        }

        public virtual Vector3 RightVector => Transform.Row0.Xyz.Normalized();
        public virtual Vector3 UpVector => Transform.Row1.Xyz.Normalized();
        public virtual Vector3 FacingVector => Transform.Row2.Xyz.Normalized();

        protected bool _anyLighting = false;
        internal         protected readonly List<ModelInstance> _models = new List<ModelInstance>();

        protected virtual bool UseNodeTransform => true;
        protected virtual Vector4? OverrideColor { get; } = null;
        internal         protected virtual Vector4? PaletteOverride { get; set; } = null;

        protected EntityBase(EntityType type, Scene scene)
        {
            Type = type;
            _scene = scene;
            _soundSource = new SoundSource(scene);
        }

        protected EntityBase(EntityType type, string nodeName, Scene scene)
        {
            Type = type;
            _scene = scene;
            _soundSource = new SoundSource(scene);
            _nodeName = nodeName;
        }

        protected EntityBase(EntityType type, NodeRef nodeRef, Scene scene)
        {
            Type = type;
            _scene = scene;
            _soundSource = new SoundSource(scene);
            NodeRef = nodeRef;
        }

        public virtual void Initialize()
        {
            _anyLighting |= _models.Any(n => n.Model.Materials.Any(m => m.Lighting != 0));
            if (_nodeName != null)
            {
                NodeRef = _scene.GetNodeRefByName(_nodeName);
            }
        }

        protected ModelInstance SetUpModel(string name, int animIndex = 0, AnimFlags animFlags = AnimFlags.None, bool firstHunt = false)
        {
            ModelInstance inst = Read.GetModelInstance(name, firstHunt);
            inst.SetAnimation(animIndex, animFlags);
            _models.Add(inst);
            return inst;
        }

        protected void SetCollision(CollisionInstance collision, int slot = 0, ModelInstance? attach = null)
        {
            var entCol = new EntityCollision(collision, this);
            SetCollisionMaxAvg(entCol);
            EntityCollision[slot] = entCol;
            _drawColUpdated = false;
            UpdateCollisionTransform(slot, Transform.ClearScale());
            UpdateLinkedInverse(slot);
            if (entCol.Collision != null)
            {
                for (int i = 0; i < entCol.Collision.Info.Points.Count; i++)
                {
                    entCol.DrawPoints.Add(entCol.Collision.Info.Points[i]);
                }
            }
            if (attach != null)
            {
                _colAttachNode = attach.Model.GetNodeByName("attach");
            }
        }

        private void SetCollisionMaxAvg(EntityCollision entCol)
        {
            if (entCol.Collision == null)
            {
                return;
            }
            int count = entCol.Collision.Info.Points.Count;
            Vector3 avg = Vector3.Zero;
            for (int i = 0; i < count; i++)
            {
                Vector3 point = entCol.Collision.Info.Points[i];
                avg.X += point.X;
                avg.Y += point.Y;
                avg.Z += point.Z;
            }
            avg /= count;
            entCol.InitialCenter = avg; // centroid
            float maxDist = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 point = entCol.Collision.Info.Points[i];
                float dist = Vector3.Distance(avg, point);
                if (dist > maxDist)
                {
                    maxDist = dist;
                }
            }
            entCol.MaxDistance = maxDist;
        }

        protected void UpdateCollisionTransform(int slot, Matrix4 transform)
        {
            EntityCollision? entCol = EntityCollision[slot];
            if (entCol != null)
            {
                entCol.Transform = transform;
                entCol.Inverse1 = transform.Inverted();
                entCol.CurrentCenter = Matrix.Vec3MultMtx4(entCol.InitialCenter, transform);
            }
        }

        protected void UpdateLinkedInverse(int slot)
        {
            EntityCollision? entCol = EntityCollision[slot];
            if (entCol != null)
            {
                entCol.Inverse2 = entCol.Transform.Inverted();
            }
        }

        protected internal void UpdateDrawCollision()
        {
            if (!_drawColUpdated || _colAttachNode != null)
            {
                Matrix4 transform = CollisionTransform;
                for (int i = 0; i < 2; i++)
                {
                    EntityCollision? entCol = EntityCollision[i];
                    if (entCol?.Collision != null)
                    {
                        CollisionInfo collision = entCol.Collision.Info;
                        for (int j = 0; j < collision.Points.Count; j++)
                        {
                            entCol.DrawPoints[j] = Matrix.Vec3MultMtx4(collision.Points[j], transform);
                        }
                    }
                }
                _drawColUpdated = true;
            }
        }

        public virtual void Destroy()
        {
        }

        protected internal virtual Matrix4 GetModelTransform(ModelInstance inst, int index)
        {
            return Matrix4.CreateScale(inst.Model.Scale) * _transform;
        }

        public virtual void GetPosition(out Vector3 position)
        {
            position = Position;
        }

        public virtual void GetVectors(out Vector3 position, out Vector3 up, out Vector3 facing)
        {
            position = Position;
            up = UpVector;
            facing = FacingVector;
        }

        public virtual bool GetTargetable()
        {
            return true;
        }

        public virtual int GetScanId(bool alternate = false)
        {
            return _scanId;
        }

        public virtual void OnScanned()
        {
        }

        public virtual bool Process()
        {
            if (Active)
            {
                for (int i = 0; i < _models.Count; i++)
                {
                    UpdateAnimFrames(_models[i]);
                }
            }
            return true;
        }

        protected void UpdateAnimFrames(ModelInstance inst)
        {
            if (_scene.FrameCount != 0 && _scene.FrameCount % 2 == 0) // todo: FPS stuff
            {
                inst.UpdateAnimFrames();
            }
        }

        protected internal virtual int GetModelRecolor(ModelInstance inst, int index)
        {
            return Recolor;
        }

        public IReadOnlyList<ModelInstance> GetModels()
        {
            return _models;
        }

        protected void AddPlaceholderModel()
        {
            ModelInstance inst = Read.GetModelInstance("pick_wpn_missile");
            inst.IsPlaceholder = true;
            _models.Add(inst);
        }
        protected internal virtual Vector4? GetOverrideColor(ModelInstance inst, int index)
        {
            return OverrideColor;
        }
        protected internal virtual LightInfo GetLightInfo()
        {
            return new LightInfo(_scene.Light1Vector, _scene.Light1Color, _scene.Light2Vector, _scene.Light2Color);
        }
        protected internal virtual void UpdateTransforms(ModelInstance inst, int index, bool transformRoomNodes = false)
        {
            Model model = inst.Model;
            model.AnimateMaterials(inst.AnimInfo);
            model.AnimateTextures(inst.AnimInfo);
            model.ComputeNodeMatrices(index: 0);
            Matrix4 transform = GetModelTransform(inst, index);
            model.AnimateNodes(index: 0, UseNodeTransform || transformRoomNodes, transform, model.Scale, inst.AnimInfo);
            model.UpdateMatrixStack();
            // todo: could skip this unless a relevant material property changed this update (and we're going to draw this entity)


        }
        protected internal void UpdateTransforms(ModelInstance inst, Matrix4 transform, int recolor)
        {
            Model model = inst.Model;
            model.AnimateMaterials(inst.AnimInfo);
            model.AnimateTextures(inst.AnimInfo);
            model.ComputeNodeMatrices(index: 0);
            model.AnimateNodes(index: 0, UseNodeTransform, transform, model.Scale, inst.AnimInfo);
            model.UpdateMatrixStack();

        }

        protected internal void UpdateMaterials(ModelInstance inst, int recolor)
        {
            Model model = inst.Model;
            model.AnimateMaterials(inst.AnimInfo);
            model.AnimateTextures(inst.AnimInfo);

        }

        protected void UpdateNodeRefVolume()
        {
            _soundSource.Volume = IsAudible(NodeRef) ? 1 : 0;
        }

        protected internal bool IsAudible(NodeRef nodeRef) => _scene.IsEntityAudible(nodeRef);
        protected internal bool IsVisible(NodeRef nodeRef) => _scene.IsEntityVisible(nodeRef);

        protected void SetTransform(Vector3Fx facing, Vector3Fx up, Vector3Fx position)
        {
            SetTransform(facing.ToFloatVector(), up.ToFloatVector(), position.ToFloatVector());
        }

        protected void SetTransform(Vector3 facing, Vector3 up, Vector3 position)
        {
            Matrix4 transform = GetTransformMatrix(facing, up);
            //transform.ExtractRotation().ToEulerAngles(out Vector3 rotation);
            //Rotation = rotation;
            //Position = position;
            transform = Matrix4.CreateScale(_scale) * transform;
            transform.Row3.Xyz = position;
            Transform = transform;
        }

        public static Matrix4 GetTransformMatrix(Vector3 facing, Vector3 up)
        {
            Vector3 right = Vector3.Cross(up, facing).Normalized();
            up = Vector3.Cross(facing, right);
            Matrix4 transform = default;
            transform.M11 = right.X;
            transform.M12 = right.Y;
            transform.M13 = right.Z;
            transform.M14 = 0;
            transform.M21 = up.X;
            transform.M22 = up.Y;
            transform.M23 = up.Z;
            transform.M24 = 0;
            transform.M31 = facing.X;
            transform.M32 = facing.Y;
            transform.M33 = facing.Z;
            transform.M34 = 0;
            transform.M41 = 0;
            transform.M42 = 0;
            transform.M43 = 0;
            transform.M44 = 1;
            return transform;
        }

        public static Matrix4 GetTransformMatrix(Vector3 facing, Vector3 up, Vector3 position)
        {
            Vector3 right = Vector3.Cross(up, facing).Normalized();
            up = Vector3.Cross(facing, right);
            Matrix4 transform = default;
            transform.M11 = right.X;
            transform.M12 = right.Y;
            transform.M13 = right.Z;
            transform.M14 = 0;
            transform.M21 = up.X;
            transform.M22 = up.Y;
            transform.M23 = up.Z;
            transform.M24 = 0;
            transform.M31 = facing.X;
            transform.M32 = facing.Y;
            transform.M33 = facing.Z;
            transform.M34 = 0;
            transform.M41 = position.X;
            transform.M42 = position.Y;
            transform.M43 = position.Z;
            transform.M44 = 1;
            return transform;
        }

        public virtual void SetActive(bool active)
        {
            Active = active;
        }

        public virtual void SetScanId(int scanId)
        {
            _scanId = scanId;
        }

        // todo: item and enemy spawners
        public virtual EntityBase? GetParent()
        {
            return null;
        }

        public virtual EntityBase? GetChild()
        {
            return null;
        }

        public virtual void HandleMessage(MessageInfo info)
        {
        }

        public virtual void CheckContactDamage(ref DamageResult result)
        {
        }

        public virtual void CheckBeamReflection(ref bool result)
        {
        }

        protected (float, float) ConstantAcceleration(float step, float velocity,
            float minVelocity = Single.MinValue, float maxVelocity = Single.MaxValue)
        {
            float newVelocity = velocity + step * 30 * 31 * _scene.FrameTime;
            newVelocity = Math.Clamp(newVelocity, minVelocity, maxVelocity);
            float displacement = velocity * _scene.FrameTime + (newVelocity - velocity) / 2 * _scene.FrameTime;
            return (newVelocity, displacement);
        }

        protected (float, float) Drag(float step, float velocity)
        {
            float decay = MathF.Pow(step, 30);
            float newVelocity = velocity * MathF.Pow(decay, _scene.FrameTime);
            float displacement = (newVelocity - velocity) / MathF.Log(decay);
            return (newVelocity, displacement);
        }

        protected internal float ExponentialDecay(float step, float value)
        {
            float decay = MathF.Pow(step, 30);
            return value * MathF.Pow(decay, _scene.FrameTime);
        }
    }

    public class ModelEntity : EntityBase
    {
        public ModelEntity(ModelInstance model, Scene scene, int recolor = 0) : base(EntityType.Model, scene)
        {
            Recolor = recolor;
            _models.Add(model);
            model.SetAnimation(0);
        }
    }
}
