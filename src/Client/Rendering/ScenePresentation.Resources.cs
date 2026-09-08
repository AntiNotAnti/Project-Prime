using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MphRead.Effects;
using MphRead.Formats;
namespace MphRead
{
    public partial class ScenePresentation
    {
        private sealed class Binding
        {
            public Binding() { }
            public int Id;
        }
        private readonly ConditionalWeakTable<object, Binding> _meshBindings = new();
        private readonly ConditionalWeakTable<Material, Binding> _materialBindings = new();
        private readonly ConditionalWeakTable<EffectElementEntry, List<int>> _effectBindings = new();
        public int GetMeshListId(Mesh mesh) => _meshBindings.GetOrCreateValue(mesh.GeometryIdentity).Id;
        public void SetMeshListId(Mesh mesh, int id) => _meshBindings.GetOrCreateValue(mesh.GeometryIdentity).Id = id;
        public int GetTextureBindingId(Material material) => _materialBindings.GetOrCreateValue(material).Id;
        public void SetTextureBindingId(Material material, int id) => _materialBindings.GetOrCreateValue(material).Id = id;
        internal List<int> GetEffectTextureBindings(EffectElementEntry element) => _effectBindings.GetOrCreateValue(element);
    }
}
