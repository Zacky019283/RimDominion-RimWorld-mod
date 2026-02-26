using Verse;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;

namespace MyMod
{
    public class SettlementIconExtension : DefModExtension
    {
        public string iconPath;
        public float scale = 1f;
        public float altOffset = 0.02f;
    }

    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.Draw))]
    public static class WorldObject_Draw_SettlementIconPatch
    {
        static bool Prefix(WorldObject __instance)
        {
            if (!(__instance is Settlement settlement))
                return true;

            var ext = settlement.def.GetModExtension<SettlementIconExtension>();
            if (ext == null)
                return true;

            Material mat = ContentFinder<Material>.Get(ext.iconPath, true);
            if (mat == null)
                return true;

            float avgTileSize = settlement.Tile.Layer.AverageTileSize;
            float size = 0.7f * avgTileSize * ext.scale;

            float alt = settlement.DrawAltitude
                       + ext.altOffset
                       + Rand.RangeSeeded(0f, 0.01f, settlement.ID);

            WorldRendererUtility.DrawQuadTangentialToPlanet(
                settlement.DrawPos,
                size,
                alt,
                mat
            );

            return false; // skip vanilla settlement icon
        }
    }
}
