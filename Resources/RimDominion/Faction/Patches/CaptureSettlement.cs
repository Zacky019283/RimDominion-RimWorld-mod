using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using System.Reflection;
using UnityEngine;

namespace RimDominion
{
    [HarmonyPatch(typeof(SettlementDefeatUtility), "CheckDefeated")]
    public static class Patch_CaptureSettlement
    {
        private static FieldInfo cachedMatField = typeof(Settlement).GetField("cachedMat", BindingFlags.Instance | BindingFlags.NonPublic);
        static bool Prefix(Settlement factionBase)
        {
            if (factionBase.Faction == Faction.OfPlayer)
                return true;

            Map map = factionBase.Map;
            if (map == null)
                return true;

            if (!SettlementDefeatUtility.IsDefeated(map, factionBase.Faction))
                return true;

            Capture(factionBase, map);
            return false;
        }

        static void Capture(Settlement settlement, Map map)
        {
            Faction oldFaction = settlement.Faction;

            settlement.SetFaction(Faction.OfPlayer);

            map.lordManager.lords.RemoveAll(l => l.faction == oldFaction);

            Find.LetterStack.ReceiveLetter(
                "Settlement Captured",
                $"You was captured a settlement named {settlement.Name}.",
                LetterDefOf.PositiveEvent,
                settlement
            );

            cachedMatField?.SetValue(settlement, null);
            if (Find.World?.renderer != null)
                Find.World.renderer.Notify_StaticWorldObjectPosChanged();
        }

    }
}
