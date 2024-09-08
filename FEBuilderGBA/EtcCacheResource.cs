using FEBuilderGBA.Properties;
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

        public string[] MakeCategoryList()
        {
            return this.Resource.Keys.Select(x => x.Split('_')[0]).Distinct().ToArray();
        }

        public string ListAll(int sortKey = 0, bool reversed = false, string filter = "")
        {
            if (filter != "")
            {
               filter += "_";
            }
            Dictionary<string, string> filtered = this.Resource.Where(x => x.Key.StartsWith(filter)).ToDictionary(x => x.Key, x => x.Value);
            StringBuilder sb = new StringBuilder();
            switch (sortKey)
            {
                case 1: // sort by category

                    if (reversed)
                    {
                        foreach (var pair in filtered.OrderByDescending(x => x.Key))
                        {
                            sb.AppendLine(pair.Key + "\t" + pair.Value);
                        }
                    }
                    else
                    {
                        foreach (var pair in filtered.OrderBy(x => x.Key))
                        {
                            sb.AppendLine(pair.Key + "\t" + pair.Value);
                        }
                    }
                    break;

                case 0: // sort by date by default
                default:

                    if (reversed)
                    {
                        foreach (var pair in filtered.Reverse())
                        {
                            sb.AppendLine(pair.Key + "\t" + pair.Value);
                        }
                    }
                    else
                    {
                        foreach (var pair in filtered)
                        {
                            sb.AppendLine(pair.Key + "\t" + pair.Value);
                        }
                    }
                    break;
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
