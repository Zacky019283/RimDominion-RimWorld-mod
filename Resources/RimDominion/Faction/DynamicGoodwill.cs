using RimWorld;
using Verse;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;

namespace RimDominion
{
    public class DynamicFactionGoodwill : WorldComponent
    {
        private int TicksPerMonth;
        private int now = 0;
        private bool initialApplied = false;

        private Dictionary<PairKey, int> pairOffsets = new Dictionary<PairKey, int>();

        public DynamicFactionGoodwill(World world) : base(world)
        {
        }

        private void ApplyInitialOffsets()
        {
            var factions = Find.FactionManager.AllFactionsListForReading;

            for (int i = 0; i < factions.Count; i++)
            {
                var a = factions[i];
                if (a == Faction.OfPlayerSilentFail || a.def.hidden) continue;

                for (int j = i + 1; j < factions.Count; j++)
                {
                    var b = factions[j];
                    if (b == Faction.OfPlayerSilentFail || b.def.hidden) continue;

                    int offset = Rand.RangeInclusive(-20, 20);

                    int cur = a.GoodwillWith(b);
                    int target = Mathf.Clamp(cur + offset, -100, 100);
                    int delta = target - cur;

                    if (delta == 0) continue;

                    a.TryAffectGoodwillWith(b, delta, false, false);

                    var key = PairKey.Of(a.loadID, b.loadID);
                    pairOffsets[key] = delta;
                }
            }
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);

            if (!fromLoad)
            {
                ApplyInitialOffsets();
                initialApplied = true;
            }
            if (!fromLoad && TicksPerMonth == 0)
                TicksPerMonth = 60000 * Rand.RangeInclusive(10, 20);
        }

        public override void WorldComponentTick()
        {
            now++;

            if (now >= TicksPerMonth)
            {
                ApplyMonthlyShift();
                now = 0;
            }
        }

        private void ApplyMonthlyShift()
        {
            var factions = Find.FactionManager.AllFactionsListForReading;

            for (int i = 0; i < factions.Count; i++)
            {
                var a = factions[i];
                if (a == Faction.OfPlayerSilentFail || a.def.hidden) continue;

                for (int j = i + 1; j < factions.Count; j++)
                {
                    var b = factions[j];
                    if (b == Faction.OfPlayerSilentFail || b.def.hidden) continue;
                    int delta = BiasedDelta(a.GoodwillWith(b));
                    a.TryAffectGoodwillWith(b, delta, false, false);

                    var key = PairKey.Of(a.loadID, b.loadID);
                    pairOffsets.TryGetValue(key, out int cur);
                    pairOffsets[key] = cur + delta;
                }
            }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref TicksPerMonth, "TicksPerMonth");
            Scribe_Values.Look(ref now, "now");
            Scribe_Values.Look(ref initialApplied, "initialApplied");

            Scribe_Collections.Look(ref pairOffsets, "pairOffsets",
                LookMode.Value, LookMode.Value);
        }

        public struct PairKey : System.IEquatable<PairKey>
        {
            public int A;
            public int B;

            public static PairKey Of(int id1, int id2)
            {
                return id1 < id2 ? new PairKey { A = id1, B = id2 }
                                 : new PairKey { A = id2, B = id1 };
            }

            public bool Equals(PairKey other) => A == other.A && B == other.B;
            public override int GetHashCode() => (A * 397) ^ B;
        }
        private int BiasedDelta(int goodwillNow)
        {
            int magnitude = Rand.RangeInclusive(1, 4);

            float chanceRaw = (goodwillNow + magnitude) / 40f;
            float chance = Mathf.Clamp(chanceRaw, 0.1f, 0.9f);

            bool positive = Rand.Value < chance;

            return positive ? magnitude : -magnitude;
        }
    }
}