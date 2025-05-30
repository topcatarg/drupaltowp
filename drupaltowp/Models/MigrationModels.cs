using System;
using System.Collections.Generic;

namespace drupaltowp.Models
{
    // Modelos para resultados de análisis
    public class PostAnalysisResult
    {
        public Dictionary<string, int> PostCountsByType { get; set; } = new();
        public int TotalPosts { get; set; }
        public int PostsWithFeaturedImage { get; set; }
        public int PostsWithBajada { get; set; }
    }

    // Modelos para información de elementos migrados
    public class MigratedPostInfo
    {
        public int DrupalPostId { get; set; }
        public int WpPostId { get; set; }
        public DateTime MigratedAt { get; set; }
    }

    public class MigratedImageInfo
    {
        public int DrupalFid { get; set; }
        public int WpId { get; set; }
        public string Filename { get; set; }
        public DateTime MigratedAt { get; set; }
    }

    // Modelos para datos de mapping (para exportación)
    public class MappingData
    {
        public List<UserMappingData> Users { get; set; } = new();
        public List<CategoryMappingData> Categories { get; set; } = new();
        public List<TagMappingData> Tags { get; set; } = new();
        public List<PostMappingData> Posts { get; set; } = new();
        public List<MediaMappingData> Media { get; set; } = new();
    }

    public class UserMappingData
    {
        public int? DrupalId { get; set; }
        public int WpId { get; set; }
        public string DrupalName { get; set; }
        public string WpUsername { get; set; }
    }

    public class CategoryMappingData
    {
        public int? DrupalId { get; set; }
        public int WpId { get; set; }
        public string DrupalName { get; set; }
        public string WpName { get; set; }
        public string Vocabulary { get; set; }
    }

    public class TagMappingData
    {
        public int? DrupalId { get; set; }
        public int WpId { get; set; }
        public string DrupalName { get; set; }
        public string WpName { get; set; }
    }

    public class PostMappingData
    {
        public int DrupalId { get; set; }
        public int WpId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class MediaMappingData
    {
        public int DrupalFid { get; set; }
        public int WpId { get; set; }
        public string DrupalFilename { get; set; }
        public string WpUrl { get; set; }
    }
}