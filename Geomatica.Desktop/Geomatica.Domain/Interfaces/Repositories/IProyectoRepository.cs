using Geomatica.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geomatica.Domain.Interfaces.Repositories
{
    public interface IProyectoRepository
    {
        Task<IReadOnlyList<ProyectoGeomatico>> BuscarAsync(
            string? texto, DateTime? desde, DateTime? hasta,
            double? minX, double? minY, double? maxX, double? maxY,
            CancellationToken ct = default);
    }
}
