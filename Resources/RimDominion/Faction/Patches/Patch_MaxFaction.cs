using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace RimDominion
{
    [HarmonyPatch]
    static class Patch_MaxFactionLimit
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            var type = typeof(WorldFactionsUIUtility)
                .GetNestedType("<>c__DisplayClass8_0", System.Reflection.BindingFlags.NonPublic);

            return AccessTools.Method(type, "<DoWindowContents>g__CanAddFaction|1");
        }

        static void Postfix(ref AcceptanceReport __result)
        {
            if (!__result.Accepted)
            {
                __result = AcceptanceReport.WasAccepted;
            }
        }
    }
}