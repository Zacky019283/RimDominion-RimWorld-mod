using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;
using System.Collections.Generic;

namespace RimDominion
{
    [HarmonyPatch(typeof(Faction), "Color", MethodType.Getter)]
    public static class FactionColorPatch
    {
        private static Dictionary<Faction, Color> cachedColors = new Dictionary<Faction, Color>();

        public static bool Prefix(Faction __instance, ref Color __result)
        {
            if (__instance.IsPlayer)
                return true;

            if (!cachedColors.TryGetValue(__instance, out Color color))
            {
                float r = Rand.Range(0f, 0.4f);
                float g = Rand.Range(0f, 0.4f);
                float b = Rand.Range(0f, 0.4f);
                color = new Color(r, g, b);
                cachedColors[__instance] = color;
            }

            __result = color;
            return false;
        }
    }
}