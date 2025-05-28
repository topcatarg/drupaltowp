using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Dapper;
using WordPressPCL;
using WordPressPCL.Models;
using MySql.Data.MySqlClient;
using drupaltowp.Models;

namespace drupaltowp
{
    public class UserMigratorWPF
    {
        private readonly string _drupalConnectionString;
        private readonly string _wpConnectionString;
        private readonly WordPressClient _wpClient;
        private readonly TextBlock _statusTextBlock;
        private readonly ScrollViewer _logScrollViewer;

        public UserMigratorWPF(string drupalConnectionString, string wpConnectionString,
                              WordPressClient wpClient, TextBlock statusTextBlock,
                              ScrollViewer logScrollViewer)
        {
            _drupalConnectionString = drupalConnectionString;
            _wpConnectionString = wpConnectionString;
            _wpClient = wpClient;
            _statusTextBlock = statusTextBlock;
            _logScrollViewer = logScrollViewer;
        }

        public async Task<Dictionary<int, int>> MigrateUsersAsync()
        {
            var userMapping = new Dictionary<int, int>();

            try
            {
                LogStatus("Iniciando migración de usuarios...");

                // 1. Obtener usuarios de Drupal
                var drupalUsers = await GetDrupalUsersAsync();
                LogStatus($"Encontrados {drupalUsers.Count()} usuarios en Drupal");

                // 2. Obtener usuarios existentes en WordPress
                var existingWpUsers = await GetExistingWordPressUsersAsync();
                LogStatus($"Encontrados {existingWpUsers.Count} usuarios existentes en WordPress");

                int migratedCount = 0;
                int skippedCount = 0;

                // 3. Migrar cada usuario
                foreach (var drupalUser in drupalUsers)
                {
                    try
                    {
                        // Verificar si el usuario ya existe por email o username
                        var existingUser = existingWpUsers.FirstOrDefault(u =>
                            u.Email?.Equals(drupalUser.Mail, StringComparison.OrdinalIgnoreCase) == true ||
                            u.UserName?.Equals(drupalUser.Name, StringComparison.OrdinalIgnoreCase) == true);

                        if (existingUser != null)
                        {
                            LogStatus($"Usuario '{drupalUser.Name}' ya existe en WordPress (ID: {existingUser.Id})");
                            userMapping[drupalUser.Uid] = existingUser.Id;
                            skippedCount++;
                            continue;
                        }

                        // Crear nuevo usuario en WordPress
                        var wpUser = new User
                        {
                            UserName = SanitizeUsername(drupalUser.Name),
                            Email = drupalUser.Mail,
                            FirstName = drupalUser.Name, // O puedes usar campos adicionales si los tienes
                            Password = GenerateRandomPassword(), // WordPress requiere una contraseña
                            Roles = DetermineUserRoles(drupalUser) // Mapear roles de Drupal a WordPress
                        };

                        var createdUser = await _wpClient.Users.CreateAsync(wpUser);
                        userMapping[drupalUser.Uid] = createdUser.Id;

                        LogStatus($"Usuario migrado: '{drupalUser.Name}' -> WordPress ID: {createdUser.Id}");
                        migratedCount++;

                        // Guardar mapeo en base de datos local
                        await SaveUserMappingAsync(drupalUser.Uid, createdUser.Id);
                    }
                    catch (Exception ex)
                    {
                        LogStatus($"Error migrando usuario '{drupalUser.Name}': {ex.Message}");
                    }

                    // Pequeña pausa para no sobrecargar la API
                    await Task.Delay(100);
                }

                LogStatus($"Migración completada: {migratedCount} usuarios migrados, {skippedCount} omitidos");
                return userMapping;
            }
            catch (Exception ex)
            {
                LogStatus($"Error general en migración de usuarios: {ex.Message}");
                throw;
            }
        }

        private async Task<IEnumerable<DrupalUser>> GetDrupalUsersAsync()
        {
            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT 
                    u.uid,
                    u.name,
                    u.mail,
                    u.created,
                    u.status,
                    GROUP_CONCAT(r.name) as roles
                FROM users u
                LEFT JOIN users_roles ur ON u.uid = ur.uid
                LEFT JOIN role r ON ur.rid = r.rid
                WHERE u.uid > 0  -- Excluir usuario anónimo
                GROUP BY u.uid, u.name, u.mail, u.created, u.status
                ORDER BY u.uid";

            return await connection.QueryAsync<DrupalUser>(query);
        }

        private async Task<List<User>> GetExistingWordPressUsersAsync()
        {
            try
            {
                var users = await _wpClient.Users.GetAllAsync();
                return users.ToList();
            }
            catch (Exception ex)
            {
                LogStatus($"Error obteniendo usuarios de WordPress: {ex.Message}");
                return new List<User>();
            }
        }

        private string SanitizeUsername(string username)
        {
            if (string.IsNullOrEmpty(username))
                return "user_" + System.Guid.NewGuid().ToString("N")[..8];

            // WordPress usernames pueden contener solo letras, números, espacios, ., -, @
            var sanitized = System.Text.RegularExpressions.Regex.Replace(username, @"[^a-zA-Z0-9\s.\-@_]", "");

            return string.IsNullOrEmpty(sanitized) ? "user_" + System.Guid.NewGuid().ToString("N")[..8] : sanitized;
        }

        private string GenerateRandomPassword()
        {
            // Generar una contraseña aleatoria de 12 caracteres
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 12)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private string[] DetermineUserRoles(DrupalUser drupalUser)
        {
            // Mapear roles de Drupal a WordPress
            // Ajusta según tus roles específicos
            if (string.IsNullOrEmpty(drupalUser.Roles))
                return new[] { "subscriber" }; // Rol por defecto

            var roles = drupalUser.Roles.ToLower();

            if (roles.Contains("administrator"))
                return new[] { "administrator" };

            if (roles.Contains("editor"))
                return new[] { "editor" };

            if (roles.Contains("author"))
                return new[] { "author" };

            if (roles.Contains("contributor"))
                return new[] { "contributor" };

            return new[] { "subscriber" }; // Rol por defecto
        }

        private async Task SaveUserMappingAsync(int drupalUserId, int wpUserId)
        {
            try
            {
                using var connection = new MySqlConnection(_wpConnectionString);
                await connection.OpenAsync();

                var query = @"
                    INSERT INTO user_mapping (drupal_user_id, wp_user_id, migrated_at)
                    VALUES (@DrupalUserId, @WpUserId, @MigratedAt)
                    ON DUPLICATE KEY UPDATE 
                        wp_user_id = @WpUserId,
                        migrated_at = @MigratedAt";

                await connection.ExecuteAsync(query, new
                {
                    DrupalUserId = drupalUserId,
                    WpUserId = wpUserId,
                    MigratedAt = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                LogStatus($"Error guardando mapeo de usuario: {ex.Message}");
            }
        }

        private void LogStatus(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _statusTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - {message}\n";
                _logScrollViewer.ScrollToEnd();
            });
        }
    }
}