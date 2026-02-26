using HarmonyLib;
using RimWorld.Planet;
using Verse;
using RimWorld;

namespace MyMod
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
            // kalau ditolak karena max vanilla, izinkan sampai 30
            if (!__result.Accepted)
            {
                __result = AcceptanceReport.WasAccepted;
            }
        }
    }
}