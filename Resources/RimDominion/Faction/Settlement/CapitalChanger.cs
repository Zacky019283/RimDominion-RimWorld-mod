using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;
using System.Reflection;

namespace RimDominion
{
    public class CapitalTracker : WorldComponent
    {
        private Dictionary<int, int> factionCapitalMap = new Dictionary<int, int>();
        public Dictionary<int, int> lastOwnerFaction = new Dictionary<int, int>();
        public Dictionary<int, string> originalTierByTile = new Dictionary<int, string>();
        private static FieldInfo cachedMatField = typeof(Settlement).GetField("cachedMat", BindingFlags.Instance | BindingFlags.NonPublic);
        private static FieldInfo allFactionsField = typeof(FactionManager).GetField("allFactions", BindingFlags.Instance | BindingFlags.NonPublic);
        private int now;
        private bool initialized;

        public CapitalTracker(World world) : base(world) { }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
        }

        public void CustomFinalize()
        {
            if (!initialized)
            {
                CacheCapitals();
                initialized = true;
            }
        }

        public override void WorldComponentTick()
        {
            now++;
            if (now >= 120)
            {
                now = 0;
                DownGradeCapital();
                generateCapital();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref factionCapitalMap, "factionCapitalMap", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref lastOwnerFaction, "lastOwnerFaction", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                CacheCapitals();
            }
        }

        public void CacheCapitals()
        {
            factionCapitalMap.Clear();

            foreach (var settlement in Find.WorldObjects.Settlements.OfType<CapitalSettlement>())
            {
                if (settlement.Faction != null)
                {
                    factionCapitalMap[settlement.Faction.loadID] = settlement.ID;

                    if (!originalTierByTile.ContainsKey(settlement.Tile.tileId))
                    {
                        originalTierByTile[settlement.Tile.tileId] = "L";
                    }
                }
            }
        }

        public int GetCapitalName(Faction faction)
        {
            if (faction != null && factionCapitalMap.TryGetValue(faction.loadID, out var value))
            {
                return value;
            }
            return 0;
        }

        public void DownGradeCapital()
        {
            var factionsWithExtraCapital = Find.FactionManager.AllFactionsVisible
                .Select(f => new
                {
                    Faction = f,
                    Capitals = Find.WorldObjects.Settlements
                        .OfType<CapitalSettlement>()
                        .Where(s => s.Faction == f)
                        .ToList()
                })
                .Where(x => x.Capitals.Count > 1)
                .ToList();

            foreach (var entry in factionsWithExtraCapital)
            {
                int factionId = entry.Faction.loadID;

                int savedCapitalID = -1;
                factionCapitalMap.TryGetValue(factionId, out savedCapitalID);

                var capitalsToDowngrade = entry.Capitals
                    .Where(c => c.ID != savedCapitalID)
                    .ToList();

                foreach (var capital in capitalsToDowngrade)
                {
                    if (!originalTierByTile.TryGetValue(capital.Tile.tileId, out string tier)) continue;
                    int tile = capital.Tile;
                    Faction faction = capital.Faction;
                    string label = "";
                    Settlement newCity = null;
                    if (tier == "S")
                    {
                        label = capital.Name.Replace("(Capital)", "(Small)").Trim();
                        newCity = (SmallCity)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldObjectDef>.GetNamed("SmallCity"));
                    }
                    else if (tier == "L")
                    {
                        label = capital.Name.Replace("(Capital)", "(Large)").Trim();
                        newCity = (LargeCity)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldObjectDef>.GetNamed("LargeCity"));
                    }

                    newCity.Tile = tile;
                    newCity.SetFaction(faction);
                    newCity.Name = label;
                    newCity.ID = capital.ID;

                    Find.WorldObjects.Remove(capital);
                    Find.WorldObjects.Add(newCity);
                }
            }
        }

        public void generateCapital()
        {
            var factionWithoutCapital = Find.FactionManager.AllFactionsListForReading.Where(f => !Find.WorldObjects.Settlements.OfType<CapitalSettlement>().Any(s => s.Faction == f) && f != Faction.OfPlayer && !f.Hidden).ToList();

            foreach (var faction in factionWithoutCapital.ToList())
            {
                if (faction == Faction.OfPlayer || faction.Hidden) continue;
                var comp = Find.World.GetComponent<FactionStrongPointWorldComp>();
                if (comp == null) return;
                float chance = float.MaxValue;
                float sigmoid = 1 / (1 + Mathf.Exp(-comp.GetFactionSP(faction) * 0.05f));
                if (chance > sigmoid)
                {
                    TransferSettlements(faction);
                    FactionDefeated(faction);
                    faction.defeated = true;
                    continue;
                }
                var settlements = Find.WorldObjects.Settlements
                    .Where(s => s.Faction == faction)
                    .ToList();

                var largeC = settlements.OfType<LargeCity>().RandomElementWithFallback();
                var smallC = settlements.OfType<SmallCity>().RandomElementWithFallback();

                Settlement chosen = null;
                if (largeC != null)
                {
                    chosen = largeC;
                    originalTierByTile[largeC.ID] = "L";
                }
                else if (largeC == null && smallC != null)
                {
                    chosen = smallC;
                    originalTierByTile[smallC.ID] = "S";
                }

                if (chosen == null)
                {
                    TransferSettlements(faction);
                    FactionDefeated(faction);
                    faction.defeated = true;
                    continue;
                }

                if (chance > 0.5)
                {
                    faction.TryGenerateNewLeader();
                }

                int tile = chosen.Tile;
                string capName = "No Name";
                if (chosen == largeC)
                    capName = chosen.Name.Replace("(Large)", "(Capital)").Trim();
                else if (chosen == smallC)
                    capName = chosen.Name.Replace("(Small)", "(Capital)").Trim();

                Find.WorldObjects.Remove(chosen);

                CapitalSettlement newCap = (CapitalSettlement)WorldObjectMaker.MakeWorldObject(
                    DefDatabase<WorldObjectDef>.GetNamed("CapitalSettlement"));

                newCap.Tile = tile;
                newCap.SetFaction(faction);
                newCap.Name = capName;
                newCap.ID = chosen.ID;

                Find.WorldObjects.Add(newCap);
                CacheCapitals();
            }
        }

        Faction GetLastOwnerFaction(Faction defeatedFaction)
        {

            var settlements = Find.WorldObjects.Settlements
                .Where(s => s.Faction == defeatedFaction)
                .ToList();

            foreach (var s in settlements)
            {
                if (lastOwnerFaction.TryGetValue(s.ID, out int lastID))
                {
                    var f = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(fID => fID.loadID == lastID);
                    if (f != null)
                        return f;
                }
            }

            return null;
        }

        public void TransferSettlements(Faction faction)
        {
            Faction newOwner = GetLastOwnerFaction(faction);

            if (newOwner == null)
                return;

            var settlements = Find.WorldObjects.Settlements
                .Where(s => s.Faction == faction)
                .ToList();

            foreach (var s in settlements)
            {
                s.SetFaction(newOwner);
                cachedMatField.SetValue(s, null);
                Find.World.renderer.Notify_StaticWorldObjectPosChanged();
            }
        }

        public void FactionDefeated(Faction faction)
        {
            if (faction == null) return;

            List<Faction> F = Find.FactionManager.AllFactionsListForReading.Where(f => !f.def.hidden && f != faction).ToList();

            foreach (var f in F)
            {
                int goodwillValue = f.GoodwillWith(faction);

                f.TryAffectGoodwillWith(faction, -goodwillValue, false, false);
            }
            var pawns = Find.WorldPawns.AllPawnsAliveOrDead.Where(p => p.Faction == faction).ToList();

            foreach (var p in pawns)
            {
                p.SetFaction(null);
            }

            List<Faction> allFactions = (List<Faction>)allFactionsField.GetValue(Find.FactionManager);
            allFactions.Remove(faction);
        }
    }
}