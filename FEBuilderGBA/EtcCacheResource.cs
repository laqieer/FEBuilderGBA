using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FEBuilderGBA
{
    class EtcCacheResource
    {
        Dictionary<string, string> Resource;
        public EtcCacheResource()
        {
            this.Resource = U.LoadTSVResourcePair2(U.ConfigEtcFilename("resource_"), false);
        }

        public int Count
        {
            get
            {
                return this.Resource.Count;
            }
        }

        public string ListAll()
        {
            StringBuilder sb = new StringBuilder();
            foreach (var pair in this.Resource)
            {
                sb.AppendLine(pair.Key + "\t" + pair.Value);
            }
            return sb.ToString();
        }

        public bool TryGetValue(string name, out string out_data)
        {
            return Resource.TryGetValue(name, out out_data);
        }

        public void Update(string name, string value)
        {
            this.Resource[name] = value;
        }

        public void Remove(string name)
        {
            if (this.Resource.ContainsKey(name))
            {
                this.Resource.Remove(name);
            }
        }

        public void Save(string romBaseFilename)
        {
            U.SaveConfigEtcTSVPair("resource_", this.Resource, romBaseFilename);
        }
    }
}
