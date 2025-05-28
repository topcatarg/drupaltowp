using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace drupaltowp.Models
{
    public class DrupalPost
    {
        public int Nid { get; set; }
        public string Title { get; set; }
        public int Uid { get; set; }
        public int Created { get; set; }
        public int Changed { get; set; }
        public int Status { get; set; }
        public string Content { get; set; }
        public string Excerpt { get; set; }
        public string Bajada { get; set; }
        public int? ImageFid { get; set; }
        public string ImageFilename { get; set; }
        public string ImageUri { get; set; }
        public List<int> Categories { get; set; } = new();
        public List<int> Tags { get; set; } = new();
    }
}
