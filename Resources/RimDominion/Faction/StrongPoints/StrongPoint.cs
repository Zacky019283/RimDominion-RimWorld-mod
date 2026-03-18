using RimWorld;
using Verse;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;

namespace RimDominion
{
    public class FactionStrongPointWorldComp : WorldComponent
    {
        private Dictionary<float, float> factionStrongPoints = new Dictionary<float, float>();
        private Dictionary<int, int> lastSettlementCount = new Dictionary<int, int>();
        private bool initialized;

        public FactionStrongPointWorldComp(World world) : base(world) { }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);

            if (!fromLoad)
            {
                RecalculateAll();
                CacheSettlementCounts();
                initialized = true;
            }
        }

        public override void WorldComponentTick()
        {
            if (!initialized) return;

            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                int current = Find.WorldObjects.Settlements.Count(s => s.Faction == f);

                if (!lastSettlementCount.TryGetValue(f.loadID, out int old))
                    old = 0;

                if (current != old)
                {
                    RecalculateAll();
                    CacheSettlementCounts();
                    break;
                }
            }
        }

        private void CacheSettlementCounts()
        {
            lastSettlementCount.Clear();

            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                int count = Find.WorldObjects.Settlements.Count(s => s.Faction == f);
                lastSettlementCount[f.loadID] = count;
            }
        }

        public void RecalculateAll()
        {
            factionStrongPoints.Clear();

            foreach (var s in Find.WorldObjects.Settlements)
            {
                if (s.Faction == null) continue;

                float sp = GetSettlementSP(s);

                if (factionStrongPoints.TryGetValue(s.Faction.loadID, out float cur))
                    factionStrongPoints[s.Faction.loadID] = cur + sp;
                else
                    factionStrongPoints[s.Faction.loadID] = sp;
            }

            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                if (!factionStrongPoints.ContainsKey(f.loadID))
                    factionStrongPoints[f.loadID] = 0;
            }

            var keys = factionStrongPoints.Keys.ToList();
            foreach (var k in keys)
            {
                float mult = Rand.Range(0.8f, 1.2f);
                factionStrongPoints[k] = factionStrongPoints[k] * mult;
            }
        }

        public float GetFactionSP(Faction f)
        {
            if (f == null) return 0;
            factionStrongPoints.TryGetValue(f.loadID, out float val);
            return val;
        }

        private float GetSettlementSP(Settlement s)
        {
            if (s is HierarchySettlements hs)
                return hs.StrongPoint;
            return 1;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref factionStrongPoints, "factionStrongPoints",
                LookMode.Value, LookMode.Value);
        }
    }

}