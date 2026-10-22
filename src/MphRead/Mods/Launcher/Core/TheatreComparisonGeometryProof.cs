#if !MPHREAD_SERVER
namespace MphRead.Mods.Launcher.Core;
/// <summary>Metadata zero is absence of evidence; exact resolved geometry must still prove both loaded worlds.</summary>
public static class TheatreComparisonGeometryProof
{
    public static bool IsExact(ulong resolved,ulong loadedA,ulong loadedB,ulong recordedA,ulong recordedB)
        =>resolved!=0&&loadedA==resolved&&loadedB==resolved
            &&(recordedA==0||recordedA==resolved)&&(recordedB==0||recordedB==resolved);
}
#endif
