using Dapper;
using drupaltowp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WordPressPCL;
using WordPressPCL.Client;
using WordPressPCL.Models;
using WordPressPCL.Utility;

namespace drupaltowp
{
    public class BibliotecaMigratorWPF
    {
        private readonly string _drupalConnectionString;
        private readonly string _wpConnectionString;
        private readonly WordPressClient _wpClient;
        private readonly TextBlock _statusTextBlock;
        private readonly ScrollViewer _logScrollViewer;
        private int CategoryBiblioteca;

        public bool Cancelar { get; set; } = false;

        // Mappings necesarios
        private Dictionary<int, int> _userMapping = new();
        private Dictionary<int, int> _categoryMapping = new();
        private Dictionary<int, MigratedPostInfo> _bibliotecaMapping = new();
        private Dictionary<int, int> _tagMapping = new();
        private readonly Dictionary<string,int> _ImageMapping = []; // Para manejar imágenes por URI
        public BibliotecaMigratorWPF(string drupalConnectionString, string wpConnectionString,
                                   WordPressClient wpClient, TextBlock statusTextBlock, ScrollViewer logScrollViewer)
        {
            _drupalConnectionString = drupalConnectionString;
            _wpConnectionString = wpConnectionString;
            _wpClient = wpClient;
            _statusTextBlock = statusTextBlock;
            _logScrollViewer = logScrollViewer;
        }

        public async Task<Dictionary<int, MigratedPostInfo>> MigrateBibliotecaAsync()
        {
            LogMessage("🚀 INICIANDO MIGRACIÓN DE BIBLIOTECA");

            try
            {
                // 1. Cargar mappings existentes
                await LoadMappingsAsync();

                // 2. Agrego una categoria biblioteca si no existe
                await AgregarCategoriaBiblioteca();

                // 2. Analizar estructura específica de biblioteca
                //await AnalyzeBibliotecaStructureAsync();
                //await BorrarTodoYEmpezarDeNuevoAsync();
                await RevisarProcesoActual();

                //No borro nada, traigo la lista para seguir desde ahi
                
                // 3. Obtener publicaciones de biblioteca de Drupal
                var bibliotecaPosts = await GetBibliotecaPostsAsync();
                LogMessage($"📊 Encontradas {bibliotecaPosts.Count} publicaciones de biblioteca");

                if (bibliotecaPosts.Count == 0)
                {
                    LogMessage("⚠️ No se encontraron publicaciones de biblioteca para migrar");
                    return _bibliotecaMapping;
                }

                // 4. Migrar cada publicación
                int migratedCount = 0 + _bibliotecaMapping.Count;
                int errorCount = 0;

                foreach (var post in bibliotecaPosts)
                {
                    if (Cancelar)
                    {
                        LogMessage($"✅ Cancelado!");
                        break;
                    }
                    try
                    {
                        if (_bibliotecaMapping.ContainsKey(post.Nid) )
                        {
                            LogMessage($"✅ Ya Migrada: {post.Title} ({migratedCount}/{bibliotecaPosts.Count})");
                            continue;
                        }
                        await MigrateBibliotecaPostAsync(post);
                        migratedCount++;
                        LogMessage($"✅ Migrada: {post.Title} ({migratedCount}/{bibliotecaPosts.Count})");
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        LogMessage($"❌ Error migrando {post.Title}: {ex.Message}");
                    }
                }

                LogMessage($"🎉 Migración completada!");
                LogMessage($"📈 Resumen: {migratedCount} migradas, {errorCount} errores");

                return _bibliotecaMapping;
            }
            catch (Exception ex)
            {
                LogMessage($"💥 Error general en migración: {ex.Message}");
                throw;
            }
        }

        private async Task AnalyzeBibliotecaStructureAsync()
        {
            LogMessage("🔍 Analizando estructura de biblioteca...");

            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();

            // Contar total de nodos biblioteca
            var totalCount = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM node WHERE type = 'biblioteca'");
            LogMessage($"   📊 Total nodos biblioteca: {totalCount}");

            // Verificar campos específicos de biblioteca
            await CheckBibliotecaFieldsAsync(connection);

            // Mostrar ejemplos
            await ShowBibliotecaExamplesAsync(connection);
        }

        private async Task CheckBibliotecaFieldsAsync(MySqlConnection connection)
        {
            LogMessage("   🔍 Campos disponibles para biblioteca:");

            var fieldsToCheck = new Dictionary<string, string>
            {
                ["body"] = "field_data_body",
                ["bajada"] = "field_data_field_bajada",
                ["featured_image"] = "field_data_field_featured_image",
                ["imagen"] = "field_data_field_imagen",
                ["archivo"] = "field_data_field_archivo",
                ["descripcion"] = "field_data_field_descripcion",
                ["autor"] = "field_data_field_autor",
                ["fecha"] = "field_data_field_fecha",
                ["video_url"] = "field_data_field_video_url",
                ["biblioteca_categorias"] = "field_data_bibliteca_categorias"
            };

            foreach (var field in fieldsToCheck)
            {
                try
                {
                    var count = await connection.QueryFirstOrDefaultAsync<int>(
                        $"SELECT COUNT(*) FROM {field.Value} WHERE bundle = 'biblioteca'");

                    if (count > 0)
                    {
                        LogMessage($"      ✅ {field.Key}: {count} registros");
                    }
                }
                catch
                {
                    // Campo no existe - ignorar
                }
            }
        }

        private async Task ShowBibliotecaExamplesAsync(MySqlConnection connection)
        {
            LogMessage("   📝 Ejemplos de publicaciones biblioteca:");

            try
            {
                var examples = await connection.QueryAsync<dynamic>(@"
                    SELECT 
                        n.nid,
                        n.title,
                        n.uid,
                        n.status,
                        n.created,
                        CASE WHEN b.body_value IS NOT NULL THEN 'SÍ' ELSE 'NO' END as tiene_body,
                        CASE WHEN img.field_featured_image_fid IS NOT NULL THEN 'SÍ' ELSE 'NO' END as tiene_imagen,
                        CASE WHEN arch.field_archivo_fid IS NOT NULL THEN 'SÍ' ELSE 'NO' END as tiene_archivo
                    FROM node n
                    LEFT JOIN field_data_body b ON n.nid = b.entity_id AND b.bundle = 'biblioteca'
                    LEFT JOIN field_data_field_featured_image img ON n.nid = img.entity_id AND img.bundle = 'biblioteca'
                    LEFT JOIN field_data_field_archivo arch ON n.nid = arch.entity_id AND arch.bundle = 'biblioteca'
                    WHERE n.type = 'biblioteca'
                    ORDER BY n.created DESC
                    LIMIT 3");

                foreach (var example in examples)
                {
                    LogMessage($"      📄 [{example.nid}] {example.title}");
                    LogMessage($"         Estado: {example.status}, Body: {example.tiene_body}, Imagen: {example.tiene_imagen}, Archivo: {example.tiene_archivo}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"   ❌ Error mostrando ejemplos: {ex.Message}");
            }
        }

        private async Task<List<BibliotecaPost>> GetBibliotecaPostsAsync()
        {
            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();

            LogMessage("🔍 Obteniendo publicaciones de biblioteca...");

            var query = @"
                SELECT 
    n.nid,
    n.title,
    n.uid,
    n.created,
    n.changed,
    n.status,
    b.body_value,
    fdfa.field_adjuntos_fid,
    fm.filename,
    fm.uri,
    fdfb.field_bajada_value,
    fdfbfda.field_biblioteca_fecha_de_alta_value,
    fdfc.field_categoria_tid,
    ttd.name,
    fdffc.field_featured_categories_tid,
    fdffi.field_featured_image_fid,
    fm2.filename filename2,
    fm2.uri uri2,
    fdft.field_tags_tid
FROM node n
LEFT JOIN field_data_body b ON n.nid = b.entity_id AND b.bundle = 'biblioteca'
left join field_data_field_adjuntos fdfa on n.nid = fdfa.entity_id AND b.bundle = 'biblioteca'
left join file_managed fm on fdfa.field_adjuntos_fid = fm.fid
left join field_data_field_bajada fdfb on fdfb.entity_id = n.nid and b.bundle = 'biblioteca'
left join field_data_field_biblioteca_fecha_de_alta fdfbfda on fdfbfda.entity_id = n.nid
left join field_data_field_categoria fdfc on fdfc.entity_id = n.nid and fdfc.bundle = 'biblioteca'
left join taxonomy_term_data ttd on ttd.tid = fdfc.field_categoria_tid
left join field_data_field_featured_categories fdffc on fdffc.entity_id = n.nid 
left join field_data_field_featured_image fdffi on fdffi.entity_id  = n.nid
left join file_managed fm2 on fm2.fid = fdffi.field_featured_image_fid
left join field_data_field_tags fdft on fdft.entity_id = n.nid
WHERE n.type = 'biblioteca'
ORDER BY n.created DESC";

            var posts = await connection.QueryAsync<BibliotecaPost>(query);
            var postList = posts.ToList();

            // Agrupar por nid para manejar múltiples categorías y archivos
            var groupedPosts = postList.GroupBy(p => p.Nid).Select(g =>
            {
                var mainPost = g.First();

                // Recopilar todas las categorías únicas
                var categories = g.Where(p => p.Field_Categoria_Tid.HasValue)
                                  .Select(p => p.Field_Categoria_Tid.Value)
                                  .Distinct()
                                  .ToList();

                // Agregar featured categories si existen
                var featuredCategories = g.Where(p => p.Field_Featured_Categories_Tid.HasValue)
                                          .Select(p => p.Field_Featured_Categories_Tid.Value)
                                          .Distinct()
                                          .ToList();

                categories.AddRange(featuredCategories);
                mainPost.Categories = categories.Distinct().ToList();

                var tags = g.Where(p => p.field_tags_tid.HasValue) // Asumiendo que tienes este campo
                .Select(p => p.field_tags_tid.Value)
                .Distinct()
                .ToList();

                mainPost.Tags = tags;

                return mainPost;
            }).ToList();

            LogMessage($"✅ Posts de biblioteca procesados: {groupedPosts.Count}");

            // Mostrar estadísticas rápidas
            var withImages = groupedPosts.Count(p => p.Field_Featured_Image_Fid.HasValue);
            var withFiles = groupedPosts.Count(p => p.Field_Adjuntos_Fid.HasValue);
            var withBajada = groupedPosts.Count(p => !string.IsNullOrEmpty(p.Field_Bajada_Value));
            var withCategories = groupedPosts.Count(p => p.Categories.Any());

            LogMessage($"   📊 Estadísticas rápidas:");
            LogMessage($"   🖼️ Con imagen destacada: {withImages}");
            LogMessage($"   📎 Con archivos adjuntos: {withFiles}");
            LogMessage($"   📝 Con bajada: {withBajada}");
            LogMessage($"   📂 Con categorías: {withCategories}");

            return groupedPosts;
        }

        private async Task MigrateBibliotecaPostAsync(BibliotecaPost bibliotecaPost)
        {
            // Preparar contenido del post
            //var content = PrepareContent(bibliotecaPost);
            var excerpt = !string.IsNullOrEmpty(bibliotecaPost.Bajada) ? bibliotecaPost.Bajada : "";

            // Obtener autor de WordPress
            var authorId = _userMapping.TryGetValue(bibliotecaPost.Uid, out int value) ? value : 1;

            // Crear post en WordPress
            var wpPost = new Post
            {
                Title = new Title(bibliotecaPost.Title),
                Content = new Content(bibliotecaPost.Content),
                Excerpt = new Excerpt(excerpt),
                Author = authorId,
                Status = bibliotecaPost.Status == 1 ? Status.Publish : Status.Draft,
                Date = DateTimeOffset.FromUnixTimeSeconds(bibliotecaPost.Created).DateTime,
                Modified = DateTimeOffset.FromUnixTimeSeconds(bibliotecaPost.Changed).DateTime
            };

            wpPost.Categories = []; 
            // Asignar categorías si existen
            if (bibliotecaPost.Categories?.Count > 0)
            {
                var wpCategories = bibliotecaPost.Categories
                    .Where(catId => _categoryMapping.ContainsKey(catId))
                    .Select(catId => _categoryMapping[catId])
                    .ToList();

                if (wpCategories.Any())
                    wpPost.Categories = wpCategories;
            }
            if (!wpPost.Categories.Contains(CategoryBiblioteca))
            {
                wpPost.Categories.Add(CategoryBiblioteca); // Siempre agregar la categoría Biblioteca
            }
            if (bibliotecaPost.Tags?.Count > 0 )
            {
                var wpTags = bibliotecaPost.Tags
                    .Where(tagId => _tagMapping.ContainsKey(tagId))
                    .Select(tagId => _tagMapping[tagId])
                    .ToList();
            }

            // Crear el post
            var createdPost = await _wpClient.Posts.CreateAsync(wpPost);

            // Procesar imagen destacada si existe
            // Guardar mapping
            await SaveBibliotecaMappingAsync(bibliotecaPost.Nid, createdPost.Id);
            _bibliotecaMapping[bibliotecaPost.Nid] = new()
            {
                DrupalPostId = bibliotecaPost.Nid,
                WpPostId = createdPost.Id,
                MigratedAt = DateTime.Now,
                Imagenes = false
            };
            //_bibliotecaMapping[bibliotecaPost.Nid] = createdPost.Id;

            //await ProcessFeaturedImageAsync(createdPost.Id, bibliotecaPost);

            


            LogMessage($"📚 Post biblioteca creado: {bibliotecaPost.Title} (WP ID: {createdPost.Id})");
        }

        private string PrepareContent(BibliotecaPost post)
        {
            var content = post.Content ?? "";
            /*
            // Agregar fecha de alta si existe
            if (!string.IsNullOrEmpty(post.FechaAlta))
            {
                content += $"\n\n<p><strong>Fecha de alta:</strong> {post.FechaAlta}</p>";
            }
            */
            // Agregar información del archivo adjunto si existe
            if (post.ArchivoFid.HasValue && !string.IsNullOrEmpty(post.ArchivoFilename))
            {
                content += $"\n\n<h3>Archivo adjunto</h3>\n<p><strong>Archivo:</strong> {post.ArchivoFilename}</p>";
            }

            return content;
        }

        private async Task ProcessFeaturedImageAsync(int postId, BibliotecaPost post)
        {
            //Hay 2 tipos de imagenes, destacada y de contenido
            //Y tambien archivos incrustados que hay que buscar. 
            //Todo esto va a tardar un huevo, tengo que pensar como optimizarlo
            try
            {
                if (post.Filename?.Length > 0)
                {
                   
                    var imagePath = post.uri.Replace("public://", "sites/default/files/");
                    LogMessage($"🖼️ Post {postId} tiene imagen: {post.uri}{post.Filename} ruta: {imagePath}");
                    //La cargo del disco, arreglo el uri
                    //la subo como imagen
                }
                if (post.Filename2?.Length > 0)
                {
                    var imagePath = post.uri2.Replace("public://", "sites/default/files/");
                    var RutaCompleta = Path.Combine("C:\\xampp7\\htdocs\\comunicarseweb", imagePath);
                    LogMessage($"🖼️ Post {postId} tiene imagen destacada: {post.uri2}{post.Filename2} ruta: {RutaCompleta}");
                    //Lo busco en el diccionario primero
                    if (_ImageMapping.TryGetValue(RutaCompleta, out var mediaId))
                    {
                        LogMessage($"   🖼️ Imagen destacada ya existe en WP: {mediaId}");
                        //Si ya existe, lo asigno al post
                        var postToUpdate = await _wpClient.Posts.GetByIDAsync(postId);
                        postToUpdate.FeaturedMedia = mediaId;
                        await _wpClient.Posts.UpdateAsync(postToUpdate);
                        return;
                    }
                    else
                    {
                        //lo traigo del disco
                        using var fileStream = new FileStream(RutaCompleta, FileMode.Open, FileAccess.Read);
                        var mediaItem = await _wpClient.Media.CreateAsync(fileStream, post.Filename2);
                        var postToUpdate = await _wpClient.Posts.GetByIDAsync(postId);
                        postToUpdate.FeaturedMedia = mediaItem.Id;
                        await _wpClient.Posts.UpdateAsync(postToUpdate);
                        //Armo una cache de archivos, asi, si existe, no lo vuelvo a buscar.
                        _ImageMapping[RutaCompleta] = mediaItem.Id;
                    }
                    //es la imagen destacada
                }
                //Chequeo si el post dentro de su codigo contiene una imagen

                // Aquí podrías implementar la migración de imagen
                // Por ahora solo registramos que existe

            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Error procesando imagen para post {postId}: {ex.Message}");
            }
        }

        private async Task RevisarProcesoActual()
        {
            using var connection = new MySqlConnection(_wpConnectionString);
            await connection.OpenAsync();
            var result = await connection.QueryAsync<int>(@"
select id
from wp_posts wp 
where wp.ID  not in (select wp_post_id  from post_mapping_biblioteca)
and wp.id > 15");
            //Si hay algo, borro esos post
            foreach (var item in result)
            {
                await _wpClient.Posts.DeleteAsync(item,true);
                LogMessage($"⚠️ Borre Post {item}");
            }
        }
        private async Task SaveBibliotecaMappingAsync(int drupalId, int wpId)
        {
            using var connection = new MySqlConnection(_wpConnectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(@"
                INSERT INTO post_mapping_biblioteca (drupal_post_id, wp_post_id, migrated_at, imagenes) 
                VALUES (@drupalId, @wpId, @migratedAt, @imagenes) 
                ON DUPLICATE KEY UPDATE 
                    wp_post_id = @wpId, 
                    migrated_at = @migratedAt",
                new { drupalId, wpId, migratedAt = DateTime.Now, imagenes = false });
        }

        private async Task LoadMappingsAsync()
        {
            LogMessage("📋 Cargando mappings existentes...");

            using var connection = new MySqlConnection(_wpConnectionString);
            await connection.OpenAsync();

            // Cargar mapeo de usuarios
            var userMappings = await connection.QueryAsync<dynamic>(
                "SELECT drupal_user_id, wp_user_id FROM user_mapping WHERE drupal_user_id IS NOT NULL");
            _userMapping = userMappings.ToDictionary(x => (int)x.drupal_user_id, x => (int)x.wp_user_id);

            // Cargar mapeo de categorías de biblioteca
            var categoryMappings = await connection.QueryAsync<dynamic>(
                "SELECT drupal_category_id, wp_category_id FROM category_mapping WHERE vocabulary = 'bibliteca_categorias'");
            _categoryMapping = categoryMappings.ToDictionary(x => (int)x.drupal_category_id, x => (int)x.wp_category_id);

            var tagMappings = await connection.QueryAsync<dynamic>(
                "SELECT drupal_tag_id, wp_tag_id FROM tag_mapping");

            var bibliotecaMappings = await connection.QueryAsync<MigratedPostInfo>(
                @"SELECT drupal_post_id DrupalPostId,
                    wp_post_id WpPostId,
                    migrated_at MigratedAt,
                    imagenes
                  from post_mapping_biblioteca");
            _bibliotecaMapping = bibliotecaMappings.ToDictionary(x => x.DrupalPostId, 
                x => new MigratedPostInfo() {  DrupalPostId = x.DrupalPostId , WpPostId = x.WpPostId, MigratedAt = x.MigratedAt,Imagenes = x.Imagenes });
                
                LogMessage($"✅ Mappings cargados: Usuarios={_userMapping.Count}, Categorías biblioteca={_categoryMapping.Count}");
        }

        private async Task AgregarCategoriaBiblioteca()
        {
            LogMessage("🔧 Verificando categoría 'Biblioteca' en WordPress...");
            // Verificar si la categoría ya existe
            var CategoryQuery = new CategoriesQueryBuilder();
            CategoryQuery.Search = "Biblioteca";
            CategoryQuery.BuildQuery();
            var existingCategory = await _wpClient.Categories.QueryAsync(CategoryQuery);
            if (existingCategory != null)
            {
                CategoryBiblioteca = existingCategory.FirstOrDefault()?.Id ?? 0;
                LogMessage("   ✅ Categoría 'Biblioteca' ya existe en WordPress");
                return;
            }
            // Crear la categoría si no existe
            var newCategory = new Category
            {
                Name = "Biblioteca",
                Slug = "biblioteca"
            };
            var createdCategory = await _wpClient.Categories.CreateAsync(newCategory);
            CategoryBiblioteca = createdCategory.Id;
            LogMessage($"   ✅ Categoría 'Biblioteca' creada con ID {createdCategory.Id}");
        }

        private void LogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}\n";

            _statusTextBlock.Dispatcher.Invoke(() =>
            {
                _statusTextBlock.Text += logEntry;
                _logScrollViewer?.ScrollToBottom();
            });
        }
    }
}
