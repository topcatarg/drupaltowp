using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace drupaltowp.Models
{
    internal class DrupalImage
    {
        public int Fid { get; set; }
        public int Uid { get; set; }
        public string Filename { get; set; }
        public string Uri { get; set; }
        public string Filemime { get; set; }
        public long Filesize { get; set; }
        public int Timestamp { get; set; }
    }
}
