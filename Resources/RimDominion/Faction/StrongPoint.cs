using RimWorld;
using Verse;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RimDominion
{
    public class FactionStrongPointWorldComp : WorldComponent
    {
        private Dictionary<float, float> factionStrongPoints = new Dictionary<float, float>();

        public FactionStrongPointWorldComp(World world) : base(world) { }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            RecalculateAll();
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
            if (Prefs.DevMode)
                LogAllFactionSP();
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

        private void LogAllFactionSP()
        {
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                factionStrongPoints.TryGetValue(f.loadID, out float sp);
                Log.Message($"[RimDominion] Faction '{f.Name}' (ID {f.loadID}) StrongPoints = {sp}");
            }
        }
    }

}
