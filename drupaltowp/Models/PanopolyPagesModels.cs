using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace drupaltowp.Models
{
    public class PanopolyPage
    {
        // Campos base del nodo
        public int Nid { get; set; }
        public string Title { get; set; }
        public int Created { get; set; }
        public int Uid { get; set; }
        public int Status { get; set; }

        // Campos del query (nombres exactos de las columnas)
        public string Body_Value { get; set; }
        public string Field_Bajada_Value { get; set; }
        public int? Field_Featured_Categories_Tid { get; set; }
        public int? Field_Noticia_Region_Site_Tid { get; set; }
        public int? Field_Tags_Tid { get; set; }
        public string Field_Volanta_Value { get; set; }

        // Propiedades derivadas para fácil acceso
        public string Content => Body_Value ?? "";
        public string Bajada => Field_Bajada_Value ?? "";
        public string Volanta => Field_Volanta_Value ?? "";
        public int? CategoryId => Field_Featured_Categories_Tid;
        public int? RegionId => Field_Noticia_Region_Site_Tid;
        public int? TagId => Field_Tags_Tid; // Individual tag de esta fila

        // Listas que se llenarán después del agrupamiento
        public List<int> Categories { get; set; } = new();
        public List<int> Tags { get; set; } = new();

        // Fecha convertida
        public DateTime CreatedDate => DateTimeOffset.FromUnixTimeSeconds(Created).DateTime;

        /// <summary>
        /// Genera el contenido completo con volanta incluida al inicio
        /// </summary>
        public string GetContentWithVolanta()
        {
            if (string.IsNullOrEmpty(Volanta))
                return Content;

            var volantaHtml = $@"<div class=""volanta"" style=""font-style: italic; color: #666; margin-bottom: 1em; font-size: 1.1em; border-left: 3px solid #0073aa; padding-left: 1em;"">
    {Volanta}
</div>";

            return volantaHtml + Content;
        }

        /// <summary>
        /// Verifica si la página tiene contenido válido
        /// </summary>
        public bool HasValidContent => !string.IsNullOrEmpty(Title) && !string.IsNullOrEmpty(Content);
    }

    /// <summary>
    /// Resultado del análisis de páginas panopoly
    /// </summary>
    public class PanopolyAnalysisResult
    {
        public int TotalPages { get; set; }
        public int PublishedPages { get; set; }
        public int PagesWithVolanta { get; set; }
        public int PagesWithBajada { get; set; }
        public int PagesWithRegion { get; set; }
        public int PagesWithCategories { get; set; }
        public int PagesWithTags { get; set; }
        public Dictionary<int?, int> RegionDistribution { get; set; } = new();
        public Dictionary<int?, int> CategoryDistribution { get; set; } = new();
    }

    /// <summary>
    /// Información de una página panopoly migrada
    /// </summary>
    public class MigratedPanopolyInfo : MigratedPostInfo
    {
        public bool HasVolanta { get; set; }
        public bool HasRegion { get; set; }
        public int CategoryCount { get; set; }
        public int TagCount { get; set; }
    }
}
