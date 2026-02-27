using HarmonyLib;
using Verse;

namespace RimDominion
{

    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        static HarmonyInit()
        {
            new Harmony("zaki.rimdominion.beta").PatchAll();
            Harmony.DEBUG = true;
        }
    }
}