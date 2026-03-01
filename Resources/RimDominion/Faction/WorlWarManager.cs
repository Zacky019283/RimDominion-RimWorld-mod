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

        public Settlement attacker;
        public Settlement defender;

        private static FieldInfo cachedMatField = typeof(Settlement).GetField("cachedMat", BindingFlags.Instance | BindingFlags.NonPublic);

        public WorldWarManager(World world) : base(world)
        {
            if (Find.World.GetComponent<WorldWarManager>() == null)
                Find.World.components.Add(this);
        }

        public override void WorldComponentTick()
        {
            var goodwill = world.GetComponent<DynamicFactionGoodwill>();
            if (goodwill == null || !goodwill.warSituation) return;

            if (interval == 0)
                interval = Prefs.DevMode ? 1 : Mathf.RoundToInt(Rand.Range(2, 10) * 60000);

            now++;
            if (now < interval) return;
            now = 0;

            if (!siegeActive)
            {
                TryStartSiege();
                Log.Message("TryStartSiege: ON");
            }
            else
            {
                SiegeTick();
            }

            if (Prefs.DevMode && siegeActive)
                ResolveSiege();
        }

        void TryStartSiege()
        {
            var settlements = Find.WorldObjects.Settlements.Cast<Settlement>()
                .Where(s => s.Faction != null && !s.Faction.def.hidden && s.Faction != Faction.OfPlayer)
                .ToList();
            if (settlements.Count < 2) return;

            Settlement attacker = settlements.RandomElement();

            Settlement defender = null;
            float minDist = float.MaxValue;

            foreach (var s in settlements)
            {
                if (s == attacker) continue;
                float dist = Find.WorldGrid.ApproxDistanceInTiles(attacker.Tile, s.Tile);
                if (dist < minDist)
                {
                    minDist = dist;
                    defender = s;
                }
            }

            if (defender == null) return;

            var nearbyAttackers = settlements
                .Where(s => s != defender && Find.WorldGrid.ApproxDistanceInTiles(s.Tile, defender.Tile) <= 10f)
                .ToList();

            if (nearbyAttackers.Any())
                attacker = nearbyAttackers.RandomElement();
            else
            {
                float closestDist = float.MaxValue;
                foreach (var s in settlements)
                {
                    if (s == defender) continue;
                    float dist = Find.WorldGrid.ApproxDistanceInTiles(s.Tile, defender.Tile);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        attacker = s;
                    }
                }
            }

            this.attacker = attacker;
            this.defender = defender;

            float distanceToTarget = Find.WorldGrid.ApproxDistanceInTiles(attacker.Tile, defender.Tile);

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
            float attackerSP = spComp.GetCurrentSP(attacker) / 10f;

            float dynamicSP = attackerSP - defenderSP;
            float sigmoid = 1f / (1f + Mathf.Exp(-0.1f * dynamicSP));
            float percent = Rand.Range(0f, 1f);

            if (percent < sigmoid)
            {
                defender.SetFaction(attacker.Faction);
                cachedMatField?.SetValue(attacker, null);
                Find.World.renderer.Notify_StaticWorldObjectPosChanged();
                Log.Message($"[WAR] Faction {attacker.Faction.Name} captured settlement {defender.Name}");
            }
            else
            {
                Log.Message($"[WAR] Siege failed: {defender.Name} resisted {attacker.Faction.Name}");
            }

            siegeActive = false;
            siegeNow = 0;
            attacker = null;
            defender = null;
        }
    }
}