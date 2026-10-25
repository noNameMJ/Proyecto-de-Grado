using System;
using System.Collections.Generic;
using System.Linq;

namespace Geomatica.Domain.Catalogos;

/// <summary>
/// Catálogo canónico y fuente única de verdad para la clasificación de extensiones de archivo,
/// categorías temáticas SIG, iconos visuales y normalización de formatos en toda la solución.
/// </summary>
public static class CatalogoFormatosGis
{
    public sealed record CategoriaDefinicion(
        string Nombre,
        string Icono,
        string BgColorHex,
        string BorderColorHex,
        string FgColorHex,
        IReadOnlySet<string> Extensiones);

    public const string CategoriaVectorial = "Vectorial";
    public const string CategoriaRaster = "Raster / Ortofoto";
    public const string CategoriaLidar = "LiDAR / Nubes";
    public const string CategoriaCad = "CAD / Planos";
    public const string CategoriaDocumentacion = "Documentación";
    public const string CategoriaComprimidos = "Comprimidos / Archivos";
    public const string CategoriaAuxiliar = "Auxiliar / Sidecar";
    public const string CategoriaOtros = "Otros";

    private static readonly HashSet<string> _extsVectoriales = new(StringComparer.OrdinalIgnoreCase)
    {
        ".shp", ".gpkg", ".geojson", ".kml", ".kmz", ".tab", ".mif", ".gml", ".gdb", ".geodatabase"
    };

    private static readonly HashSet<string> _extsRaster = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tif", ".tiff", ".img", ".dem", ".asc", ".dt2", ".ecw", ".jp2", ".sid", ".mrf", ".vrt"
    };

    private static readonly HashSet<string> _extsLidar = new(StringComparer.OrdinalIgnoreCase)
    {
        ".las", ".laz", ".zlas", ".xyz", ".pts", ".ply", ".e57"
    };

    private static readonly HashSet<string> _extsCad = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dwg", ".dxf", ".dgn"
    };

    private static readonly HashSet<string> _extsDocumentacion = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".csv", ".txt", ".md", ".pptx", ".ppt", ".json"
    };

    private static readonly HashSet<string> _extsComprimidos = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2"
    };

    private static readonly HashSet<string> _extsAuxiliares = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dbf", ".shx", ".prj", ".cpg", ".sbn", ".sbx", ".tfw", ".ovr", ".aux.xml", ".rrd", ".aux", ".j2w", ".wld", ".qmd", ".xml"
    };

    public static readonly IReadOnlyList<CategoriaDefinicion> Categorias = new List<CategoriaDefinicion>
    {
        new(
            Nombre: CategoriaVectorial,
            Icono: "🗺️",
            BgColorHex: "#E8F4EC",
            BorderColorHex: "#B2DFBF",
            FgColorHex: "#1A5C34",
            Extensiones: _extsVectoriales
        ),
        new(
            Nombre: CategoriaRaster,
            Icono: "🛰️",
            BgColorHex: "#E3F2FD",
            BorderColorHex: "#BBDEFB",
            FgColorHex: "#1565C0",
            Extensiones: _extsRaster
        ),
        new(
            Nombre: CategoriaLidar,
            Icono: "☁️",
            BgColorHex: "#F3E5F5",
            BorderColorHex: "#E1BEE7",
            FgColorHex: "#6A1B9A",
            Extensiones: _extsLidar
        ),
        new(
            Nombre: CategoriaCad,
            Icono: "📐",
            BgColorHex: "#FFF8E1",
            BorderColorHex: "#FFE082",
            FgColorHex: "#B78103",
            Extensiones: _extsCad
        ),
        new(
            Nombre: CategoriaDocumentacion,
            Icono: "📄",
            BgColorHex: "#F5F5F5",
            BorderColorHex: "#E0E0E0",
            FgColorHex: "#424242",
            Extensiones: _extsDocumentacion
        )
    };

    public static IReadOnlySet<string> ExtensionesVectoriales => _extsVectoriales;
    public static IReadOnlySet<string> ExtensionesRaster => _extsRaster;
    public static IReadOnlySet<string> ExtensionesLidar => _extsLidar;
    public static IReadOnlySet<string> ExtensionesCad => _extsCad;
    public static IReadOnlySet<string> ExtensionesDocumentacion => _extsDocumentacion;
    public static IReadOnlySet<string> ExtensionesComprimidos => _extsComprimidos;
    public static IReadOnlySet<string> ExtensionesAuxiliares => _extsAuxiliares;

    /// <summary>
    /// Normaliza una extensión de archivo garantizando el punto inicial y minúsculas invariantes.
    /// Ejemplo: "SHP" -> ".shp", ".TIF" -> ".tif", "" -> "".
    /// </summary>
    public static string NormalizarExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        string limpia = extension.Trim().ToLowerInvariant();
        return limpia.StartsWith('.') ? limpia : "." + limpia;
    }

    public static bool EsVectorial(string? extension) => _extsVectoriales.Contains(NormalizarExtension(extension));
    public static bool EsShapefile(string? extension) => string.Equals(NormalizarExtension(extension), ".shp", StringComparison.OrdinalIgnoreCase);
    public static bool EsGeoPackage(string? extension) => string.Equals(NormalizarExtension(extension), ".gpkg", StringComparison.OrdinalIgnoreCase);
    public static bool EsGdb(string? extension)
    {
        var norm = NormalizarExtension(extension);
        return string.Equals(norm, ".gdb", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(norm, ".geodatabase", StringComparison.OrdinalIgnoreCase);
    }
    public static bool EsRaster(string? extension) => _extsRaster.Contains(NormalizarExtension(extension));
    public static bool EsLidar(string? extension) => _extsLidar.Contains(NormalizarExtension(extension));
    public static bool EsCad(string? extension) => _extsCad.Contains(NormalizarExtension(extension));
    public static bool EsDocumentacion(string? extension) => _extsDocumentacion.Contains(NormalizarExtension(extension));
    public static bool EsComprimido(string? extension) => _extsComprimidos.Contains(NormalizarExtension(extension));
    public static bool EsAuxiliar(string? extension) => _extsAuxiliares.Contains(NormalizarExtension(extension));

    /// <summary>
    /// Determina si una extensión corresponde a algún tipo de dato espacial (Vectorial, Ráster, LiDAR, CAD o GDB).
    /// </summary>
    public static bool EsEspacial(string? extension)
    {
        var norm = NormalizarExtension(extension);
        return _extsVectoriales.Contains(norm) ||
               _extsRaster.Contains(norm) ||
               _extsLidar.Contains(norm) ||
               _extsCad.Contains(norm);
    }

    /// <summary>
    /// Obtiene la definición de categoría para una extensión dada.
    /// </summary>
    public static CategoriaDefinicion? ObtenerDefinicionCategoria(string? extension)
    {
        var norm = NormalizarExtension(extension);
        if (string.IsNullOrEmpty(norm)) return null;

        return Categorias.FirstOrDefault(c => c.Extensiones.Contains(norm));
    }

    /// <summary>
    /// Retorna el nombre de categoría principal para una extensión dada.
    /// </summary>
    public static string ObtenerCategoria(string? extension)
    {
        var norm = NormalizarExtension(extension);
        if (string.IsNullOrEmpty(norm)) return CategoriaOtros;

        var def = ObtenerDefinicionCategoria(norm);
        if (def != null) return def.Nombre;

        if (EsAuxiliar(norm)) return CategoriaAuxiliar;
        if (EsComprimido(norm)) return CategoriaComprimidos;

        return CategoriaOtros;
    }

    /// <summary>
    /// Devuelve el conjunto de extensiones asociadas a una clave de filtro de categoría
    /// (e.g. "vectorial", "raster", "lidar", "cad", "docs", "documentacion", "comprimidos").
    /// </summary>
    public static HashSet<string> ObtenerExtensionesPorCategoria(string? categoria)
    {
        if (string.IsNullOrWhiteSpace(categoria) ||
            categoria.Equals("Todos", StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        string clave = categoria.Trim().ToLowerInvariant();
        return clave switch
        {
            "vectorial" => new HashSet<string>(_extsVectoriales, StringComparer.OrdinalIgnoreCase),
            "raster" or "ráster" => new HashSet<string>(_extsRaster, StringComparer.OrdinalIgnoreCase),
            "lidar" => new HashSet<string>(_extsLidar, StringComparer.OrdinalIgnoreCase),
            "cad" => new HashSet<string>(_extsCad, StringComparer.OrdinalIgnoreCase),
            "docs" or "documentacion" or "documentación" => new HashSet<string>(
                _extsDocumentacion.Concat(_extsComprimidos),
                StringComparer.OrdinalIgnoreCase),
            "comprimidos" => new HashSet<string>(_extsComprimidos, StringComparer.OrdinalIgnoreCase),
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// Devuelve el emoji/icono correspondiente a una extensión o carpeta.
    /// Preserva la coherencia visual en exploradores, fichas y árboles virtuales.
    /// </summary>
    public static string ObtenerIcono(string? extension, bool esCarpeta = false)
    {
        if (esCarpeta) return "📁";

        var ext = NormalizarExtension(extension);
        if (string.IsNullOrEmpty(ext)) return "📄";

        return ext switch
        {
            ".gdb" or ".geodatabase" => "🗃️",
            ".shp" or ".gpkg" or ".geojson" or ".kml" or ".kmz" or ".tab" or ".mif" or ".gml" => "🗺️",
            ".tif" or ".tiff" or ".img" or ".dem" or ".asc" or ".dt2" or ".ecw" or ".jp2" or ".sid" or ".mrf" or ".vrt" => "🛰️",
            ".las" or ".laz" or ".zlas" or ".xyz" or ".pts" or ".ply" or ".e57" => "☁️",
            ".dwg" or ".dxf" or ".dgn" => "📐",
            ".pdf" => "📕",
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".bz2" => "🗜️",
            ".xlsx" or ".xls" or ".csv" => "📊",
            ".doc" or ".docx" or ".txt" => "📝",
            _ when EsAuxiliar(ext) => "📎",
            _ => "📄"
        };
    }
}

/// <summary>
/// Métodos de extensión para trabajar con cadenas de rutas y extensiones de archivo de manera fluida.
/// </summary>
public static class ExtensionHelper
{
    public static string NormalizarExtension(this string? extension) => CatalogoFormatosGis.NormalizarExtension(extension);
}
