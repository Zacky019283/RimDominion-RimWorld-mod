using RimWorld;
using Verse;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using System;

namespace RimDominion
{
    public class DynamicFactionGoodwill : WorldComponent
    {
        private int TicksPerMonth;
        private int now = 0;
        private bool initialApplied = false;
        public bool warSituation = false;
        private List<int> keysA;
        private List<int> keysB;
        private List<int> values;

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
            CheckWarDeclarations();
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

                    var key = PairKey.Of(a.loadID, b.loadID);
                    pairOffsets.TryGetValue(key, out int x);

                    float sigmoid = 1f / (1f + Mathf.Exp(-0.2f * x));
                    float percent = Rand.Range(0f, 1f);

                    int magnitude = Rand.RangeInclusive(1, 6);
                    int delta = percent < sigmoid ? magnitude : -magnitude;

                    a.TryAffectGoodwillWith(b, delta, false, false);

                    pairOffsets[key] = x + delta;
                }
            }
        }

        private void CheckWarDeclarations()
        {
            var factions = Find.FactionManager.AllFactionsListForReading;

            for (int i = 0; i < factions.Count; i++)
            {
                var a = factions[i];
                if (a.def.hidden) continue;

                for (int j = i + 1; j < factions.Count; j++)
                {
                    var b = factions[j];
                    if (b.def.hidden) continue;

                    int goodwill = a.GoodwillWith(b);

                    if (goodwill <= -80)
                    {
                        if (!a.HostileTo(b))
                        {
                            a.HostileTo(b);
                        }
                        warSituation = true;
                    }
                }
            }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref TicksPerMonth, "TicksPerMonth");
            Scribe_Values.Look(ref now, "now");
            Scribe_Values.Look(ref initialApplied, "initialApplied");

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                keysA = new List<int>(pairOffsets.Count);
                keysB = new List<int>(pairOffsets.Count);
                values = new List<int>(pairOffsets.Count);

                foreach (var kv in pairOffsets)
                {
                    keysA.Add(kv.Key.A);
                    keysB.Add(kv.Key.B);
                    values.Add(kv.Value);
                }
            }

            Scribe_Collections.Look(ref keysA, "pairA", LookMode.Value);
            Scribe_Collections.Look(ref keysB, "pairB", LookMode.Value);
            Scribe_Collections.Look(ref values, "pairV", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pairOffsets = new Dictionary<PairKey, int>();

                if (keysA != null && keysB != null && values != null)
                {
                    int n = Mathf.Min(keysA.Count, keysB.Count, values.Count);
                    for (int i = 0; i < n; i++)
                    {
                        var key = PairKey.Of(keysA[i], keysB[i]);
                        pairOffsets[key] = values[i];
                    }
                }
            }
        }

        public struct PairKey : IEquatable<PairKey>
        {
            public int A;
            public int B;

            public PairKey(int a, int b)
            {
                A = a;
                B = b;
            }

            public static PairKey Of(int a, int b) => new PairKey(a, b);

            public bool Equals(PairKey other) => A == other.A && B == other.B;

            public override bool Equals(object obj) => obj is PairKey other && Equals(other);

            public override int GetHashCode() => (A * 397) ^ B;
        }
    }
}