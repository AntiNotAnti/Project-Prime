using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
namespace MphRead.Mods.Replay;
internal static partial class ReplayAssetCheckpoint
{
    // Node.Transform is rebuilt by Model.ComputeNodeMatrices (Model.cs529/533);
    // Node.Animation and matrix-stack values by EntityBase.UpdateTransforms (529-536).
    // the mutable registry stores whichever instance was last submitted, not actor state.
    // Material.RenderMode is rewritten by Renderer.UpdateMaterials for each submission.
    internal static Dictionary<string,byte[]> CanonicalStateWithoutDrawCaches(byte[] bytes)
    {
        var parts=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        using var stream=new MemoryStream(bytes,false);using var reader=new BinaryReader(stream);
        if(reader.ReadUInt16()!=1)throw new InvalidDataException("Unsupported comparison asset layout.");
        int count=Count();parts["ModelCount"]=BitConverter.GetBytes(count);
        for(int i=0;i<count;i++)
        {
            string name=reader.ReadString();bool first=reader.ReadBoolean();string path=name+":"+first;
            int nodes=Count();parts[path+".NodeCount"]=BitConverter.GetBytes(nodes);
            for(int n=0;n<nodes;n++)
            {
                Fields(path+".Nodes["+n+"]",NodeFields,true,false);
                parts[path+".Bounds["+n+"]"]=reader.ReadBytes(24);
            }
            int meshes=Count();parts[path+".MeshCount"]=BitConverter.GetBytes(meshes);
            for(int n=0;n<meshes;n++)Fields(path+".Meshes["+n+"]",MeshFields,false,false);
            int materials=Count();parts[path+".MaterialCount"]=BitConverter.GetBytes(materials);
            for(int n=0;n<materials;n++)Fields(path+".Materials["+n+"]",MaterialFields,false,true);
            int matrices=Count();parts[path+".MatrixStackCount"]=BitConverter.GetBytes(matrices);
            if(reader.ReadBytes(checked(matrices*4)).Length!=matrices*4)throw new EndOfStreamException();
        }
        parts["RoomActivation"]=reader.ReadBytes(checked((int)(stream.Length-stream.Position)));
        return parts;
        int Count(){int value=reader.ReadInt32();if(value<0||value>262144)throw new InvalidDataException("Asset comparison exceeds its bound.");return value;}
        void Fields(string path,PropertyInfo[] properties,bool node,bool material)
        {
            foreach(var property in properties)
            {
                long at=stream.Position;_ = Read(reader,property.PropertyType);long end=stream.Position;
                if(node&&property.Name is "Transform" or "Animation"||material&&property.Name=="RenderMode")continue;
                stream.Position=at;parts[path+"."+property.Name]=reader.ReadBytes(checked((int)(end-at)));stream.Position=end;
            }
        }
    }
}
