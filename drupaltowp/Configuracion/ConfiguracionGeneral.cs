using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace drupaltowp.Configuracion
{
    internal class ConfiguracionGeneral
    {
        public static readonly string UrlsitioWP = "http://localhost/comunicarsewp/wp-json/";
        public static readonly string UrlsitioDrupal = "http://localhost/comunicarseweb";
        public static readonly string Usuario = "gonzalo";
        public static readonly string Password = "suwr haUK hkOu MqTL MnHk NTTz";
        public static readonly string DrupalconnectionString = "Server=localhost;Database=comunicarse_drupal;User ID=root;Password=root;Port=3306";
        public static readonly string WPconnectionString = "Server=localhost;Database=comunicarse_wp;User ID=root;Password=root;Port=3306";
        public static readonly string DrupalFileRoute = "C:\\datos\\source\\Repos\\Docker-Composes\\Apache y mysql\\web\\comunicarseweb\\sites\\default\\files";
        public static readonly string WPFileRoute = "C:\\datos\\source\\Repos\\Docker-Composes\\Apache y mysql\\web\\comunicarsewp\\wp-content\\uploads";
        public static readonly string WPGenericImageFileName = "generic-post-image.png";
        public static readonly string DrupalGenericImageFileName = "webp.net-resizeimage_1.png";
        public static readonly DateTime FechaMinimaImagen = new(2022, 1, 1);
        public static readonly string LogFilePath = @"C:\temp\drupaltowp_migration.log";
        public static readonly int IdImagenGenerica = 13934; // ID de la imagen genérica en WordPress

    }
}
