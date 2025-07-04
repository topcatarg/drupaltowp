﻿using drupaltowp.Configuracion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace drupaltowp.Models;

public class MigratedPostWithImage
{
    public int DrupalPostId { get; set; }
    public int WpPostId { get; set; }
    public string PostTitle { get; set; }
    public DateTime PostDate    { get; set; }

    public bool NeedsGenericImage => PostDate < ConfiguracionGeneral.FechaMinimaImagen;
    // Todos los archivos asociados
    public List<PostFile> Files { get; set; } = new();

    // Separados por tipo para fácil acceso
    public List<PostFile> Images { get; set; } = new();
    public PostFile FeaturedImage { get; set; }
    public List<PostFile> ContentImages { get; set; } = new();
    public List<PostFile> Documents { get; set; } = new();

    public bool ContainsImages => Files.Any(f => f.FileType == "image");

}

public class PostFile
{
    public int Fid { get; set; }
    public string Filename { get; set; }
    public string Uri { get; set; }
    public string Filemime { get; set; }
    public long Filesize { get; set; }
    public string Module { get; set; }
    public string FileType { get; set; } // image, pdf, document, other
    public bool IsFeaturedImage { get; set; }
}
public class DrupalImageInfo
{
    public int Fid { get; set; }
    public int Uid { get; set; }
    public string FileName { get; set; }
    public string Uri { get; set; }

    public string Filemime { get; set; }
    public long Filesize { get; set; }
    public int Timestamp { get; set; }
    public DateTime Uploaddate => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp).DateTime;
}

public class PostAttachedImage
{
    public int PostId { get; set; }
    public int ImageFid { get; set; }
    public string Filename { get; set; }
    public string Uri   { get; set; }
    public bool IsFeatured {  get; set; }
    public string AttachmentType { get; set; }

}

public class CopiedImageInfo
{
    public int DrupalFid { get; set; }
    public string DrupalFilename { get; set; }
    public string WpRelativePath    { get; set; }
    public string WpFullPath { get; set; }
    public long Filesize { get; set; }
    public string Filemime { get; set; }
    public int Timestamp { get; set; }
    public int UserId { get; set; }

}

public class WordPressMediaInfo
{
    public int DrupalFid { get; set; }
    public int WpId { get; set; }
    public string WpUrl { get; set; }
    public string Filename { get; set; }
    public int WpPostId { get; set; }

}

public class ImageMigrationSummary
{
    public int TotalPostsProcessed { get; set; }
    public int PostsWithGenericImage {  get; set; }
    public int PostsWithOriginalImage { get; set; }
    public int FilesProcessed { get; set; }
    public int FilesCopied { get; set; }
    public int FilesSkipped { get; set; }
    public int PostsUpdated { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
}

public class ContentImageMatch
{
    public string OriginalUrl { get; set; }
    public string Filename { get; set; }
    public string FullMatch { get; set; }
    public string CleanPath { get; set; }
    public int? Fid { get; set; }
    public bool IsDrupalMediaJson { get; set; }
}
/// <summary>
/// Información de un documento copiado desde Drupal a WordPress
/// </summary>
public class CopiedDocumentInfo
{
    /// <summary>
    /// Información del archivo original de Drupal
    /// </summary>
    public PostFile OriginalFile { get; set; }

    /// <summary>
    /// Nombre del archivo copiado (único para evitar conflictos)
    /// </summary>
    public string CopiedFilename { get; set; }

    /// <summary>
    /// ID del media en WordPress
    /// </summary>
    public int WpMediaId { get; set; }

    /// <summary>
    /// URL del archivo en WordPress
    /// </summary>
    public string WpUrl { get; set; }

    /// <summary>
    /// Ruta local del archivo copiado
    /// </summary>
    public string LocalPath { get; set; }

    /// <summary>
    /// Indica si el archivo fue copiado (true) o ya existía (false)
    /// </summary>
    public bool WasCopied { get; set; }
}

