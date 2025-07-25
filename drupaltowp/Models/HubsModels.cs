using System;
using System.Collections.Generic;

namespace drupaltowp.Models;

public class HubPublicationData
{
    public int Nid { get; set; }
    public string Titulo { get; set; }
    public int Uid { get; set; }
    public DateTime Creado { get; set; }
    public int Status { get; set; }
    public string Cuerpo { get; set; }
    public string Bajada { get; set; }
    public string Volanta { get; set; } // Nueva propiedad para la volanta
    public int? Categoria { get; set; }
    public string NombreCategoria { get; set; }
    public List<int?> ImagenesDestacadas { get; set; } = new List<int?>();
    public List<int> Tags { get; set; } = new List<int>();
}
/// <summary>
/// Modelo para el resultado del query de hubs desde Drupal
/// </summary>
public class HubQueryResult
{
    public int Nid { get; set; }
    public string Titulo { get; set; }
    public int Uid { get; set; }
    public DateTime Creado { get; set; }
    public int Status { get; set; }
    public string Cuerpo { get; set; }
    public string Bajada { get; set; }
    public int? Imagen_Destacada { get; set; }
    public int? Categoria { get; set; }
    public string Nombre_Categoria { get; set; }
    public int? Tags { get; set; }
    public string Volanta { get; set; } // Nueva propiedad para la volanta
}