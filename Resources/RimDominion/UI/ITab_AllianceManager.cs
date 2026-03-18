using Verse;
using RimWorld;
using UnityEngine;
using System.Linq;

namespace RimDominion
{
    public class ITab_AllianceManager : MainTabWindow
    {
        private Vector2 scrollPosition;

        public override Vector2 InitialSize => new Vector2(800f, 600f);

        public override void DoWindowContents(Rect rect)
        {
            var manager = Find.World.GetComponent<AllianceManager>();

            if (manager == null)
            {
                Widgets.Label(rect, "Alliance manager not found");
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 40f), "Global Alliances");
            Text.Font = GameFont.Small;

            Rect outRect = new Rect(0f, 50f, rect.width, rect.height - 50f);

            float height = manager.alliances.Count * 110f;
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, height);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            float y = 0f;

            foreach (var alliance in manager.alliances)
            {
                Rect box = new Rect(0f, y, viewRect.width, 100f);
                Widgets.DrawMenuSection(box);

                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(10f, y + 5f, 400f, 30f), alliance.name);
                Text.Font = GameFont.Small;

                float memberY = y + 35f;

                foreach (var memberID in alliance.members)
                {
                    Faction faction = Find.FactionManager.AllFactionsListForReading.Where(f => f.loadID == memberID).FirstOrDefault();
                    if (faction == null) continue;

                    Widgets.Label(new Rect(20f, memberY, 400f, 25f), "- " + faction.Name);
                    memberY += 22f;
                }

                y += 110f;
            }

            Widgets.EndScrollView();
        }
    }
}
