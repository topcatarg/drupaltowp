-- Estandarizar todas las tablas de mapping con la misma estructura
-- Ejecutar estos comandos en tu base de datos comuwp

-- Tabla para mapear usuarios (estandarizada)
CREATE TABLE IF NOT EXISTS user_mapping (
    id INT AUTO_INCREMENT PRIMARY KEY,
    drupal_user_id INT NOT NULL,
    wp_user_id BIGINT UNSIGNED NOT NULL,
    drupal_name VARCHAR(255),
    wp_username VARCHAR(255),
    migrated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY unique_drupal_user (drupal_user_id),
    KEY idx_wp_user (wp_user_id)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- Tabla para mapear categorías (estandarizada)
CREATE TABLE IF NOT EXISTS category_mapping (
    id INT AUTO_INCREMENT PRIMARY KEY,
    drupal_category_id INT NOT NULL,
    wp_category_id BIGINT UNSIGNED NOT NULL,
    drupal_name VARCHAR(255),
    wp_name VARCHAR(255),
    vocabulary VARCHAR(255),
    migrated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY unique_drupal_category (drupal_category_id, vocabulary),
    KEY idx_wp_category (wp_category_id)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- Tabla para mapear tags (estandarizada)
CREATE TABLE IF NOT EXISTS tag_mapping (
    id INT AUTO_INCREMENT PRIMARY KEY,
    drupal_tag_id INT NOT NULL,
    wp_tag_id BIGINT UNSIGNED NOT NULL,
    drupal_name VARCHAR(255),
    wp_name VARCHAR(255),
    migrated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY unique_drupal_tag (drupal_tag_id),
    KEY idx_wp_tag (wp_tag_id)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- Tabla para mapear posts (estandarizada)

CREATE TABLE `post_mapping_biblioteca` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `drupal_post_id` int(11) NOT NULL,
  `wp_post_id` bigint(20) unsigned NOT NULL,
  `migrated_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `Imagenes` tinyint(1) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unique_drupal_post` (`drupal_post_id`),
  KEY `idx_wp_post` (`wp_post_id`)
) ENGINE=InnoDB AUTO_INCREMENT=11081 DEFAULT CHARSET=latin1;

-- Tabla para mapear medios/imágenes (estandarizada)
CREATE TABLE IF NOT EXISTS media_mapping (
    id INT AUTO_INCREMENT PRIMARY KEY,
    drupal_file_id INT NOT NULL,
    wp_media_id BIGINT UNSIGNED NOT NULL,
    drupal_filename VARCHAR(255),
    wp_filename VARCHAR(255),
    drupal_uri TEXT,
    wp_url TEXT,
    migrated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY unique_drupal_file (drupal_file_id),
    KEY idx_wp_media (wp_media_id)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- Scripts para migrar datos existentes si las tablas ya tienen datos
-- (Ejecutar solo si necesitas migrar datos de tablas con nombres diferentes)

-- Ejemplo para migrar datos de wp_drupal_migration_mapping a category_mapping
-- INSERT INTO category_mapping (drupal_category_id, wp_category_id, drupal_name, wp_name, vocabulary, migrated_at)
-- SELECT drupal_id, wp_id, drupal_name, wp_name, vocabulary, created_at 
-- FROM wp_drupal_migration_mapping;

-- Ejemplo para migrar datos de drupal_wp_tag_mapping a tag_mapping  
-- INSERT INTO tag_mapping (drupal_tag_id, wp_tag_id, drupal_name, wp_name, migrated_at)
-- SELECT drupal_id, wp_id, drupal_name, wp_name, created_at 
-- FROM drupal_wp_tag_mapping;