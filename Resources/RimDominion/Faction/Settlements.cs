using RimWorld.Planet;
using Verse;

namespace RimDominion
{
    [StaticConstructorOnStartup]
    public class CapitalSettlement : Settlement
    {
        public float StrongPoint = 2.5f;
    }
    [StaticConstructorOnStartup]
    public class Village : Settlement
    {
        public float StrongPoint = 0.7f;
    }
    [StaticConstructorOnStartup]
    public class SmallCity : Settlement
    {
        public float StrongPoint = 1f;
    }
    [StaticConstructorOnStartup]
    public class LargeCity : Settlement
    {
        public float StrongPoint = 2;
    }
}
