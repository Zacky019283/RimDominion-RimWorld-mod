using System.Collections.Generic;
using RimWorld.Planet;
using Verse;
using RimWorld;
using System.Linq;

namespace RimDominion
{
    public class NeighborManager : WorldComponent
    {
        private Dictionary<int, HashSet<int>> factionNeighbors = new Dictionary<int, HashSet<int>>();
        private int updateTick = 0;
        private int updateInterval = 5000;
        private bool initialized;

        public NeighborManager(World world) : base(world)
        {
        }

        public override void WorldComponentTick()
        {
            updateTick++;
            if (!initialized)
            {
                RebuildNeighbors();
            }
            if (updateTick >= updateInterval)
            {
                RebuildNeighbors();
                updateTick = 0;
            }
        }

        public HashSet<int> GetNeighbors(Faction faction)
        {
            if (faction == null) return null;

            if (factionNeighbors.TryGetValue(faction.loadID, out var set))
                return set;

            return null;
        }

        public bool IsNeighbor(Faction a, Faction b)
        {
            if (a == null || b == null) return false;

            var neighbors = GetNeighbors(a);
            if (neighbors == null) return false;

            return neighbors.Contains(b.loadID);
        }

        public void RebuildNeighbors()
        {
            factionNeighbors.Clear();

            var factions = Find.FactionManager.AllFactionsListForReading;

            foreach (var f in factions)
            {
                if (f == null) continue;
                factionNeighbors[f.loadID] = new HashSet<int>();
            }

            var settlements = Find.WorldObjects.Settlements;

            foreach (var s in settlements)
            {
                if (s.Faction == null) continue;

                int factionId = s.Faction.loadID;

                float minDist = float.MaxValue;
                int nearestFaction = -1;

                foreach (var other in settlements)
                {
                    if (other == s) continue;
                    if (other.Faction == null) continue;
                    if (other.Faction == s.Faction) continue;

                    float distance = Find.WorldGrid.ApproxDistanceInTiles(s.Tile, other.Tile);

                    if (distance < minDist)
                    {
                        minDist = distance;
                        nearestFaction = other.Faction.loadID;
                    }
                }

                if (nearestFaction == -1) continue;

                factionNeighbors[factionId].Add(nearestFaction);
                factionNeighbors[nearestFaction].Add(factionId);

            }
            var Factions = Find.FactionManager.AllFactionsListForReading.Where(f => !f.Hidden && factionNeighbors.ContainsKey(f.loadID)).ToList();

            foreach (var f in Factions)
            {
                if (!Prefs.DevMode)
                    break;
                Log.Message(f.Name + ":");
                var nF = factionNeighbors[f.loadID];
                foreach (var nf in nF)
                {
                    Faction fac = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(fn => fn.loadID == nf);
                    Log.Message("  " + fac.Name);
                }
                Log.Message("");
            }
            if (!initialized)
                initialized = true;
        }

        public void CustomFinalize()
        {
            RebuildNeighbors();
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref initialized, "initialized");
        }
    }
}