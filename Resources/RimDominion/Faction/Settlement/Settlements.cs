using RimWorld.Planet;
using Verse;

namespace RimDominion
{
    [StaticConstructorOnStartup]
    public class HierarchySettlements : Settlement
    {
        public virtual float StrongPoint => 1f;
    }
    [StaticConstructorOnStartup]
    public class CapitalSettlement : HierarchySettlements
    {
        public override float StrongPoint => 10.3f;
    }
    [StaticConstructorOnStartup]
    public class LargeCity : HierarchySettlements
    {
        public override float StrongPoint => 7.4f;
    }
    [StaticConstructorOnStartup]
    public class SmallCity : HierarchySettlements
    {
        public override float StrongPoint => 4.7f;
    }
    [StaticConstructorOnStartup]
    public class Village : HierarchySettlements
    {
        public override float StrongPoint => 2.4f;
    }
}
