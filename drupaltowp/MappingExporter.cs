using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using Dapper;
using MySql.Data.MySqlClient;
using Microsoft.Win32;
using System.Windows;

namespace drupaltowp
{
    public class MappingExporter
    {
        private readonly string _wpConnectionString;
        private readonly TextBlock _statusTextBlock;
        private readonly ScrollViewer _logScrollViewer;

        public MappingExporter(string wpConnectionString, TextBlock statusTextBlock, ScrollViewer logScrollViewer)
        {
            _wpConnectionString = wpConnectionString;
            _statusTextBlock = statusTextBlock;
            _logScrollViewer = logScrollViewer;
        }

        public async Task ExportAllMappingsAsync()
        {
            try
            {
                LogMessage("Iniciando exportación de mappings...");

                // Diálogo para seleccionar carpeta de destino
                var dialog = new SaveFileDialog
                {
                    Title = "Guardar reporte de migración",
                    Filter = "Archivos HTML (*.html)|*.html|Archivos CSV (*.csv)|*.csv",
                    FileName = $"MigracionReport_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (dialog.ShowDialog() != true)
                    return;

                var filePath = dialog.FileName;
                var isHtml = Path.GetExtension(filePath).ToLower() == ".html";

                if (isHtml)
                {
                    await ExportToHtmlAsync(filePath);
                }
                else
                {
                    await ExportToCsvAsync(filePath);
                }

                LogMessage($"Reporte exportado exitosamente: {filePath}");
                MessageBox.Show($"Reporte exportado exitosamente:\n{filePath}",
                               "Exportación Completada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogMessage($"Error exportando mappings: {ex.Message}");
                throw;
            }
        }

        private async Task ExportToHtmlAsync(string filePath)
        {
            var mappings = await GetAllMappingsAsync();

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset='utf-8'>");
            html.AppendLine("    <title>Reporte de Migración Drupal a WordPress</title>");
            html.AppendLine("    <style>");
            html.AppendLine("        body { font-family: Arial, sans-serif; margin: 20px; }");
            html.AppendLine("        h1 { color: #2c3e50; }");
            html.AppendLine("        h2 { color: #34495e; border-bottom: 2px solid #3498db; padding-bottom: 5px; }");
            html.AppendLine("        table { border-collapse: collapse; width: 100%; margin-bottom: 30px; }");
            html.AppendLine("        th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
            html.AppendLine("        th { background-color: #f2f2f2; font-weight: bold; }");
            html.AppendLine("        tr:nth-child(even) { background-color: #f9f9f9; }");
            html.AppendLine("        .summary { background-color: #e8f4f8; padding: 15px; border-radius: 5px; margin-bottom: 20px; }");
            html.AppendLine("        .date { color: #7f8c8d; font-style: italic; }");
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");

            html.AppendLine($"    <h1>Reporte de Migración Drupal a WordPress</h1>");
            html.AppendLine($"    <p class='date'>Generado el: {DateTime.Now:dd/MM/yyyy HH:mm:ss}</p>");

            // Resumen
            html.AppendLine("    <div class='summary'>");
            html.AppendLine("        <h3>Resumen de Migración</h3>");
            html.AppendLine($"        <p><strong>Usuarios migrados:</strong> {mappings.Users.Count}</p>");
            html.AppendLine($"        <p><strong>Categorías migradas:</strong> {mappings.Categories.Count}</p>");
            html.AppendLine($"        <p><strong>Tags migrados:</strong> {mappings.Tags.Count}</p>");
            html.AppendLine($"        <p><strong>Posts migrados:</strong> {mappings.Posts.Count}</p>");
            html.AppendLine($"        <p><strong>Imágenes migradas:</strong> {mappings.Media.Count}</p>");
            html.AppendLine("    </div>");

            // Usuarios
            if (mappings.Users.Any())
            {
                html.AppendLine("    <h2>Usuarios</h2>");
                html.AppendLine("    <table>");
                html.AppendLine("        <tr><th>ID Drupal</th><th>ID WordPress</th><th>Nombre Drupal</th><th>Usuario WordPress</th></tr>");
                foreach (var user in mappings.Users)
                {
                    html.AppendLine($"        <tr><td>{user.DrupalId}</td><td>{user.WpId}</td><td>{user.DrupalName}</td><td>{user.WpUsername}</td></tr>");
                }
                html.AppendLine("    </table>");
            }

            // Categorías
            if (mappings.Categories.Any())
            {
                html.AppendLine("    <h2>Categorías</h2>");
                html.AppendLine("    <table>");
                html.AppendLine("        <tr><th>ID Drupal</th><th>ID WordPress</th><th>Nombre Drupal</th><th>Nombre WordPress</th><th>Vocabulario</th></tr>");
                foreach (var category in mappings.Categories)
                {
                    html.AppendLine($"        <tr><td>{category.DrupalId}</td><td>{category.WpId}</td><td>{category.DrupalName}</td><td>{category.WpName}</td><td>{category.Vocabulary}</td></tr>");
                }
                html.AppendLine("    </table>");
            }

            // Tags
            if (mappings.Tags.Any())
            {
                html.AppendLine("    <h2>Tags</h2>");
                html.AppendLine("    <table>");
                html.AppendLine("        <tr><th>ID Drupal</th><th>ID WordPress</th><th>Nombre Drupal</th><th>Nombre WordPress</th></tr>");
                foreach (var tag in mappings.Tags)
                {
                    html.AppendLine($"        <tr><td>{tag.DrupalId}</td><td>{tag.WpId}</td><td>{tag.DrupalName}</td><td>{tag.WpName}</td></tr>");
                }
                html.AppendLine("    </table>");
            }

            // Posts
            if (mappings.Posts.Any())
            {
                html.AppendLine("    <h2>Posts</h2>");
                html.AppendLine("    <table>");
                html.AppendLine("        <tr><th>ID Drupal</th><th>ID WordPress</th><th>Fecha Migración</th></tr>");
                foreach (var post in mappings.Posts)
                {
                    html.AppendLine($"        <tr><td>{post.DrupalId}</td><td>{post.WpId}</td><td>{post.CreatedAt:dd/MM/yyyy HH:mm}</td></tr>");
                }
                html.AppendLine("    </table>");
            }

            // Imágenes
            if (mappings.Media.Any())
            {
                html.AppendLine("    <h2>Imágenes/Medios</h2>");
                html.AppendLine("    <table>");
                html.AppendLine("        <tr><th>FID Drupal</th><th>ID WordPress</th><th>Nombre Archivo</th><th>URL WordPress</th></tr>");
                foreach (var media in mappings.Media.Take(100)) // Limitar a 100 para no hacer el HTML muy largo
                {
                    html.AppendLine($"        <tr><td>{media.DrupalFid}</td><td>{media.WpId}</td><td>{media.DrupalFilename}</td><td><a href='{media.WpUrl}' target='_blank'>{media.WpUrl}</a></td></tr>");
                }
                if (mappings.Media.Count > 100)
                {
                    html.AppendLine($"        <tr><td colspan='4'><em>... y {mappings.Media.Count - 100} imágenes más</em></td></tr>");
                }
                html.AppendLine("    </table>");
            }

            html.AppendLine("</body>");
            html.AppendLine("</html>");

            await File.WriteAllTextAsync(filePath, html.ToString(), Encoding.UTF8);
        }

        private async Task ExportToCsvAsync(string filePath)
        {
            var mappings = await GetAllMappingsAsync();
            var csv = new StringBuilder();

            // Crear múltiples archivos CSV
            var baseFileName = Path.GetFileNameWithoutExtension(filePath);
            var directory = Path.GetDirectoryName(filePath);

            // Usuarios
            if (mappings.Users.Any())
            {
                var usersCsv = new StringBuilder();
                usersCsv.AppendLine("DrupalId,WordPressId,DrupalName,WordPressUsername");
                foreach (var user in mappings.Users)
                {
                    usersCsv.AppendLine($"{user.DrupalId},{user.WpId},\"{user.DrupalName}\",\"{user.WpUsername}\"");
                }
                await File.WriteAllTextAsync(Path.Combine(directory, $"{baseFileName}_usuarios.csv"), usersCsv.ToString(), Encoding.UTF8);
            }

            // Categorías
            if (mappings.Categories.Any())
            {
                var categoriesCsv = new StringBuilder();
                categoriesCsv.AppendLine("DrupalId,WordPressId,DrupalName,WordPressName,Vocabulary");
                foreach (var category in mappings.Categories)
                {
                    categoriesCsv.AppendLine($"{category.DrupalId},{category.WpId},\"{category.DrupalName}\",\"{category.WpName}\",\"{category.Vocabulary}\"");
                }
                await File.WriteAllTextAsync(Path.Combine(directory, $"{baseFileName}_categorias.csv"), categoriesCsv.ToString(), Encoding.UTF8);
            }

            // Tags
            if (mappings.Tags.Any())
            {
                var tagsCsv = new StringBuilder();
                tagsCsv.AppendLine("DrupalId,WordPressId,DrupalName,WordPressName");
                foreach (var tag in mappings.Tags)
                {
                    tagsCsv.AppendLine($"{tag.DrupalId},{tag.WpId},\"{tag.DrupalName}\",\"{tag.WpName}\"");
                }
                await File.WriteAllTextAsync(Path.Combine(directory, $"{baseFileName}_tags.csv"), tagsCsv.ToString(), Encoding.UTF8);
            }

            // Posts
            if (mappings.Posts.Any())
            {
                var postsCsv = new StringBuilder();
                postsCsv.AppendLine("DrupalId,WordPressId,FechaMigracion");
                foreach (var post in mappings.Posts)
                {
                    postsCsv.AppendLine($"{post.DrupalId},{post.WpId},{post.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                }
                await File.WriteAllTextAsync(Path.Combine(directory, $"{baseFileName}_posts.csv"), postsCsv.ToString(), Encoding.UTF8);
            }

            // Imágenes
            if (mappings.Media.Any())
            {
                var mediaCsv = new StringBuilder();
                mediaCsv.AppendLine("DrupalFid,WordPressId,NombreArchivo,URLWordPress");
                foreach (var media in mappings.Media)
                {
                    mediaCsv.AppendLine($"{media.DrupalFid},{media.WpId},\"{media.DrupalFilename}\",\"{media.WpUrl}\"");
                }
                await File.WriteAllTextAsync(Path.Combine(directory, $"{baseFileName}_imagenes.csv"), mediaCsv.ToString(), Encoding.UTF8);
            }
        }

        private async Task<MappingData> GetAllMappingsAsync()
        {
            var mappings = new MappingData();

            using var connection = new MySqlConnection(_wpConnectionString);
            await connection.OpenAsync();

            // Usuarios
            mappings.Users = (await connection.QueryAsync<UserMappingData>(
                "SELECT drupal_user_id as DrupalId, wp_user_id as WpId, drupal_name as DrupalName, wp_username as WpUsername FROM user_mapping ORDER BY drupal_user_id")).ToList();

            // Categorías
            mappings.Categories = (await connection.QueryAsync<CategoryMappingData>(
                "SELECT drupal_category_id as DrupalId, wp_category_id as WpId, drupal_name as DrupalName, wp_name as WpName, vocabulary as Vocabulary FROM category_mapping ORDER BY drupal_category_id")).ToList();

            // Tags
            mappings.Tags = (await connection.QueryAsync<TagMappingData>(
                "SELECT drupal_tag_id as DrupalId, wp_tag_id as WpId, drupal_name as DrupalName, wp_name as WpName FROM tag_mapping ORDER BY drupal_tag_id")).ToList();

            // Posts
            mappings.Posts = (await connection.QueryAsync<PostMappingData>(
                "SELECT drupal_post_id as DrupalId, wp_post_id as WpId, migrated_at as CreatedAt FROM post_mapping ORDER BY migrated_at DESC")).ToList();

            // Imágenes
            mappings.Media = (await connection.QueryAsync<MediaMappingData>(
                "SELECT drupal_file_id as DrupalFid, wp_media_id as WpId, drupal_filename as DrupalFilename, wp_url as WpUrl FROM media_mapping ORDER BY drupal_file_id")).ToList();

            return mappings;
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

    // Clases para los datos de mapping
    public class MappingData
    {
        public List<UserMappingData> Users { get; set; } = new();
        public List<CategoryMappingData> Categories { get; set; } = new();
        public List<TagMappingData> Tags { get; set; } = new();
        public List<PostMappingData> Posts { get; set; } = new();
        public List<MediaMappingData> Media { get; set; } = new();
    }

    public class UserMappingData
    {
        public int? DrupalId { get; set; }
        public int WpId { get; set; }
        public string DrupalName { get; set; }
        public string WpUsername { get; set; }
    }

    public class CategoryMappingData
    {
        public int? DrupalId { get; set; }
        public int WpId { get; set; }
        public string DrupalName { get; set; }
        public string WpName { get; set; }
        public string Vocabulary { get; set; }
    }

    public class TagMappingData
    {
        public int? DrupalId { get; set; }
        public int WpId { get; set; }
        public string DrupalName { get; set; }
        public string WpName { get; set; }
    }

    public class PostMappingData
    {
        public int DrupalId { get; set; }
        public int WpId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class MediaMappingData
    {
        public int DrupalFid { get; set; }
        public int WpId { get; set; }
        public string DrupalFilename { get; set; }
        public string WpUrl { get; set; }
    }
}