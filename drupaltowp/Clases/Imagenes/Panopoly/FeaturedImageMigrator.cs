using Dapper;
using drupaltowp.Configuracion;
using drupaltowp.Models;
using drupaltowp.Services;
using drupaltowp.ViewModels;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WordPressPCL;

namespace drupaltowp.Clases.Imagenes.Panopoly;

internal class FeaturedImageMigrator(LoggerViewModel _logger, List<MigratedPostWithImage> PostList, MappingService _mappingService, WordPressClient _wpClient)
{

    public async Task FeaturedImageProcessor()
    {
        int total = PostList.Count;
        int count = 0;
        foreach (var post in PostList)
        {
            // ✅ PASO 1: Verificar si ya está en el CACHE LOCAL (BÚSQUEDA EN MEMORIA O(1))
            if (_mappingService.MediaMapping.TryGetValue(post.FeaturedImage.Fid, out int existingWpId))
            {
                // Ya existe - solo asignar al post
                await AssignFeaturedImageToPostAsync(post.WpPostId, existingWpId);
                _logger.LogInfo($"📸 Imagen destacada existente asignada: {post.FeaturedImage.Filename} (ID: {existingWpId})");
                return;
            }
            // ✅ PASO 2: No existe - subir, asignar y guardar en mapeo
            var wpMediaId = await MigrateFileToWordPressAsync(post.FeaturedImage);

            if (!wpMediaId.HasValue)
            {
                _logger.LogError($"Error subiendo imagen destacada: {post.FeaturedImage.Filename}");
                return; // Error
            }

            // Asignar como imagen destacada del post
            await AssignFeaturedImageToPostAsync(post.WpPostId, wpMediaId.Value);

            // Guardar en BD Y actualizar cache local
            await SaveFileMapping(post.FeaturedImage.Fid, wpMediaId.Value, post.FeaturedImage.Filename);

            _logger.LogInfo($"📸 Imagen destacada migrada y asignada: {post.FeaturedImage.Filename} (ID: {wpMediaId.Value})");

            count++;

            if (count % 100 == 0)
            {
                var percentage = (count * 100.0) / total;
                _logger.LogInfo($"Procesados {count} de {total} ({percentage:F1}%)");
            }


        }
    }

    private async Task SaveFileMapping(int drupalFid, int wpMediaId, string filename)
    {
        try
        {
            // 1️⃣ GUARDAR EN BASE DE DATOS
            using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(@"
            INSERT INTO media_mapping (drupal_file_id, wp_media_id, drupal_filename, migrated_at) 
            VALUES (@drupalFid, @wpMediaId, @filename, NOW())
            ON DUPLICATE KEY UPDATE 
                wp_media_id = @wpMediaId, 
                migrated_at = NOW()",
                new { drupalFid, wpMediaId, filename });

            // 2️⃣ ACTUALIZAR CACHE LOCAL (mantener sincronizado)
            _mappingService.MediaMapping[drupalFid] = wpMediaId;

            _logger.LogInfo($"💾 Mapeo guardado: Drupal FID {drupalFid} → WP ID {wpMediaId} ({filename})");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error guardando mapeo para {filename}: {ex.Message}");
            throw; // Re-lanzar para que el llamador sepa que falló
        }
    }

    private async Task<int?> MigrateFileToWordPressAsync(PostFile file)
    {
        try
        {
            var drupalPath = file.Uri.Replace("public://", "");
            var sourcePath = Path.Combine(ConfiguracionGeneral.DrupalFileRoute, drupalPath);

            if (!File.Exists(sourcePath))
            {
                _logger.LogWarning($"Archivo no encontrado: {sourcePath}");
                return null;
            }

            // Usar la API de WordPress para subir el archivo
            using var fileStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
            var mediaItem = await _wpClient.Media.CreateAsync(fileStream, file.Filename);

            return mediaItem.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error migrando archivo {file.Filename}: {ex.Message}");
            return null;
        }
    }

    private static async Task AssignFeaturedImageToPostAsync(int postId, int mediaId)
    {
        using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync(@"
                INSERT INTO wp_postmeta (post_id, meta_key, meta_value) 
                VALUES (@postId, '_thumbnail_id', @mediaId)
                ON DUPLICATE KEY UPDATE meta_value = @mediaId",
            new { postId, mediaId });
    }
}
