using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MyMod
{
    using System.Collections.Generic;
    using HarmonyLib;
    using UnityEngine;

    [HarmonyPatch(typeof(Faction), "Color", MethodType.Getter)]
    public static class FactionColorPatch
    {
        // Dictionary simpen warna per instance Faction
        private static Dictionary<Faction, Color> cachedColors = new Dictionary<Faction, Color>();

        public static bool Prefix(Faction __instance, ref Color __result)
        {
            if (!cachedColors.TryGetValue(__instance, out Color color))
            {
                float r = Rand.Range(0f, 0.4f);
                float g = Rand.Range(0f, 0.4f);
                float b = Rand.Range(0f, 0.4f);
                color = new Color(r, g, b);
                cachedColors[__instance] = color; // simpen supaya konsisten
            }

            __result = color; // return warna cached
            return false; // skip getter asli
        }
    }
}