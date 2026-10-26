using FluentAssertions;
using Geomatica.Desktop.Services;
using Geomatica.Desktop.ViewModels;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Geomatica.UnitTests.ViewModels
{
    public class EditarSubproyectoTests
    {
        private readonly List<FormularioProyectoViewModel.MunicipioItem> _municipiosProyecto = new()
        {
            new("68081", "Barrancabermeja"),
            new("68547", "Piedecuesta")
        };

        private readonly List<FormularioProyectoViewModel.MunicipioItem> _todosMunicipios = new()
        {
            new("68081", "Barrancabermeja"),
            new("68547", "Piedecuesta"),
            new("20295", "Gamarra"),
            new("11001", "Bogotá D.C.")
        };

        [Fact]
        public void Inicializacion_ModoEdicion_CargaValoresExistentes()
        {
            var sub = new SubproyectoItemFormulario
            {
                IdSubproyecto = 42,
                IdProyecto = 7,
                Nombre = "Frente Fluvial Sur",
                Subcarpeta = @"Datos_Espaciales\Sector_Sur",
                MpioCdpmp = "68081",
                MunicipioNombre = "Barrancabermeja",
                Lat = 7.062000,
                Lon = -73.854000,
                Descripcion = "Punto de desembarque de equipos"
            };

            var vm = new EditarSubproyectoViewModel(
                sub,
                rutaProyecto: @"D:\Proyectos\RioMagdalena",
                subcarpetasDisponibles: new[] { @"Datos_Espaciales\Sector_Sur", @"Datos_Espaciales\Sector_Norte" },
                municipiosDisponibles: _municipiosProyecto,
                todosMunicipiosCatalogo: _todosMunicipios,
                latProyectoStr: "7.000000",
                lonProyectoStr: "-73.800000");

            vm.EsNuevo.Should().BeFalse();
            vm.IdSubproyecto.Should().Be(42);
            vm.IdProyecto.Should().Be(7);
            vm.Titulo.Should().Contain("Frente Fluvial Sur");
            vm.Nombre.Should().Be("Frente Fluvial Sur");
            vm.Subcarpeta.Should().Be(@"Datos_Espaciales\Sector_Sur");
            vm.MunicipioTexto.Should().Be("Barrancabermeja");
            vm.LatStr.Should().Be("7.062000");
            vm.LonStr.Should().Be("-73.854000");
            vm.Descripcion.Should().Be("Punto de desembarque de equipos");

            vm.NombreEsValido.Should().BeTrue();
            vm.SubcarpetaSintaxisValida.Should().BeTrue();
            vm.MunicipioEsValido.Should().BeTrue();
            vm.CoordenadasValidas.Should().BeTrue();
            vm.PuedeGuardar.Should().BeTrue();
        }

        [Fact]
        public void Inicializacion_ModoCreacion_AsignaValoresPorDefectoYSugerencias()
        {
            var vm = new EditarSubproyectoViewModel(
                subproyecto: null,
                rutaProyecto: @"D:\Proyectos\Test",
                subcarpetasDisponibles: new[] { "Carpeta1" },
                municipiosDisponibles: _municipiosProyecto,
                todosMunicipiosCatalogo: _todosMunicipios,
                latProyectoStr: "7.123456",
                lonProyectoStr: "-73.654321");

            vm.EsNuevo.Should().BeTrue();
            vm.IdSubproyecto.Should().Be(0);
            vm.Titulo.Should().Contain("Nuevo Subproyecto");
            vm.Nombre.Should().BeEmpty();
            vm.MunicipioTexto.Should().Be("Barrancabermeja"); // Primer municipio sugerido
            vm.LatStr.Should().Be("7.123456"); // Coordenada heredada del proyecto
            vm.LonStr.Should().Be("-73.654321");
            vm.PuedeGuardar.Should().BeFalse(); // Nombre y subcarpeta aún vacíos
        }

        [Fact]
        public void ValidacionNombre_Vacio_BloqueaGuardado()
        {
            var sub = new SubproyectoItemFormulario
            {
                IdSubproyecto = 1,
                Nombre = "Sitio A",
                Subcarpeta = "SubA"
            };

            var vm = new EditarSubproyectoViewModel(sub, null, null, _municipiosProyecto, _todosMunicipios);
            vm.PuedeGuardar.Should().BeTrue();

            vm.Nombre = "   ";

            vm.NombreEsValido.Should().BeFalse();
            vm.NombreMensaje.Should().NotBeNullOrWhiteSpace();
            vm.PuedeGuardar.Should().BeFalse();
        }

        [Fact]
        public void ValidacionSubcarpeta_CaracteresInvalidos_BloqueaGuardado()
        {
            var sub = new SubproyectoItemFormulario
            {
                IdSubproyecto = 1,
                Nombre = "Sitio A",
                Subcarpeta = "SubA"
            };

            var vm = new EditarSubproyectoViewModel(sub, null, null, _municipiosProyecto, _todosMunicipios);

            vm.Subcarpeta = @"Carpeta*Con?Caracteres|Prohibidos";

            vm.SubcarpetaSintaxisValida.Should().BeFalse();
            vm.SubcarpetaEstadoIcono.Should().Be("❌");
            vm.PuedeGuardar.Should().BeFalse();
        }

        [Fact]
        public void ValidacionSubcarpeta_ExistenciaEnDisco_MarcaIconoSegunExistenciaFisica()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "Geomatica_Sub_Edit_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var subExistente = Path.Combine(tempDir, "CarpetaFisicaReal");
            Directory.CreateDirectory(subExistente);

            try
            {
                var sub = new SubproyectoItemFormulario
                {
                    IdSubproyecto = 1,
                    Nombre = "Sitio A",
                    Subcarpeta = "CarpetaFisicaReal"
                };

                var vm = new EditarSubproyectoViewModel(sub, tempDir, null, _municipiosProyecto, _todosMunicipios);

                // Carpeta que sí existe
                vm.SubcarpetaExisteEnDisco.Should().BeTrue();
                vm.SubcarpetaEstadoIcono.Should().Be("✅");
                vm.PuedeGuardar.Should().BeTrue();

                // Carpeta que no existe en disco pero tiene sintaxis válida
                vm.Subcarpeta = "CarpetaQueNoExisteAun";
                vm.SubcarpetaSintaxisValida.Should().BeTrue();
                vm.SubcarpetaExisteEnDisco.Should().BeFalse();
                vm.SubcarpetaEstadoIcono.Should().Be("⚠️");
                vm.PuedeGuardar.Should().BeTrue(); // No debe bloquear, solo advertir
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public void ValidacionMunicipio_ReconocidoEnDIVIPOLA_AsignaCodigoYMarcaValido()
        {
            var sub = new SubproyectoItemFormulario
            {
                IdSubproyecto = 1,
                Nombre = "Sitio A",
                Subcarpeta = "SubA"
            };

            var vm = new EditarSubproyectoViewModel(sub, null, null, _municipiosProyecto, _todosMunicipios);

            // Búsqueda por nombre sin tilde (slug / normalizado)
            vm.MunicipioTexto = "Bogota D.C.";

            vm.MunicipioEsValido.Should().BeTrue();
            vm.MunicipioEstadoIcono.Should().Be("✅");
            vm.MpioCdpmpDetectado.Should().Be("11001");
            vm.MunicipioNombreDetectado.Should().Be("Bogotá D.C.");
            vm.PuedeGuardar.Should().BeTrue();
        }

        [Fact]
        public void ValidacionMunicipio_NoReconocido_BloqueaGuardado()
        {
            var sub = new SubproyectoItemFormulario
            {
                IdSubproyecto = 1,
                Nombre = "Sitio A",
                Subcarpeta = "SubA"
            };

            var vm = new EditarSubproyectoViewModel(sub, null, null, _municipiosProyecto, _todosMunicipios);

            vm.MunicipioTexto = "MunicipioInexistenteDePrueba";

            vm.MunicipioEsValido.Should().BeFalse();
            vm.MunicipioEstadoIcono.Should().Be("❌");
            vm.PuedeGuardar.Should().BeFalse();
        }

        [Fact]
        public void ValidacionCoordenadas_ValoresInvalidos_BloqueaGuardado()
        {
            var sub = new SubproyectoItemFormulario
            {
                IdSubproyecto = 1,
                Nombre = "Sitio A",
                Subcarpeta = "SubA"
            };

            var vm = new EditarSubproyectoViewModel(sub, null, null, _municipiosProyecto, _todosMunicipios);

            vm.LatStr = "NoEsUnNumero";
            vm.LonStr = "-73.854000";

            vm.CoordenadasValidas.Should().BeFalse();
            vm.CoordenadasEstadoIcono.Should().Be("❌");
            vm.PuedeGuardar.Should().BeFalse();

            // Corregir a decimal válido
            vm.LatStr = "7.062000";
            vm.CoordenadasValidas.Should().BeTrue();
            vm.CoordenadasEstadoIcono.Should().Be("📍");
            vm.PuedeGuardar.Should().BeTrue();
        }

        [Fact]
        public void CopiarCoordenadasProyecto_AsignaLatYLongDelProyecto()
        {
            var sub = new SubproyectoItemFormulario
            {
                IdSubproyecto = 1,
                Nombre = "Sitio A",
                Subcarpeta = "SubA"
            };

            var vm = new EditarSubproyectoViewModel(
                sub,
                null,
                null,
                _municipiosProyecto,
                _todosMunicipios,
                latProyectoStr: "8.123456",
                lonProyectoStr: "-73.987654");

            vm.PuedeCopiarCoordenadas.Should().BeTrue();
            vm.CopiarCoordenadasProyectoCommand.Execute(null);

            vm.LatStr.Should().Be("8.123456");
            vm.LonStr.Should().Be("-73.987654");
            vm.CoordenadasValidas.Should().BeTrue();
        }

        [Fact]
        public void AplicarCambiosASubproyecto_ActualizaInstanciaDestinoYMarcaModificado()
        {
            var sub = new SubproyectoItemFormulario
            {
                IdSubproyecto = 10,
                IdProyecto = 3,
                Nombre = "Nombre Antiguo",
                Subcarpeta = "CarpetaAntigua",
                MpioCdpmp = "68081",
                MunicipioNombre = "Barrancabermeja",
                Lat = 7.0,
                Lon = -73.0,
                Descripcion = "Desc antigua"
            };

            var vm = new EditarSubproyectoViewModel(sub, null, null, _municipiosProyecto, _todosMunicipios);

            vm.Nombre = "Nombre Nuevo y Mejorado";
            vm.Subcarpeta = @"Datos_Espaciales\Sector_Nuevo";
            vm.MunicipioTexto = "Piedecuesta";
            vm.LatStr = "7.085000";
            vm.LonStr = "-73.125000";
            vm.Descripcion = "Nueva descripción técnica";

            bool closeFired = false;
            bool? closeResult = null;
            vm.RequestClose += (res) =>
            {
                closeFired = true;
                closeResult = res;
            };

            vm.GuardarCommand.Execute(null);

            closeFired.Should().BeTrue();
            closeResult.Should().BeTrue();

            sub.Nombre.Should().Be("Nombre Nuevo y Mejorado");
            sub.Subcarpeta.Should().Be(@"Datos_Espaciales\Sector_Nuevo");
            sub.MpioCdpmp.Should().Be("68547");
            sub.MunicipioNombre.Should().Be("Piedecuesta");
            sub.Lat.Should().BeApproximately(7.085, 0.0001);
            sub.Lon.Should().BeApproximately(-73.125, 0.0001);
            sub.Descripcion.Should().Be("Nueva descripción técnica");
            sub.EsModificado.Should().BeTrue();
        }

        [Fact]
        public void FormularioProyecto_EditarSubproyecto_ConHandler_SincronizaYActualizaPins()
        {
            var mockProy = new Mock<IProyectoRepository>();
            var mockMuni = new Mock<IMunicipioRepository>();
            var mockNotif = new Mock<INotificationService>();

            var vm = new CrearProyectoViewModel(
                mockProy.Object,
                mockMuni.Object,
                new ProyectoArchivosService(),
                navigateBack: () => { },
                notifications: mockNotif.Object);

            var sub = new SubproyectoItemFormulario
            {
                IdSubproyecto = 5,
                Nombre = "Subproyecto Original",
                Subcarpeta = "CarpetaOriginal"
            };
            vm.Subproyectos.Add(sub);

            bool pinsNotified = false;
            vm.SubproyectosPinsChanged += (lista) => pinsNotified = true;

            // Inyectar handler de edición modal
            vm.DialogoEditarSubproyectoHandler = (item) =>
            {
                item.Nombre = "Subproyecto Editado Desde Modal";
                item.Subcarpeta = "CarpetaEditada";
                return true; // Simula usuario pulsando "Guardar Cambios"
            };

            vm.EditarSubproyectoCommand.Execute(sub);

            sub.Nombre.Should().Be("Subproyecto Editado Desde Modal");
            sub.Subcarpeta.Should().Be("CarpetaEditada");
            sub.EsModificado.Should().BeTrue();
            pinsNotified.Should().BeTrue();
            mockNotif.Verify(n => n.ShowInfo(It.Is<string>(s => s.Contains("actualizado")), It.IsAny<string?>(), It.IsAny<int>()), Times.Once);
        }

        [Fact]
        public void FormularioProyecto_MostrarFormularioSubproyecto_ConHandler_AgregaNuevoSubproyectoYNotificaPins()
        {
            var mockProy = new Mock<IProyectoRepository>();
            var mockMuni = new Mock<IMunicipioRepository>();
            var mockNotif = new Mock<INotificationService>();

            var vm = new CrearProyectoViewModel(
                mockProy.Object,
                mockMuni.Object,
                new ProyectoArchivosService(),
                navigateBack: () => { },
                notifications: mockNotif.Object);

            bool pinsNotified = false;
            vm.SubproyectosPinsChanged += (lista) => pinsNotified = true;

            // Inyectar handler de creación modal
            vm.DialogoNuevoSubproyectoHandler = () =>
            {
                return new SubproyectoItemFormulario
                {
                    IdSubproyecto = 0,
                    Nombre = "Nuevo Frente Creado Desde Modal",
                    Subcarpeta = @"Datos_Espaciales\Frente_Nuevo",
                    Lat = 7.15,
                    Lon = -73.12,
                    EsModificado = true
                };
            };

            vm.MostrarFormularioSubproyectoCommand.Execute(null);

            vm.Subproyectos.Should().HaveCount(1);
            vm.HasSubproyectos.Should().BeTrue();
            vm.TotalSubproyectos.Should().Be(1);
            vm.Subproyectos[0].Nombre.Should().Be("Nuevo Frente Creado Desde Modal");
            vm.Subproyectos[0].Subcarpeta.Should().Be(@"Datos_Espaciales\Frente_Nuevo");
            pinsNotified.Should().BeTrue();
            mockNotif.Verify(n => n.ShowInfo(It.Is<string>(s => s.Contains("añadido")), It.IsAny<string?>(), It.IsAny<int>()), Times.Once);
        }

        [Fact]
        public void FormularioProyecto_MostrarFormularioSubproyecto_ConHandler_Cancelado_NoAgregaNada()
        {
            var mockProy = new Mock<IProyectoRepository>();
            var mockMuni = new Mock<IMunicipioRepository>();
            var mockNotif = new Mock<INotificationService>();

            var vm = new CrearProyectoViewModel(
                mockProy.Object,
                mockMuni.Object,
                new ProyectoArchivosService(),
                navigateBack: () => { },
                notifications: mockNotif.Object);

            // Handler que simula usuario cancelando la ventana emergente
            vm.DialogoNuevoSubproyectoHandler = () => null;

            vm.MostrarFormularioSubproyectoCommand.Execute(null);

            vm.Subproyectos.Should().BeEmpty();
            vm.HasSubproyectos.Should().BeFalse();
            vm.TotalSubproyectos.Should().Be(0);
        }
    }
}

