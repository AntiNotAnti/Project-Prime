using System;
using MphRead.Export;

namespace MphRead
{
    public static class AssetTools
    {


        public static void ReadAndExport(string name, bool firstHunt = false, MetaDir dir = MetaDir.Models)
        {
            Model? model = Read.GetModelInstanceOrNull(name, firstHunt, dir, noCache: true)?.Model;
            if (model == null)
            {
                model = Read.GetRoomModelInstanceOrNull(name)?.Model;
                if (model == null)
                {
                    Console.WriteLine($"No model or room with the name {name} could be found.");
                    return;
                }
            }
            try
            {
                Images.ExportImages(model);
                Collada.ExportModel(model);
                Console.WriteLine("Exported successfully.");
            }
            catch
            {
                Console.WriteLine("Failed to export model. Verify your export path is accessible.");
            }
        }

    }
}
