using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using drupaltowp.Business;

namespace drupaltowp.ViewModels;

public class PanopolyPhaseViewModel : INotifyPropertyChanged
{
    private readonly PanopolyMigrationBusiness _panopolyBusiness;

    public PanopolyPhaseViewModel(PanopolyMigrationBusiness panopolyBusiness)
    {
        _panopolyBusiness = panopolyBusiness;

        // Inicializar commands
        AnalyzePanopolyCommand = new RelayCommand(async () => await _panopolyBusiness.AnalyzePanopolyStructureAsync());
        MigratePanopolyPagesCommand = new RelayCommand(async () => await _panopolyBusiness.MigratePanopolyPagesAsync());
        MigrateImagesCommand = new RelayCommand(async () => await _panopolyBusiness.MigrateImagesAsync());
        ProcessContentImagesCommand = new RelayCommand(async () => await _panopolyBusiness.ProcessContentImagesAsync());
        ShowPanopolyStatusCommand = new RelayCommand(async () => await _panopolyBusiness.ShowPanopolyStatusAsync());
        ValidatePanopolyMigrationCommand = new RelayCommand(async () => await _panopolyBusiness.ValidatePanopolyMigrationAsync());
        AnalyzeImageContentCommand = new RelayCommand(async () => await _panopolyBusiness.AnalyzeImageContentAsync());
        MigrateArchiveCommand = new RelayCommand(async () => await _panopolyBusiness.MigrateArchiveAsync());
    }

    #region Properties
    public string PhaseTitle => "📄 FASE 3: MIGRACIÓN PANOPOLY";
    public string PhaseColor => "#E67E22";
    #endregion

    #region Commands
    public ICommand AnalyzePanopolyCommand { get; }
    public ICommand MigratePanopolyPagesCommand { get; }
    public ICommand MigrateImagesCommand { get; }
    public ICommand ProcessContentImagesCommand { get; }
    public ICommand ShowPanopolyStatusCommand { get; }
    public ICommand ValidatePanopolyMigrationCommand { get; }
    public ICommand AnalyzeImageContentCommand { get; }

    public ICommand MigrateArchiveCommand { get; }

    #endregion

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
}