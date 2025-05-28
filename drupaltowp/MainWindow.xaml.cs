using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Dapper;
using drupaltowp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using WordPressPCL;
using WordPressPCL.Models;
using System.Security.Policy;

namespace drupaltowp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private readonly string Urlsitio = "http://localhost/comuwp/wp-json/";
        private readonly string UrlsitioDrupal = "http://localhost/comunicarseweb";
        private readonly string Usuario = "admin";
        private readonly string Password = "nblx pVoP rFeT Xz4b H1XC kEtV";
        private readonly string DrupalconnectionString = "Server=localhost;Database=comunicarseweb;User ID=root;Password=;Port=3306";
        private readonly string WPconnectionString = "Server=localhost;Database=comuwp;User ID=root;Password=;Port=3306";
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            BotonPaginas.IsEnabled = false;
            // 1. Descargar las páginas desde MySQL
            var conexion = new DatabaseConnection();
            await conexion.ConnectAsync();

            IEnumerable<Pagina> paginas;
            using (var conn = conexion.Connection)
            {
                paginas = await conn.QueryAsync<Pagina>(
                    @"SELECT 
                        n.nid, 
                        n.title, 
                        b.body_value AS contenido,
                        bj.field_bajada_value AS bajada
                      FROM 
                        node n
                      JOIN 
                        field_data_body b ON n.nid = b.entity_id
                      LEFT JOIN
                        field_data_field_bajada bj ON n.nid = bj.entity_id
                      WHERE 
                        n.type = 'page';"
                );
            }
            await conexion.DisconnectAsync();

            // 2. Conectar a WordPress
            var wpClient = new WordPressClient("http://localhost/comuwp/wp-json/");
            wpClient.Auth.UseBasicAuth("admin", "nblx pVoP rFeT Xz4b H1XC kEtV");
            //nblx pVoP rFeT Xz4b H1XC kEtV
            //await wpClient.Auth.RequestJWTokenAsync("admin", "nblx pVoP rFeT Xz4b H1XC kEtV");
            

            // 3. Guardar cada página en WordPress
            int count = 0;
            foreach (var pagina in paginas)
            {
                var wpPage = new WordPressPCL.Models.Page
                {
                    Title = new Title(pagina.title),
                    Content = new Content(pagina.contenido),
                    Excerpt = new Excerpt(pagina.bajada),
                    Status = Status.Publish // O Draft si prefieres revisar antes de publicar
                   
                };

                await wpClient.Pages.CreateAsync(wpPage);
                count++;
            }

            MessageBox.Show($"{count} páginas migradas a WordPress.");
            BotonPaginas.IsEnabled = true;
        }

        private async void MigrateCategoriesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Deshabilitar botón mientras migra
                MigrateCategoriesButton.IsEnabled = false;
                StatusTextBlock.Text = ""; // Limpiar status

                // Configurar cliente WordPress
                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario,Password);

            

                // Crear migrador
                var migrator = new CategoryMigratorWPF(DrupalconnectionString, WPconnectionString, wpClient, StatusTextBlock, LogScrollViewer);

                // Ejecutar migración
                var categoryMapping = await migrator.MigrateCategoriesAsync("panopoly_categories");

                MessageBox.Show($"Migración completada!\n{categoryMapping.Count} categorías migradas.",
                               "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                
                categoryMapping = await migrator.MigrateCategoriesAsync("bibliteca_categorias");

                MessageBox.Show($"Migración completada!\n{categoryMapping.Count} categorías migradas.",
                               "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en migración: {ex.Message}", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Reactivar botón
                MigrateCategoriesButton.IsEnabled = true;
            }
        }

        // Placeholders para los otros botones
        // Reemplaza el método MigratePostsButton_Click en MainWindow.xaml.cs
        private async void MigratePostsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Deshabilitar botón mientras migra
                MigratePostsButton.IsEnabled = false;
                StatusTextBlock.Text = ""; // Limpiar status

                // Configurar cliente WordPress
                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario, Password);

                // Crear migrador
                var migrator = new PostMigratorWPF(DrupalconnectionString, WPconnectionString, wpClient, StatusTextBlock, LogScrollViewer);

                // Ejecutar migración
                var postMapping = await migrator.MigratePostsAsync();

                MessageBox.Show($"Migración completada!\n{postMapping.Count} posts migrados.",
                               "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en migración: {ex.Message}\n\nDetalles: {ex.InnerException?.Message}",
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Reactivar botón
                MigratePostsButton.IsEnabled = true;
            }
        }

        private async void MigrateUsersButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Deshabilitar botón mientras migra
                MigrateUsersButton.IsEnabled = false;
                StatusTextBlock.Text = ""; // Limpiar status

                // Configurar cliente WordPress
                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario, Password);

                // Crear migrador
                var migrator = new UserMigratorWPF(DrupalconnectionString, WPconnectionString, wpClient, StatusTextBlock, LogScrollViewer);

                // Ejecutar migración
                var userMapping = await migrator.MigrateUsersAsync();

                MessageBox.Show($"Migración completada!\n{userMapping.Count} usuarios procesados.",
                               "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en migración: {ex.Message}", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Reactivar botón
                MigrateUsersButton.IsEnabled = true;
            }
        }
        // Reemplaza el método MigrateImagesButton_Click en MainWindow.xaml.cs
        private async void MigrateImagesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Deshabilitar botón mientras migra
                MigrateImagesButton.IsEnabled = false;
                StatusTextBlock.Text = ""; // Limpiar status

                // Configurar cliente WordPress
                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario, Password);

                // Crear migrador (ajusta la URL de tu sitio Drupal si es diferente)
                var migrator = new ImageMigratorWPF(
                    DrupalconnectionString,
                    WPconnectionString,
                    wpClient,
                    StatusTextBlock,
                    LogScrollViewer,
                    UrlsitioDrupal // Cambia por la URL de tu sitio Drupal
                );

                // Ejecutar migración
                var mediaMapping = await migrator.MigrateImagesAsync();

                MessageBox.Show($"Migración de imágenes completada!\n{mediaMapping.Count} imágenes procesadas.",
                               "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en migración de imágenes: {ex.Message}\n\nDetalles: {ex.InnerException?.Message}",
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Reactivar botón
                MigrateImagesButton.IsEnabled = true;
            }
        }

        // Reemplaza el método ExportMappingButton_Click en MainWindow.xaml.cs
        private async void ExportMappingButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Deshabilitar botón mientras exporta
                ExportMappingButton.IsEnabled = false;
                StatusTextBlock.Text = ""; // Limpiar status

                // Crear exportador
                var exporter = new MappingExporter(WPconnectionString, StatusTextBlock, LogScrollViewer);

                // Ejecutar exportación
                await exporter.ExportAllMappingsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exportando mappings: {ex.Message}\n\nDetalles: {ex.InnerException?.Message}",
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Reactivar botón
                ExportMappingButton.IsEnabled = true;
            }
        }
        // Event handler para limpiar el log
        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "";
        }

        private async void MigrateTagsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Deshabilitar botón mientras migra
                MigrateTagsButton.IsEnabled = false;
                StatusTextBlock.Text = ""; // Limpiar status

                // Configurar cliente WordPress
                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario, Password);

                // Crear migrador
                var migrator = new TagMigratorWPF(DrupalconnectionString, WPconnectionString, wpClient, StatusTextBlock, LogScrollViewer);

                // Ejecutar migración (usar el nombre de tu vocabulario de tags en Drupal)
                var tagMapping = await migrator.MigrateTagsAsync("tags"); // Cambia "tags" por el nombre real de tu vocabulario

                MessageBox.Show($"Migración completada!\n{tagMapping.Count} tags migrados.",
                               "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en migración: {ex.Message}", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Reactivar botón
                MigrateTagsButton.IsEnabled = true;
            }
        }
    }
}