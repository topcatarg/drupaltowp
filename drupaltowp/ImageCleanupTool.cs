using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Dapper;
using MySql.Data.MySqlClient;
using WordPressPCL;

namespace drupaltowp
{
    public class ImageCleanupTool
    {
        private readonly string _wpConnectionString;
        private readonly WordPressClient _wpClient;
        private readonly TextBlock _statusTextBlock;
        private readonly ScrollViewer _logScrollViewer;

        public ImageCleanupTool(string wpConnectionString, WordPressClient wpClient,
                               TextBlock statusTextBlock, ScrollViewer logScrollViewer)
        {
            _wpConnectionString = wpConnectionString;
            _wpClient = wpClient;
            _statusTextBlock = statusTextBlock;
            _logScrollViewer = logScrollViewer;
        }

        public async Task CleanupMigratedImagesAsync()
        {
            try
            {
                LogMessage("🧹 Iniciando limpieza de imágenes migradas...");

                // 1. Obtener lista de imágenes migradas
                var migratedImages = await GetMigratedImagesAsync();
                LogMessage($"📊 Encontradas {migratedImages.Count} imágenes migradas");

                if (migratedImages.Count == 0)
                {
                    LogMessage("✅ No hay imágenes migradas para limpiar");
                    return;
                }

                // 2. Mostrar confirmación
                var result = MessageBox.Show(
                    $"Se encontraron {migratedImages.Count} imágenes migradas.\n\n" +
                    "¿Deseas eliminarlas de WordPress?\n\n" +
                    "⚠️ Esta acción no se puede deshacer.",
                    "Confirmar Limpieza",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    LogMessage("❌ Limpieza cancelada por el usuario");
                    return;
                }

                // 3. Eliminar imágenes de WordPress
                int deletedCount = 0;
                int errorCount = 0;

                foreach (var image in migratedImages)
                {
                    try
                    {
                        await _wpClient.Media.DeleteAsync(image.WpId);
                        deletedCount++;

                        if (deletedCount % 10 == 0)
                        {
                            LogMessage($"🗑️ Eliminadas: {deletedCount}/{migratedImages.Count}");
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        LogMessage($"⚠️ Error eliminando imagen ID {image.WpId}: {ex.Message}");
                    }
                }

                // 4. Limpiar tabla de mapping
                await CleanupMappingTableAsync();

                LogMessage($"✅ Limpieza completada:");
                LogMessage($"   📊 Imágenes eliminadas: {deletedCount}");
                LogMessage($"   ❌ Errores: {errorCount}");
                LogMessage($"   🧹 Tabla de mapping limpiada");

                MessageBox.Show(
                    $"Limpieza completada exitosamente:\n\n" +
                    $"✅ Imágenes eliminadas: {deletedCount}\n" +
                    $"❌ Errores: {errorCount}",
                    "Limpieza Completada",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogMessage($"💥 Error en limpieza: {ex.Message}");
                MessageBox.Show($"Error en limpieza: {ex.Message}", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        public async Task ShowMigrationStatusAsync()
        {
            try
            {
                LogMessage("📊 Verificando estado de migración de imágenes...");

                using var connection = new MySqlConnection(_wpConnectionString);
                await connection.OpenAsync();

                // Contar imágenes en WordPress
                var wpImageCount = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT COUNT(*) FROM wp_posts WHERE post_type = 'attachment'");

                // Contar mappings
                var mappingCount = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT COUNT(*) FROM media_mapping");

                // Contar imágenes de hoy
                var todayImages = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT COUNT(*) FROM wp_posts WHERE post_type = 'attachment' AND DATE(post_date) = CURDATE()");

                LogMessage($"📈 ESTADO ACTUAL:");
                LogMessage($"   🖼️ Total imágenes en WordPress: {wpImageCount}");
                LogMessage($"   📋 Mappings registrados: {mappingCount}");
                LogMessage($"   📅 Imágenes subidas hoy: {todayImages}");

                // Mostrar últimas imágenes migradas
                var recentImages = await connection.QueryAsync<dynamic>(@"
                    SELECT post_title, post_date 
                    FROM wp_posts 
                    WHERE post_type = 'attachment' 
                    ORDER BY post_date DESC 
                    LIMIT 5");

                if (recentImages.Any())
                {
                    LogMessage($"📋 Últimas 5 imágenes:");
                    foreach (var img in recentImages)
                    {
                        LogMessage($"   - {img.post_title} ({img.post_date})");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Error verificando estado: {ex.Message}");
            }
        }

        private async Task<List<MigratedImageInfo>> GetMigratedImagesAsync()
        {
            using var connection = new MySqlConnection(_wpConnectionString);
            await connection.OpenAsync();

            // Obtener imágenes de la tabla de mapping
            var mappingImages = await connection.QueryAsync<MigratedImageInfo>(@"
                SELECT 
                    drupal_file_id as DrupalFid,
                    wp_media_id as WpId,
                    drupal_filename as Filename,
                    migrated_at as MigratedAt
                FROM media_mapping
                ORDER BY migrated_at DESC");

            var imageList = mappingImages.ToList();

            // Si no hay en mapping, buscar por fecha (imágenes migradas hoy)
            if (imageList.Count == 0)
            {
                var todayImages = await connection.QueryAsync<MigratedImageInfo>(@"
                    SELECT 
                        0 as DrupalFid,
                        ID as WpId,
                        post_title as Filename,
                        post_date as MigratedAt
                    FROM wp_posts 
                    WHERE post_type = 'attachment' 
                    AND DATE(post_date) = CURDATE()
                    ORDER BY post_date DESC");

                imageList = todayImages.ToList();
            }

            return imageList;
        }

        private async Task CleanupMappingTableAsync()
        {
            using var connection = new MySqlConnection(_wpConnectionString);
            await connection.OpenAsync();

            var deletedMappings = await connection.ExecuteAsync("DELETE FROM media_mapping");
            LogMessage($"🧹 Eliminados {deletedMappings} registros de mapping");
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

    public class MigratedImageInfo
    {
        public int DrupalFid { get; set; }
        public int WpId { get; set; }
        public string Filename { get; set; }
        public DateTime MigratedAt { get; set; }
    }
}