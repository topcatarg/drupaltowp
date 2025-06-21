using Dapper;
using drupaltowp.Configuracion;
using drupaltowp.Models;
using drupaltowp.Services;
using drupaltowp.ViewModels;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WordPressPCL;
using WordPressPCL.Models;

namespace drupaltowp.Clases.Publicaciones.Opinion;

internal class OpinionPostMigrator
{
    private readonly LoggerViewModel _logger;
    private readonly WordPressClient _wpClient;
    private readonly MappingService _mappingService;

    private int IdCategoriaOpinion = 1;
    public bool Cancelar { get; set; } = false;

    public OpinionPostMigrator(LoggerViewModel logger, WordPressClient wpClient )
    {
        _logger = logger;
        _wpClient = wpClient;
        _mappingService = new(logger);
    }

    public async Task MigratePosts()
    {
        //Cargo los mapeos
        await _mappingService.LoadMappingsForContentType(ContentType.Opinion);
        _logger.LogInfo("Se cargaron los mapeos");
        //Creo la categoria Opinion
        IdCategoriaOpinion = await GetOrCreateOpinionCategoryAsync();
        //Traigo los posts:
        var opinionPosts = await GetOpinionPostsAsync();
        int processed = 0;
        int total = opinionPosts.Count;
        foreach (var post in opinionPosts)
        {
            try
            {
                if (_mappingService.IsPostMigrated(post.Nid, ContentType.Opinion))
                {
                    _logger.LogMessage($"Post ya migrado {post.Title}");
                    continue;
                }
                var wpPostId = await MigrateOpinionPostAsync(post);
                if (wpPostId.HasValue)
                {
                    processed++;
                    _logger.LogMessage($"✅ Migrado: [{post.Nid}] {post.Title}");
                    // Log progreso cada 5 páginas
                    if (processed % 10 == 0)
                    {
                        var percentage = (processed * 100.0) / total;
                        _logger.LogInfo($"📊 Progreso: {processed:N0}/{total:N0} ({percentage:F1}%)");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogMessage($"❌ Error migrando {post.Title}: {ex.Message}");
            }
        }
    }

    private async Task<int?> MigrateOpinionPostAsync(OpinionPost opinionPost)
    {
        // Preparar el contenido formateado para MH Magazine
        var formattedContent = PrepareOpinionContentForMHMagazine(opinionPost);

        // Obtener autor de WordPress
        var authorId = _mappingService.GetWordPressUserId(opinionPost.Uid);

        // Crear post en WordPress
        var wpPost = new Post
        {
            Title = new Title(opinionPost.Title),
            Content = new Content(formattedContent),
            Excerpt = new Excerpt(opinionPost.FraseOpinion ?? opinionPost.Bajada ?? ""),
            Author = authorId,
            Status = opinionPost.Status == 1 ? Status.Publish : Status.Draft,
            Date = DateTimeOffset.FromUnixTimeSeconds(opinionPost.Created).DateTime
        };

        // Asignar a la categoría "Opinión"
        wpPost.Categories = new List<int> { IdCategoriaOpinion };

        // Asignar tags si existen
        if (opinionPost.Tags?.Count > 0)
        {
            wpPost.Tags = _mappingService.GetWordPressTags(opinionPost.Tags);
        }

        // Crear el post
        var createdPost = await _wpClient.Posts.CreateAsync(wpPost);

        // Procesar foto del autor si existe
        if (opinionPost.AutorPictureFid.HasValue)
        {
            await ProcessAuthorPhotoAsync(createdPost.Id, opinionPost);
        }

        // Agregar metadatos específicos para MH Magazine
        await AddMHMagazineMetadataAsync(createdPost.Id, opinionPost);

        // Guardar mapping
        await _mappingService.SaveOpinionPostMappingAsync(opinionPost.Nid, createdPost.Id);

        return createdPost.Id;
    }

    private static string PrepareOpinionContentForMHMagazine(OpinionPost post)
    {
        var content = new List<string>();

        // 2. INFORMACIÓN DEL AUTOR - Estructura completa con foto
        if (!string.IsNullOrEmpty(post.NombreAutor))
        {
            var autorSection = new List<string>();

            // HTML del autor con foto si existe
            if (post.AutorPictureFid.HasValue && !string.IsNullOrEmpty(post.AutorPictureFilename))
            {
                // Si hay foto del autor, crear estructura completa
                autorSection.Add($@"
<div class=""mh-author-bio opinion-author"" style=""margin-bottom: 2em; padding: 20px; background: #f8f9fa; border-radius: 8px; border-left: 3px solid #3498db; display: flex; align-items: center; gap: 15px;"">
    <div class=""author-avatar"" style=""flex-shrink: 0;"">
        <img src=""[AUTHOR_PHOTO_URL]"" alt=""{post.NombreAutor}"" style=""width: 80px; height: 80px; border-radius: 50%; object-fit: cover; border: 3px solid #3498db;"">
    </div>
    <div class=""author-info"">
        <h4 style=""margin: 0 0 5px 0; color: #2c3e50; font-size: 1.2em;"">{post.NombreAutor}</h4>");

                if (!string.IsNullOrEmpty(post.ResponsabilidadAutor))
                {
                    autorSection.Add($@"        <p style=""margin: 0; color: #7f8c8d; font-style: italic; font-size: 0.95em;"">{post.ResponsabilidadAutor}</p>");
                }

                autorSection.Add(@"    </div>
</div>");
            }
            else
            {
                // Sin foto, versión simplificada
                autorSection.Add($@"
<div class=""mh-author-bio opinion-author"" style=""margin-bottom: 2em; padding: 15px; background: #f1f2f6; border-radius: 5px; border-left: 3px solid #3498db;"">
    <h4 style=""margin: 0 0 5px 0; color: #2c3e50;"">{post.NombreAutor}</h4>");

                if (!string.IsNullOrEmpty(post.ResponsabilidadAutor))
                {
                    autorSection.Add($@"    <p style=""margin: 0; color: #7f8c8d; font-style: italic;"">{post.ResponsabilidadAutor}</p>");
                }

                autorSection.Add("</div>");
            }

            content.AddRange(autorSection);
        }

        // 1. FRASE DE OPINIÓN (lo que está resaltado debajo del título) - MUY IMPORTANTE
        if (!string.IsNullOrEmpty(post.FraseOpinion))
        {
            content.Add($@"
<div class=""mh-excerpt opinion-highlight"" style=""font-size: 1.3em; font-weight: 600; color: #2c3e50; margin-bottom: 2em; line-height: 1.4; border-left: 4px solid #e74c3c; padding-left: 20px; background: #f8f9fa; padding: 20px; border-radius: 5px; font-style: italic;"">
    ""{post.FraseOpinion}""
</div>");
        }

        

        // 3. BAJADA si existe (como subtítulo adicional)
        if (!string.IsNullOrEmpty(post.Bajada))
        {
            content.Add($@"
<div class=""mh-bajada"" style=""font-size: 1.1em; color: #555; margin-bottom: 1.5em; line-height: 1.6; padding: 15px; background: #ffffff; border: 1px solid #e0e0e0; border-radius: 5px;"">
    {post.Bajada}
</div>");
        }

        // 4. CONTENIDO PRINCIPAL - Con formato mejorado para opinión
        var mainContent = post.Content ?? "";

        if (!string.IsNullOrEmpty(mainContent))
        {
            // Mejorar formato de párrafos para lectura de opinión
            mainContent = mainContent.Replace("</p><p>", "</p>\n\n<p>");

            content.Add($@"
<div class=""opinion-content"" style=""font-size: 1.1em; line-height: 1.8; color: #2c3e50; text-align: justify;"">
    {mainContent}
</div>");
        }

       

        return string.Join("\n", content);
    }

    private async Task ProcessAuthorPhotoAsync(int postId, OpinionPost post)
    {
        try
        {
            // Verificar si la imagen ya fue migrada
            if (_mappingService.MediaMapping.TryGetValue(post.AutorPictureFid.Value, out int existingWpId))
            {
                // Actualizar el contenido con la URL existente
                await UpdateContentWithAuthorPhotoAsync(postId, existingWpId);
                return;
            }

            // Migrar la imagen del autor
            var wpMediaId = await MigrateImageToWordPressAsync(post.AutorPictureFid.Value, post.AutorPictureUri, post.AutorPictureFilename);

            if (wpMediaId.HasValue)
            {
                // Actualizar el contenido con la nueva URL
                await UpdateContentWithAuthorPhotoAsync(postId, wpMediaId.Value);
                _mappingService.MediaMapping[post.AutorPictureFid.Value] = wpMediaId.Value;

                // Guardar mapping de imagen
                await _mappingService.SaveMediaMappingAsync(post.AutorPictureFid.Value, wpMediaId.Value, post.AutorPictureFilename);
            }
        }
        catch (Exception ex)
        {
            _logger.LogMessage($"⚠️ Error procesando foto del autor: {ex.Message}");
        }
    }

    private async Task<int?> MigrateImageToWordPressAsync(int drupalFid, string imageUri, string filename)
    {
        try
        {
            // Construir ruta de imagen desde Drupal
            var imagePath = imageUri.Replace("public://", "");
            var fullImagePath = Path.Combine(ConfiguracionGeneral.DrupalFileRoute, imagePath);

            if (!File.Exists(fullImagePath))
            {
                _logger.LogMessage($"⚠️ Imagen no encontrada: {fullImagePath}");
                return null;
            }

            // Subir imagen a WordPress usando la API
            using var fileStream = new FileStream(fullImagePath, FileMode.Open, FileAccess.Read);
            var mediaItem = await _wpClient.Media.CreateAsync(fileStream, filename);

            _logger.LogMessage($"✅ Imagen migrada: {filename} -> ID: {mediaItem.Id}");
            return mediaItem.Id;
        }
        catch (Exception ex)
        {
            _logger.LogMessage($"❌ Error subiendo imagen {filename}: {ex.Message}");
            return null;
        }
    }
    private async Task UpdateContentWithAuthorPhotoAsync(int postId, int mediaId)
    {
        try
        {
            // Obtener la URL de la imagen desde WordPress
            var media = await _wpClient.Media.GetByIDAsync(mediaId);
            var imageUrl = media.SourceUrl;

            // Obtener el contenido actual del post
            using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
            await connection.OpenAsync();

            var currentContent = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT post_content FROM wp_posts WHERE ID = @postId",
                new { postId });

            if (!string.IsNullOrEmpty(currentContent) && currentContent.Contains("[AUTHOR_PHOTO_URL]"))
            {
                // Reemplazar el placeholder con la URL real
                var updatedContent = currentContent.Replace("[AUTHOR_PHOTO_URL]", imageUrl);

                // Actualizar el contenido del post
                await connection.ExecuteAsync(
                    "UPDATE wp_posts SET post_content = @content WHERE ID = @postId",
                    new { content = updatedContent, postId });

                _logger.LogMessage($"✅ Foto del autor actualizada en el contenido");
            }
        }
        catch (Exception ex)
        {
            _logger.LogMessage($"⚠️ Error actualizando foto del autor en contenido: {ex.Message}");
        }
    }

    private async Task AddMHMagazineMetadataAsync(int postId, OpinionPost post)
    {
        using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
        await connection.OpenAsync();

        var metadata = new List<(string key, string value)>
        {
            // Metadatos específicos de MH Magazine Theme
            ("_mh_post_layout", "default"), // Layout por defecto
            ("_mh_featured_post", "0"), // No destacado por defecto
            ("_mh_post_type", "opinion") // Marcar como post de opinión
        };

        // Frase de opinión como metadato especial
        if (!string.IsNullOrEmpty(post.FraseOpinion))
        {
            metadata.Add(("_mh_opinion_quote", post.FraseOpinion));
            metadata.Add(("_mh_has_highlight", "1"));
        }

        // Información del autor personalizado
        if (!string.IsNullOrEmpty(post.NombreAutor))
        {
            metadata.Add(("_mh_custom_author_name", post.NombreAutor));
            metadata.Add(("_mh_has_custom_author", "1"));
        }

        if (!string.IsNullOrEmpty(post.ResponsabilidadAutor))
        {
            metadata.Add(("_mh_author_title", post.ResponsabilidadAutor));
        }

        // Foto del autor
        if (post.AutorPictureFid.HasValue)
        {
            _mappingService.GetWordPressMediaId(post.AutorPictureFid.Value);

            metadata.Add(("_mh_author_photo_fid", _mappingService.GetWordPressMediaId(post.AutorPictureFid.Value).ToString()));
            metadata.Add(("_mh_has_author_photo", "1"));
        }

        // Bajada como subtítulo
        if (!string.IsNullOrEmpty(post.Bajada))
        {
            metadata.Add(("_mh_subtitle", post.Bajada));
        }

        // Insertar metadatos
        foreach (var (key, value) in metadata)
        {
            await connection.ExecuteAsync(@"
                    INSERT INTO wp_postmeta (post_id, meta_key, meta_value) 
                    VALUES (@postId, @metaKey, @metaValue)
                    ON DUPLICATE KEY UPDATE meta_value = @metaValue",
                new { postId, metaKey = key, metaValue = value });
        }
    }
          
        
    #region Categoria
    private async Task<int> GetOrCreateOpinionCategoryAsync()
    {
        try
        {
            // Buscar si ya existe la categoría "Opinión"
            var categories = await _wpClient.Categories.GetAllAsync();
            var opinionCategory = categories.FirstOrDefault(c =>
                c.Name.Equals("Opinión", StringComparison.OrdinalIgnoreCase) ||
                c.Slug.Equals("opinion", StringComparison.OrdinalIgnoreCase));

            if (opinionCategory != null)
            {
                return opinionCategory.Id;
            }

            // Crear la categoría si no existe
            var newCategory = new Category
            {
                Name = "Opinión",
                Slug = "opinion",
                Description = "Artículos de opinión y columnas editoriales"
            };

            var createdCategory = await _wpClient.Categories.CreateAsync(newCategory);
            _logger.LogMessage($"✅ Categoría 'Opinión' creada con ID: {createdCategory.Id}");
            
            return createdCategory.Id;
        }
        catch (Exception ex)
        {
            _logger.LogMessage($"⚠️ Error creando categoría Opinión: {ex.Message}");
            return 1; // Fallback a "Uncategorized"
        }
    }
    #endregion

    #region Obtener Posts
    private async Task<List<OpinionPost>> GetOpinionPostsAsync()
    {
        using var connection = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString);
        await connection.OpenAsync();
        string Query = @"
select n.nid,
n.title titulo,
n.created,
fdb.body_value cuerpo,
fdfb.field_bajada_value,
fdffc.field_featured_categories_tid,
fdfnrs.field_noticia_region_site_tid,
fdft.field_tags_tid tags,
fdfv.field_volanta_value,
fdfof.field_opinion_frase_value frase_opinion,
fdfr.field_responsabilidad_value responsabilidad_autor,
fdfan.field_autor_nombre_value nombre_autor,
fdfup.field_user_picture_fid , 
fm.filename, 
fm.uri
from node n 
left join field_data_body fdb on fdb.entity_id = n.nid
left join field_data_field_bajada fdfb  on fdfb.entity_id = n.nid
left join field_data_field_featured_categories fdffc on fdffc.entity_id = n.nid
left join field_data_field_noticia_region_site fdfnrs on fdfnrs.entity_id = n.nid
left join field_data_field_tags fdft on fdft.entity_id = n.nid
left join field_data_field_volanta fdfv on fdfv.entity_id = n.nid
left join field_data_field_opinion_frase fdfof on fdfof.entity_id = n.nid
left join field_data_field_responsabilidad fdfr on fdfr.entity_id = n.nid
left join field_data_field_autor_nombre fdfan on fdfan.entity_id = n.nid
left join field_data_field_user_picture fdfup on fdfup.entity_id = n.nid
left join file_managed fm on fm.fid  = fdfup.field_user_picture_fid
where n.type = 'opinion'
Order by n.created DESC";
        var rawPosts = await connection.QueryAsync<OpinionPostRaw>(Query);
        var groupedPosts = rawPosts
                .GroupBy(p => p.Nid)
                .Select(group => GroupOpinionPostRows(group))
                .ToList();

        _logger.LogMessage($"✅ Posts de opinión procesados: {groupedPosts.Count}");

        return groupedPosts;

    }

    private static OpinionPost GroupOpinionPostRows(IGrouping<int, OpinionPostRaw> group)
    {
        var mainPost = group.First();

        // Recopilar todos los tags únicos de todas las filas
        var allTags = group
            .Where(p => p.Tags.HasValue)
            .Select(p => p.Tags.Value)
            .Distinct()
            .ToList();

        return new OpinionPost
        {
            Nid = mainPost.Nid,
            Title = mainPost.Titulo,
            Created = mainPost.Created,
            Uid = mainPost.Uid ?? 1,
            Status = mainPost.Status ?? 1,

            // Contenido
            Content = mainPost.Cuerpo,
            Bajada = mainPost.Field_Bajada_Value,
            FraseOpinion = mainPost.Frase_Opinion,
            Volanta = mainPost.Field_Volanta_Value,

            // Autor
            NombreAutor = mainPost.Nombre_Autor,
            ResponsabilidadAutor = mainPost.Responsabilidad_Autor,
            AutorPictureFid = mainPost.Field_User_Picture_Fid,
            AutorPictureFilename = mainPost.Filename,
            AutorPictureUri = mainPost.Uri,

            // Categorización
            CategoryId = mainPost.Field_Featured_Categories_Tid,
            RegionId = mainPost.Field_Noticia_Region_Site_Tid,
            Tags = allTags
        };
    }
    #endregion
}
