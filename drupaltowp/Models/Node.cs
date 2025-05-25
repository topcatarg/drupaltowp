namespace drupaltowp.Models
{
    public class Node
    {
        public int nid { get; set; }
        public int? vid { get; set; }
        public string type { get; set; }
        public string language { get; set; }
        public string title { get; set; }
        public int uid { get; set; }
        public int status { get; set; }
        public int created { get; set; }
        public int changed { get; set; }
        public int comment { get; set; }
        public int promote { get; set; }
        public int sticky { get; set; }
        public int tnid { get; set; }
        public int translate { get; set; }
        public Guid uuid { get; set; } // Cambiado de string a Guid
    }
}