using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace drupaltowp.Clases.Publicaciones.Hubs;

public static class HubRollBackQuerys
{
    public const string DeletePostMeta = @"
        DELETE FROM wp_postmeta
        WHERE post_id = @postId;";

    public const string DeletePost = @"
        DELETE FROM wp_posts
        WHERE ID = @postId;";

    public const string DeleteMapping = @"
        DELETE FROM post_mapping_hubs
        WHERE wp_post_id = @id;";

    public const string DeleteTermRelationship = @"
        DELETE FROM wp_term_relationships
        WHERE object_id = @postId;";

    public const string DeleteTagTermTaxonomy = @"
        DELETE FROM wp_term_taxonomy
        WHERE taxonomy = 'tag_hub'
        ";

    public const string DeleteCategoryTermTaxonomy = @"
        DELETE FROM wp_term_taxonomy
        WHERE taxonomy = 'category_hub';";

    public const string DeleteTerm = @"
        DELETE FROM wp_terms
        WHERE slug like 'hub-%' 
        ";

    public const string DeleteTaxonomyMapping = @"
        DELETE FROM taxonomy_mapping
        WHERE taxonomy IN ('category_hub', 'tag_hub');";
}
