using Geomatica.AppCore.UseCases;
using Geomatica.Domain.Interfaces.Repositories;
using Geomatica.Desktop.Services;
using System;

namespace Geomatica.Desktop.ViewModels
{
    /// <summary>
    /// ViewModel para la creación de proyectos geomáticos.
    /// Hereda de <see cref="FormularioProyectoViewModel"/> configurado en modo creación.
    /// Preserva compatibilidad total con DI y pruebas unitarias existentes.
    /// </summary>
    public class CrearProyectoViewModel : FormularioProyectoViewModel
    {
        public CrearProyectoViewModel(
            CrearProyectoUseCase crearProyectoUseCase,
            IMunicipioRepository municipioRepository,
            ProyectoArchivosService proyectoArchivosService,
            Action navigateBack,
            Action? onProyectoCreado = null,
            INotificationService? notifications = null,
            GuardarSubproyectoUseCase? guardarSubproyectoUseCase = null,
            ObtenerSubproyectosUseCase? obtenerSubproyectosUseCase = null,
            EliminarSubproyectoUseCase? eliminarSubproyectoUseCase = null)
            : base(crearProyectoUseCase, municipioRepository, proyectoArchivosService, navigateBack, onProyectoCreado, notifications, guardarSubproyectoUseCase, obtenerSubproyectosUseCase, eliminarSubproyectoUseCase)
        {
        }

        public CrearProyectoViewModel(
            IProyectoRepository proyectoRepository,
            IMunicipioRepository municipioRepository,
            ProyectoArchivosService proyectoArchivosService,
            Action navigateBack,
            Action? onProyectoCreado = null,
            INotificationService? notifications = null,
            GuardarSubproyectoUseCase? guardarSubproyectoUseCase = null,
            ObtenerSubproyectosUseCase? obtenerSubproyectosUseCase = null,
            EliminarSubproyectoUseCase? eliminarSubproyectoUseCase = null)
            : this(new CrearProyectoUseCase(proyectoRepository), municipioRepository, proyectoArchivosService, navigateBack, onProyectoCreado, notifications, guardarSubproyectoUseCase, obtenerSubproyectosUseCase, eliminarSubproyectoUseCase)
        {
        }
    }
}

