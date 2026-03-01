using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
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
            int villCount = targetCount * 2;

            SpawnSettlementsOfType<LargeCity>(layer, largeCount);
            SpawnSettlementsOfType<SmallCity>(layer, smallCount);
            SpawnSettlementsOfType<Village>(layer, villCount);
            foreach (var s in Find.WorldObjects.Settlements)
            {
                EnsureRoadConnection(s);
            }
            SpawnAdjacentSettlementsFor<CapitalSettlement>(layer, 2 , 6);
            SpawnAdjacentSettlementsFor<LargeCity>(layer, 1, 3);

            foreach (var s in Find.WorldObjects.Settlements)
            {
                if (s.Faction != null)
                {
                    EnforceLocalFactionDominance(s);
                }

            }
            foreach (Faction f in Find.FactionManager.AllFactions)
            {
                FixDuplicateCapitals(f);
            }

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
                .FirstOrDefault(s => !(s as INameableWorldObject)?.Name?.Contains("(Capital)") ?? true);

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

        private static void EnforceLocalFactionDominance(Settlement s)
        {
            const float radius = 20f;

            var all = Find.WorldObjects.Settlements;

            int countA = 0;
            Dictionary<Faction, int> countOther = new Dictionary<Faction, int>();
            Faction facA = s.Faction;

            foreach (var other in all)
            {
                if (other == s || other.Faction == null)
                    continue;

                float dist = Find.WorldGrid.ApproxDistanceInTiles(s.Tile, other.Tile);
                if (dist > radius)
                    continue;

                if (other.Faction == facA)
                {
                    countA++;
                }
                else
                {
                    if (!countOther.ContainsKey(other.Faction))
                        countOther[other.Faction] = 0;

                    countOther[other.Faction]++;
                }
            }

            if (countOther.Count != 1)
                return;

            var facB = countOther.Keys.First();
            int countB = countOther[facB];

            int diff = countB - countA;

            if (diff <= 0)
                return;

            AssignFactionAndName(s, facB);
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
        public static void SpawnAdjacentSettlementsFor<T>(PlanetLayer layer, int min, int max)
            where T : Settlement
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

                    Settlement s = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOfLocal<SmallCity>.Def);
                    s.Tile = tile;
                    AssignFactionAndName(s, src.Faction);

                    Find.WorldObjects.Add(s);

                    SpawnStoneRoad(src.Tile, tile);

                    spawned++;
                }
            }
        }
        private static void SpawnStoneRoad(int a, int b)
        {
            RoadDef road = DefDatabase<RoadDef>.GetNamed("StoneRoad");
            Find.WorldGrid.OverlayRoad(a, b, road);
        }

        public static void EnsureRoadConnection(Settlement s)
        {
            if (s == null)
                return;

            var grid = Find.WorldGrid;

            List<PlanetTile> neigh = new List<PlanetTile>();
            grid.GetTileNeighbors(s.Tile, neigh);

            foreach (int n in neigh)
            {
                if (grid.GetRoadDef(s.Tile, n) != null)
                    return;
            }

            Settlement nearest = null;
            float best = float.MaxValue;

            foreach (var other in Find.WorldObjects.Settlements)
            {
                if (other == s)
                    continue;

                float dist = grid.ApproxDistanceInTiles(s.Tile, other.Tile);
                if (dist < best)
                {
                    best = dist;
                    nearest = other;
                }
            }

            if (nearest == null)
                return;

            RoadDef road = GetSettlementRoad(nearest);
            if (road == null)
                road = DefDatabase<RoadDef>.AllDefsListForReading.FirstOrDefault();

            int current = s.Tile;
            int target = nearest.Tile;

            int safety = 200;

            while (current != target && safety-- > 0)
            {
                List<PlanetTile> ns = new List<PlanetTile>();
                grid.GetTileNeighbors(current, ns);

                int next = -1;
                float bestDist = float.MaxValue;

                foreach (int n in ns)
                {
                    float d = grid.ApproxDistanceInTiles(n, target);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        next = n;
                    }
                }

                if (next < 0)
                    break;

                grid.OverlayRoad(current, next, road);
                current = next;
            }
        }

        private static RoadDef GetSettlementRoad(Settlement s)
        {
            var grid = Find.WorldGrid;

            List<PlanetTile> neigh = new List<PlanetTile>();
            grid.GetTileNeighbors(s.Tile, neigh);

            foreach (int n in neigh)
            {
                var r = grid.GetRoadDef(s.Tile, n);
                if (r != null)
                    return r;
            }

            return null;
        }
    }

    public static class WorldObjectDefOfLocal<T> where T : WorldObject
    {
        public static WorldObjectDef Def =
            DefDatabase<WorldObjectDef>.AllDefsListForReading
            .First(d => d.worldObjectClass == typeof(T));
    }
}