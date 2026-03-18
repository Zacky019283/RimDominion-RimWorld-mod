using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;
using System.Collections.Generic;
using RimWorld.Planet;

namespace RimDominion
{
    public class FactionColorWorldComp : WorldComponent
    {
        public Dictionary<int, Color> savedColors = new Dictionary<int, Color>();
        public FactionColorWorldComp(World world) : base(world) { }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref savedColors, "factionColors", LookMode.Value, LookMode.Value);
        }
    }

    [HarmonyPatch(typeof(Faction), "Color", MethodType.Getter)]
    public static class FactionColorPatch
    {
        public static bool Prefix(Faction __instance, ref Color __result)
        {
            if (__instance.IsPlayer) return true;
            var comp = Find.World?.GetComponent<FactionColorWorldComp>();
            if (comp == null)
            {
                __result = Color.gray;
                return false;
            }
            if (!comp.savedColors.TryGetValue(__instance.loadID, out Color color))
            {
                color = new Color(Rand.Range(0f, 0.4f), Rand.Range(0f, 0.4f), Rand.Range(0f, 0.4f));
                comp.savedColors[__instance.loadID] = color;
            }
            __result = color;
            return false;
        }
    }

    [StaticConstructorOnStartup]
    public static class FactionColorInit
    {
        static FactionColorInit()
        {
            LongEventHandler.QueueLongEvent(() =>
            {
                if (Find.World != null && Find.World.GetComponent<FactionColorWorldComp>() == null)
                    Find.World.components.Add(new FactionColorWorldComp(Find.World));
            }, "LoadingFactionColors", false, null);
        }
    }
}