using System;

namespace MphRead.Mods.Render;

/// <summary>Both CPU ownership and graphics ownership must be released after a failure.</summary>
internal static class OwnerCleanup
{
    internal static void Release(Action cleanup, Action graphics, Action<Exception> report)
    {
        try { cleanup(); }
        catch (Exception ex) { try { report(ex); } catch { } }
        finally
        {
            try { graphics(); }
            catch (Exception ex) { try { report(ex); } catch { } }
        }
    }
}
