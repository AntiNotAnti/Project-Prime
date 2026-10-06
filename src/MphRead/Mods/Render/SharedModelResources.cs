using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render
{
    /// <summary>Display lists live on shared model data. Leases prevent one scene
    /// from destroying a list another scene still draws. Called on the GL owner thread.</summary>
    internal static class SharedModelResources
    {
        private static readonly Dictionary<Model, int> Owners = new();
        internal static int RetainedModelCount { get { lock (Owners) return Owners.Count; } }
        internal static void Retain(Model model)
        {
            lock (Owners) Owners[model] = Owners.GetValueOrDefault(model) + 1;
        }
        internal static void Release(Model model, bool canReleaseNativeResources = true)
        {
            var deleted = new HashSet<int>();
            lock (Owners)
            {
                if (!Owners.TryGetValue(model, out int count)) return;
                if (count > 1) { Owners[model] = count - 1; return; }
                Owners.Remove(model);
                // Clear every handle before touching the driver. A failed delete
                // must not leave stale IDs or a permanent managed model root.
                foreach (Mesh mesh in model.Meshes)
                {
                    if (mesh.ListId != 0) deleted.Add(mesh.ListId);
                    mesh.ListId = 0;
                }
            }
            if (canReleaseNativeResources && RenderResourceLifetime.CanReleaseNativeInCurrentScope)
                foreach (int list in deleted) GL.DeleteLists(list, 1);
        }

        internal static void ReleaseAll(HashSet<Model>? leases, bool canReleaseNativeResources)
        {
            if (leases == null) return;
            foreach (Model model in leases.ToArray())
            {
                // Remove before native deletion: a failed delete must not let
                // the managed finally path decrement another scene's lease.
                leases.Remove(model);
                Release(model, canReleaseNativeResources);
            }
        }
    }
}
