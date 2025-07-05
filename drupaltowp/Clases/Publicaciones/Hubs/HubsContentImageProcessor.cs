using drupaltowp.Configuracion;
using drupaltowp.Services;
using drupaltowp.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WordPressPCL;

namespace drupaltowp.Clases.Publicaciones.Hubs;

internal class HubsContentImageProcessor
{
    private readonly LoggerViewModel _logger;
    private readonly WordPressClient _wpClient;
    private readonly MappingService _mappingService;

    private int IdCategoriaOpinion = 1;
    public bool Cancelar { get; set; } = false;

    public HubsContentImageProcessor(LoggerViewModel logger, CancellationService cancellation = default)
    {
        _logger = logger;
        // Configurar cliente WordPress
        _wpClient = new WordPressClient(ConfiguracionGeneral.UrlsitioWP);
        _wpClient.Auth.UseBasicAuth(ConfiguracionGeneral.Usuario, ConfiguracionGeneral.Password);
        _mappingService = new(logger);
    }

    internal async Task ProcessHubsContentImagesAsync()
    {
        throw new NotImplementedException();
    }
}
