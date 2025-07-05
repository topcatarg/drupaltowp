using System;
using System.Collections.Generic;

namespace drupaltowp.Models
{
    // Modelo para posts de Drupal
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

    // Modelo específico para posts de biblioteca
    public class BibliotecaPost
    {
        public int Nid { get; set; }
        public string Title { get; set; } = "";
        public int Uid { get; set; }
        public int Created { get; set; }
        public int Changed { get; set; }
        public int Status { get; set; }
        public string? Body_Value { get; set; }
        public int? Field_Adjuntos_Fid { get; set; }
        public string? Filename { get; set; } // Nombre del archivo adjunto
        public string? uri { get; set; } // URI del archivo adjunto
        public string? Field_Bajada_Value { get; set; }
        public string? Field_Biblioteca_Fecha_De_Alta_Value { get; set; }
        public int? Field_Categoria_Tid { get; set; }
        public string? Name { get; set; } // Nombre de la categoría
        public int? Field_Featured_Categories_Tid { get; set; }
        public int? Field_Featured_Image_Fid { get; set; }
        public string? Filename2 { get; set; } // Nombre de la imagen destacada (fm2.filename)
        public string? uri2 { get; set; } // URI de la imagen destacada (fm2.uri)
        // Propiedades derivadas para compatibilidad
        public string? Content => Body_Value;
        public string? Bajada => Field_Bajada_Value;
        public string? FechaAlta => Field_Biblioteca_Fecha_De_Alta_Value;
        public int? ImageFid => Field_Featured_Image_Fid;
        public string? ImageFilename => Filename2;
        public int? ArchivoFid => Field_Adjuntos_Fid;
        public string? ArchivoFilename => Filename;
        public int? field_tags_tid { get; set; }

        // Lista para múltiples categorías (se llenará por separado)
        public List<int> Categories { get; set; } = new();

        public List<int> Tags { get; set; } = new();
    }

    // Modelo para usuarios de Drupal
    public class DrupalUser
    {
        public int Uid { get; set; }
        public string Name { get; set; }
        public string Mail { get; set; }
        public int Created { get; set; }
        public int Status { get; set; }
        public string Roles { get; set; }
    }

    // Modelo para categorías de Drupal
    public class DrupalCategory
    {
        public int Tid { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Weight { get; set; }
        public string VocabularyName { get; set; }
        public string VocabularyMachineName { get; set; }
        public int ParentTid { get; set; }
    }

    // Modelo para tags de Drupal
    public class DrupalTag
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }

    // Modelo para imágenes de Drupal
    public class DrupalImage
    {
        public int Fid { get; set; }
        public int Uid { get; set; }
        public string Filename { get; set; }
        public string Uri { get; set; }
        public string Filemime { get; set; }
        public long Filesize { get; set; }
        public int Timestamp { get; set; }
    }

    // Modelo para páginas de Drupal
    public class Pagina
    {
        public int nid { get; set; }
        public string title { get; set; }
        public string contenido { get; set; }
        public string bajada { get; set; }
    }

}