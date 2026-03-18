using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;
using RimWorld.Planet;

namespace RimDominion
{
    public class TerritoryManager : WorldComponent
    {
        public Dictionary<int, int> tileOwner = new Dictionary<int, int>();
        public Dictionary<int, List<(Vector3, Vector3)>> borderCache = new Dictionary<int, List<(Vector3, Vector3)>>();
        private Dictionary<Faction, Material> factionMatCache = new Dictionary<Faction, Material>();
        private Dictionary<int, Material> tileMatCache = new Dictionary<int, Material>();
        private Dictionary<int, Faction> lastFaction = new Dictionary<int, Faction>();

        public TerritoryManager(World world) : base(world)
        {
        }

        public void BuildTerritories()
        {
            tileOwner.Clear();

            var grid = Find.WorldGrid;
            int tiles = grid.TilesCount;

            float[] bestInfluence = new float[tiles];
            int[] bestSettlement = new int[tiles];

            for (int i = 0; i < tiles; i++)
            {
                bestInfluence[i] = float.MinValue;
                bestSettlement[i] = -1;
            }

            var queue = new Queue<(int tile, int settlementId, float influence)>();

            var settlements = Find.WorldObjects.Settlements
                .Where(s => s.Faction != null && (s is SmallCity || s is LargeCity || s is CapitalSettlement))
                .ToList();

            foreach (var s in settlements)
            {
                float power = 0;

                if (s is LargeCity || s is CapitalSettlement) power = Rand.Range(65, 90);
                else if (s is SmallCity) power = Rand.Range(50, 65);

                queue.Enqueue((s.Tile, s.ID, power));
            }

            List<PlanetTile> neighbors = new List<PlanetTile>();

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();

                if (node.influence <= 0)
                    continue;

                if (node.influence <= bestInfluence[node.tile])
                    continue;

                bestInfluence[node.tile] = node.influence;
                bestSettlement[node.tile] = node.settlementId;

                neighbors.Clear();
                grid.GetTileNeighbors(node.tile, neighbors);

                foreach (var n in neighbors)
                {
                    var tile = grid[n];

                    if (tile.WaterCovered)
                        continue;

                    float cost = 4f;

                    if (tile.hilliness == Hilliness.Mountainous) cost += 2.5f;
                    if (tile.hilliness == Hilliness.Impassable) cost += 5;
                    if (tile.Biomes == BiomeDefOf.Desert) cost += 1.5f;
                    if (tile.Biomes == BiomeDefOf.IceSheet || tile.Biomes == BiomeDefOf.SeaIce) cost += 3;
                    if (tile.Biomes == BiomeDefOf.ColdBog) cost += 1.5f;
                    if (tile.Biomes == BiomeDefOf.Scarlands) cost += 2;
                    if (tile.Biomes == BiomeDefOf.Tundra) cost += 1.2f;
                    if (tile.Biomes == BiomeDefOf.TropicalSwamp) cost += 1.25f;
                    foreach (var settlement in Find.WorldObjects.Settlements.Where(s => s is LargeCity || s is CapitalSettlement || s is SmallCity).ToList())
                    {
                        if (tile.tile.tileId == settlement.Tile.tileId) cost = float.MaxValue;
                    }

                    float aFormula = 0.3f * tile.temperature;
                    cost = cost - Mathf.Abs(aFormula);

                    float newInfluence = node.influence - Mathf.Abs(cost * 5f);

                    if (newInfluence > bestInfluence[n])
                    {
                        queue.Enqueue((n, node.settlementId, newInfluence));
                    }
                }
            }

            for (int i = 0; i < tiles; i++)
            {
                if (bestSettlement[i] != -1)
                    tileOwner[i] = bestSettlement[i];
            }
        }

        private void BuildBorders()
        {
            borderCache.Clear();

            var grid = Find.WorldGrid;

            foreach (var tile in tileOwner.Keys)
            {
                if (grid[tile].WaterCovered) continue;

                int owner = tileOwner[tile];

                List<PlanetTile> neighbors = new List<PlanetTile>();
                grid.GetTileNeighbors(tile, neighbors);

                bool allSame = true;

                foreach (var n in neighbors)
                {
                    if (!tileOwner.TryGetValue(n, out int nOwner) || nOwner != owner)
                    {
                        allSame = false;
                        break;
                    }
                }

                if (allSame) continue;

                var verts = new List<Vector3>();
                grid.GetTileVertices(tile, verts);

                List<(Vector3, Vector3)> lines = new List<(Vector3, Vector3)>();

                foreach (var n in neighbors)
                {
                    if (!tileOwner.TryGetValue(n, out int nOwner) || nOwner != owner)
                    {
                        var nVerts = new List<Vector3>();
                        grid.GetTileVertices(n, nVerts);

                        List<Vector3> best = new List<Vector3>();

                        foreach (var v in verts)
                        {
                            float bestDist = float.MaxValue;

                            foreach (var nv in nVerts)
                            {
                                float dist = (v - nv).sqrMagnitude;
                                if (dist < bestDist)
                                {
                                    bestDist = dist;
                                }
                            }

                            if (bestDist < 0.001f)
                            {
                                best.Add(v);
                            }
                        }

                        if (best.Count > 1)
                        {
                            for (int i = 0; i < best.Count; i++)
                            {
                                var a = best[i];
                                var b = best[(i + 1) % best.Count];
                                lines.Add((a, b));
                            }
                        }
                    }
                }

                if (lines.Count > 0)
                {
                    borderCache[tile] = lines;
                }
            }
        }

        private void RebuildTileMaterialCache()
        {
            tileMatCache.Clear();

            foreach (var kvp in borderCache)
            {
                var settlement = GetOwnerSettlement(kvp.Key);
                if (settlement == null || settlement.Faction == null) continue;

                var mat = GetMaterialForFaction(settlement.Faction);
                if (mat != null)
                {
                    tileMatCache[kvp.Key] = mat;
                }
            }
        }

        public void DrawBorders()
        {
            foreach (var kvp in borderCache)
            {
                foreach (var line in kvp.Value)
                {
                    Settlement settlement = GetOwnerSettlement(kvp.Key);
                    if (settlement == null) continue;
                    Color color = settlement.Faction.Color;
                    Material mat = SolidColorMaterials.NewSolidColorMaterial(color, ShaderDatabase.WorldOverlayTransparentLit);
                    GenDraw.DrawWorldLineBetween(line.Item1, line.Item2, mat, 0.5f);
                }
            }
        }

        private Material GetMaterialForFaction(Faction faction)
        {
            if (faction == null) return null;

            if (!factionMatCache.TryGetValue(faction, out Material mat))
            {
                mat = SolidColorMaterials.NewSolidColorMaterial(faction.Color, ShaderDatabase.WorldOverlayTransparentLit);
                factionMatCache[faction] = mat;
            }

            return mat;
        }

        public Settlement GetOwnerSettlement(int tile)
        {
            if (!tileOwner.TryGetValue(tile, out int id))
                return null;

            return Find.WorldObjects.Settlements.FirstOrDefault(s => s.ID == id);
        }

        public Faction GetOwnerFaction(PlanetTile tile)
        {
            var s = GetOwnerSettlement(tile);
            if (s == null) return null;
            return s.Faction;
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            if (fromLoad) return;

            BuildTerritories();
            LongEventHandler.ExecuteWhenFinished(() => {
                BuildBorders();
                RebuildTileMaterialCache();
            });
        }

        public override void WorldComponentTick()
        {
            if (Find.TickManager.TicksGame % 300 != 0) return;

            bool changed = false;

            foreach (var s in Find.WorldObjects.Settlements)
            {
                if (!lastFaction.TryGetValue(s.ID, out var oldFaction))
                {
                    lastFaction[s.ID] = s.Faction;
                    continue;
                }

                if (oldFaction != s.Faction)
                {
                    lastFaction[s.ID] = s.Faction;
                    changed = true;
                }
            }

            if (changed)
            {
                RebuildTileMaterialCache();
            }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref tileOwner, "tileOwner");
            Scribe_Values.Look(ref borderCache, "borderCache");
            Scribe_Values.Look(ref factionMatCache, "factionMatCache");
            Scribe_Values.Look(ref tileMatCache, "tileMatCache");
            Scribe_Values.Look(ref lastFaction, "lastFaction");
        }
    }
}
