using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Dapper;
using drupaltowp.Models;
using WordPressPCL;
using WordPressPCL.Models;
using drupaltowp.ViewModels;
using drupaltowp.Clases.Imagenes;

namespace drupaltowp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Configuración

        private readonly string Urlsitio = "http://localhost/comunicarsewp/wp-json/";
        private readonly string UrlsitioDrupal = "http://localhost/comunicarseweb";
        private readonly string Usuario = "gonzalo";
        private readonly string Password = "suwr haUK hkOu MqTL MnHk NTTz";
        private readonly string DrupalconnectionString = "Server=localhost;Database=comunicarse_drupal;User ID=root;Password=root;Port=3306";
        private readonly string WPconnectionString = "Server=localhost;Database=comunicarse_wp;User ID=root;Password=root;Port=3306";

        #endregion

        #region ViewModel
        public LoggerViewModel _loggerViewModel { get; private set; }
        #endregion
        BibliotecaMigratorWPF _bibliotecaMigratorWPF;
        public MainWindow()
        {
            _loggerViewModel = new();
            InitializeComponent();
            this.DataContext = _loggerViewModel;
            
        }

        
        #region Métodos de Verificación

        private async void CheckPrerequisitesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CheckPrerequisitesButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario, Password);

                var migrator = new PostMigratorWPF(DrupalconnectionString, WPconnectionString, wpClient, StatusTextBlock, LogScrollViewer);

                var prereqsOk = await migrator.CheckPrerequisitesAsync();

                if (prereqsOk)
                {
                    MessageBox.Show("✅ Todos los prerrequisitos están OK.\n\nPuedes proceder con la migración.",
                                   "Prerrequisitos OK", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("⚠️ Algunos prerrequisitos no se cumplen.\n\nRevisa el log para más detalles.",
                                   "Prerrequisitos Faltantes", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error verificando prerrequisitos: {ex.Message}", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                CheckPrerequisitesButton.IsEnabled = true;
            }
        }

        private async void ShowStatusButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowStatusButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario, Password);

                var migrator = new PostMigratorWPF(DrupalconnectionString, WPconnectionString, wpClient, StatusTextBlock, LogScrollViewer);

                await migrator.ShowMigrationStatusAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error mostrando estado: {ex.Message}", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ShowStatusButton.IsEnabled = true;
            }
        }

        private async void AnalyzeDatabaseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AnalyzeDatabaseButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var analyzer = new DrupalAnalyzer(DrupalconnectionString, StatusTextBlock, LogScrollViewer);
                await analyzer.AnalyzeDatabaseStructureAsync();

                MessageBox.Show("Análisis de estructura completado. Revisa el log para ver todos los detalles.",
                               "Análisis Completado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en análisis: {ex.Message}", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AnalyzeDatabaseButton.IsEnabled = true;
            }
        }

        #endregion

        #region Métodos de Análisis por Tipo de Contenido

        private async void AnalyzeAllTypesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AnalyzeAllTypesButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var analyzer = new DrupalAnalyzer(DrupalconnectionString, StatusTextBlock, LogScrollViewer);
                await analyzer.AnalyzeAllContentTypesAsync();

                MessageBox.Show("Análisis de todos los tipos completado.",
                               "Análisis Completado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AnalyzeAllTypesButton.IsEnabled = true;
            }
        }

        private async void AnalyzeBibliotecaButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AnalyzeBibliotecaButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var analyzer = new DrupalAnalyzer(DrupalconnectionString, StatusTextBlock, LogScrollViewer);
                await analyzer.AnalyzeBibliotecaAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AnalyzeBibliotecaButton.IsEnabled = true;
            }
        }

        private async void AnalyzePanopolyPageButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AnalyzePanopolyPageButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var analyzer = new DrupalAnalyzer(DrupalconnectionString, StatusTextBlock, LogScrollViewer);
                await analyzer.AnalyzePanopolyPageAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AnalyzePanopolyPageButton.IsEnabled = true;
            }
        }

        private async void AnalyzeAgendaButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AnalyzeAgendaButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var analyzer = new DrupalAnalyzer(DrupalconnectionString, StatusTextBlock, LogScrollViewer);
                await analyzer.AnalyzeAgendaAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AnalyzeAgendaButton.IsEnabled = true;
            }
        }

        private async void AnalyzeNewsletterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AnalyzeNewsletterButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var analyzer = new DrupalAnalyzer(DrupalconnectionString, StatusTextBlock, LogScrollViewer);
                await analyzer.AnalyzeNewsletterAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AnalyzeNewsletterButton.IsEnabled = true;
            }
        }

        private async void AnalyzeVideosButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AnalyzeVideosButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var analyzer = new DrupalAnalyzer(DrupalconnectionString, StatusTextBlock, LogScrollViewer);
                await analyzer.AnalyzeVideosAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AnalyzeVideosButton.IsEnabled = true;
            }
        }

        private async void AnalyzeOpinionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AnalyzeOpinionButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var analyzer = new DrupalAnalyzer(DrupalconnectionString, StatusTextBlock, LogScrollViewer);
                await analyzer.AnalyzeOpinionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AnalyzeOpinionButton.IsEnabled = true;
            }
        }

        private async void AnalyzeWebformButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AnalyzeWebformButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var analyzer = new DrupalAnalyzer(DrupalconnectionString, StatusTextBlock, LogScrollViewer);
                await analyzer.AnalyzeWebformAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AnalyzeWebformButton.IsEnabled = true;
            }
        }

        #endregion

        #region Métodos de Migración de Usuarios y Taxonomía

        private async void MigrateUsersButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MigrateUsersButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario, Password);

                var migrator = new UserMigratorWPF(DrupalconnectionString, WPconnectionString, wpClient, StatusTextBlock, LogScrollViewer);
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
                MigrateUsersButton.IsEnabled = true;
            }
        }

        private async void MigrateCategoriesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MigrateCategoriesButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario, Password);

                var migrator = new CategoryMigratorWPF(DrupalconnectionString, WPconnectionString, wpClient, StatusTextBlock, LogScrollViewer);

                var categoryMapping1 = await migrator.MigrateCategoriesAsync("panopoly_categories");
                MessageBox.Show($"Migración completada!\n{categoryMapping1.Count} categorías (panopoly) migradas.",
                               "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

                var categoryMapping2 = await migrator.MigrateCategoriesAsync("bibliteca_categorias");
                MessageBox.Show($"Migración completada!\n{categoryMapping2.Count} categorías (biblioteca) migradas.",
                               "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en migración: {ex.Message}", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                MigrateCategoriesButton.IsEnabled = true;
            }
        }

        private async void MigrateTagsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MigrateTagsButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario, Password);

                var migrator = new TagMigratorWPF(DrupalconnectionString, WPconnectionString, wpClient, StatusTextBlock, LogScrollViewer);
                var tagMapping = await migrator.MigrateTagsAsync("tags");

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
                MigrateTagsButton.IsEnabled = true;
            }
        }

        #endregion

        #region Métodos de Migración de Contenido

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BotonPaginas.IsEnabled = false;
                StatusTextBlock.Text = "";

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

                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario, Password);

                int count = 0;
                foreach (var pagina in paginas)
                {
                    var wpPage = new WordPressPCL.Models.Page
                    {
                        Title = new Title(pagina.title),
                        Content = new Content(pagina.contenido),
                        Excerpt = new Excerpt(pagina.bajada),
                        Status = Status.Publish
                    };

                    await wpClient.Pages.CreateAsync(wpPage);
                    count++;
                }

                MessageBox.Show($"{count} páginas migradas a WordPress.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BotonPaginas.IsEnabled = true;
            }
        }

        private async void AnalyzePostsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AnalyzePostsButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var analyzer = new DrupalAnalyzer(DrupalconnectionString, StatusTextBlock, LogScrollViewer);
                await analyzer.AnalyzeDrupalPostsAsync();

                var analysisResult = await analyzer.GetPostAnalysisAsync();

                var summary = $"RESUMEN DEL ANÁLISIS:\n\n" +
                             $"📊 Total de posts: {analysisResult.TotalPosts}\n" +
                             $"🖼️ Posts con imagen destacada: {analysisResult.PostsWithFeaturedImage}\n" +
                             $"📝 Posts con bajada: {analysisResult.PostsWithBajada}\n\n" +
                             $"Distribución por tipo:\n";

                foreach (var typeCount in analysisResult.PostCountsByType)
                {
                    summary += $"• {typeCount.Key}: {typeCount.Value} posts\n";
                }

                summary += "\n¿Proceder con la migración?";

                var result = MessageBox.Show(summary, "Análisis Completado",
                                            MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var wpClient = new WordPressClient(Urlsitio);
                    wpClient.Auth.UseBasicAuth(Usuario, Password);
                    var migrator = new PostMigratorWPF(DrupalconnectionString, WPconnectionString, wpClient, StatusTextBlock, LogScrollViewer);
                    await ExecutePostMigrationAsync(migrator);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en análisis: {ex.Message}\n\nDetalles: {ex.InnerException?.Message}",
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AnalyzePostsButton.IsEnabled = true;
            }
        }

        private async void MigratePostsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MigratePostsButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario, Password);

                var migrator = new PostMigratorWPF(DrupalconnectionString, WPconnectionString, wpClient, StatusTextBlock, LogScrollViewer);
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
                MigratePostsButton.IsEnabled = true;
            }
        }

        private async Task ExecutePostMigrationAsync(PostMigratorWPF migrator)
        {
            var postMapping = await migrator.MigratePostsAsync();
            MessageBox.Show($"Migración completada!\n{postMapping.Count} posts migrados.",
                           "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void RollbackPostsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RollbackPostsButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario, Password);

                var migrator = new PostMigratorWPF(DrupalconnectionString, WPconnectionString, wpClient, StatusTextBlock, LogScrollViewer);
                await migrator.RollbackPostMigrationAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en rollback: {ex.Message}\n\nDetalles: {ex.InnerException?.Message}",
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RollbackPostsButton.IsEnabled = true;
            }
        }

        #endregion

        #region Métodos de Migración de Imágenes

        private async void MigrateImagesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MigrateImagesButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario, Password);

                var migrator = new ImageMigratorWPF(
                    DrupalconnectionString,
                    WPconnectionString,
                    wpClient,
                    StatusTextBlock,
                    LogScrollViewer,
                    UrlsitioDrupal
                );

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
                MigrateImagesButton.IsEnabled = true;
            }
        }

        private async void CleanupImagesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CleanupImagesButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario, Password);

                var cleanupTool = new ImageCleanupTool(WPconnectionString, wpClient, StatusTextBlock, LogScrollViewer);

                await cleanupTool.ShowMigrationStatusAsync();
                await cleanupTool.CleanupMigratedImagesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en limpieza: {ex.Message}\n\nDetalles: {ex.InnerException?.Message}",
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                CleanupImagesButton.IsEnabled = true;
            }
        }

        #endregion

        #region Métodos de Herramientas

        private async void ValidateMigrationButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ValidateMigrationButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario, Password);

                var migrator = new PostMigratorWPF(DrupalconnectionString, WPconnectionString, wpClient, StatusTextBlock, LogScrollViewer);
                await migrator.ValidateMigrationAsync();

                MessageBox.Show("Validación completada. Revisa el log para los resultados.",
                               "Validación Completada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en validación: {ex.Message}", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ValidateMigrationButton.IsEnabled = true;
            }
        }

        private async void ExportMappingButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExportMappingButton.IsEnabled = false;
                StatusTextBlock.Text = "";

                var exporter = new MappingExporter(WPconnectionString, StatusTextBlock, LogScrollViewer);
                await exporter.ExportAllMappingsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exportando mappings: {ex.Message}\n\nDetalles: {ex.InnerException?.Message}",
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ExportMappingButton.IsEnabled = true;
            }
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            _loggerViewModel.Clear();
        }

        #endregion

        // Agregar estos métodos a la clase MainWindow en MainWindow.xaml.cs

        private async void AnalyzeBibliotecaDetailedButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                if (button != null) button.IsEnabled = false;

                StatusTextBlock.Text = "";

                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario, Password);

                var migrator = new BibliotecaMigratorWPF(
                    DrupalconnectionString,
                    WPconnectionString,
                    wpClient,
                    StatusTextBlock,
                    LogScrollViewer);

                //var analysisResult = await migrator.GetBibliotecaAnalysisAsync();
                /*
                var summary = $"ANÁLISIS DETALLADO DE BIBLIOTECA:\n\n" +
                             $"📊 Total publicaciones: {analysisResult.TotalBiblioteca}\n" +
                             $"🖼️ Con imagen destacada: {analysisResult.WithFeaturedImage}\n" +
                             $"📝 Con bajada: {analysisResult.WithBajada}\n" +
                             $"📄 Con descripción: {analysisResult.WithDescripcion}\n" +
                             $"👤 Con autor: {analysisResult.WithAutor}\n" +
                             $"🎥 Con video: {analysisResult.WithVideoUrl}\n" +
                             $"📎 Con archivo: {analysisResult.WithArchivo}\n" +
                             $"📂 Con categorías: {analysisResult.WithCategories}\n\n" +
                             $"¿Proceder con la migración?";

                var result = MessageBox.Show(summary, "Análisis de Biblioteca Completado",
                                            MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await ExecuteBibliotecaMigrationAsync(migrator);
                }*/
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en análisis: {ex.Message}\n\nDetalles: {ex.InnerException?.Message}",
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                var button = sender as Button;
                if (button != null) button.IsEnabled = true;
            }
        }

        private async void MigrateBibliotecaButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                if (button != null) button.IsEnabled = false;

                StatusTextBlock.Text = "";

                var wpClient = new WordPressClient(Urlsitio);
                wpClient.Auth.UseBasicAuth(Usuario, Password);

                _bibliotecaMigratorWPF = new BibliotecaMigratorWPF(
                    DrupalconnectionString,
                    WPconnectionString,
                    wpClient,
                    StatusTextBlock,
                    LogScrollViewer);

                await ExecuteBibliotecaMigrationAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en migración de biblioteca: {ex.Message}\n\nDetalles: {ex.InnerException?.Message}",
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                var button = sender as Button;
                if (button != null) button.IsEnabled = true;
            }
        }

        private async Task ExecuteBibliotecaMigrationAsync()
        {
            var bibliotecaMapping = await _bibliotecaMigratorWPF.MigrateBibliotecaAsync();
            MessageBox.Show($"Migración de biblioteca completada!\n{bibliotecaMapping.Count} publicaciones migradas.",
                           "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CancelMigrateBibliotecaButton_Click(object sender, RoutedEventArgs e)
        {
            _bibliotecaMigratorWPF.Cancelar = true;
        }

        private async void SmartMigrateImagesButton_Click(object sender, RoutedEventArgs e)
        {
            _loggerViewModel.LogMessage("Empieza el proceso inteligente");
            var smartImageMigrator = new SmartImageMigrator(_loggerViewModel);
            await smartImageMigrator.SmartMigrateImagesAsync();

        }


    }
}