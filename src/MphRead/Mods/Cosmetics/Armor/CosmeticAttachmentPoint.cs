using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
namespace MphRead.Mods.Cosmetics.Armor
{
    public readonly record struct CosmeticAttachmentPoint(string Name, int NodeIndex);
    public static class CosmeticAttachments
    {
        private static readonly ConditionalWeakTable<Model, CosmeticAttachmentPoint[]> Cache = new();
        public static CosmeticAttachmentPoint[] For(Model model) => Cache.GetValue(model, Build);
        private static CosmeticAttachmentPoint[] Build(Model model)
        {
            string[] names = { "head", "chest", "shoulder", "gun", "hand", "hip", "leg", "foot" };
            var points = new List<CosmeticAttachmentPoint>();
            for (int i = 1; i < model.Nodes.Count; i++)
                foreach (string name in names)
                    if (model.Nodes[i].Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                    { points.Add(new(name, i)); break; }
            if (points.Count == 0)
                for (int i = 1; i < model.Nodes.Count; i += Math.Max(1, model.Nodes.Count / 10))
                    points.Add(new(model.Nodes[i].Name, i));
            return points.ToArray();
        }
    }
}
