using RimWorld.Planet;
using Verse;
using RimWorld;
using System.Linq;
using UnityEngine;
using System.Reflection;

namespace RimDominion
{
    public class WorldWarManager : WorldComponent
    {
        public bool siegeActive;
        public int now = 0;
        public int interval;
        public int siegeNow;
        public int startSiegeTick;
        public float travelProgress;
        public float randomDivider = Rand.Range(10, 15);

        public Settlement attacker;
        public Settlement defender;

        private static FieldInfo cachedMatField = typeof(Settlement).GetField("cachedMat", BindingFlags.Instance | BindingFlags.NonPublic);

        public WorldWarManager(World world) : base(world)
        {
        }

        public override void WorldComponentTick()
        {
            var goodwill = world.GetComponent<DynamicFactionGoodwill>();
            if (goodwill == null) return;
            var comp = Find.World.GetComponent<WorldWarEvent>();
            if (interval == 0)
                interval = Prefs.DevMode ? 1 : Mathf.RoundToInt(Rand.Range(2, 10) * 60000);

            now++;
            if (now < interval) return;
            now = 0;
            if (comp.IsWWState)
                interval = Prefs.DevMode ? 1 : Mathf.RoundToInt(Rand.Range(1, 6) * 60000);
            else interval = Prefs.DevMode ? 1 : Mathf.RoundToInt(Rand.Range(2, 10) * 60000);

            if (!siegeActive)
            {
                TryStartSiege();
                if (Prefs.DevMode)
                    ResolveSiege();
            }
            else
            {
                SiegeTick();
            }

        }

        void TryStartSiege()
        {
            var factions = Find.FactionManager.AllFactionsListForReading.Where(f => f != null && !f.def.hidden && f != Faction.OfPlayer).ToList();

            if (factions.Count < 2) return;

            var attackerFaction = factions.RandomElement();
            var neighborManager = Find.World.GetComponent<NeighborManager>();
            var hostileFactions = factions
                .Where(f => f != attackerFaction && attackerFaction.HostileTo(f) && neighborManager.IsNeighbor(attackerFaction, f))
                .ToList();

            if (hostileFactions.Count == 0) return;

            var defenderFaction = hostileFactions.RandomElement();

            var attackerSettlement = Find.WorldObjects.Settlements
                .Where(s => s.Faction == attackerFaction && !(s is Village)).RandomElement();

            var defenderSettlements = Find.WorldObjects.Settlements
                .Where(s => s.Faction == defenderFaction && !(s is Village))
                .ToList();

            if (defenderSettlements.Count == 0) return;
            Settlement defender = null;
            float minDist = float.MaxValue;

            foreach (var d in defenderSettlements)
            {
                float dist = Find.WorldGrid.ApproxDistanceInTiles(attackerSettlement.Tile, d.Tile);
                if (dist < minDist)
                {
                    minDist = dist;
                    defender = d;
                }
            }

            if (defender == null) return;

            var attackerSettlements = Find.WorldObjects.Settlements.Where(s => s.Faction == attackerSettlement.Faction && !(s is Village)).ToList();

            this.defender = defender;
            float minDist2 = float.MaxValue;
            foreach (var a in attackerSettlements)
            {
                float dist = Find.WorldGrid.ApproxDistanceInTiles(a.Tile, defender.Tile);
                if (dist < minDist2)
                {
                    minDist2 = dist;
                    attacker = a;
                }
            }
            if (attacker == null) return;

            var spComp = Find.World.GetComponent<SettlementStrongPointComp>();
            float distanceToTarget = Find.WorldGrid.ApproxDistanceInTiles(attacker.Tile, defender.Tile);

            spComp.ReduceSP(attacker, spComp.GetCurrentSP(attacker) / randomDivider);
            if (Prefs.DevMode)
            {
                siegeActive = true;
                Log.Message($"[WAR DEV] Siege started: {attacker.Name} ({attacker.Faction.Name}) → {defender.Name} ({defender.Faction.Name}) | Distance {distanceToTarget:F1}");
            }
            else
            {
                siegeActive = false;
                startSiegeTick = Find.TickManager.TicksGame + Mathf.RoundToInt(60000f * distanceToTarget / 6f);
                travelProgress = 0f;
            }
        }

        void SiegeTick()
        {
            if (!siegeActive)
            {
                if (Find.TickManager.TicksGame < startSiegeTick || defender == null || attacker == null) return;
                siegeActive = true;
                siegeNow = 0;
                Log.Message($"[WAR] Siege started: {attacker.Name} ({attacker.Faction.Name}) → {defender.Name} ({defender.Faction.Name})");
            }

            if (!Prefs.DevMode)
            {
                siegeNow++;
                int warTime = Rand.Range(1, 5) * 25000;
                if (siegeNow >= warTime)
                    ResolveSiege();
            }
        }

        void ResolveSiege()
        {
            if (attacker == null || defender == null) return;

            var spComp = Find.World.GetComponent<SettlementStrongPointComp>();
            float defenderSP = spComp.GetCurrentSP(defender);
            float attackerSP = spComp.GetCurrentSP(attacker) / randomDivider;

            float dynamicSP = attackerSP - defenderSP;
            float sigmoid = 1f / (1f + Mathf.Exp(-0.1f * dynamicSP));
            float percent = Rand.Range(0f, 1f);

            if (percent < sigmoid)
            {
                spComp.ReduceSP(defender, attackerSP);
                var tracker = Find.World.GetComponent<CapitalTracker>();

                if (defender.Faction != null)
                {
                    tracker.lastOwnerFaction[defender.ID] = defender.Faction.loadID;
                }
                defender.SetFaction(attacker.Faction);
                cachedMatField?.SetValue(defender, null);
                Find.World.renderer.Notify_StaticWorldObjectPosChanged();
                if (Prefs.DevMode)
                    Log.Message($"[WAR] Faction {attacker.Faction.Name} captured settlement {defender.Name}");
            }
            else
            {
                if (Prefs.DevMode)
                    Log.Message($"[WAR] Siege failed: {defender.Name} resisted {attacker.Faction.Name}");
            }

            siegeActive = false;
            siegeNow = 0;
            attacker = null;
            defender = null;
        }
    }
}