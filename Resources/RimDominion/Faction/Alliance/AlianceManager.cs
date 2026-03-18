using Verse;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;

namespace RimDominion
{
    public class AllianceManager : WorldComponent
    {
        public List<Alliance> alliances = new List<Alliance>();

        private int nextAllianceID = 1;

        private int daysPassed = 0;
        private int lastTickCheck = 0;

        public AllianceManager(World world) : base(world)
        {
        }

        public override void WorldComponentTick()
        { // a day = 60000
            if (Find.TickManager.TicksGame - lastTickCheck < 60)
                return;

            lastTickCheck = Find.TickManager.TicksGame;
            daysPassed++;

            TryCreateAlliances();
            TryExpandAlliances();
            CleanupAlliances();
        }

        private float Sigmoid(float x)
        {
            const float c = 0.000001f;
            return 1f / (1f + (float)System.Math.Exp(-c * x));
        }

        private bool InAnyAlliance(Faction a)
        {
            foreach (var alliance in alliances)
            {
                if (alliance.members.Contains(a.loadID))
                    return true;
            }
            return false;
        }

        private void TryCreateAlliances()
        {
            var factions = Find.FactionManager.AllFactionsListForReading;

            for (int i = 0; i < factions.Count; i++)
            {
                for (int j = i + 1; j < factions.Count; j++)
                {
                    var a = factions[i];
                    var b = factions[j];

                    if (a == b) continue;
                    if (a == Faction.OfPlayer || a.Hidden || b == Faction.OfPlayer || b.Hidden) continue;

                    if (a.GoodwillWith(b) == 100)
                    {
                        if (InAnyAlliance(a) || InAnyAlliance(b))
                            return;
                        float x = daysPassed;
                        float chance = Sigmoid(x);

                        if (Rand.Value < chance)
                        {
                            CreateAlliance(a, b);
                        }
                    }
                }
            }
        }

        private void TryExpandAlliances()
        {
            var factions = Find.FactionManager.AllFactionsListForReading;

            foreach (var alliance in alliances)
            {
                if (alliance.members.Count >= 5)
                    continue;
                foreach (var f in factions)
                {
                    if (alliance.members.Contains(f.loadID))
                        continue;

                    if (InAnyAlliance(f)) continue;

                    bool hostile = false;
                    int liked = 0;

                    foreach (var memberID in alliance.members)
                    {
                        var m = Find.FactionManager.AllFactionsListForReading.Where(fac => fac.loadID == memberID).FirstOrDefault();

                        if (m.HostileTo(f))
                        {
                            hostile = true;
                            break;
                        }

                        if (m.GoodwillWith(f) > 80)
                            liked++;
                    }

                    if (hostile) continue;

                    if (liked >= alliance.members.Count * 0.5f)
                    {
                        float x =daysPassed;
                        float chance = Sigmoid(x);

                        if (Rand.Value < chance)
                        {
                            alliance.members.Add(f.loadID);
                        }
                    }
                }
            }
        }

        private void CreateAlliance(Faction a, Faction b)
        {
            Alliance alliance = new Alliance();

            alliance.id = nextAllianceID++;
            alliance.name = a.Name + " Pact";

            alliance.members.Add(a.loadID);
            alliance.members.Add(b.loadID);

            alliances.Add(alliance);
        }

        private void CleanupAlliances()
        {
            for (int i = alliances.Count - 1; i >= 0; i--)
            {
                var alliance = alliances[i];

                alliance.members.RemoveWhere(id => Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.loadID == id) == null);

                if (alliance.members.Count == 0)
                {
                    alliances.RemoveAt(i);
                }
            }
        }
    }
}
