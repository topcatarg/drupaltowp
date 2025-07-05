using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Controls;
using Dapper;
using drupaltowp.Models;
using MySql.Data.MySqlClient;
using WordPressPCL;

namespace drupaltowp
{
    public class ImageMigratorWPF
    {
        private readonly string _drupalConnectionString;
        private readonly string _wpConnectionString;
        private readonly WordPressClient _wpClient;
        private readonly TextBlock _statusTextBlock;
        private readonly ScrollViewer _logScrollViewer;
        private readonly string _drupalSiteUrl;

        public ImageMigratorWPF(string drupalConnectionString, string wpConnectionString,
                               WordPressClient wpClient, TextBlock statusTextBlock,
                               ScrollViewer logScrollViewer, string drupalSiteUrl = "http://localhost/drupal")
        {
            _drupalConnectionString = drupalConnectionString;
            _wpConnectionString = wpConnectionString;
            _wpClient = wpClient;
            _statusTextBlock = statusTextBlock;
            _logScrollViewer = logScrollViewer;
            _drupalSiteUrl = drupalSiteUrl;
        }

        public async Task<Dictionary<int, int>> MigrateImagesAsync()
        {
            LogMessage("Iniciando migración de imágenes...");
            var mediaMapping = new Dictionary<int, int>();

            try
            {
                // Obtener todas las imágenes de Drupal
                var drupalImages = await GetDrupalImagesAsync();
                LogMessage($"Encontradas {drupalImages.Count} imágenes en Drupal");

                int migratedCount = 0;
                int errorCount = 0;

                foreach (var image in drupalImages)
                {
                    try
                    {
                        // Verificar si ya existe en WordPress
                        var existingMapping = await GetExistingMappingAsync(image.Fid);
                        if (existingMapping.HasValue)
                        {
                            mediaMapping[image.Fid] = existingMapping.Value;
                            LogMessage($"Imagen ya migrada: {image.Filename}");
                            continue;
                        }

                        var wpMediaId = await MigrateImageAsync(image);
                        if (wpMediaId.HasValue)
                        {
                            mediaMapping[image.Fid] = wpMediaId.Value;
                            await SaveMediaMappingAsync(image.Fid, wpMediaId.Value, image.Filename, image.Uri);
                            migratedCount++;
                            LogMessage($"Migrada imagen: {image.Filename} ({migratedCount + errorCount}/{drupalImages.Count})");
                        }
                        else
                        {
                            errorCount++;
                            LogMessage($"Error migrando imagen: {image.Filename}");
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        LogMessage($"Error migrando imagen {image.Filename}: {ex.Message}");
                    }
                }

                LogMessage($"Migración completada: {migratedCount} imágenes migradas, {errorCount} errores");
                return mediaMapping;
            }
            catch (Exception ex)
            {
                LogMessage($"Error en migración: {ex.Message}");
                throw;
            }
        }

        private async Task<List<DrupalImage>> GetDrupalImagesAsync()
        {
            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT 
                    fid,
                    uid,
                    filename,
                    uri,
                    filemime,
                    filesize,
                    timestamp
                FROM file_managed 
                WHERE filemime LIKE 'image/%'
                AND status = 1
                ORDER BY timestamp";

            var images = await connection.QueryAsync<DrupalImage>(query);
            return images.ToList();
        }

        private async Task<int?> GetExistingMappingAsync(int drupalFid)
        {
            using var connection = new MySqlConnection(_wpConnectionString);
            await connection.OpenAsync();

            var wpId = await connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT wp_media_id FROM media_mapping WHERE drupal_file_id = @drupalFid",
                new { drupalFid });

            return wpId;
        }

        private async Task<int?> MigrateImageAsync(DrupalImage image)
        {
            try
            {
                // Construir URL completa de la imagen
                var imagePath = image.Uri.Replace("public://", "sites/default/files/");
                var imageUrl = $"{_drupalSiteUrl}/{imagePath}";

                // Descargar imagen
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(5); // Timeout largo para imágenes grandes

                using var response = await httpClient.GetAsync(imageUrl);
                if (!response.IsSuccessStatusCode)
                {
                    LogMessage($"No se pudo descargar imagen: {imageUrl} - Status: {response.StatusCode}");
                    return null;
                }

                using var imageStream = await response.Content.ReadAsStreamAsync();

                if (imageStream.Length == 0)
                {
                    LogMessage($"Imagen vacía: {imageUrl}");
                    return null;
                }

                // Subir a WordPress
                var mediaItem = await _wpClient.Media.CreateAsync(imageStream, image.Filename);

                LogMessage($"Imagen subida exitosamente: {image.Filename} -> ID: {mediaItem.Id}");
                return mediaItem.Id;
            }
            catch (HttpRequestException ex)
            {
                LogMessage($"Error HTTP descargando {image.Filename}: {ex.Message}");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                LogMessage($"Timeout descargando {image.Filename}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                LogMessage($"Error general migrando {image.Filename}: {ex.Message}");
                return null;
            }
        }

        private async Task SaveMediaMappingAsync(int drupalFid, int wpId, string filename, string uri)
        {
            using var connection = new MySqlConnection(_wpConnectionString);
            await connection.OpenAsync();

            // Obtener URL de WordPress
            var wpMedia = await _wpClient.Media.GetByIDAsync(wpId);

            await connection.ExecuteAsync(@"
                INSERT INTO media_mapping (drupal_file_id, wp_media_id, drupal_filename, wp_filename, drupal_uri, wp_url) 
                VALUES (@drupalFid, @wpId, @drupalFilename, @wpFilename, @drupalUri, @wpUrl)
                ON DUPLICATE KEY UPDATE 
                    wp_media_id = @wpId, 
                    wp_filename = @wpFilename, 
                    wp_url = @wpUrl",
                new
                {
                    drupalFid,
                    wpId,
                    drupalFilename = filename,
                    wpFilename = wpMedia.Title?.Rendered ?? filename,
                    drupalUri = uri,
                    wpUrl = wpMedia.SourceUrl
                });
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