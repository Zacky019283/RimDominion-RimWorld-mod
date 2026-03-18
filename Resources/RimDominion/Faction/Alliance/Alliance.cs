using System.Collections.Generic;

namespace RimDominion
{
    public class Alliance
    {
        public int id;
        public string name;
        public HashSet<int> members = new HashSet<int>();
    }
}
