using Dapper;
using drupaltowp.Clases.Imagenes;
using drupaltowp.Clases.Imagenes.Panopoly;
using drupaltowp.Clases.Publicaciones.Panopoly;
using drupaltowp.Models;
using drupaltowp.ViewModels;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WordPressPCL;
using WordPressPCL.Models;

namespace drupaltowp;

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
    PanopolyMigrator _panopolyMigrator;
    public LoggerViewModel LoggerViewModel { get; private set; }
    public ViewModelLocator ViewModelLocator { get; private set; }

    public MainWindow()
    {
        // Crear servicios
        LoggerViewModel = new LoggerViewModel();
        var coordinatorService = new Services.MigrationCoordinatorService(LoggerViewModel);
        ViewModelLocator = new ViewModelLocator(coordinatorService, LoggerViewModel);

        InitializeComponent();
        this.DataContext = this;

        // Mensaje inicial
        LoggerViewModel.LogInfo("🚀 Sistema de migración iniciado");
        LoggerViewModel.LogSuccess("✅ Servicios configurados correctamente");


    }






}