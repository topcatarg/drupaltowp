using Dapper;
using drupaltowp.Configuracion;
using drupaltowp.Models;
using drupaltowp.Services;
using drupaltowp.ViewModels;
using MySql.Data.MySqlClient;
using Mysqlx.Prepare;
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
    private int IdCategoriaTemporal = 0; // ID de la categoría temporal que se creará para evitar "Uncategorized"
    public OpinionPostMigrator(LoggerViewModel logger, WordPressClient wpClient )
    {
        _logger = logger;
        _wpClient = wpClient;
        _mappingService = new(logger);
    }

    public async Task MigratePosts()
    {// Cargar los mapeos
        await _mappingService.LoadMappingsForContentType(ContentType.Opinion);
        _logger.LogInfo("✅ Mapeos cargados");

        // Traer los posts de Drupal
        var opinionPosts = await GetOpinionPostsAsync();
        int processed = 0;
        int total = opinionPosts.Count;

        foreach (var post in opinionPosts)
        {
            try
            {
                if (Cancelar)
                {
                    _logger.LogWarning("⚠️ Migración cancelada");
                    break;
                }

                if (_mappingService.IsPostMigrated(post.Nid, ContentType.Opinion))
                {
                    _logger.LogMessage($"⏭️ Post ya migrado: {post.Title}");
                    continue;
                }

                var wpPostId = await MigrateOpinionPostAsync(post);
                if (wpPostId.HasValue)
                {
                    processed++;
                    _logger.LogMessage($"✅ Migrado: [{post.Nid}] {post.Title}");

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

        _logger.LogSuccess($"✅ Migración completada: {processed} posts migrados");
    
    }

    private async Task<int?> MigrateOpinionPostAsync(OpinionPost opinionPost)
    {
       // Preparar contenido limpio(solo el texto de Drupal)
            var cleanContent = opinionPost.Content ?? "";

        // Obtener autor de WordPress
        var authorId = _mappingService.GetWordPressUserId(opinionPost.Uid);

        // 1. CREAR POST COMO TIPO 'POST' USANDO WORDPRESS API
        var wpPost = new Post
        {
            Title = new Title(opinionPost.Title),
            Content = new Content(cleanContent),
            Excerpt = new Excerpt(opinionPost.FraseOpinion ?? opinionPost.Bajada ?? ""),
            Author = authorId,
            Status = opinionPost.Status == 1 ? Status.Publish : Status.Draft,
            Date = DateTimeOffset.FromUnixTimeSeconds(opinionPost.Created).DateTime
            // NO asignamos Type aquí, dejamos que sea 'post' por defecto
        };

        // Obtener categorías y tags estándar para la creación inicial
        var standardCategories = await GetStandardCategoriesAsync(opinionPost);
        var standardTags = await GetStandardTagsAsync(opinionPost);

        // Crear el post usando WordPress API
        var createdPost = await _wpClient.Posts.CreateAsync(wpPost);

        //Agregar manualmente las categorías y tags
        if (standardCategories.Count > 0)
        {
         await   AgregarTaxonomiasAsync(standardCategories, createdPost.Id);
        }
        if (standardTags.Count > 0)
        {
         await   AgregarTaxonomiasAsync(standardTags, createdPost.Id);
        }
        // 2. CONVERTIR A TIPO 'OPINION' MANUALMENTE
        await ConvertPostToOpinionTypeAsync(createdPost.Id);

        // 3. AGREGAR METADATOS ESPECÍFICOS DE OPINION
        await AddOpinionMetadataAsync(createdPost.Id, opinionPost);

        // 4. PROCESAR FOTO DEL AUTOR SI EXISTE
        if (opinionPost.AutorPictureFid.HasValue)
        {
            await ProcessAuthorPhotoAsync(createdPost.Id, opinionPost);
        }

        // 6. GUARDAR MAPPING
        await _mappingService.SaveOpinionPostMappingAsync(opinionPost.Nid, createdPost.Id);

        return createdPost.Id;
    }

    /// <summary>
    /// Asigna una categoría específica de Opinion al post
    /// </summary>
    private async Task AgregarTaxonomiasAsync(List<int> Taxonomias, int PostId)
    {
        foreach (int Id in Taxonomias)
        {
            try
            {
                using MySqlConnection connection = new(ConfiguracionGeneral.WPconnectionString);
                await connection.OpenAsync();
                // Asignar al post
                var inserted = await connection.ExecuteAsync(@"
            INSERT IGNORE INTO wp_term_relationships (object_id, term_taxonomy_id)
            VALUES (@PostId, @TaxonomyId)",
                    new { PostId, TaxonomyId = Id });

                if (inserted > 0)
                {
                    await connection.ExecuteAsync(@"
            UPDATE wp_term_taxonomy 
            SET count = count + 1 
            WHERE term_taxonomy_id = @TaxonomyId",
                new { TaxonomyId = Id });
                    // Actualizar contador
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error asignando taxonomia {Id}: {ex.Message}");
            }
        }
    }
    /// <summary>
    /// Agrega metadatos específicos de Opinion
    /// </summary>
    private async Task AddOpinionMetadataAsync(int postId, OpinionPost post)
    {
        using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
        await connection.OpenAsync();

        var metadata = new List<(string key, string value)>
            {
                // Metadatos específicos del custom post type
                ("_mh_post_type", "opinion"),
                ("_mh_post_layout", "default"),
                ("_mh_featured_post", "0")
            };

        // Frase de opinión
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
            var wpMediaId = _mappingService.GetWordPressMediaId(post.AutorPictureFid.Value);
            if (wpMediaId > 0)
            {
                metadata.Add(("_mh_author_photo_fid", wpMediaId.ToString()));
                metadata.Add(("_mh_has_author_photo", "1"));
            }
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
    /// <summary>
    /// Convierte un post de tipo 'post' a tipo 'opinion'
    /// </summary>
    private async Task ConvertPostToOpinionTypeAsync(int postId)
    {
        try
        {
            using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
            await connection.OpenAsync();

            // Cambiar el post_type a 'opinion' y actualizar GUID en una sola query
            var siteUrl = ConfiguracionGeneral.UrlsitioWP.TrimEnd('/');
            var newGuid = $"{siteUrl}/?post_type=opinion&p={postId}";

            await connection.ExecuteAsync(@"
                    UPDATE wp_posts 
                    SET post_type = 'opinion', 
                        guid = @Guid 
                    WHERE ID = @PostId",
                new { Guid = newGuid, PostId = postId });

            _logger.LogInfo($"✅ Post {postId} convertido a tipo 'opinion'");
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Error convirtiendo post {postId} a opinion: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Obtiene tags estándar para la creación inicial del post
    /// </summary>
    private async Task<List<int>> GetStandardTagsAsync(OpinionPost opinionPost)
    {
        var tags = new List<int>();

        try
        {
            if (opinionPost.Tags?.Count > 0)
            {
                foreach (var drupalTagId in opinionPost.Tags)
                {

                    _mappingService.TagMapping.TryGetValue(drupalTagId,out int WpId);
                    if (WpId > 0)
                    {
                        tags.Add(WpId);
                        continue; // Si ya existe, no necesitamos buscar más
                    }
                    string tagName = await GetTaxonomyNameFromDrupalAsync(drupalTagId);
                    int newTagId = await _mappingService.MigrateSingleTaxonomyDBDirectAsync(tagName, drupalTagId, "tag_opinion","op-");
                    tags.Add(newTagId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"⚠️ Error obteniendo tags estándar: {ex.Message}");
        }

        return tags;
    }

    /// <summary>
    /// Obtiene categorías estándar para la creación inicial del post
    /// </summary>
    private async Task<List<int>> GetStandardCategoriesAsync(OpinionPost opinionPost)
    {
        var categories = new List<int>();

        try
        {
            // Si tiene categoría específica, buscar su equivalente en WordPress
            if (opinionPost.CategoryId.HasValue)
            {
                _mappingService.CategoryMapping.TryGetValue(opinionPost.CategoryId.Value, out int wpCategoryId);
                if (wpCategoryId > 0)
                {
                    categories.Add(wpCategoryId);
                }
                //Tengo que agregar la categoria que no existe.
                else
                {
                    string categoryName = await GetTaxonomyNameFromDrupalAsync(opinionPost.CategoryId.Value);
                    int newCategoryId= await _mappingService.MigrateSingleTaxonomyDBDirectAsync(categoryName, opinionPost.CategoryId.Value,"categoria_opinion","op-");
                    categories.Add(newCategoryId);
                }
            }
            // Siempre agregar una categoría "temporal" que luego quitaremos
            // Esto evita que WordPress asigne "Uncategorized"
            await GetOrCreateTempCategoryAsync();
            if (IdCategoriaTemporal > 0 && !categories.Contains(IdCategoriaTemporal))
            {
                categories.Add(IdCategoriaTemporal);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"⚠️ Error obteniendo categorías estándar: {ex.Message}");
        }

        return categories;
    }

    private async Task ChangeTaxonomyType(int TermId, string TaxonomyType)
    {
        try
        {
            using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
            await connection.OpenAsync();
            // Actualizar el taxonomy_type del término
            await connection.ExecuteAsync(@"
                    UPDATE wp_term_taxonomy 
                    SET taxonomy = @TaxonomyType
                    WHERE term_id = @CategoryId",
                new { TaxonomyType = TaxonomyType, CategoryId = TermId });
            await connection.ExecuteAsync(@"
                    UPDATE wp_terms 
                    SET name = replace(name,'op_','')
                    WHERE term_id = @CategoryId",
                new { CategoryId = TermId });
            _logger.LogInfo($"✅ Taxonomía del término {TermId} cambiada a '{TaxonomyType}'");
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Error cambiando taxonomía del término {TermId}: {ex.Message}");
            throw;
        }
    }
    private async Task<string> GetTaxonomyNameFromDrupalAsync(int id)
    {
        const string query = @"
            SELECT 
                t.name
            FROM taxonomy_term_data t
            WHERE tid = @id
            ";

        using var connection = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString);
        await connection.OpenAsync();
        string result = await connection.ExecuteScalarAsync<string>(query, new { id });
        return result;
    }

    /// <summary>
    /// Obtiene o crea una categoría temporal para evitar "Uncategorized"
    /// </summary>
    private async Task GetOrCreateTempCategoryAsync()
    {
        try
        {
            if (IdCategoriaTemporal > 0)
            {
                return; // Ya tenemos la categoría temporal
            }
            // Buscar si ya existe una categoría temporal
            var categories = await _wpClient.Categories.GetAllAsync();
            var tempCategory = categories.FirstOrDefault(c => c.Name == "Temporal-Migration");

            if (tempCategory != null)
            {
                IdCategoriaTemporal = tempCategory.Id;
                return;
            }

            // Crear categoría temporal
            var newCategory = new Category
            {
                Name = "Temporal-Migration",
                Slug = "temporal-migration",
                Description = "Categoría temporal para migración - será removida automáticamente"
            };

            var createdCategory = await _wpClient.Categories.CreateAsync(newCategory);
            IdCategoriaTemporal = createdCategory.Id;
            return ;
        }
        catch
        {
            return ; // Fallback a "Uncategorized"
        }
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
