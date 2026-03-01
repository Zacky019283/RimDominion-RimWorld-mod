using RimWorld.Planet;
using Verse;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RimDominion
{
    public class WorldWarManager : WorldComponent
    {
        public bool caravanActive;
        public bool siegeActive;

        public int now;
        public int intervalTicks;

        public int siegeNow;
        public int siegeInterval;

        public Settlement attacker;
        public Settlement defender;
        public float caravanPower;

        public WorldWarManager(World world) : base(world)
        {
        }

        public override void WorldComponentTick()
        {
            var goodwill = world.GetComponent<DynamicFactionGoodwill>();
            if (goodwill == null || !goodwill.warSituation) return;

            if (!caravanActive && !siegeActive)
            {
                if (intervalTicks == 0)
                {
                    if (Prefs.DevMode)
                        intervalTicks = 1;
                    else intervalTicks = Rand.Range(2, 10) * 60000;
                }

                now++;

                if (now >= intervalTicks)
                {
                    now = 0;
                    intervalTicks = 0;
                    TryLaunchCaravan();
                }
            }
            else if (caravanActive)
            {
                TravelTick();
            }
            else if (siegeActive)
            {
                SiegeTick();
            }
        }

        void TryLaunchCaravan()
        {
            var settlements = Find.WorldObjects.Settlements.Cast<Settlement>()
                .Where(s => !s.Faction.def.hidden)
                .ToList();

            if (settlements.Count < 2) return;

            attacker = settlements.RandomElement();
            defender = FindNearestEnemySettlement(attacker);

            if (defender == null) return;

            float dist = Find.WorldGrid.TraversalDistanceBetween(attacker.Tile, defender.Tile);

            if (dist > 20f)
            {
                attacker = FindCloserSettlement(defender, settlements);
                if (attacker == null) return;
            }

            caravanPower = GetStrongPoint(attacker);
            caravanActive = true;
        }

        void TravelTick()
        {
            float dist = Find.WorldGrid.TraversalDistanceBetween(attacker.Tile, defender.Tile, false);
            float speedPerTick = 10f / 60000f;
            dist -= speedPerTick;

            if (dist <= 0f)
            {
                caravanActive = false;
                siegeActive = true;
                siegeNow = 0;
                siegeInterval = Rand.RangeInclusive(2, 5) * 2500;
            }
        }

        void SiegeTick()
        {
            siegeNow++;

            if (siegeNow < siegeInterval) return;

            siegeActive = false;
            var spComp = Find.World.GetComponent<SettlementStrongPointComp>();
            float settlementSP = spComp.GetCurrentSP(defender);
            float dynamicSP = settlementSP - caravanPower;

            float sigmoid = 1f / (1f + Mathf.Exp(-0.1f * dynamicSP));
            float percent = Rand.Range(0f, 1f);

            if (percent < sigmoid)
            {
                defender.SetFaction(attacker.Faction);
                Find.World.renderer.Notify_StaticWorldObjectPosChanged();
                Log.Message($"Faction {attacker.Faction.Name} was captured {defender.Name}");
            }
        }

        Settlement FindNearestEnemySettlement(Settlement from)
        {
            return Find.WorldObjects.Settlements
                .Where(s => s.Faction != from.Faction && !s.Faction.def.hidden)
                .OrderBy(s => Find.WorldGrid.TraversalDistanceBetween(from.Tile, s.Tile))
                .FirstOrDefault();
        }

        Settlement FindCloserSettlement(Settlement target, List<Settlement> pool)
        {
            return pool
                .Where(s => s.Faction != target.Faction)
                .OrderBy(s => Find.WorldGrid.TraversalDistanceBetween(s.Tile, target.Tile))
                .FirstOrDefault();
        }

        float GetStrongPoint(Settlement s)
        {
            var fsp = Find.World.GetComponent<FactionStrongPointWorldComp>();
            if (s.Faction == null) return 0f;
            return fsp.GetFactionSP(s.Faction) / 10f;
        }
    }
}