namespace drupaltowp.Clases.Publicaciones.Hubs;

/// <summary>
/// Clase que contiene todas las consultas SQL para la migración de hubs
/// </summary>
public static class HubQueries
{
    #region Drupal Queries

    /// <summary>
    /// Query principal para obtener datos de hubs desde Drupal
    /// </summary>
    public const string GetHubsFromDrupal = @"
            SELECT
                n.nid,
                n.title as titulo,
                n.uid,
                FROM_UNIXTIME(n.created) as creado,
                n.status,
                fdb.body_value as cuerpo,
                fdfb.field_bajada_value as bajada,
                fdfbii.field_basic_image_image_fid as imagen_destacada,
                fdfh.field_hub_tid as categoria,
                ttd.name as nombre_categoria,
                fdft.field_tags_tid as tags,
                f.field_volanta_value as volanta
            FROM node n
            LEFT JOIN field_data_body fdb ON fdb.entity_id = n.nid
            LEFT JOIN field_data_field_bajada fdfb ON fdfb.entity_id = n.nid
            LEFT JOIN field_data_field_basic_image_image fdfbii ON fdfbii.entity_id = n.nid
            LEFT JOIN field_data_field_hub fdfh ON fdfh.entity_id = n.nid
            LEFT JOIN taxonomy_term_data ttd ON ttd.tid = fdfh.field_hub_tid
            LEFT JOIN field_data_field_tags fdft ON fdft.entity_id = n.nid
            LEFT JOIN field_data_field_volanta f ON f.entity_id = n.nid
            WHERE n.type = 'hubs'
                AND fdb.body_value IS NOT NULL
                AND n.status = 1
            ORDER BY n.nid desc, fdft.field_tags_tid";

    #endregion

    #region WordPress Queries

    /// <summary>
    /// Query para insertar un nuevo post en WordPress
    /// </summary>
    public const string InsertWordPressPost = @"
            INSERT INTO wp_posts (
                post_author, 
                post_date, 
                post_date_gmt, 
                post_content, 
                post_title, 
                post_excerpt, 
                post_status, 
                comment_status, 
                ping_status, 
                post_password, 
                post_name, 
                to_ping, 
                pinged, 
                post_modified, 
                post_modified_gmt, 
                post_content_filtered, 
                post_parent, 
                guid, 
                menu_order, 
                post_type, 
                post_mime_type, 
                comment_count
            ) VALUES (
                @author, 
                @date, 
                @date_gmt, 
                @content, 
                @title, 
                @excerpt, 
                'publish', 
                'open', 
                'open', 
                '', 
                @slug, 
                '', 
                '', 
                @modified, 
                @modified_gmt, 
                '', 
                0, 
                @guid, 
                0, 
                'hubs', 
                '', 
                0
            )";

    /// <summary>
    /// Query para actualizar el GUID del post
    /// </summary>
    public const string UpdatePostGuid = @"
            UPDATE wp_posts 
            SET guid = @guid 
            WHERE ID = @postId";

    /// <summary>
    /// Query para agregar meta field al post
    /// </summary>
    public const string InsertPostMeta = @"
            INSERT INTO wp_postmeta (post_id, meta_key, meta_value) 
            VALUES (@postId, @metaKey, @metaValue)";

    /// <summary>
    /// Query para agregar imagen destacada al post
    /// </summary>
    public const string InsertFeaturedImageMeta = @"
            INSERT INTO wp_postmeta (post_id, meta_key, meta_value) 
            VALUES (@postId, '_thumbnail_id', @imageId)";

    #endregion

    #region Taxonomy Queries

    /// <summary>
    /// Query para asociar taxonomía con post
    /// </summary>
    public const string InsertTermRelationship = @"
            INSERT IGNORE INTO wp_term_relationships (object_id, term_taxonomy_id, term_order) 
            VALUES (@postId, @taxonomyId, 0)";

    /// <summary>
    /// Query para actualizar contador de taxonomía
    /// </summary>
    public const string UpdateTaxonomyCount = @"
            UPDATE wp_term_taxonomy 
            SET count = count + 1 
            WHERE term_taxonomy_id = @taxonomyId";

    /// <summary>
    /// Query para obtener term_taxonomy_id de una categoría
    /// </summary>
    public const string GetCategoryTaxonomyId = @"
            SELECT term_taxonomy_id 
            FROM wp_term_taxonomy 
            WHERE term_id = @termId AND taxonomy = 'category'";

    /// <summary>
    /// Query para obtener term_taxonomy_id de un tag
    /// </summary>
    public const string GetTagTaxonomyId = @"
            SELECT term_taxonomy_id 
            FROM wp_term_taxonomy 
            WHERE term_id = @termId AND taxonomy = 'post_tag'";

    #endregion

    #region Utility Queries

    /// <summary>
    /// Query para obtener el último ID insertado
    /// </summary>
    public const string GetLastInsertId = "SELECT LAST_INSERT_ID();";

    #endregion
}