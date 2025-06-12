using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace drupaltowp.ViewModels;

public class QuickControlsViewModel : INotifyPropertyChanged
{
    private readonly LoggerViewModel _loggerViewModel;
    private readonly Func<Task> _showStatusAction;

    public QuickControlsViewModel(LoggerViewModel loggerViewModel, Func<Task> showStatusAction)
    {
        _loggerViewModel = loggerViewModel;
        _showStatusAction = showStatusAction;

        // Inicializar commands
        ClearLogCommand = new RelayCommand(ExecuteClearLog);
        ShowStatusCommand = new RelayCommand(async () => await ExecuteShowStatus());
        CancelOperationCommand = new RelayCommand(ExecuteCancelOperation);
    }

    #region Commands
    public ICommand ClearLogCommand { get; }
    public ICommand ShowStatusCommand { get; }
    public ICommand CancelOperationCommand { get; }
    #endregion

    #region Command Implementations
    private void ExecuteClearLog()
    {
        _loggerViewModel.Clear();
        _loggerViewModel.LogInfo("🧹 Log limpiado");
    }

    private async Task ExecuteShowStatus()
    {
        try
        {
            _loggerViewModel.LogInfo("📊 Obteniendo estado del sistema...");
            await _showStatusAction?.Invoke();
        }
        catch (Exception ex)
        {
            _loggerViewModel.LogError($"Error mostrando estado: {ex.Message}");
        }
    }

    private void ExecuteCancelOperation()
    {
        _loggerViewModel.LogWarning("⏹️ Solicitud de cancelación (funcionalidad por implementar)");
        // TODO: Implementar lógica de cancelación
    }
    #endregion

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
}