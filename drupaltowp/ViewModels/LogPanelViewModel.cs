using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace drupaltowp.ViewModels;

public class LogPanelViewModel : INotifyPropertyChanged
{
    private readonly LoggerViewModel _loggerViewModel;

    public LogPanelViewModel(LoggerViewModel loggerViewModel)
    {
        _loggerViewModel = loggerViewModel;

        // Suscribirse a cambios del LoggerViewModel
        _loggerViewModel.PropertyChanged += OnLoggerPropertyChanged;
    }

    #region Properties que exponen LoggerViewModel
    public string LogText => _loggerViewModel.LogText;

    public bool AutoScroll
    {
        get => _loggerViewModel.AutoScroll;
        set => _loggerViewModel.AutoScroll = value;
    }
    #endregion

    #region Event Handlers
    private void OnLoggerPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        // Propagar cambios del LoggerViewModel
        switch (e.PropertyName)
        {
            case nameof(LoggerViewModel.LogText):
                OnPropertyChanged(nameof(LogText));
                break;
            case nameof(LoggerViewModel.AutoScroll):
                OnPropertyChanged(nameof(AutoScroll));
                break;
        }
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