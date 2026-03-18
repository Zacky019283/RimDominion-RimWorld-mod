using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Data;
using Verse;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace RimDominion
{
    [HarmonyPatch(typeof(FactionGenerator), "GenerateFactionsIntoWorldLayer")]
    public static class Patch_GenerateFactionsIntoWorldLayer
    {
        static MethodInfo initFactionsMI =
            typeof(FactionGenerator).GetMethod(
                "InitializeFactions",
                BindingFlags.NonPublic | BindingFlags.Static);

        public static bool Prefix(PlanetLayer layer, List<FactionDef> factions = null)
        {
            var faction = Find.FactionManager.AllFactionsListForReading.Where(f => !f.IsPlayer && !f.def.hidden);
            initFactionsMI.Invoke(null, new object[] { layer, factions });

            var validFactions = Find.World.factionManager.AllFactionsListForReading
                .Where(f => f.def.settlementGenerationWeight > 0);

            if (!validFactions.Any())
            {
                Find.IdeoManager.SortIdeos();
                return false;
            }

            float angleFactor = layer.Def.viewAngleSettlementsFactorCurve
                .Evaluate(Mathf.Clamp01(layer.ViewAngle / 180f));

            float per100k = layer.Def.settlementsPer100kTiles.RandomInRange;
            float scale = Find.World.info.overallPopulation.GetScaleFactor();

            int targetCount = GenMath.RoundRandom(
                layer.TilesCount / 100000f * per100k * scale * angleFactor);

            targetCount -= Find.WorldObjects.AllSettlementsOnLayer(layer).Count;
            var capitals = Find.WorldObjects.Settlements.Where(s => s.Faction == faction && s is CapitalSettlement).ToList();

            int largeCount = Mathf.RoundToInt(targetCount * 0.5f);
            int smallCount = Mathf.RoundToInt(targetCount * 0.9f);
            int villageCount = Mathf.RoundToInt(targetCount * 2);

            SpawnSettlementsOfType<LargeCity>(layer, largeCount);
            SpawnSettlementsOfType<SmallCity>(layer, smallCount);
            SpawnSettlementsOfType<Village>(layer, villageCount);
            SpawnAdjacentSettlementsFor<CapitalSettlement, LargeCity>(0, 1);
            SpawnAdjacentSettlementsFor<CapitalSettlement, SmallCity>(1, 5);
            SpawnAdjacentSettlementsFor<LargeCity, SmallCity>(1, 3);
            foreach (Faction f in Find.FactionManager.AllFactions)
            {
                FixDuplicateCapitals(f);
            }
            TerritorySmoother();

            Find.IdeoManager.SortIdeos();
            return false;
        }

        private static void FixDuplicateCapitals(Faction faction)
        {
            var capitals = Find.WorldObjects.Settlements
                .Where(s => s.Faction == faction && s is CapitalSettlement)
                .ToList();

            if (capitals.Count <= 1) return;

            Settlement wrong = capitals
                .FirstOrDefault(s => !s.Name?.Contains("(Capital)") ?? true);

            if (wrong == null) return;

            int tile = wrong.Tile;
            Find.WorldObjects.Remove(wrong);

            LargeCity newCap = (LargeCity)WorldObjectMaker.MakeWorldObject(
                DefDatabase<WorldObjectDef>.AllDefsListForReading
                    .First(d => d.worldObjectClass == typeof(LargeCity))
            );

            newCap.SetFaction(faction);
            newCap.Tile = tile;

            if (newCap is INameableWorldObject nameable)
            {
                string name = SettlementNameGenerator.GenerateSettlementName(newCap, null) + " (Large)";
                if (name.Contains(" Village"))
                    name = name.Replace(" Village", "").Trim();
                nameable.Name = name;
            }

            Find.WorldObjects.Add(newCap);
        }

        private static void TerritorySmoother()
        {
            List<Settlement> settlements = Find.WorldObjects.Settlements;

            foreach (var settlement in settlements)
            {
                float radius = 20f;
                var a = Find.WorldGrid;
                var settlementsInRange = Find.WorldObjects.Settlements.Where(s => a.ApproxDistanceInTiles(settlement.Tile, s.Tile) <= radius).ToList();
                int value1 = 0;
                int value2 = 0;
                Faction b = settlement.Faction;
                foreach (var neighbor in settlementsInRange)
                {
                    b = neighbor.Faction;
                    if (neighbor.Faction != settlement.Faction)
                    {
                        value2++;
                        continue;
                    }
                    else value1++;
                }
                if (value2 > value1)
                {
                    settlement.SetFaction(b);
                }
            }
        }

        private static void SpawnSettlementsOfType<T>(
            PlanetLayer layer,
            int count)
            where T : Settlement
        {

            int spawned = 0;
            int safety = count * 20;

            while (spawned < count && safety-- > 0)
            {
                int tile = TileFinder.RandomSettlementTileFor(layer, null, false, null);
                if (tile < 0) continue;

                T s = (T)WorldObjectMaker.MakeWorldObject(WorldObjectDefOfLocal<T>.Def);
                s.Tile = tile;

                Find.WorldObjects.Add(s);

                ResolveSettlementFaction(s);

                if (s.Faction != null)
                    spawned++;
            }
        }

        private static CapitalSettlement FindFactionCapital(Faction faction)
        {
            List<WorldObject> objs = Find.WorldObjects.AllWorldObjects;

            for (int i = 0; i < objs.Count; i++)
            {
                CapitalSettlement cap = objs[i] as CapitalSettlement;
                if (cap != null && cap.Faction == faction)
                    return cap;
            }

            return null;
        }

        private static Dictionary<int, int> capitalAnchorTiles = new Dictionary<int, int>();

        private static int GetCapitalAnchorTile(CapitalSettlement capital)
        {
            if (capital == null) return -1;

            int id = capital.ID;

            if (capitalAnchorTiles.TryGetValue(id, out int anchorTile))
                return anchorTile;

            List<PlanetTile> candidates = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(capital.Tile, candidates);

            HashSet<int> visited = new HashSet<int> { capital.Tile };
            Queue<(int tile, int depth)> queue = new Queue<(int, int)>();
            queue.Enqueue((capital.Tile, 0));

            while (queue.Count > 0)
            {
                var (tile, depth) = queue.Dequeue();
                if (depth >= 4) continue;

                List<PlanetTile> neigh = new List<PlanetTile>();
                Find.WorldGrid.GetTileNeighbors(tile, neigh);

                foreach (int n in neigh)
                {
                    if (visited.Add(n))
                    {
                        candidates.Add(n);
                        queue.Enqueue((n, depth + 1));
                    }
                }
            }

            if (candidates.Count == 0)
                anchorTile = capital.Tile;
            else
                anchorTile = candidates.RandomElement();

            capitalAnchorTiles[id] = anchorTile;
            return anchorTile;
        }

        private static bool AssignFactionFromCapitalRadiusSettlements(Settlement target, List<Settlement> capitals, float capitalRadius)
        {
            List<Settlement> candidates = new List<Settlement>();

            foreach (var s in Find.WorldObjects.Settlements)
            {
                if (s == target || s.Faction == null)
                    continue;

                bool withinAnyCapital = false;

                foreach (var cap in capitals)
                {
                    float distCap = Find.WorldGrid.ApproxDistanceInTiles(s.Tile, cap.Tile);
                    if (distCap <= capitalRadius)
                    {
                        withinAnyCapital = true;
                        break;
                    }
                }

                if (withinAnyCapital)
                    candidates.Add(s);
            }

            if (candidates.Count == 0)
                return false;

            float bestDist = float.MaxValue;
            Settlement nearest = null;

            foreach (var s in candidates)
            {
                float dist = Find.WorldGrid.ApproxDistanceInTiles(target.Tile, s.Tile);

                if (dist < bestDist)
                {
                    bestDist = dist;
                    nearest = s;
                }
            }

            if (nearest == null)
                return false;

            AssignFactionAndName(target, nearest.Faction);
            return true;
        }

        private static Faction FindClosestCapitalFaction(int tile, List<Settlement> candidates)
        {
            float bestDist = float.MaxValue;
            List<Faction> best = new List<Faction>();

            foreach (Settlement s in candidates)
            {
                CapitalSettlement cap = FindFactionCapital(s.Faction);
                if (cap == null) continue;

                int anchor = GetCapitalAnchorTile(cap);
                float dist = Find.WorldGrid.TraversalDistanceBetween(tile, anchor);

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best.Clear();
                    best.Add(s.Faction);
                }
                else if (Mathf.Approximately(dist, bestDist))
                {
                    best.Add(s.Faction);
                }
            }

            if (best.Count == 0) return null;
            if (best.Count == 1) return best[0];

            return best.RandomElement();
        }

        private static void AssignFactionAndName(Settlement settlement, Faction faction)
        {
            settlement.SetFaction(faction);

            if (settlement is INameableWorldObject nameable)
            {
                string name = SettlementNameGenerator.GenerateSettlementName(settlement, null);

                if (settlement is LargeCity)
                {
                    name += " (Large)";
                    if (name.Contains("Village"))
                        name = name.Replace("Village", "").Trim();
                }
                else if (settlement is SmallCity)
                {
                    name += " (Small)";
                    if (name.Contains("Village"))
                        name = name.Replace("Village", "").Trim();
                }

                else if (settlement is Village)
                {
                    name += " village";
                    if (name.Contains("Village village"))
                        name = name.Replace("Village", "").Trim();
                    else if (name.Contains("City village"))
                        name = name.Replace("City", "").Trim();
                    else if (name.Contains("village"))
                        name = name.Replace("village", "Village").Trim();
                }
                nameable.Name = name;
            }
        }

        private static void ResolveSettlementFaction(Settlement settlement)
        {
            var capitals = Find.WorldObjects.Settlements
                .Where(s => s.def.defName == "CapitalSettlement")
                .ToList();

            float CapitalRadius = Rand.RangeInclusive(20, 40);
            List<Settlement> inRange = new List<Settlement>();

            foreach (var cap in capitals)
            {
                float dist = Find.WorldGrid.ApproxDistanceInTiles(settlement.Tile, cap.Tile);
                if (dist <= CapitalRadius)
                    inRange.Add(cap);
            }

            if (inRange.Count == 1)
            {
                AssignFactionAndName(settlement, inRange[0].Faction);
            }
            else if (inRange.Count > 1)
            {
                Faction chosenFaction = FindClosestCapitalFaction(settlement.Tile, inRange);

                if (chosenFaction != null)
                    AssignFactionAndName(settlement, chosenFaction);
                else
                    Find.WorldObjects.Remove(settlement);
            }
            else
            {
                bool ok = AssignFactionFromCapitalRadiusSettlements(
                    settlement,
                    capitals,
                    CapitalRadius
                );

                if (!ok)
                {
                    Find.WorldObjects.Remove(settlement);
                }
            }
        }
        private static void SpawnAdjacentSettlementsFor<T, S>(int min, int max)
            where T : Settlement
            where S : Settlement
        {
            var sources = Find.WorldObjects.Settlements
                .OfType<T>()
                .Where(s => s.Faction != null)
                .ToList();

            foreach (var src in sources)
            {
                int desired = Rand.RangeInclusive(min, max);

                List<PlanetTile> neighbors = new List<PlanetTile>();
                Find.WorldGrid.GetTileNeighbors(src.Tile, neighbors);

                neighbors.Shuffle();

                int spawned = 0;

                foreach (int tile in neighbors)
                {
                    if (spawned >= desired)
                        break;

                    if (Find.WorldObjects.AnyWorldObjectAt(tile))
                        continue;

                    if (Find.WorldGrid[tile].WaterCovered)
                        continue;

                    if (Find.WorldGrid[tile].Biomes == BiomeDefOf.SeaIce)
                        continue;

                    Settlement s = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOfLocal<S>.Def);
                    s.Tile = tile;
                    AssignFactionAndName(s, src.Faction);

                    Find.WorldObjects.Add(s);

                    Find.WorldGrid.OverlayRoad(src.Tile, tile, DefDatabase<RoadDef>.GetNamed("StoneRoad"));

                    spawned++;
                }
            }
        }
    }

    public static class WorldObjectDefOfLocal<T> where T : WorldObject
    {
        public static WorldObjectDef Def =
            DefDatabase<WorldObjectDef>.AllDefsListForReading
            .First(d => d.worldObjectClass == typeof(T));
    }
    /// <summary>
    /// temporary class
    /// |
    /// |
    /// |
    /// |
    /// V
    /// </summary>


    [HarmonyPatch(typeof(WorldRenderer), nameof(WorldRenderer.DrawWorldLayers))]
    public static class Patch_WorldRenderer_DrawTerritoryBorder
    {
        static void Postfix()
        {
            var territory = Find.World.GetComponent<TerritoryManager>();
            if (territory.borderCache != null)
              Find.World.GetComponent<TerritoryManager>().DrawBorders();
        }
    }

}