using drupaltowp.Configuracion;
using drupaltowp.Services;
using drupaltowp.ViewModels;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WordPressPCL;
using Dapper;

namespace drupaltowp.Clases.Publicaciones.Hubs;

internal class HubsPublicationsRollback
{

    private readonly LoggerViewModel _logger;
    private readonly WordPressClient _wpClient;
    private readonly MappingService _mappingService;

    private int IdCategoriaOpinion = 1;
    public bool Cancelar { get; set; } = false;

    public HubsPublicationsRollback(LoggerViewModel logger, CancellationService cancellation = default)
    {
        _logger = logger;
        // Configurar cliente WordPress
        _wpClient = new WordPressClient(ConfiguracionGeneral.UrlsitioWP);
        _wpClient.Auth.UseBasicAuth(ConfiguracionGeneral.Usuario, ConfiguracionGeneral.Password);
        _mappingService = new(logger);
    }

    public async Task RollbackPublicationsAsync()
    {
        await _mappingService.LoadMappingsForContentType(ContentType.Hubs);
        using MySqlConnection wpConnection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
        await wpConnection.OpenAsync();
        foreach (var post in _mappingService.HubsMapping)
        {
            await _wpClient.Posts.DeleteAsync(post.Value.WpPostId);
            //Lo borro del mapeo
            await wpConnection.ExecuteAsync(@"
DELETE
FROM post_mapping_hubs
WHERE wp_post_id = @id", new {id = post.Value.WpPostId});
            _logger.LogMessage($"se borro el post con id: {post.Value.WpPostId}");
        }
    }
}
