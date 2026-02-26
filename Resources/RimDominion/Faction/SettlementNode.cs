using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace MyMod
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
            // panggil private vanilla
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

            for (int i = 0; i < targetCount; i++)
            {
                Settlement settlement =
                    (Settlement)WorldObjectMaker.MakeWorldObject(layer.Def.SettlementWorldObjectDef);

                settlement.Tile = TileFinder.RandomSettlementTileFor(layer, null, false, null);
                // TANPA FACTION
                Find.WorldObjects.Add(settlement);

                ResolveSettlementFaction(settlement);
            }

            foreach (var s in Find.WorldObjects.Settlements)
            {
                if (s.Faction != null)
                    EnforceLocalFactionDominance(s);
            }
            SpawnVillages();
            Find.IdeoManager.SortIdeos();
            return false;
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

            // sudah pernah dibuat → pakai yang sama
            if (capitalAnchorTiles.TryGetValue(id, out int anchorTile))
                return anchorTile;

            // cari semua tile dalam radius 4
            List<PlanetTile> candidates = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(capital.Tile, candidates);

            // BFS radius 4
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

            // jika tidak ada kandidat (harusnya tidak terjadi)
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

            // 1) Ambil settlement B yang berada dalam radius capital + punya faksi
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

            // Tidak ada settlement B yang valid
            if (candidates.Count == 0)
                return false;

            // 2) Cari settlement B yg paling dekat ke A
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

            // tidak dapat nearest
            if (nearest == null)
                return false;

            // 3) Assign faction B ke A
            AssignFactionAndName(target, nearest.Faction);
            return true;
        }

        private static void EnforceLocalFactionDominance(Settlement s)
        {
            const float radius = 20f;

            var all = Find.WorldObjects.Settlements;

            int countA = 0; // jumlah settlement faksi A dalam radius
            Dictionary<Faction, int> countOther = new Dictionary<Faction, int>(); // faksi lain
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

            // Jika lebih dari 1 faksi lain dalam radius → return
            if (countOther.Count != 1)
                return;

            // Ambil satu-satunya faksi B
            var facB = countOther.Keys.First();
            int countB = countOther[facB];

            // Rumus: jumlah B - jumlah A
            int diff = countB - countA;

            // Jika diff ≤ 2 → tidak berubah
            if (diff <= 0)
                return;

            // Jika diff > 2 → pindah faksi
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
                nameable.Name = SettlementNameGenerator.GenerateSettlementName(settlement, null);
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
                // settlement berada DI LUAR radius capital
                // → pakai logika baru: cari settlement dalam radius capital terdekat

                bool ok = AssignFactionFromCapitalRadiusSettlements(
                    settlement,
                    capitals,
                    CapitalRadius
                );

                if (!ok)
                {
                    // fallback terakhir: delete agar tidak error GUI
                    Find.WorldObjects.Remove(settlement);
                }
            }
        }

        public static void SpawnVillages()
        {
            WorldGrid grid = Find.WorldGrid;
            var settlements = Find.WorldObjects.Settlements;

            if (settlements.Count == 0) return;

            int targetVillages = settlements.Count * 2;


            int spawned = 0;
            int safety = grid.TilesCount * 2;

            while (spawned < targetVillages && safety-- > 0)
            {
                int tile = Rand.Range(0, grid.TilesCount);
                if (grid[tile].WaterCovered) continue;
                if (!TileFinder.IsValidTileForNewSettlement(tile)) continue;
                var biome = grid[tile].Biomes;

                if (biome == BiomeDefOf.IceSheet || biome == BiomeDefOf.SeaIce)
                    continue;

                // cari settlement terdekat
                Settlement nearest = null;
                float bestDist = float.MaxValue;

                foreach (var s in settlements)
                {
                    if (s.Faction == null) continue;

                    float dist = grid.ApproxDistanceInTiles(tile, s.Tile);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        nearest = s;
                    }
                }

                if (nearest == null) continue;

                // spawn village
                Settlement village =
                    (Settlement)WorldObjectMaker.MakeWorldObject(
                        DefDatabase<WorldObjectDef>.GetNamed("Village")
                    );

                village.Tile = tile;
                village.SetFaction(nearest.Faction);
                village.Name = SettlementNameGenerator.GenerateSettlementName(village) + " village";
                if (village.Name.Contains("Village village"))
                    village.Name = village.Name.Replace("Village village", "Village").Trim();

                if (village.Name.Contains("City village"))
                    village.Name = village.Name.Replace("City village", "Village").Trim();

                if (village.Name.Contains("village"))
                    village.Name = village.Name.Replace("village", "Village").Trim();
                Find.WorldObjects.Add(village);

                spawned++;
            }
        }
    }
}
