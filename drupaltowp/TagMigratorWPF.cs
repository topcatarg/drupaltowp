using Dapper;
using drupaltowp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WordPressPCL;
using WordPressPCL.Models;
using WordPressPCL.Utility;

namespace drupaltowp
{
    public class TagMigratorWPF
    {
        private readonly string _drupalConnectionString;
        private readonly string _wpConnectionString;
        private readonly WordPressClient _wpClient;
        private readonly TextBlock _statusTextBlock;
        private readonly ScrollViewer _logScrollViewer;

        public TagMigratorWPF(string drupalConnectionString, string wpConnectionString,
                             WordPressClient wpClient, TextBlock statusTextBlock, ScrollViewer logScrollViewer)
        {
            _drupalConnectionString = drupalConnectionString;
            _wpConnectionString = wpConnectionString;
            _wpClient = wpClient;
            _statusTextBlock = statusTextBlock;
            _logScrollViewer = logScrollViewer;
        }

        public async Task<Dictionary<int, int>> MigrateTagsAsync(string vocabularyName)
        {
            var tagMapping = new Dictionary<int, int>();

            try
            {
                LogMessage("=== MIGRACIÓN ULTRA-RÁPIDA DE TAGS ===");

                // 1. Carga paralela de datos
                LogMessage("Cargando datos en paralelo...");
                var drupalTagsTask = GetDrupalTagsAsync(vocabularyName);
                var wpTagsTask = GetAllWordPressTagsAsync();
                var existingMappingTask = GetExistingMappingsAsync();

                await Task.WhenAll(drupalTagsTask, wpTagsTask, existingMappingTask);

                var drupalTags = await drupalTagsTask;
                var wpTags = await wpTagsTask;
                var existingMappings = await existingMappingTask;

                LogMessage($"✓ Drupal: {drupalTags.Count} tags");
                LogMessage($"✓ WordPress: {wpTags.Count} tags");
                LogMessage($"✓ Mapeos existentes: {existingMappings.Count}");

                if (drupalTags.Count == 0) return tagMapping;

                // 2. Crear diccionarios para búsqueda O(1)
                var wpTagDict = wpTags.ToDictionary(t => t.Name.ToLowerInvariant(), t => t);
                var existingMappingDict = existingMappings;

                // 3. Clasificar tags ultra-rápido
                var newTags = new List<DrupalTag>();
                int reusedMappings = 0;
                int existingTags = 0;

                foreach (var drupalTag in drupalTags)
                {
                    // Primero verificar mapeo existente
                    if (existingMappingDict.TryGetValue(drupalTag.Id, out int mappedWpId))
                    {
                        tagMapping[drupalTag.Id] = mappedWpId;
                        reusedMappings++;
                        continue;
                    }

                    // Luego verificar si existe el tag en WP
                    if (wpTagDict.TryGetValue(drupalTag.Name.ToLowerInvariant(), out Tag existingTag))
                    {
                        tagMapping[drupalTag.Id] = existingTag.Id;
                        existingTags++;
                    }
                    else
                    {
                        newTags.Add(drupalTag);
                    }
                }

                LogMessage($"✓ Mapeos reutilizados: {reusedMappings}");
                LogMessage($"✓ Tags existentes: {existingTags}");
                LogMessage($"✓ Tags nuevos a crear: {newTags.Count}");

                // 4. Crear tags nuevos con máxima concurrencia
                if (newTags.Count > 0)
                {
                    LogMessage("Iniciando creación masiva de tags...");
                    await CreateTagsMaxConcurrencyAsync(newTags, tagMapping);
                }

                // 5. Guardar mapeos en una sola operación
                if (tagMapping.Count > existingMappings.Count)
                {
                    LogMessage("Guardando mapeos nuevos...");
                    await SaveNewMappingsAsync(tagMapping, drupalTags, existingMappingDict);
                }

                LogMessage($"=== MIGRACIÓN COMPLETADA EN TIEMPO RÉCORD ===");
                LogMessage($"✓ Total procesado: {tagMapping.Count} tags");
                LogMessage($"✓ Nuevos creados: {newTags.Count}");

                return tagMapping;
            }
            catch (Exception ex)
            {
                LogMessage($"❌ ERROR: {ex.Message}", true);
                throw;
            }
        }

        private async Task CreateTagsMaxConcurrencyAsync(List<DrupalTag> newTags, Dictionary<int, int> tagMapping)
        {
            const int maxConcurrency = 50; // Local = sin límites!
            using var semaphore = new SemaphoreSlim(maxConcurrency);
            var successCount = 0;
            var errorCount = 0;

            // Crear todas las tareas de una vez
            var tasks = newTags.Select(async tag =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var wpTag = new Tag
                    {
                        Name = tag.Name,
                        Description = tag.Description ?? "",
                        Slug = GenerateSlugFast(tag.Name)
                    };

                    var createdTag = await _wpClient.Tags.CreateAsync(wpTag);

                    lock (tagMapping)
                    {
                        tagMapping[tag.Id] = createdTag.Id;
                    }

                    Interlocked.Increment(ref successCount);

                    // Log progreso cada 100
                    if (successCount % 100 == 0)
                    {
                        LogMessage($"  ✓ Creados: {successCount}/{newTags.Count}");
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errorCount);
                    // Solo loggear errores críticos para no saturar
                    if (errorCount <= 10)
                    {
                        LogMessage($"  ❌ Error '{tag.Name}': {ex.Message}", true);
                    }
                    return false;
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            // Ejecutar todo en paralelo
            var results = await Task.WhenAll(tasks);

            LogMessage($"✓ Creación completada: {successCount} éxitos, {errorCount} errores");
        }

        private async Task<Dictionary<int, int>> GetExistingMappingsAsync()
        {
            try
            {
                using var connection = new MySqlConnection(_wpConnectionString);
                await connection.OpenAsync();

                var mappings = await connection.QueryAsync<(int DrupalId, int WpId)>(
                    "SELECT drupal_tag_id, wp_tag_id FROM tag_mapping"
                );

                return mappings.ToDictionary(m => m.DrupalId, m => m.WpId);
            }
            catch
            {
                return new Dictionary<int, int>();
            }
        }

        private async Task SaveNewMappingsAsync(Dictionary<int, int> allMappings, List<DrupalTag> drupalTags, Dictionary<int, int> existingMappings)
        {
            try
            {
                // Solo guardar mapeos nuevos
                var newMappings = allMappings
                    .Where(kvp => !existingMappings.ContainsKey(kvp.Key))
                    .Select(kvp => new
                    {
                        DrupalId = kvp.Key,
                        WpId = kvp.Value,
                        TagName = drupalTags.First(t => t.Id == kvp.Key).Name
                    })
                    .ToList();

                if (newMappings.Count == 0) return;

                using var connection = new MySqlConnection(_wpConnectionString);
                await connection.OpenAsync();

                // Inserción masiva ultra-rápida
                const int batchSize = 5000; // Lotes enormes para local
                for (int i = 0; i < newMappings.Count; i += batchSize)
                {
                    var batch = newMappings.Skip(i).Take(batchSize);

                    await connection.ExecuteAsync(@"
                        INSERT INTO tag_mapping (drupal_tag_id, wp_tag_id, drupal_name, migrated_at)
                        VALUES (@DrupalId, @WpId, @TagName, NOW())", batch);

                    LogMessage($"  ✓ Guardados {Math.Min(i + batchSize, newMappings.Count)}/{newMappings.Count} mapeos");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Error guardando mapeos: {ex.Message}", true);
            }
        }

        private async Task<List<Tag>> GetAllWordPressTagsAsync()
        {
            var allTags = new List<Tag>();
            const int perPage = 100; // Máximo permitido por WP API

            int page = 1;
            IEnumerable<Tag> currentPageOfTags;

            // This warning is crucial as we cannot determine total pages from the API
            LogMessage("Advertencia: No se puede obtener el total de páginas de la API (WordPressPCL 2.1.0). Se realizará un recorrido paginado hasta obtener una página vacía.", true);

            do
            {
                TagsQueryBuilder qb = new()
                {
                    Page = page,
                    PerPage = perPage
                };
                // For WordPressPCL 2.1.0, Query() is the expected method for paginated lists
                // when GetAsync has restricted signatures.

                currentPageOfTags = await _wpClient.Tags.QueryAsync(qb);

                if (currentPageOfTags?.Any() == true)
                {
                    allTags.AddRange(currentPageOfTags);
                    LogMessage($"  ✓ Obtenidos {allTags.Count} tags (página {page})");
                    page++;
                }
                else
                {
                    // If the current page is empty or null, it means we've fetched all tags
                    break;
                }

            } while (true); // Loop indefinitely until 'break' condition is met

            return allTags;
        }

        private async Task<List<DrupalTag>> GetDrupalTagsAsync(string vocabularyName)
        {
            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();

            // Query optimizado con índices
            var query = @"
                SELECT 
                    t.tid as Id,
                    t.name as Name,
                    COALESCE(t.description, '') as Description
                FROM taxonomy_term_data t
                INNER JOIN taxonomy_vocabulary v ON t.vid = v.vid
                WHERE v.machine_name = @VocabularyName
                ORDER BY t.tid"; // Orden por ID es más rápido que por nombre

            var tags = await connection.QueryAsync<DrupalTag>(query, new { VocabularyName = vocabularyName });
            return tags.ToList();
        }

        private static string GenerateSlugFast(string name)
        {
            if (string.IsNullOrEmpty(name)) return "tag";

            // Versión ultra-optimizada del slug
            return name.ToLowerInvariant()
                      .Replace(" ", "-")
                      .Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u")
                      .Replace("ñ", "n").Replace("ü", "u")
                      .Replace(",", "").Replace(".", "").Replace("(", "").Replace(")", "")
                      .Replace("[", "").Replace("]", "").Replace("\"", "").Replace("'", "")
                      .Replace("/", "-").Replace("\\", "-");
        }

        private void LogMessage(string message, bool isError = false)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var logEntry = $"[{timestamp}] {message}\n";

                _statusTextBlock.Text += logEntry;
                _logScrollViewer.ScrollToEnd();
            });
        }
    }
}