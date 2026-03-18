using RimWorld.Planet;
using RimWorld;
using Verse;

namespace RimDominion
{
    public class WorldWarEvent : WorldComponent
    {
        int numberofWW = 1;
        int numberofEnemy = 0;
        public bool IsWWState;
        public bool finishSendLetter;
        int now;

        public WorldWarEvent(World world) : base(world) { }

        public override void WorldComponentTick()
        {
            now++;
            if (!IsWWState && now >= 60)
                WWTriggerer();
            SendLetter();
        }

        public void WWTriggerer()
        {
            now = 0;
            var faction = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < faction.Count; i++)
            {
                Faction a = faction[i];
                if (a == Faction.OfPlayer || a.Hidden) continue;

                for (int j = i + 1; j <faction.Count; j++)
                {
                    Faction b = faction[j];

                    if (b == Faction.OfPlayer || b.Hidden || !a.HostileTo(b)) continue;
                    numberofEnemy++;
                }
            }

            if (numberofEnemy >= faction.Count / 1.3)
            {
                IsWWState = true;
                numberofEnemy = 0;
                numberofWW++;
            }

            else if (numberofEnemy <= faction.Count / 1.7)
            {
                IsWWState = false;
                finishSendLetter = false;
                numberofEnemy = 0;
            }
        }

        public void SendLetter()
        {
            if (!finishSendLetter && IsWWState)
            {
                numberofWW++;
                LetterMaker.MakeLetter($"World War {numberofWW}", $"Reports from across the planet speak of open war between major factions. Long-standing tensions have finally erupted into widespread conflict.\n\nMajor factions are mobilizing their forces and smaller settlements are being drawn into the conflict. WW{numberofWW} has begun.", LetterDefOf.NegativeEvent);
                finishSendLetter = true;
            }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref finishSendLetter, "finishSendLetter");
            Scribe_Values.Look(ref numberofWW, "NWW");
            Scribe_Values.Look(ref IsWWState, "IsWW");
        }
    }
}
