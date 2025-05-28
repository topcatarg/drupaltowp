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
        private readonly ScrollViewer _logScrollViewer;

        public CategoryMigratorWPF(string drupalConnectionString, string wordpressConnectionString,
                                   WordPressClient wpClient, TextBlock statusTextBlock, ScrollViewer logScrollViewer)
        {
            _drupalConnectionString = drupalConnectionString;
            _wordpressConnectionString = wordpressConnectionString;
            _wpClient = wpClient;
            _categoryMapping = new Dictionary<int, int>();
            _statusTextBlock = statusTextBlock;
            _logScrollViewer = logScrollViewer;
            _dispatcher = _statusTextBlock.Dispatcher;
        }

        private void UpdateStatus(string message)
        {
            _dispatcher.Invoke(() =>
            {
                _statusTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - {message}\n";
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
                await LoadExistingMappingAsync(vocabularyMachineName);

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
                        await MigrateSingleCategoryAsync(drupalCategory, vocabularyMachineName);
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

        private async Task MigrateSingleCategoryAsync(DrupalCategory drupalCategory, string vocabulary)
        {
            // Verificar si ya existe en WordPress
            var existingCategories = await _wpClient.Categories.GetAllAsync();
            var existing = existingCategories.FirstOrDefault(c =>
                c.Name.Equals(drupalCategory.Name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                UpdateStatus($"⚠️ Categoría '{drupalCategory.Name}' ya existe en WordPress");
                _categoryMapping[drupalCategory.Tid] = existing.Id;
                await SaveMappingAsync(drupalCategory.Tid, existing.Id, drupalCategory.Name, vocabulary);
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
            await SaveMappingAsync(drupalCategory.Tid, createdCategory.Id, drupalCategory.Name, vocabulary);
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

        #region Métodos para manejo de tabla de mapeo - CORREGIDOS

        private async Task CreateMappingTableAsync()
        {
            const string createTableQuery = @"
            CREATE TABLE IF NOT EXISTS category_mapping (
                id INT AUTO_INCREMENT PRIMARY KEY,
                drupal_category_id INT NOT NULL,
                wp_category_id BIGINT UNSIGNED NOT NULL,
                drupal_name VARCHAR(255),
                wp_name VARCHAR(255),
                vocabulary VARCHAR(255),
                migrated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                UNIQUE KEY unique_drupal_category (drupal_category_id, vocabulary),
                KEY idx_wp_category (wp_category_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=latin1";

            using var connection = new MySqlConnection(_wordpressConnectionString);
            await connection.ExecuteAsync(createTableQuery);
            UpdateStatus("✅ Tabla de mapeo creada/verificada: category_mapping");
        }

        private async Task LoadExistingMappingAsync(string vocabulary)
        {
            const string query = @"
            SELECT drupal_category_id, wp_category_id 
            FROM category_mapping 
            WHERE vocabulary = @Vocabulary";

            using var connection = new MySqlConnection(_wordpressConnectionString);
            var mappings = await connection.QueryAsync<(int DrupalId, int WordPressId)>(
                query, new { Vocabulary = vocabulary });

            foreach (var (drupalId, wordPressId) in mappings)
            {
                _categoryMapping[drupalId] = wordPressId;
            }

            UpdateStatus($"✅ Cargados {mappings.Count()} mapeos existentes de categorías para {vocabulary}");
        }

        private async Task SaveMappingAsync(int drupalId, int wordpressId, string drupalName, string vocabulary)
        {
            const string insertQuery = @"
            INSERT INTO category_mapping 
            (drupal_category_id, wp_category_id, drupal_name, vocabulary, migrated_at) 
            VALUES (@DrupalId, @WordPressId, @DrupalName, @Vocabulary, @MigratedAt)
            ON DUPLICATE KEY UPDATE 
                wp_category_id = VALUES(wp_category_id),
                drupal_name = VALUES(drupal_name),
                migrated_at = CURRENT_TIMESTAMP";

            using var connection = new MySqlConnection(_wordpressConnectionString);
            await connection.ExecuteAsync(insertQuery, new
            {
                DrupalId = drupalId,
                WordPressId = wordpressId,
                DrupalName = drupalName,
                Vocabulary = vocabulary,
                MigratedAt = DateTime.Now
            });
        }

        public async Task<Dictionary<int, int>> GetMappingAsync(string vocabulary)
        {
            const string query = @"
            SELECT drupal_category_id, wp_category_id 
            FROM category_mapping 
            WHERE vocabulary = @Vocabulary";

            using var connection = new MySqlConnection(_wordpressConnectionString);
            var mappings = await connection.QueryAsync<(int DrupalId, int WordPressId)>(
                query, new { Vocabulary = vocabulary });

            return mappings.ToDictionary(m => m.DrupalId, m => m.WordPressId);
        }

        #endregion
    }
}