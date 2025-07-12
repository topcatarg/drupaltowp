using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace drupaltowp.Models;

public class OpinionPostRaw
{
    public int Nid { get; set; }
    public string Titulo { get; set; }
    public int Created { get; set; }
    public int? Uid { get; set; }
    public int? Status { get; set; }

    // Contenido
    public string Cuerpo { get; set; }

    // Campos específicos de opinión
    public string Field_Bajada_Value { get; set; }
    public int? Field_Featured_Categories_Tid { get; set; }
    public int? Field_Noticia_Region_Site_Tid { get; set; }
    public int? Tags { get; set; }
    public string Field_Volanta_Value { get; set; }
    public string Frase_Opinion { get; set; }
    public string Responsabilidad_Autor { get; set; }
    public string Nombre_Autor { get; set; }
    public int? Field_User_Picture_Fid { get; set; }
    public string Filename { get; set; }
    public string Uri { get; set; }
}

// Modelo para posts de opinión - PROCESADO
public class OpinionPost
{
    public int Nid { get; set; }
    public string Title { get; set; }
    public int Uid { get; set; }
    public int Created { get; set; }
    public int Status { get; set; }

    // Contenido
    public string Content { get; set; }
    public string Bajada { get; set; }          // field_bajada_value
    public string FraseOpinion { get; set; }    // field_opinion_frase_value - LO RESALTADO
    public string Volanta { get; set; }         // field_volanta_value (parece no usarse)

    // Autor personalizado
    public string NombreAutor { get; set; }           // field_autor_nombre_value
    public string ResponsabilidadAutor { get; set; }  // field_responsabilidad_value

    // Foto del autor
    public int? AutorPictureFid { get; set; }    // field_user_picture_fid
    public string AutorPictureFilename { get; set; }  // filename del autor
    public string AutorPictureUri { get; set; }       // uri del autor

    // Categorización
    public int? CategoryId { get; set; }         // field_featured_categories_tid
    public int? RegionId { get; set; }          // field_noticia_region_site_tid
    public List<int> Tags { get; set; } = new(); // field_tags_tid (múltiples)
}

public class OpinionMappedPost
{
    public int DrupalPostId { get; set; }
    public int WpPostId { get; set; }
    public string PostTitle { get; set; }
    public string PostType { get; set; }
    public string PostStatus { get; set; }
}
