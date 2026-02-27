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
        private Dictionary<int, int> factionStrongPoints = new Dictionary<int, int>();

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

                int sp = GetSettlementSP(s);

                if (factionStrongPoints.TryGetValue(s.Faction.loadID, out int cur))
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
                factionStrongPoints[k] = Mathf.RoundToInt(factionStrongPoints[k] * mult);
            }
        }

        public int GetFactionSP(Faction f)
        {
            if (f == null) return 0;
            factionStrongPoints.TryGetValue(f.loadID, out int val);
            return val;
        }

        private int GetSettlementSP(Settlement s)
        {
            if (s is LargeCity) return 3;
            if (s is SmallCity) return 2;
            if (s is Village) return 1;
            return 1;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref factionStrongPoints, "factionStrongPoints",
                LookMode.Value, LookMode.Value);
        }
    }

    public class SettlementStrongPointComp : WorldObjectComp
    {
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos())
                yield return g;

            yield return new Command_Action
            {
                defaultLabel = "Show StrongPoint",
                action = () =>
                {
                    var comp = Find.World.GetComponent<FactionStrongPointWorldComp>();
                    int sp = comp.GetFactionSP(parent.Faction);
                    Messages.Message("StrongPoint: " + sp, MessageTypeDefOf.NeutralEvent);
                }
            };
        }

        public override void PostMapGenerate()
        {
            base.PostMapGenerate();
            Show();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
        }

        public override void PostAdd()
        {
            base.PostAdd();
            Show();
        }

        private void Show()
        {
            var comp = Find.World.GetComponent<FactionStrongPointWorldComp>();
            int sp = comp.GetFactionSP(parent.Faction);
            Messages.Message("StrongPoint: " + sp, MessageTypeDefOf.NeutralEvent);
        }
    }
}
