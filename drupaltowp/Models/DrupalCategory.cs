using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace drupaltowp.Models
{
    internal class DrupalCategory
    {
        public int Tid { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Weight { get; set; }
        public string VocabularyName { get; set; }
        public string VocabularyMachineName { get; set; }
        public int ParentTid { get; set; }

    }
}
