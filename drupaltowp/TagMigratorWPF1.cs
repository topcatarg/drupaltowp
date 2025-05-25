using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;
using MySql.Data.MySqlClient;
using Dapper;
using WordPressPCL;
using WordPressPCL.Models;
using System.Windows;
using drupaltowp.Models;

namespace drupaltowp
{
    public class TagMigratorWPF1
    {
        private readonly string _drupalConnectionString;
        private readonly string _wpConnectionString;
        private readonly WordPressClient _wpClient;
        private readonly TextBlock _statusTextBlock;
        private readonly ScrollViewer _logScrollViewer;

        public TagMigratorWPF1(string drupalConnectionString, string wpConnectionString,
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
                LogMessage("=== INICIANDO MIGRACIÓN DE TAGS ===");
                LogMessage($"Vocabulario Drupal: {vocabularyName}");

                // 1. Obtener tags de Drupal
                var drupalTags = await GetDrupalTagsAsync(vocabularyName);
                LogMessage($"Tags encontrados en Drupal: {drupalTags.Count}");

                if (drupalTags.Count == 0)
                {
                    LogMessage("No se encontraron tags para migrar.");
                    return tagMapping;
                }

                // 2. Migrar cada tag a WordPress
                int migratedCount = 0;
                foreach (var tag in drupalTags)
                {
                    try
                    {
                        LogMessage($"Migrando tag: '{tag.Name}' (ID: {tag.Id})");

                        // Verificar si el tag ya existe en WordPress
                        var existingTag = await CheckIfTagExistsAsync(tag.Name);

                        if (existingTag != null)
                        {
                            LogMessage($"  Tag ya existe en WordPress (ID: {existingTag.Id})");
                            tagMapping[tag.Id] = existingTag.Id;
                        }
                        else
                        {
                            // Crear nuevo tag en WordPress
                            var wpTag = new Tag
                            {
                                Name = tag.Name,
                                Description = tag.Description ?? "",
                                Slug = GenerateSlug(tag.Name)
                            };

                            var createdTag = await _wpClient.Tags.CreateAsync(wpTag);
                            tagMapping[tag.Id] = createdTag.Id;
                            migratedCount++;

                            LogMessage($"  Tag creado exitosamente (ID WP: {createdTag.Id})");
                        }

                        // Guardar mapeo en base de datos
                        await SaveTagMappingAsync(tag.Id, tagMapping[tag.Id], tag.Name);
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"  ERROR al migrar tag '{tag.Name}': {ex.Message}", true);
                    }
                }

                LogMessage($"=== MIGRACIÓN COMPLETADA ===");
                LogMessage($"Tags nuevos creados: {migratedCount}");
                LogMessage($"Total de mapeos: {tagMapping.Count}");

                return tagMapping;
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR GENERAL en migración: {ex.Message}", true);
                throw;
            }
        }

        private async Task<List<DrupalTag>> GetDrupalTagsAsync(string vocabularyName)
        {
            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT 
                    t.tid as Id,
                    t.name as Name,
                    t.description as Description
                FROM taxonomy_term_data t
                INNER JOIN taxonomy_vocabulary v ON t.vid = v.vid
                WHERE v.machine_name = @VocabularyName
                ORDER BY t.name";

            var tags = await connection.QueryAsync<DrupalTag>(query, new { VocabularyName = vocabularyName });
            return tags.ToList();
        }

        private async Task<Tag> CheckIfTagExistsAsync(string tagName)
        {
            try
            {
                var tags = await _wpClient.Tags.GetAllAsync();
                return tags.FirstOrDefault(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private string GenerateSlug(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "tag";

            return name.ToLower()
                      .Replace(" ", "-")
                      .Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u")
                      .Replace("ñ", "n")
                      .Replace("ü", "u")
                      .Replace(",", "")
                      .Replace(".", "")
                      .Replace("(", "").Replace(")", "")
                      .Replace("[", "").Replace("]", "")
                      .Replace("\"", "").Replace("'", "")
                      .Replace("/", "-").Replace("\\", "-");
        }

        private async Task SaveTagMappingAsync(int drupalId, int wpId, string tagName)
        {
            try
            {
                using var connection = new MySqlConnection(_wpConnectionString);
                await connection.OpenAsync();

                var query = @"
                    INSERT INTO drupal_wp_tag_mapping (drupal_tag_id, wp_tag_id, tag_name, created_at)
                    VALUES (@DrupalId, @WpId, @TagName, NOW())
                    ON DUPLICATE KEY UPDATE 
                        wp_tag_id = VALUES(wp_tag_id),
                        tag_name = VALUES(tag_name),
                        updated_at = NOW()";

                await connection.ExecuteAsync(query, new
                {
                    DrupalId = drupalId,
                    WpId = wpId,
                    TagName = tagName
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Advertencia: No se pudo guardar mapeo para tag '{tagName}': {ex.Message}");
            }
        }

        private void LogMessage(string message, bool isError = false)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string logEntry = $"[{timestamp}] {message}\n";

                _statusTextBlock.Text += logEntry;
                _logScrollViewer.ScrollToEnd();
            });
        }


    }
}