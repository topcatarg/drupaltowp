using Dapper;
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
using WordPressPCL.Models;

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
            //Borro en wp_post_meta
            await wpConnection.ExecuteAsync(HubRollBackQuerys.DeletePostMeta, new { postId = post.Value.WpPostId });
            //borro el post en wordpress
            await wpConnection.ExecuteAsync(HubRollBackQuerys.DeletePost, new { postId = post.Value.WpPostId });
            //Borro las relaciones de terminos
            await wpConnection.ExecuteAsync(HubRollBackQuerys.DeleteTermRelationship, new { postId = post.Value.WpPostId });
            //Borro el post en mapping
            await wpConnection.ExecuteAsync(HubRollBackQuerys.DeleteMapping, new { id = post.Value.WpPostId });
            //Lo borro del mapeo
            _logger.LogMessage($"se borro el post con id: {post.Value.WpPostId}");
        }
        //Borro las categorias de hub creadas. 
        await wpConnection.ExecuteAsync(HubRollBackQuerys.DeleteCategoryTermTaxonomy);
        await wpConnection.ExecuteAsync(HubRollBackQuerys.DeleteTagTermTaxonomy);
        await wpConnection.ExecuteAsync(HubRollBackQuerys.DeleteTerm);
        _logger.LogMessage($"se borraron todos los terminos");
    }
}
