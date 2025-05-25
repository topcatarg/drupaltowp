using Dapper;
using drupaltowp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using WordPressPCL;
using WordPressPCL.Models;

namespace drupaltowp
{
    internal class CategoryMigratorWPF
    {
    private readonly string _drupalConnectionString;
        private readonly string _wordpressConnectionString;
        private readonly WordPressClient _wpClient;
        private readonly Dictionary<int, int> _categoryMapping;
        private readonly TextBlock _statusTextBlock;
        private readonly Dispatcher _dispatcher;
        private readonly ScrollViewer _logScrollViewer; // Agregar esta referencia


        public CategoryMigratorWPF(string drupalConnectionString, string wordpressConnectionString,
                                   WordPressClient wpClient, TextBlock statusTextBlock, ScrollViewer logScrollViewer)
        {
            _drupalConnectionString = drupalConnectionString;
            _wordpressConnectionString = wordpressConnectionString;
            _wpClient = wpClient;
            _categoryMapping = new Dictionary<int, int>();
            _statusTextBlock = statusTextBlock;
            _logScrollViewer = logScrollViewer; // Agregar esta línea
            _dispatcher = _statusTextBlock.Dispatcher;
        }

        private void UpdateStatus(string message)
        {
            _dispatcher.Invoke(() =>
            {
                _statusTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - {message}\n";

                // Hacer scroll automático al final
                _logScrollViewer.ScrollToBottom();
            });
        }

        public async Task<Dictionary<int, int>> MigrateCategoriesAsync(string vocabularyMachineName = "categories")
        {
            try
            {
                UpdateStatus($"🚀 Iniciando migración de categorías del vocabulario: {vocabularyMachineName}");

                // 0. Crear tabla de mapeo si no existe
                await CreateMappingTableAsync();

                // 1. Cargar mapeo existente de la BD
                await LoadExistingMappingAsync("category");

                // 2. Extraer categorías de Drupal 7
                UpdateStatus("📊 Extrayendo categorías de Drupal 7...");
                var drupalCategories = await GetDrupalCategoriesAsync(vocabularyMachineName);
                UpdateStatus($"✅ Encontradas {drupalCategories.Count} categorías en Drupal");

                if (drupalCategories.Count == 0)
                {
                    UpdateStatus("⚠️ No se encontraron categorías para migrar");
                    return _categoryMapping;
                }

                // 3. Ordenar por jerarquía (padres primero)
                UpdateStatus("🔄 Ordenando categorías por jerarquía...");
                var orderedCategories = OrderCategoriesByHierarchy(drupalCategories);

                // 4. Migrar una por una
                int processed = 0;
                int skipped = 0;
                int errors = 0;

                UpdateStatus($"🔄 Iniciando migración de {orderedCategories.Count} categorías...");

                foreach (var drupalCategory in orderedCategories)
                {
                    // Skip si ya está migrada
                    if (_categoryMapping.ContainsKey(drupalCategory.Tid))
                    {
                        UpdateStatus($"↻ Ya migrada: {drupalCategory.Name} (ID: {drupalCategory.Tid})");
                        skipped++;
                        continue;
                    }

                    try
                    {
                        await MigrateSingleCategoryAsync(drupalCategory);
                        UpdateStatus($"✅ Migrada: {drupalCategory.Name} (ID: {drupalCategory.Tid})");
                        processed++;

                        // Pequeña pausa para no saturar la API
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        UpdateStatus($"❌ Error migrando {drupalCategory.Name}: {ex.Message}");
                        errors++;
                    }
                }

                UpdateStatus($"🎉 Migración completada!");
                UpdateStatus($"📈 Resumen: {processed} migradas, {skipped} ya existían, {errors} errores");
                UpdateStatus($"📊 Total en mapeo: {_categoryMapping.Count} categorías");

                return _categoryMapping;
            }
            catch (Exception ex)
            {
                UpdateStatus($"💥 Error general en migración: {ex.Message}");
                throw;
            }
        }

        private async Task<List<DrupalCategory>> GetDrupalCategoriesAsync(string vocabularyMachineName)
        {
            const string query = @"
            SELECT 
                t.tid,
                t.name,
                t.description,
                t.weight,
                v.name as vocabulary_name,
                v.machine_name as vocabulary_machine_name,
                COALESCE(h.parent, 0) as parent_tid
            FROM taxonomy_term_data t
            LEFT JOIN taxonomy_vocabulary v ON t.vid = v.vid
            LEFT JOIN taxonomy_term_hierarchy h ON t.tid = h.tid
            WHERE v.machine_name = @VocabularyMachineName
            ORDER BY h.parent, t.weight, t.name";

            using var connection = new MySqlConnection(_drupalConnectionString);
            return (await connection.QueryAsync<DrupalCategory>(query, new { VocabularyMachineName = vocabularyMachineName })).ToList();
        }

        private List<DrupalCategory> OrderCategoriesByHierarchy(List<DrupalCategory> categories)
        {
            var ordered = new List<DrupalCategory>();
            var processed = new HashSet<int>();

            void ProcessCategory(int parentId)
            {
                var children = categories.Where(c => c.ParentTid == parentId && !processed.Contains(c.Tid)).ToList();

                foreach (var child in children.OrderBy(c => c.Weight).ThenBy(c => c.Name))
                {
                    if (!processed.Contains(child.Tid))
                    {
                        ordered.Add(child);
                        processed.Add(child.Tid);
                        ProcessCategory(child.Tid);
                    }
                }
            }

            ProcessCategory(0);
            return ordered;
        }

        private async Task MigrateSingleCategoryAsync(DrupalCategory drupalCategory)
        {
            // Verificar si ya existe en WordPress
            var existingCategories = await _wpClient.Categories.GetAllAsync();
            var existing = existingCategories.FirstOrDefault(c =>
                c.Name.Equals(drupalCategory.Name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                UpdateStatus($"⚠️ Categoría '{drupalCategory.Name}' ya existe en WordPress");
                _categoryMapping[drupalCategory.Tid] = existing.Id;
                await SaveMappingAsync("category", drupalCategory.Tid, existing.Id, drupalCategory.Name);
                return;
            }

            // Determinar padre en WordPress
            int? wordpressParentId = null;
            if (drupalCategory.ParentTid > 0 && _categoryMapping.TryGetValue(drupalCategory.ParentTid, out int value))
            {
                wordpressParentId = value;
            }

            // Crear nueva categoría
            var wpCategory = new Category
            {
                Name = drupalCategory.Name,
                Description = drupalCategory.Description ?? string.Empty,
                Slug = GenerateSlug(drupalCategory.Name),

            };
            if (wordpressParentId.HasValue && wordpressParentId.Value > 0)
            {
                wpCategory.Parent = wordpressParentId.Value;
            }

            var createdCategory = await _wpClient.Categories.CreateAsync(wpCategory);
            _categoryMapping[drupalCategory.Tid] = createdCategory.Id;

            // Guardar mapeo en BD
            await SaveMappingAsync("category", drupalCategory.Tid, createdCategory.Id, drupalCategory.Name);
        }

        private string GenerateSlug(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "category";

            return name.ToLowerInvariant()
                       .Replace(" ", "-")
                       .Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u")
                       .Replace("ñ", "n").Replace("ü", "u")
                       .Replace("'", "").Replace("\"", "").Replace("/", "-").Replace("\\", "-")
                       .Replace("&", "y").Replace("%", "").Replace("#", "").Replace("@", "")
                       .Replace("!", "").Replace("?", "").Replace("(", "").Replace(")", "")
                       .Replace("[", "").Replace("]", "").Replace("{", "").Replace("}", "")
                       .Replace(",", "").Replace(".", "").Replace(";", "").Replace(":", "")
                       .Replace("+", "").Replace("=", "").Replace("*", "").Replace("^", "")
                       .Replace("$", "").Replace("|", "").Replace("<", "").Replace(">", "")
                       .Replace("~", "").Replace("`", "");
        }

        #region Métodos para manejo de tabla de mapeo

        private async Task CreateMappingTableAsync()
        {
            const string createTableQuery = @"
            CREATE TABLE IF NOT EXISTS wp_drupal_migration_mapping (
                id INT AUTO_INCREMENT PRIMARY KEY,
                content_type VARCHAR(50) NOT NULL,
                drupal_id INT NOT NULL,
                wordpress_id INT NOT NULL,
                drupal_title VARCHAR(255),
                migrated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                INDEX idx_content_drupal (content_type, drupal_id),
                INDEX idx_content_wordpress (content_type, wordpress_id),
                UNIQUE KEY unique_mapping (content_type, drupal_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

            using var connection = new MySqlConnection(_wordpressConnectionString);
            await connection.ExecuteAsync(createTableQuery);
            UpdateStatus("✅ Tabla de mapeo creada/verificada: wp_drupal_migration_mapping");
        }

        private async Task LoadExistingMappingAsync(string contentType)
        {
            const string query = @"
            SELECT drupal_id, wordpress_id 
            FROM wp_drupal_migration_mapping 
            WHERE content_type = @ContentType";

            using var connection = new MySqlConnection(_wordpressConnectionString);
            var mappings = await connection.QueryAsync<(int DrupalId, int WordPressId)>(
                query, new { ContentType = contentType });

            foreach (var (drupalId, wordPressId) in mappings)
            {
                _categoryMapping[drupalId] = wordPressId;
            }

            UpdateStatus($"✅ Cargados {mappings.Count()} mapeos existentes de {contentType}");
        }

        private async Task SaveMappingAsync(string contentType, int drupalId, int wordpressId, string title = null)
        {
            const string insertQuery = @"
            INSERT INTO wp_drupal_migration_mapping 
            (content_type, drupal_id, wordpress_id, drupal_title) 
            VALUES (@ContentType, @DrupalId, @WordPressId, @Title)
            ON DUPLICATE KEY UPDATE 
                wordpress_id = VALUES(wordpress_id),
                drupal_title = VALUES(drupal_title),
                migrated_at = CURRENT_TIMESTAMP";

            using var connection = new MySqlConnection(_wordpressConnectionString);
            await connection.ExecuteAsync(insertQuery, new
            {
                ContentType = contentType,
                DrupalId = drupalId,
                WordPressId = wordpressId,
                Title = title
            });
        }

        public async Task<Dictionary<int, int>> GetMappingAsync(string contentType)
        {
            const string query = @"
            SELECT drupal_id, wordpress_id 
            FROM wp_drupal_migration_mapping 
            WHERE content_type = @ContentType";

            using var connection = new MySqlConnection(_wordpressConnectionString);
            var mappings = await connection.QueryAsync<(int DrupalId, int WordPressId)>(
                query, new { ContentType = contentType });

            return mappings.ToDictionary(m => m.DrupalId, m => m.WordPressId);
        }

        #endregion
    }
}

