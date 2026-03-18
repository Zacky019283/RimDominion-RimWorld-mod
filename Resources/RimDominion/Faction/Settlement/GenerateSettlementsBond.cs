using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld.Planet;
using RimWorld;
using Verse;

namespace RimDominion
{
    public class GenerateSettlementsBond : WorldComponent
    {
        public GenerateSettlementsBond(World world) : base(world) { }

        public Dictionary<int, List<int>> bond = new Dictionary<int, List<int>>();
        private static FieldInfo cachedMatField = typeof(Settlement).GetField("cachedMat", BindingFlags.Instance | BindingFlags.NonPublic);
        private int now;
        private bool initialized;

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
        }

        public void CustomFinalize()
        {
            if (!initialized)
            {
                bond = GenerateSettlementBonds();
                bond = RemoveMultiParents(bond);
                FactionChanger();
                RemoveSettlementWithoutBond();
                foreach (Village v in Find.WorldObjects.Settlements.OfType<Village>())
                    BondRoadConnection(DefDatabase<RoadDef>.GetNamed("DirtPath"), v);
                foreach (Settlement s in Find.WorldObjects.Settlements)
                    EnsureRoadConnection(s);
                LimitVillagesPerParent();
                initialized = true;
            }
        }

        public override void WorldComponentTick()
        {
            now++;
            if (now <= 30) return;
            now = 0;
            FactionChanger();
        }

        public Dictionary<int, List<int>> GenerateSettlementBonds()
        {
            var grid = Find.WorldGrid;

            List<Village> villages = Find.WorldObjects.Settlements.OfType<Village>().ToList();
            List<SmallCity> smallCities = Find.WorldObjects.Settlements.OfType<SmallCity>().ToList();
            List<LargeCity> largeCities = Find.WorldObjects.Settlements.OfType<LargeCity>().ToList();
            List<CapitalSettlement> capitals = Find.WorldObjects.Settlements.OfType<CapitalSettlement>().ToList();

            Dictionary<int, List<int>> bonds = new Dictionary<int, List<int>>();

            foreach (var v in villages)
            {
                SmallCity nearest = null;
                float best = float.MaxValue;

                foreach (var sc in smallCities)
                {
                    float dist = grid.ApproxDistanceInTiles(v.Tile, sc.Tile);
                    if (dist < best)
                    {
                        best = dist;
                        nearest = sc;
                    }
                }

                if (nearest != null)
                {
                    if (!bonds.ContainsKey(nearest.ID))
                        bonds[nearest.ID] = new List<int>();

                    bonds[nearest.ID].Add(v.ID);
                }
            }

            foreach (var lc in largeCities)
            {
                foreach (var sc in smallCities)
                {
                    float dist = grid.ApproxDistanceInTiles(lc.Tile, sc.Tile);
                    if (dist < 1.5f)
                    {
                        if (!bonds.ContainsKey(lc.ID))
                            bonds[lc.ID] = new List<int>();
                        if (bonds.Values.Any(list => list.Contains(sc.ID)))
                            continue;

                        bonds[lc.ID].Add(sc.ID);
                    }
                }
            }

            foreach (var cap in capitals)
            {
                foreach (var sc in smallCities)
                {
                    float dist = grid.ApproxDistanceInTiles(cap.Tile, sc.Tile);
                    if (dist < 1.5f)
                    {
                        if (!bonds.ContainsKey(cap.ID))
                            bonds[cap.ID] = new List<int>();
                        if (bond.Values.Any(list => list.Contains(sc.ID)))
                            continue;

                        bonds[cap.ID].Add(sc.ID);
                    }
                }
            }

            return bonds;
        }

        public Dictionary<int, List<int>> RemoveMultiParents(Dictionary<int, List<int>> bonds)
        {
            Dictionary<int, int> childToParent = new Dictionary<int, int>();

            var pairs = bonds.SelectMany(kv => kv.Value.Select(child => new { parent = kv.Key, child }))
                             .GroupBy(x => x.child);

            foreach (var g in pairs)
            {
                var chosen = g.RandomElement();
                childToParent[g.Key] = chosen.parent;
            }

            Dictionary<int, List<int>> clean = new Dictionary<int, List<int>>();

            foreach (var kv in childToParent)
            {
                if (!clean.ContainsKey(kv.Value))
                    clean[kv.Value] = new List<int>();

                clean[kv.Value].Add(kv.Key);
            }

            return clean;
        }

        public void FactionChanger()
        {
            var settlements = Find.WorldObjects.Settlements
                .Where(s => (s is SmallCity || s is Village) && !bond.ContainsKey(s.ID))
                .ToList();

            var largeSettlements = Find.WorldObjects.Settlements
                .Where(s => !(s is Village) && bond.ContainsKey(s.ID))
                .ToList();

            var parentLookup = bond
                .SelectMany(kv => kv.Value.Select(child => new { parent = kv.Key, child }))
                .ToDictionary(x => x.child, x => x.parent);

            foreach (var ls in largeSettlements)
            {
                if (parentLookup.TryGetValue(ls.ID, out int parentID))
                {
                    var parent = Find.WorldObjects.Settlements.FirstOrDefault(s => s.ID == parentID);
                    if (parent != null && parent.Faction != ls.Faction)
                    {
                        ls.SetFaction(parent.Faction);
                        cachedMatField?.SetValue(ls, null);
                        Find.World.renderer.Notify_StaticWorldObjectPosChanged();
                    }
                }

                if (!bond.TryGetValue(ls.ID, out var children))
                    continue;

                foreach (var ss in settlements.Where(s => children.Contains(s.ID) && s.Faction != ls.Faction))
                {
                    ss.SetFaction(ls.Faction);
                    cachedMatField?.SetValue(ss, null);
                    Find.World.renderer.Notify_StaticWorldObjectPosChanged();
                }
            }
        }

        public void RemoveSettlementWithoutBond()
        {
            List<Settlement> settlements = Find.WorldObjects.Settlements.ToList();
            foreach (var s in settlements)
            {
                bool isParent = bond.ContainsKey(s.ID);
                bool isChild = bond.Values.Any(l => l.Contains(s.ID));

                if (!isParent && !isChild)
                {
                    Find.WorldObjects.Remove(s);
                    continue;
                }

                if (!(s is Village)) continue;

                float radius = Rand.Range(10, 16);
                int parentID = -1;
                foreach (var kv in bond)
                {
                    if (kv.Value.Contains(s.ID))
                    {
                        parentID = kv.Key;
                        break;
                    }
                }
                Settlement parent = Find.WorldObjects.Settlements.FirstOrDefault(se => se.ID == parentID);
                
                if (Find.WorldGrid.ApproxDistanceInTiles(parent.Tile, s.Tile) > radius || PathCrossesWater(parent.Tile, s.Tile))
                {
                    Find.WorldObjects.Remove(s);
                }
            }
        }

        private bool PathCrossesWater(int startTile, int targetTile)
        {
            var grid = Find.WorldGrid;
            int current = startTile;
            int safety = 200;

            while (current != targetTile && safety-- > 0)
            {
                List<PlanetTile> neighbors = new List<PlanetTile>();
                grid.GetTileNeighbors(current, neighbors);

                int next = -1;
                float bestDist = float.MaxValue;

                foreach (int n in neighbors)
                {
                    float dist = grid.ApproxDistanceInTiles(n, targetTile);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        next = n;
                    }
                }

                if (next < 0) break;

                if (grid[next].WaterCovered)
                    return true;

                current = next;
            }

            return false;
        }

        public static void BondRoadConnection<T>(RoadDef def, T s)
    where T : Settlement
        {
            var grid = Find.WorldGrid;

            List<PlanetTile> neigh = new List<PlanetTile>();
            grid.GetTileNeighbors(s.Tile, neigh);

            foreach (int n in neigh)
            {
                if (grid.GetRoadDef(s.Tile, n) != null)
                    return;
            }
            var bondComp = Find.World.GetComponent<GenerateSettlementsBond>().bond;
            int parentID = bondComp.First(kv => kv.Value.Contains(s.ID)).Key;
            var parent = Find.WorldObjects.Settlements.FirstOrDefault(se => se.ID == parentID);

            if (parent == null) return;

            int current = s.Tile;
            int target = parent.Tile;

            int safety = 200;

            List<int> path = new List<int>();

            while (current != target && safety-- > 0)
            {
                List<PlanetTile> ns = new List<PlanetTile>();
                grid.GetTileNeighbors(current, ns);

                int next = -1;
                float bestDist = float.MaxValue;

                foreach (int n in ns)
                {
                    if (Find.WorldGrid[n].WaterCovered)
                        continue;

                    float d = grid.ApproxDistanceInTiles(n, target);

                    if (d < bestDist)
                    {
                        bestDist = d;
                        next = n;
                    }
                }

                if (next < 0)
                    return;

                path.Add(next);
                current = next;
            }

            current = s.Tile;

            foreach (int step in path)
            {
                grid.OverlayRoad(current, step, def);
                current = step;
            }
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

            List<int> path = new List<int>();

            while (current != target && safety-- > 0)
            {
                List<PlanetTile> ns = new List<PlanetTile>();
                grid.GetTileNeighbors(current, ns);

                int next = -1;
                float bestDist = float.MaxValue;

                foreach (int n in ns)
                {
                    if (Find.WorldGrid[n].WaterCovered)
                        continue;

                    float d = grid.ApproxDistanceInTiles(n, target);

                    if (d < bestDist)
                    {
                        bestDist = d;
                        next = n;
                    }
                }

                if (next < 0)
                    return;

                path.Add(next);
                current = next;
            }

            current = s.Tile;

            foreach (int step in path)
            {
                grid.OverlayRoad(current, step, road);
                current = step;
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

        public void LimitVillagesPerParent()
        {
            int maxVillage = Rand.RangeInclusive(4, 6);

            foreach (var kv in bond)
            {
                var parentID = kv.Key;

                List<Village> villages = kv.Value.Select(id => Find.WorldObjects.Settlements.FirstOrDefault(s => s.ID == id)).OfType<Village>().ToList();

                if (villages.Count <= maxVillage)
                    continue;

                int removeCount = villages.Count - maxVillage;

                var toRemove = villages.InRandomOrder().Take(removeCount).ToList();

                foreach (var v in toRemove)
                {
                    Find.WorldObjects.Remove(v);
                    kv.Value.Remove(v.ID);
                }
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref bond, "bond", LookMode.Value, LookMode.Value);
        }
    }
}
