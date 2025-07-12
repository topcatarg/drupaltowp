using Dapper;
using drupaltowp.Configuracion;
using drupaltowp.Helpers;
using drupaltowp.Models;
using drupaltowp.Services;
using drupaltowp.ViewModels;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using WordPressPCL;
using WordPressPCL.Client;
using WordPressPCL.Models;

namespace drupaltowp.Clases.Publicaciones.Hubs;

public class HubsPublicationsMigrator
{
    private readonly LoggerViewModel _logger;
    private readonly WordPressClient _wpClient;
    private readonly MappingService _mappingService;
    private int _hubsCategoryId = 12368;

    public bool Cancelar = false;
    public HubsPublicationsMigrator(LoggerViewModel logger)
    {
        _logger = logger;

        // Configurar cliente WordPress
        _wpClient = new WordPressClient(ConfiguracionGeneral.UrlsitioWP);
        _wpClient.Auth.UseBasicAuth(ConfiguracionGeneral.Usuario, ConfiguracionGeneral.Password);

        // Inicializar servicio de mapeo
        _mappingService = new MappingService(_logger, ConfiguracionGeneral.WPconnectionString);
    }

    public async Task MigrateHubsPublicationsAsync()
    {
        try
        {
            _logger.LogProcess("🌐 Iniciando migración de publicaciones de hubs...");

            // 1. Cargar mapeos necesarios
            await LoadRequiredMappings();

            // 2. Crear/obtener categoría Hubs
            await EnsureHubsCategoryExists();

            // 3. Obtener datos de hubs desde Drupal
            var hubsData = await GetHubsDataFromDrupal();

            // 4. Agrupar datos por publicación
            var groupedHubs = GroupHubsByPublication(hubsData);

            _logger.LogInfo($"📊 Procesando {groupedHubs.Count} publicaciones de hubs...");

            // 5. Migrar cada publicación
            await MigrateHubsPublications(groupedHubs);

            _logger.LogSuccess("✅ Migración de publicaciones de hubs completada");
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Error en migración de hubs: {ex.Message}");
            throw;
        }
    }

    private async Task LoadRequiredMappings()
    {
        _logger.LogInfo("📋 Cargando mapeos necesarios...");
        await _mappingService.LoadMappingsForContentType(ContentType.Hubs);
    }

    private async Task EnsureHubsCategoryExists()
    {
        try
        {
            _logger.LogInfo("📂 Verificando categoría 'Hubs'...");

            if (_hubsCategoryId != 0)
            {
                _logger.LogInfo($"Categoria existente con id: {_hubsCategoryId}");
                return;
            }
            // Buscar si ya existe la categoría
            var categories = await _wpClient.Categories.GetAllAsync();
            var hubsCategory = categories.FirstOrDefault(c =>
                c.Name.Equals("Hubs", StringComparison.OrdinalIgnoreCase));

            if (hubsCategory != null)
            {
                _hubsCategoryId = hubsCategory.Id;
                _logger.LogInfo($"✅ Categoría 'Hubs' encontrada (ID: {_hubsCategoryId})");
            }
            else
            {
                // Crear la categoría
                var newCategory = new Category
                {
                    Name = "Hubs",
                    Description = "Publicaciones del tipo Hubs migradas desde Drupal",
                    Slug = "hubs"
                };

                var createdCategory = await _wpClient.Categories.CreateAsync(newCategory);
                _hubsCategoryId = createdCategory.Id;
                _logger.LogSuccess($"✅ Categoría 'Hubs' creada (ID: {_hubsCategoryId})");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Error manejando categoría Hubs: {ex.Message}");
            _hubsCategoryId = 1; // Categoría por defecto
        }
    }

    private async Task<List<HubQueryResult>> GetHubsDataFromDrupal()
    {
        _logger.LogInfo("🔍 Obteniendo datos de hubs desde Drupal...");

        using var connection = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString);
        await connection.OpenAsync();

        var query = @"
                SELECT 
                    n.nid,
                    n.title as titulo,
                    n.uid,
                    n.created as creado,
                    n.status,
                    fdb.body_value as cuerpo,
                    fdfb.field_bajada_value as bajada,
                    fdfbii.field_basic_image_image_fid as imagen_destacada,
                    fdfh.field_hub_tid as categoria,
                    ttd.name as nombre_categoria,
                    fdft.field_tags_tid as tags
                FROM node n
                LEFT JOIN field_data_body fdb ON fdb.entity_id = n.nid
                LEFT JOIN field_data_field_bajada fdfb ON fdfb.entity_id = n.nid
                LEFT JOIN field_data_field_basic_image_image fdfbii ON fdfbii.entity_id = n.nid
                LEFT JOIN field_data_field_hub fdfh ON fdfh.entity_id = n.nid
                LEFT JOIN taxonomy_term_data ttd ON ttd.tid = fdfh.field_hub_tid
                LEFT JOIN field_data_field_tags fdft ON fdft.entity_id = n.nid
                WHERE n.type = 'hubs'
                AND fdb.body_value IS NOT NULL
                AND n.status = 1
                ORDER BY n.nid, fdft.field_tags_tid";

        var hubsData = await connection.QueryAsync<HubQueryResult>(query);
        _logger.LogInfo($"📊 Obtenidos {hubsData.Count()} registros de hubs");

        return hubsData.ToList();
    }
    private Dictionary<int, HubPublicationData> GroupHubsByPublication(List<HubQueryResult> hubsData)
    {
        _logger.LogInfo("📋 Agrupando datos por publicación...");

        var groupedHubs = new Dictionary<int, HubPublicationData>();

        foreach (var row in hubsData)
        {
            int nid = row.Nid;

            if (!groupedHubs.ContainsKey(nid))
            {
                // Crear nueva entrada
                groupedHubs[nid] = new HubPublicationData
                {
                    Nid = nid,
                    Titulo = row.Titulo,
                    Uid = row.Uid,
                    Creado = row.Creado,
                    Status = row.Status,
                    Cuerpo = row.Cuerpo,
                    Bajada = row.Bajada,
                    Categoria = row.Categoria,
                    NombreCategoria = row.Nombre_Categoria,
                    ImagenesDestacadas = [],
                    Tags = []
                };
            }

            var hub = groupedHubs[nid];

            // Agregar imagen destacada si existe y no está ya agregada
            if (row.Imagen_Destacada.HasValue && !hub.ImagenesDestacadas.Contains(row.Imagen_Destacada))
            {
                hub.ImagenesDestacadas.Add(row.Imagen_Destacada);
            }

            // Agregar tag si existe y no está ya agregado
            if (row.Tags.HasValue && !hub.Tags.Contains(row.Tags.Value))
            {
                hub.Tags.Add(row.Tags.Value);
            }
        }

        _logger.LogInfo($"✅ Agrupados en {groupedHubs.Count} publicaciones únicas");
        return groupedHubs;
    }

    private async Task MigrateHubsPublications(Dictionary<int, HubPublicationData> groupedHubs)
    {
        int migratedCount = 0;
        int skippedCount = 0;
        int total = groupedHubs.Count;
        foreach (var kvp in groupedHubs)
        {
            if (Cancelar)
            {
                _logger.LogWarning("⚠️ Migración cancelada por el usuario");
                break;
            }

            try
            {
                var hub = kvp.Value;

                // Verificar si ya fue migrado
                if (_mappingService.HubsMapping.ContainsKey(hub.Nid))
                {
                    skippedCount++;
                    continue;
                }

                // Crear contenido combinando bajada + cuerpo
                var content = BuildPostContent(hub);

                // Obtener categorías para WordPress
                var wpCategories = await GetWordPressCategories(hub);

                // Obtener tags para WordPress
                var wpTags = await GetWordPressTags(hub);

                // Migro Imagen Destacada
                //Migro la imagen destacada
                int MediaId = await HandleFeaturedImage(hub);

                // Crear post en WordPress
                var wpPost = new Post
                {
                    Title = new Title(hub.Titulo),
                    Content = new Content(content),
                    Excerpt = new Excerpt(hub.Bajada ?? ""),
                    Status = Status.Publish,
                    Date = DateTimeOffset.FromUnixTimeSeconds(hub.Creado).DateTime,
                    Author = _mappingService.UserMapping.GetValueOrDefault(hub.Uid, 1),
                    Categories = wpCategories,
                    Tags = wpTags,
                    FeaturedMedia= MediaId,
                };

                var createdPost = await _wpClient.Posts.CreateAsync(wpPost);

                // Guardar en BD
                await _mappingService.SaveHubsPostMappingAsync(hub.Nid, createdPost.Id);

                // Manejar imágenes destacadas múltiples
                await HandleMultipleFeaturedImages(hub, createdPost.Id);

                migratedCount++;
                _logger.LogInfo($"✅ Hub migrado: {hub.Titulo} (Drupal: {hub.Nid} → WP: {createdPost.Id})");

                // Log progreso cada 5 páginas
                if (migratedCount % 10 == 0)
                {
                    var percentage = (migratedCount * 100.0) / total;
                    _logger.LogInfo($"📊 Progreso: {migratedCount:N0}/{total:N0} ({percentage:F1}%)");
                }
                // Pequeña pausa para no sobrecargar
                //await Task.Delay(100, _cancellationService.CurrentToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error migrando hub {kvp.Key}: {ex.Message}");
                skippedCount++;
            }
        }

        _logger.LogSuccess($"🎉 Migración completada: {migratedCount} hubs migrados, {skippedCount} omitidos");
    }

    /// <summary>
    /// Crea la imagen destacada
    /// </summary>
    /// <param name="hub"></param>
    /// <returns></returns>
    private async Task<int> HandleFeaturedImage(HubPublicationData hub)
    {
        if (hub.ImagenesDestacadas.Count < 1)
            return 0;
        //Busco la imagen
        //Obtenemos la imagen de drupal
        var drupalImage = await ImageHelpers.GetDrupalImageData(hub.ImagenesDestacadas[0].Value);
        //La migramos
        int wpId = await _mappingService.MigrateSingleFileAsync(drupalImage, _wpClient);
        return wpId;
    }

    private string BuildPostContent(HubPublicationData hub)
    {
        var content = "";

        // Agregar bajada al inicio si existe
        if (!string.IsNullOrWhiteSpace(hub.Bajada))
        {
            content += $"<div class=\"hub-bajada\">{hub.Bajada}</div>\n\n";
        }

        // Agregar cuerpo principal
        if (!string.IsNullOrWhiteSpace(hub.Cuerpo))
        {
            content += hub.Cuerpo;
        }

        return content;
    }

    private async Task<List<int>> GetWordPressCategories(HubPublicationData hub)
    {
        var categories = new List<int> { _hubsCategoryId }; // Siempre incluir categoría Hubs
        
        if (!hub.Categoria.HasValue)
        {
            return categories;
        }
        // Agregar categoría original si existe mapeo
        if (_mappingService.CategoryMapping.TryGetValue(hub.Categoria.Value, out int wpCategoryId))
        {
            if (!categories.Contains(wpCategoryId))
            {
                categories.Add(wpCategoryId);
            }
        }
        else
        {
            //No existe la categoria, la agregamos.
            wpCategoryId = await _mappingService.MigrateSingleCategory(hub.NombreCategoria, hub.Categoria.Value, _wpClient, ContentType.Hubs);
            categories.Add(wpCategoryId );
        }

        return categories;
    }

    private async Task<List<int>> GetWordPressTags(HubPublicationData hub)
    {
        var wpTags = new List<int>();

        foreach (var tagId in hub.Tags)
        {
            if (_mappingService.TagMapping.ContainsKey(tagId))
            {
                wpTags.Add(_mappingService.TagMapping[tagId]);
            }
        }

        return wpTags;
    }

    private async Task HandleMultipleFeaturedImages(HubPublicationData hub, int wpPostId)
    {
        if (hub.ImagenesDestacadas.Count <= 1)
            return;

        try
        {
            
            // Las imágenes adicionales las agregamos al final del contenido

            var additionalImages = hub.ImagenesDestacadas.Skip(1).Where(img => img.HasValue).ToList();

            if (additionalImages.Count != 0)
            {
                _logger.LogInfo($"📸 Hub {hub.Nid} tiene {additionalImages.Count} imágenes adicionales que se agregarán al contenido");
                string NuevoCuerpo = hub.Cuerpo;
                foreach (var img in additionalImages)
                {
                    //Obtenemos la imagen de drupal
                    var drupalImage = await ImageHelpers.GetDrupalImageData(img.Value);
                    //La migramos
                    int wpId = await _mappingService.MigrateSingleFileAsync(drupalImage,_wpClient);
                    var wpMedia = await _wpClient.Media.GetByIDAsync(wpId);
                    //La agregamos al contenido
                    string Texto = ImageHelpers.GenerateWordPressImageHtml(wpMedia, drupalImage.Filename);
                    NuevoCuerpo += Texto;
                }
                if (NuevoCuerpo != hub.Cuerpo)
                {
                    using MySqlConnection DrupalConnection = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString    );
                    await DrupalConnection.OpenAsync();
                    // Actualizar el contenido del post
                    await DrupalConnection.ExecuteAsync(
                        "UPDATE wp_posts SET post_content = @content WHERE ID = @postId",
                        new { content = NuevoCuerpo, wpPostId });
                    _logger.LogInfo("Se actualizo el cuerpo de la publicacion");
                }


                
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Error manejando imágenes múltiples para hub {hub.Nid}: {ex.Message}");
        }
    }

}
