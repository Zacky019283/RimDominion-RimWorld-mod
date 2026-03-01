using RimWorld;
using Verse;
using RimWorld.Planet;
using System.Linq;
using HarmonyLib;

namespace RimDominion
{
	[HarmonyPatch(typeof(FactionGenerator))]
	[HarmonyPatch(nameof(FactionGenerator.NewGeneratedFaction),
		new System.Type[] { typeof(PlanetLayer), typeof(FactionGeneratorParms) })]
	public static class Patch_FactionInitialSettlementIsCapital
	{
		static bool Prefix(PlanetLayer layer, FactionGeneratorParms parms, ref Faction __result)
		{
			FactionDef factionDef = parms.factionDef;
			parms.ideoGenerationParms.forFaction = factionDef;
			Faction faction = new Faction();
			faction.def = factionDef;
			faction.loadID = Find.UniqueIDsManager.GetNextFactionID();
			faction.colorFromSpectrum = FactionGenerator.NewRandomColorFromSpectrum(faction);
			faction.hidden = parms.hidden;
			if (factionDef.humanlikeFaction)
			{
				faction.ideos = new FactionIdeosTracker(faction);
				if (!faction.IsPlayer || !ModsConfig.IdeologyActive || !Find.GameInitData.startedFromEntry)
				{
					faction.ideos.ChooseOrGenerateIdeo(parms.ideoGenerationParms);
				}
			}
			if (!factionDef.isPlayer)
			{
				if (factionDef.fixedName != null)
				{
					faction.Name = factionDef.fixedName;
				}
				else
				{
					string text = "";
					for (int i = 0; i < 10; i++)
					{
						string text2 = NameGenerator.GenerateName(faction.def.factionNameMaker, Find.FactionManager.AllFactionsVisible.Select((Faction fac) => fac.Name), false, null);
						if (text2.Length <= 20)
						{
							text = text2;
						}
					}
					if (text.NullOrEmpty())
					{
						text = NameGenerator.GenerateName(faction.def.factionNameMaker, Find.FactionManager.AllFactionsVisible.Select((Faction fac) => fac.Name), false, null);
					}
					faction.Name = text;
				}
			}
			foreach (Faction faction2 in Find.FactionManager.AllFactionsListForReading)
			{
				faction.TryMakeInitialRelationsWith(faction2);
			}
			if (!faction.Hidden && !factionDef.isPlayer)
			{
				bool hasCapital = Find.WorldObjects.AllWorldObjects
					.OfType<Settlement>()
					.Any(s => s.Faction == faction && s is CapitalSettlement);

				if (!hasCapital)
				{
					int tile = -1;
					int tries = 20;

					while (tries-- > 0 && tile < 0)
					{
						tile = TileFinder.RandomSettlementTileFor(layer, faction, false, null);
					}

					if (tile >= 0)
					{
						CapitalSettlement capital =
							(CapitalSettlement)WorldObjectMaker.MakeWorldObject(
								DefDatabase<WorldObjectDef>.AllDefsListForReading
								.First(d => d.worldObjectClass == typeof(CapitalSettlement))
							);

						capital.SetFaction(faction);
						capital.Tile = tile;

						if (capital is INameableWorldObject nameable)
						{
							string name = SettlementNameGenerator.GenerateSettlementName(capital, null) + " (Capital)";
							if (name.Contains("Village"))
								name = name.Replace(" Village", "").Trim();
							nameable.Name = name;
						}

						Find.WorldObjects.Add(capital);
					}
				}
			}
			faction.TryGenerateNewLeader();
			__result = faction;
			return false;
		}
	}

	public class WorldObjectCompProperties_Capital : WorldObjectCompProperties
	{
		public WorldObjectCompProperties_Capital()
		{
			this.compClass = typeof(WorldObjectComp_Capital);
		}
	}

	public class WorldObjectComp_Capital : WorldObjectComp
	{
		public bool isCapital = false;

		public WorldObjectComp_Capital() { }

		public override void PostExposeData()
		{
			base.PostExposeData();
			Scribe_Values.Look(ref isCapital, "isCapital", false);
		}

		public bool IsCapital(Settlement s)
		{
			var comp = s.GetComponent<WorldObjectComp_Capital>();
			return comp != null && comp.isCapital;
		}
	}
}