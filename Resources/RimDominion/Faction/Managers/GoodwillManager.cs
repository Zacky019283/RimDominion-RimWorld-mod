using RimWorld;
using Verse;
using RimWorld.Planet;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace RimDominion
{
    public class DynamicFactionGoodwill : WorldComponent
    {
        private int tickCounter;
        private int ticksPerMonth = 60000 * 15;

        private Dictionary<PairKey, int> warStartTick = new Dictionary<PairKey, int>();
        private Dictionary<PairKey, int> pairOffsets = new Dictionary<PairKey, int>();
        private int warDuration = 60000 * 25;
        private bool initialized;

        public DynamicFactionGoodwill(World world) : base(world)
        {
        }

        public override void WorldComponentTick()
        {
            tickCounter++;
            if (!initialized)
                SimulateDiplomacy();

            if (tickCounter >= ticksPerMonth && !Prefs.DevMode)
            {
                SimulateDiplomacy();
                tickCounter = 0;
            }
            else if (tickCounter >= 60 && Prefs.DevMode)
            {
                SimulateDiplomacy();
                tickCounter = 0;
            }

            HandleWars();
        }

        private void SimulateDiplomacy()
        {
            var factions = Find.FactionManager.AllFactionsListForReading;
            var neighborComp = Find.World.GetComponent<NeighborManager>();
            var allianceComp = Find.World.GetComponent<AllianceManager>();

            for (int i = 0; i < factions.Count; i++)
            {
                var a = factions[i];
                if (a == Faction.OfPlayerSilentFail || a.def.hidden) continue;

                for (int j = i + 1; j < factions.Count; j++)
                {
                    var b = factions[j];
                    if (b == Faction.OfPlayerSilentFail || b.def.hidden) continue;
                    if (neighborComp.IsNeighbor(a, b))
                    {
                        a.TryAffectGoodwillWith(b, -100, false, false);
                        continue;
                    }

                    if (!initialized)
                    {
                        if (neighborComp.IsNeighbor(a, b))
                        {
                            a.TryAffectGoodwillWith(b, Mathf.RoundToInt(Rand.Range(-30, 40)), false, false);
                        }
                        else
                            a.TryAffectGoodwillWith(b, Mathf.RoundToInt(Rand.Range(-5, 7)), false, false);
                        initialized = true;
                        continue;
                    }

                    int goodwill = a.GoodwillWith(b);

                    float resistance = Mathf.Abs(goodwill) / 100f;
                    float inertia = 1f - resistance;

                    float noise = Rand.Range(-5f, 5f) * inertia;
                    float neighborDiplomacy = 0;

                    if (neighborComp != null)
                    {
                        var neighbors = neighborComp.GetNeighbors(a);
                        if (neighbors != null && neighbors.Contains(b.loadID))
                            neighborDiplomacy = Rand.Range(-5, 8);
                    }

                    float rivalry = 0f;

                    if (a.def.techLevel != b.def.techLevel)
                        rivalry -= 1.5f;

                    if (a.GoodwillWith(b) <= -100)
                        rivalry -= 2f;

                    float alliancePull = 0f;

                    if (allianceComp != null)
                    {
                        foreach (var al in allianceComp.alliances)
                        {
                            bool aIn = al.members.Contains(a.loadID);
                            bool bIn = al.members.Contains(b.loadID);

                            if (aIn && bIn)
                            {
                                float target = 80f;
                                alliancePull += (target - goodwill) * 0.05f;
                            }
                        }
                    }

                    float equilibrium = (0f - goodwill) * 0.02f;

                    float deltaFloat = noise + neighborDiplomacy + alliancePull + equilibrium + rivalry;

                    int delta = Mathf.RoundToInt(deltaFloat);

                    if (!neighborComp.IsNeighbor(a, b) && ((a.GoodwillWith(b) <= -40 && delta < 0) || (a.GoodwillWith(b) >= 80 && delta > 0)))
                    {
                        if (Rand.Value >= 0.01)
                            a.TryAffectGoodwillWith(b, Rand.RangeInclusive(-20, -5));
                        continue;
                    }

                    if (a.HostileTo(b))
                        continue;

                    if (Rand.Value <= 0.02)
                        delta -= Mathf.RoundToInt(Rand.Range(10, 20));

                    var alliances = Find.World.GetComponent<AllianceManager>().alliances;

                    foreach (var alliance in alliances)
                    {
                        if ((alliance.members.Contains(a.loadID) && !alliance.members.Contains(b.loadID)) || (!alliance.members.Contains(a.loadID) && alliance.members.Contains(b.loadID)))
                        {
                            delta -= 2;
                            break;
                        }
                    }

                    if (delta == 0) continue;

                    if ((goodwill <= -100 && delta < 0) || (goodwill >= 100 && delta > 0))
                        continue;

                    a.TryAffectGoodwillWith(b, delta, false, false);

                    if (a.GoodwillWith(b) <= -80)
                        a.HostileTo(b);
                    else
                        a.AllyOrNeutralTo(b);

                    Log.Message($"{a.Name} goodwill with {b.Name} is {a.GoodwillWith(b)}");
                }
            }
        }

        private void HandleWars()
        {
            var factions = Find.FactionManager.AllFactionsListForReading;
            int currentTick = Find.TickManager.TicksGame;

            for (int i = 0; i < factions.Count; i++)
            {
                var a = factions[i];
                if (a == Faction.OfPlayerSilentFail || a.def.hidden) continue;

                for (int j = i + 1; j < factions.Count; j++)
                {
                    var b = factions[j];
                    if (b == Faction.OfPlayerSilentFail || b.def.hidden) continue;

                    var key = PairKey.Of(a.loadID, b.loadID);

                    if (a.HostileTo(b))
                    {
                        if (!warStartTick.ContainsKey(key))
                            warStartTick[key] = currentTick;

                        int startTick = warStartTick[key];

                        if (currentTick - startTick >= warDuration)
                        {
                            StopWar(a, b);
                            warStartTick.Remove(key);
                        }
                    }
                    else
                    {
                        if (warStartTick.ContainsKey(key))
                            warStartTick.Remove(key);
                    }
                }
            }
        }

        private void StopWar(Faction a, Faction b)
        {
            int goodwillNow = a.GoodwillWith(b);
            a.TryAffectGoodwillWith(b, -goodwillNow - 30, false);
            a.AllyOrNeutralTo(b);
        }

        public struct PairKey : IEquatable<PairKey>
        {
            public int A;
            public int B;

            public PairKey(int a, int b)
            {
                if (a < b)
                {
                    A = a;
                    B = b;
                }
                else
                {
                    A = b;
                    B = a;
                }
            }

            public static PairKey Of(int a, int b)
            {
                return new PairKey(a, b);
            }

            public bool Equals(PairKey other)
            {
                return A == other.A && B == other.B;
            }

            public override bool Equals(object obj)
            {
                return obj is PairKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (A * 397) ^ B;
            }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref tickCounter, "tickCounter");
            Scribe_Values.Look(ref warDuration, "warDuration");
            Scribe_Values.Look(ref initialized, "initialized");
        }
    }
}