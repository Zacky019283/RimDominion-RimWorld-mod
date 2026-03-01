using RimWorld.Planet;
using Verse;
using System.Collections.Generic;
using UnityEngine;

namespace RimDominion
{
    public class SettlementStrongPointComp : WorldComponent
    {
        private Dictionary<int, float> settlementSP = new Dictionary<int, float>();
        private int tick;

        public SettlementStrongPointComp(World world) : base(world) { }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);

            if (fromLoad) return;

            foreach (var s in Find.WorldObjects.Settlements)
            {
                settlementSP[s.ID] = GetBaseSP(s);
            }
        }

        public override void WorldComponentTick()
        {
            tick++;
            if (tick < 60000) return;
            tick = 0;

            var factionSPComp = world.GetComponent<FactionStrongPointWorldComp>();

            foreach (var s in Find.WorldObjects.Settlements)
            {
                if (s.Faction == null) continue;

                float baseSP = GetBaseSP(s);
                float factionSP = factionSPComp.GetFactionSP(s.Faction);

                if (factionSP <= 0) continue;

                float cur = GetCurrentSP(s);
                float x = cur;
                float sigmoid = 1f / (1f + Mathf.Exp((-factionSP) * x));
                float gain = sigmoid * baseSP;

                settlementSP[s.ID] = cur + gain;
            }
        }

        public float GetCurrentSP(Settlement s)
        {
            settlementSP.TryGetValue(s.ID, out float val);
            return val;
        }

        float GetBaseSP(Settlement s)
        {
            if (s is HierarchySettlements h) return h.StrongPoint;
            return 1f;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref settlementSP, "settlementSP", LookMode.Value, LookMode.Value);
        }
    }
}
