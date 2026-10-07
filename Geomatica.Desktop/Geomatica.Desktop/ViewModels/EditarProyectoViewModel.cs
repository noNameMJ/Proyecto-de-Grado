using Geomatica.AppCore.UseCases;
using Geomatica.Data.Repositories;
using Geomatica.Desktop.Services;
using Geomatica.Domain.Interfaces.Repositories;
using System;

namespace Geomatica.Desktop.ViewModels
{
    /// <summary>
    /// ViewModel para la edición de proyectos geomáticos existentes.
    /// Hereda de <see cref="FormularioProyectoViewModel"/> configurado en modo edición.
    /// Preserva compatibilidad total con DI y llamadas existentes.
    /// </summary>
    public class EditarProyectoViewModel : FormularioProyectoViewModel
    {
        public new int IdProyecto => base.IdProyecto ?? 0;

        public EditarProyectoViewModel(
            ActualizarProyectoUseCase actualizarProyectoUseCase,
            IMunicipioRepository municipioRepository,
            ProyectoDetalleDto proyecto,
            Action navigateBack,
            Action? onProyectoEditado = null,
            EliminarProyectoUseCase? eliminarProyectoUseCase = null,
            INotificationService? notifications = null,
            ProyectoArchivosService? archivosService = null)
            : base(actualizarProyectoUseCase, municipioRepository, proyecto, navigateBack, onProyectoEditado, eliminarProyectoUseCase, notifications, archivosService)
        {
        }

        public EditarProyectoViewModel(
            IProyectoRepository proyectoRepository,
            IMunicipioRepository municipioRepository,
            ProyectoDetalleDto proyecto,
            Action navigateBack,
            Action? onProyectoEditado = null,
            EliminarProyectoUseCase? eliminarProyectoUseCase = null,
            INotificationService? notifications = null,
            ProyectoArchivosService? archivosService = null)
            : this(new ActualizarProyectoUseCase(proyectoRepository), municipioRepository, proyecto, navigateBack, onProyectoEditado, eliminarProyectoUseCase, notifications, archivosService)
        {
        }
    }
}
