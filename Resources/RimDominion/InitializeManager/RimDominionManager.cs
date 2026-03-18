using RimWorld.Planet;

namespace RimDominion
{
    public class RimDominionManager : WorldComponent
    {
        CapitalTracker capitals;
        NeighborManager neighbor;
        GenerateSettlementsBond sbond;

        public RimDominionManager(World world) : base(world)
        {
            neighbor = world.GetComponent<NeighborManager>();
            capitals = world.GetComponent<CapitalTracker>();
            sbond = world.GetComponent<GenerateSettlementsBond>();
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            if (!fromLoad)
            {
                sbond?.CustomFinalize();
                neighbor?.CustomFinalize();
                capitals?.CustomFinalize();
            }
        }
    }
}
