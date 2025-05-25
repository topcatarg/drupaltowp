using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace drupaltowp.Models
{
    internal class DrupalUser
    {
        public int Uid { get; set; }
        public string Name { get; set; }
        public string Mail { get; set; }
        public long Created { get; set; }
        public int Status { get; set; }
        public string Roles { get; set; }
    }
}
