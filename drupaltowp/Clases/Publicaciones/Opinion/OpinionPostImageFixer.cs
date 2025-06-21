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
using drupaltowp.Models;

namespace drupaltowp.Clases.Publicaciones.Opinion;

internal class OpinionPostImageFixer
{
    private readonly LoggerViewModel _logger;
    private readonly WordPressClient _wpClient;
    private readonly MappingService _mappingService;

    private int IdCategoriaOpinion = 1;
    public bool Cancelar { get; set; } = false;

    public OpinionPostImageFixer(LoggerViewModel logger, WordPressClient wpClient)
    {
        _logger = logger;
        _wpClient = wpClient;
        _mappingService = new(logger);
    }

    public async Task FixPostAsync()
    {

        //Cargo los mapeos
        await _mappingService.LoadMappingsForContentType(ContentType.Opinion);
        //Agarro el post de opinion
        foreach (var post in _mappingService.OpinionMapping)

        {
            //busco la imagen en drupal
            int drupalid = await ObtenerDrupalPost(post.Value.DrupalPostId);
            //busco el mapeo de la imagen
            var wpId = _mappingService.GetWordPressMediaId(drupalid);
            //corrijo la metadata del post en wp
            await ActualizarMeta(wpId, post.Value.WpPostId);
        }



    }

    public async Task ActualizarMeta(int wpId, int postId)
    {
        //Actualizo la imagen destacada del post
        _logger.LogProcess($"Actualizando imagen destacada del post {postId} con ID de imagen {wpId}");
        string query = @$"update wp_postmeta wp 
set meta_value = {wpId} 
where wp.post_id = {postId}
and wp.meta_key = '_mh_author_photo_fid' ";
        using var wpConnection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
        await wpConnection.ExecuteAsync(query);
    }

    public async Task<int> ObtenerDrupalPost(int drupalId)
    {
        string Query = @$"
select n.nid,
n.title titulo,
n.created,
fdb.body_value cuerpo,
fdfb.field_bajada_value,
fdffc.field_featured_categories_tid,
fdfnrs.field_noticia_region_site_tid,
fdft.field_tags_tid tags,
fdfv.field_volanta_value,
fdfof.field_opinion_frase_value frase_opinion,
fdfr.field_responsabilidad_value responsabilidad_autor,
fdfan.field_autor_nombre_value nombre_autor,
fdfup.field_user_picture_fid , 
fm.filename, 
fm.uri
from node n 
left join field_data_body fdb on fdb.entity_id = n.nid
left join field_data_field_bajada fdfb  on fdfb.entity_id = n.nid
left join field_data_field_featured_categories fdffc on fdffc.entity_id = n.nid
left join field_data_field_noticia_region_site fdfnrs on fdfnrs.entity_id = n.nid
left join field_data_field_tags fdft on fdft.entity_id = n.nid
left join field_data_field_volanta fdfv on fdfv.entity_id = n.nid
left join field_data_field_opinion_frase fdfof on fdfof.entity_id = n.nid
left join field_data_field_responsabilidad fdfr on fdfr.entity_id = n.nid
left join field_data_field_autor_nombre fdfan on fdfan.entity_id = n.nid
left join field_data_field_user_picture fdfup on fdfup.entity_id = n.nid
left join file_managed fm on fm.fid  = fdfup.field_user_picture_fid
where n.type = 'opinion'
and n.nid = {drupalId}
Order by n.created DESC";
        using var drupalConnection = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString);
        var drupalPost = await drupalConnection.QueryFirstOrDefaultAsync<OpinionPostRaw>(Query);
        return (int)(drupalPost.Field_User_Picture_Fid);
    }
    
}
