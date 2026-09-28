using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Geomatica.Desktop.Models;

namespace Geomatica.Desktop.Services
{
    public record CadImportResult(
        bool Success,
        string? GeoPackagePath,
        IReadOnlyList<string> Capas,
        string? MensajeError = null,
        bool FromCache = false,
        string ProveedorUtilizado = ""
    );

    public interface ICadImporterService
    {
        bool IsArcPyAvailable { get; }
        bool IsACadSharpAvailable { get; }
        string ProveedorActivo { get; }
        string? PythonExecutablePath { get; }

        string ObtenerRutaCache(string cadFilePath);
        void LimpiarCache();

        Task<CadImportResult> ImportarCadAsync(string cadFilePath, CancellationToken ct = default);
        Task<IReadOnlyList<CadCapaInfo>> ObtenerCapasCadAsync(string cadFilePath, CancellationToken ct = default);
    }
}

