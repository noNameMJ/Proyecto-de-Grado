using FluentAssertions;
using Geomatica.AppCore.UseCases;
using Geomatica.Desktop.Services;
using Geomatica.Desktop.ViewModels;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Geomatica.UnitTests.ViewModels;

public class ImportarSubproyectosCsvTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string CrearArchivoTemporal(string contenido, Encoding? encoding = null)
    {
        string path = Path.Combine(Path.GetTempPath(), $"subproy_test_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, contenido, encoding ?? Encoding.UTF8);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Limpieza silenciosa de archivos temporales
            }
        }
    }

    #region 1. Pruebas de Parseo de Coordenadas

    [Theory]
    [InlineData("7.0654", 7.0654)]
    [InlineData("7,0654", 7.0654)]
    [InlineData("-73.8542", -73.8542)]
    [InlineData("-73,8542", -73.8542)]
    [InlineData("73.8542 W", -73.8542)]
    [InlineData("73,8542W", -73.8542)]
    [InlineData("73.8542 O", -73.8542)]
    [InlineData("73.8542O", -73.8542)]
    [InlineData("7.0654 N", 7.0654)]
    [InlineData("7.0654°", 7.0654)]
    [InlineData("73.8542° W", -73.8542)]
    [InlineData("4.1234 S", -4.1234)]
    public void TryParseCoordenada_ValoresValidos_DebeRetornarVerdaderoYValorCorrecto(string input, double esperado)
    {
        bool ok = SubproyectoCsvPreviewItem.TryParseCoordenada(input, out double valor);

        ok.Should().BeTrue();
        Math.Abs(valor - esperado).Should().BeLessThan(0.0001);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("coordenada")]
    [InlineData("--73.5")]
    public void TryParseCoordenada_ValoresInvalidos_DebeRetornarFalso(string? input)
    {
        bool ok = SubproyectoCsvPreviewItem.TryParseCoordenada(input, out double valor);

        ok.Should().BeFalse();
        valor.Should().Be(0);
    }

    #endregion

    #region 2. Pruebas de Normalización de Slug de Subcarpetas

    [Theory]
    [InlineData("Embarcadero El Llanito", "embarcadero_el_llanito")]
    [InlineData("Muelle #1 - Rampa Norte (Fluvial)", "muelle_1_rampa_norte_fluvial")]
    [InlineData("Estación Puerto Boyacá", "estacion_puerto_boyaca")]
    [InlineData("  Espacios   Múltiples  ", "espacios_multiples")]
    [InlineData("ÁÉÍÓÚ_áéíóú_ñÑ", "aeiou_aeiou_nn")]
    public void GenerarSlug_TextoConAcentosYSimbolos_DebeGenerarSlugLimpio(string input, string esperado)
    {
        string slug = ImportarSubproyectosCsvViewModel.GenerarSlug(input);

        slug.Should().Be(esperado);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void GenerarSlug_TextoVacio_DebeRetornarVacio(string? input)
    {
        string slug = ImportarSubproyectosCsvViewModel.GenerarSlug(input!);

        slug.Should().BeEmpty();
    }

    #endregion

    #region 3. Pruebas de Parseo RFC 4180 de Líneas CSV

    [Fact]
    public void ParseCsvLine_LineaSimplePuntoYComa_DebeDividirCampos()
    {
        string linea = "Embarcadero 1;carpeta_1;7.06;-73.85;Barrancabermeja;Detalle";

        var campos = ImportarSubproyectosCsvViewModel.ParseCsvLine(linea, ';');

        campos.Should().HaveCount(6);
        campos[0].Should().Be("Embarcadero 1");
        campos[1].Should().Be("carpeta_1");
        campos[2].Should().Be("7.06");
        campos[3].Should().Be("-73.85");
        campos[4].Should().Be("Barrancabermeja");
        campos[5].Should().Be("Detalle");
    }

    [Fact]
    public void ParseCsvLine_ConCamposEntreComillasYDelimitadoresInternos_DebeRespetarComillas()
    {
        string linea = "Muelle Central,\"Zona A; Rampa 2, Muelle Fluvial\",7.06,-73.85";

        var campos = ImportarSubproyectosCsvViewModel.ParseCsvLine(linea, ',');

        campos.Should().HaveCount(4);
        campos[0].Should().Be("Muelle Central");
        campos[1].Should().Be("Zona A; Rampa 2, Muelle Fluvial");
        campos[2].Should().Be("7.06");
        campos[3].Should().Be("-73.85");
    }

    [Fact]
    public void ParseCsvLine_ConComillasDoblesEscapadas_DebeDesescaparComillas()
    {
        string linea = "\"Muelle \"\"Norte\"\"\";directorio;7.06";

        var campos = ImportarSubproyectosCsvViewModel.ParseCsvLine(linea, ';');

        campos.Should().HaveCount(3);
        campos[0].Should().Be("Muelle \"Norte\"");
        campos[1].Should().Be("directorio");
        campos[2].Should().Be("7.06");
    }

    #endregion

    #region 4. Detección de Delimitador

    [Fact]
    public void DetectarDelimitador_ArchivoConPuntoYComa_DebeDetectarPuntoYComa()
    {
        string path = CrearArchivoTemporal("Nombre;Subcarpeta;Lat;Lon\r\nSitio 1;sitio_1;7.0;-73.8\r\nSitio 2;sitio_2;7.1;-73.9\r\n");

        string sep = ImportarSubproyectosCsvViewModel.DetectarDelimitador(path);

        sep.Should().Be(";");
    }

    [Fact]
    public void DetectarDelimitador_ArchivoConComas_DebeDetectarComa()
    {
        string path = CrearArchivoTemporal("Nombre,Subcarpeta,Lat,Lon\r\nSitio 1,sitio_1,7.0,-73.8\r\nSitio 2,sitio_2,7.1,-73.9\r\n");

        string sep = ImportarSubproyectosCsvViewModel.DetectarDelimitador(path);

        sep.Should().Be(",");
    }

    #endregion

    #region 5. Auto-Mapeo de Columnas y Lectura de Archivo

    [Fact]
    public void CargarArchivo_ConEncabezadosEstandar_DebeMapearAutomaticamenteYGenerarPreview()
    {
        string csv = @"Nombre;Subcarpeta;Latitud;Longitud;Municipio;Descripcion
Embarcadero El Llanito;embarcadero_llanito;7.0654;-73.8542;Barrancabermeja;Muelle de acceso
Rampa Yondó;rampa_yondo;7.0812;-73.9015;Yondó;Rampa vehicular";

        string path = CrearArchivoTemporal(csv);

        var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 10);
        vm.CargarArchivo(path);

        vm.Delimitador.Should().Be(";");
        vm.ColumnaNombre.Should().Be("Nombre");
        vm.ColumnaSubcarpeta.Should().Be("Subcarpeta");
        vm.ColumnaLatitud.Should().Be("Latitud");
        vm.ColumnaLongitud.Should().Be("Longitud");
        vm.ColumnaMunicipio.Should().Be("Municipio");
        vm.ColumnaDescripcion.Should().Be("Descripcion");

        vm.TotalFilas.Should().Be(2);
        vm.TotalValidas.Should().Be(2);
        vm.PuedeAceptar.Should().BeTrue();

        var fila1 = vm.FilasPreview[0];
        fila1.Nombre.Should().Be("Embarcadero El Llanito");
        fila1.Subcarpeta.Should().Be("embarcadero_llanito");
        fila1.Lat.Should().BeApproximately(7.0654, 0.0001);
        fila1.Lon.Should().BeApproximately(-73.8542, 0.0001);
        fila1.Municipio.Should().Be("Barrancabermeja");
        fila1.Descripcion.Should().Be("Muelle de acceso");
        fila1.EsValidoParaImportar.Should().BeTrue();
        fila1.EstadoIcono.Should().Be("✅");
    }

    [Fact]
    public void CargarArchivo_ConSinonimosEnEncabezados_DebeAutoMapearCorrectamente()
    {
        string csv = @"Frente,Folder,Y,X,Ciudad,Observaciones
Frente 1,frente_01,7.05,-73.80,Barrancabermeja,Primera etapa";

        string path = CrearArchivoTemporal(csv);

        var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 10);
        vm.CargarArchivo(path);

        vm.ColumnaNombre.Should().Be("Frente");
        vm.ColumnaSubcarpeta.Should().Be("Folder");
        vm.ColumnaLatitud.Should().Be("Y");
        vm.ColumnaLongitud.Should().Be("X");
        vm.ColumnaMunicipio.Should().Be("Ciudad");
        vm.ColumnaDescripcion.Should().Be("Observaciones");

        vm.TotalFilas.Should().Be(1);
        vm.FilasPreview[0].Nombre.Should().Be("Frente 1");
        vm.FilasPreview[0].Subcarpeta.Should().Be("frente_01");
    }

    [Fact]
    public void CargarArchivo_SinColumnaSubcarpeta_DebeGenerarSlugDerivadoDelNombre()
    {
        string csv = @"Nombre;Lat;Lon
Muelle San Silvestre;7.0722;-73.8611";

        string path = CrearArchivoTemporal(csv);

        var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 10);
        vm.CargarArchivo(path);

        vm.TotalFilas.Should().Be(1);
        var fila = vm.FilasPreview[0];
        fila.Nombre.Should().Be("Muelle San Silvestre");
        fila.Subcarpeta.Should().Be("muelle_san_silvestre");
    }

    #endregion

    #region 6. Validación de Filas y Comandos de Selección

    [Fact]
    public void ValidarFila_SinNombre_DebeMarcarseInvalidaParaImportar()
    {
        var item = new SubproyectoCsvPreviewItem
        {
            NumeroFila = 1,
            Nombre = "   ",
            LatStr = "7.0",
            LonStr = "-73.8"
        };

        item.Validar();

        item.EsValidoParaImportar.Should().BeFalse();
        item.EstadoIcono.Should().Be("❌");
        item.EstadoTexto.Should().Contain("Falta nombre");
    }

    [Fact]
    public void ValidarFila_SinCoordenadas_DebeSerValidaConAdvertencia()
    {
        var item = new SubproyectoCsvPreviewItem
        {
            NumeroFila = 1,
            Nombre = "Punto Sin Coordenadas",
            LatStr = "",
            LonStr = ""
        };

        item.Validar();

        item.EsValidoParaImportar.Should().BeTrue();
        item.TieneCoordenadasValidas.Should().BeFalse();
        item.EstadoIcono.Should().Be("⚠️");
        item.EstadoTexto.Should().Contain("Sin coordenadas");
    }

    [Fact]
    public void SeleccionarYDeseleccionarTodas_DebeAfectarTotalValidas()
    {
        string csv = @"Nombre;Lat;Lon
Sitio 1;7.0;-73.8
Sitio 2;7.1;-73.9";

        string path = CrearArchivoTemporal(csv);

        var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 10);
        vm.CargarArchivo(path);

        vm.TotalValidas.Should().Be(2);

        // Deseleccionar todas
        vm.DeseleccionarTodasCommand.Execute(null);
        vm.TotalValidas.Should().Be(0);
        vm.PuedeAceptar.Should().BeFalse();

        // Seleccionar todas
        vm.SeleccionarTodasCommand.Execute(null);
        vm.TotalValidas.Should().Be(2);
        vm.PuedeAceptar.Should().BeTrue();
    }

    [Fact]
    public void ObtenerSubproyectosAprobados_DebeRetornarSoloFilasSeleccionadasYValidas()
    {
        string csv = @"Nombre;Subcarpeta;Lat;Lon;Descripcion
Sitio A;carpeta_a;7.0;-73.8;Detalle A
Sitio B;carpeta_b;7.1;-73.9;Detalle B
;carpeta_c;7.2;-73.7;Fila sin nombre";

        string path = CrearArchivoTemporal(csv);

        var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 105);
        vm.CargarArchivo(path);

        vm.TotalFilas.Should().Be(3);
        // Excluir la fila B manualmente
        vm.FilasPreview[1].Incluir = false;

        var aprobados = vm.ObtenerSubproyectosAprobados();

        // Debe incluir únicamente la Fila A (Fila B excluida, Fila C sin nombre)
        aprobados.Should().HaveCount(1);
        aprobados[0].Nombre.Should().Be("Sitio A");
        aprobados[0].IdProyecto.Should().Be(105);
        aprobados[0].Subcarpeta.Should().Be("carpeta_a");
        aprobados[0].Descripcion.Should().Be("Detalle A");
        aprobados[0].EsModificado.Should().BeTrue();
    }

    #endregion

    #region 7. Integración con FormularioProyectoViewModel

    [Fact]
    public void FormularioProyecto_ImportarSubproyectosCsvCommand_AgregaSubproyectosImportados()
    {
        // Arrange
        var mockProy = new Mock<IProyectoRepository>();
        var mockMuni = new Mock<IMunicipioRepository>();
        var mockNotif = new Mock<INotificationService>();

        mockMuni.Setup(m => m.ListarDepartamentosAsync())
            .ReturnsAsync(new List<DepartamentoDto> { new("68", "Santander") });

        var crearUseCase = new CrearProyectoUseCase(mockProy.Object);
        var guardarSubUseCase = new GuardarSubproyectoUseCase(mockProy.Object);
        var obtenerSubUseCase = new ObtenerSubproyectosUseCase(mockProy.Object);
        var eliminarSubUseCase = new EliminarSubproyectoUseCase(mockProy.Object);

        var vm = new FormularioProyectoViewModel(
            crearUseCase,
            mockMuni.Object,
            new ProyectoArchivosService(),
            navigateBack: () => { },
            onProyectoCreado: null,
            notifications: mockNotif.Object,
            guardarSubproyectoUseCase: guardarSubUseCase,
            obtenerSubproyectosUseCase: obtenerSubUseCase,
            eliminarSubproyectoUseCase: eliminarSubUseCase);

        string csvPath = CrearArchivoTemporal("Nombre;Subcarpeta;Lat;Lon\r\nSitio 1;s1;7.0;-73.8\r\n");

        // Simular selección de archivo y diálogo de importación
        vm.SelectorArchivoCsvHandler = () => csvPath;
        vm.DialogoImportacionCsvHandler = (path) => new List<SubproyectoItemFormulario>
        {
            new()
            {
                Nombre = "Embarcadero Importado 1",
                Subcarpeta = "embarcadero_01",
                Lon = -73.85,
                Lat = 7.06,
                MunicipioNombre = "Barrancabermeja"
            },
            new()
            {
                Nombre = "Embarcadero Importado 2",
                Subcarpeta = "embarcadero_02",
                Lon = -73.86,
                Lat = 7.07,
                MunicipioNombre = "Barrancabermeja"
            }
        };

        vm.Subproyectos.Should().BeEmpty();

        // Act
        vm.ImportarSubproyectosCsvCommand.Execute(null);

        // Assert
        vm.Subproyectos.Should().HaveCount(2);
        vm.Subproyectos[0].Nombre.Should().Be("Embarcadero Importado 1");
        vm.Subproyectos[1].Nombre.Should().Be("Embarcadero Importado 2");
        vm.HasSubproyectos.Should().BeTrue();
        vm.TotalSubproyectos.Should().Be(2);

        mockNotif.Verify(n => n.ShowSuccess(
            It.Is<string>(s => s.Contains("2 subproyectos")),
            It.IsAny<string?>(),
            It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void FormularioProyecto_ImportarSubproyectosCsvCommand_Cancelacion_NoAgregaSubproyectos()
    {
        // Arrange
        var mockProy = new Mock<IProyectoRepository>();
        var mockMuni = new Mock<IMunicipioRepository>();

        mockMuni.Setup(m => m.ListarDepartamentosAsync())
            .ReturnsAsync(new List<DepartamentoDto> { new("68", "Santander") });

        var crearUseCase = new CrearProyectoUseCase(mockProy.Object);
        var vm = new FormularioProyectoViewModel(
            crearUseCase,
            mockMuni.Object,
            new ProyectoArchivosService(),
            navigateBack: () => { });

        // Simular que el usuario cancela el diálogo de selección de archivo
        vm.SelectorArchivoCsvHandler = () => null;

        // Act
        vm.ImportarSubproyectosCsvCommand.Execute(null);

        // Assert
        vm.Subproyectos.Should().BeEmpty();
    }

    [Fact]
    public void CargarArchivo_SinFilaDeEncabezados_DetectaAutomaticamenteDatosYCoordenadas()
    {
        // Formato idéntico a Libro1.csv (sin encabezados, punto y coma, coma decimal, 5 columnas)
        string csv = "4,778882;-74,764139;TOLIMA;AMBALEMA;\"4,778882;-74,764139;AMBALEMA;TOLIMA\"\r\n" +
                     "5,544154;-72,905820;BOYACA;AQUITANIA;\"5,544154;-72,905820;AQUITANIA;BOYACA\"\r\n";

        string path = CrearArchivoTemporal(csv);

        var munis = new List<FormularioProyectoViewModel.MunicipioItem>
        {
            new("73032", "AMBALEMA"),
            new("15047", "AQUITANIA")
        };
        var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 1, municipiosDisponibles: munis);
        vm.CargarArchivo(path);

        vm.TotalFilas.Should().Be(2);
        vm.TotalValidas.Should().Be(2);
        vm.PuedeAceptar.Should().BeTrue();

        vm.ColumnaLatitud.Should().Be("Columna 1");
        vm.ColumnaLongitud.Should().Be("Columna 2");
        vm.ColumnaNombre.Should().Be("Columna 4");

        var fila1 = vm.FilasPreview[0];
        fila1.Lat.Should().BeApproximately(4.778882, 0.0001);
        fila1.Lon.Should().BeApproximately(-74.764139, 0.0001);
        fila1.Nombre.Should().Be("AMBALEMA");
        fila1.Subcarpeta.Should().Be("ambalema");
    }

    [Fact]
    public void SubcarpetaPredeterminada_Y_CrearSubcarpetaPorNombre_AplicaRutaCombinadaAFilas()
    {
        string csv = "Nombre;Lat;Lon\r\n" +
                     "Sitio Alfa;4.7788;-74.7641\r\n" +
                     "Sitio Beta;5.5441;-72.9058\r\n";

        string path = CrearArchivoTemporal(csv);
        var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 10);
        vm.CargarArchivo(path);

        // Sin subcarpeta predeterminada, usa los slugs
        vm.FilasPreview[0].Subcarpeta.Should().Be("sitio_alfa");
        vm.FilasPreview[1].Subcarpeta.Should().Be("sitio_beta");

        // Asignar subcarpeta predeterminada con CrearSubcarpetaPorNombre = true
        vm.SubcarpetaPredeterminada = "Datos_Espaciales/Embarcaderos";
        vm.FilasPreview[0].Subcarpeta.Should().Be("Datos_Espaciales/Embarcaderos/sitio_alfa");
        vm.FilasPreview[1].Subcarpeta.Should().Be("Datos_Espaciales/Embarcaderos/sitio_beta");

        // Cambiar a CrearSubcarpetaPorNombre = false
        vm.CrearSubcarpetaPorNombre = false;
        vm.FilasPreview[0].Subcarpeta.Should().Be("Datos_Espaciales/Embarcaderos");
        vm.FilasPreview[1].Subcarpeta.Should().Be("Datos_Espaciales/Embarcaderos");
    }

    [Fact]
    public void SeleccionarCarpetaBaseCommand_ConRutaProyecto_CalculaSubcarpetaRelativa()
    {
        string baseDir = OperatingSystem.IsWindows() ? @"C:\Proyectos\Proyecto_A" : "/proyectos/proyecto_a";
        string subDir = Path.Combine(baseDir, "Levantamientos", "Frente_1");

        var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 10, rutaProyecto: baseDir);
        vm.SelectorCarpetaDialogHandler = () => subDir;

        vm.SeleccionarCarpetaBaseCommand.Execute(null);

        vm.SubcarpetaPredeterminada.Should().Be("Levantamientos/Frente_1");
        vm.SubcarpetasDisponibles.Should().Contain("Levantamientos/Frente_1");
    }

    [Fact]
    public void SeleccionarCarpetaFilaCommand_PermiteAsignarSubcarpetaIndividual()
    {
        string baseDir = OperatingSystem.IsWindows() ? @"C:\Proyectos\Proyecto_A" : "/proyectos/proyecto_a";
        string subDir = Path.Combine(baseDir, "Especifica", "Punto_1");

        string csv = "Nombre;Lat;Lon\r\nSitio 1;4.7788;-74.7641\r\n";
        string path = CrearArchivoTemporal(csv);

        var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 10, rutaProyecto: baseDir);
        vm.CargarArchivo(path);
        vm.SelectorCarpetaDialogHandler = () => subDir;

        var fila = vm.FilasPreview[0];
        vm.SeleccionarCarpetaFilaCommand.Execute(fila);

        fila.Subcarpeta.Should().Be("Especifica/Punto_1");
    }

    [Fact]
    public void CsvIncompleto_SinSubcarpetaNiNombre_GeneraFallbacksYPermiteImportar()
    {
        // CSV que solo tiene coordenadas (sin nombre, sin subcarpeta)
        string csv = "4.7788;-74.7641\r\n" +
                     "5.5441;-72.9058\r\n";
        string path = CrearArchivoTemporal(csv);

        var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 5);
        vm.CargarArchivo(path);

        vm.TotalFilas.Should().Be(2);
        vm.TotalValidas.Should().Be(2);
        vm.PuedeAceptar.Should().BeTrue();

        var aprobados = vm.ObtenerSubproyectosAprobados();
        aprobados.Should().HaveCount(2);

        aprobados[0].Nombre.Should().NotBeNullOrWhiteSpace();
        aprobados[0].Subcarpeta.Should().NotBeNullOrWhiteSpace();
        aprobados[0].Lat.Should().BeApproximately(4.7788, 0.0001);
        aprobados[0].Lon.Should().BeApproximately(-74.7641, 0.0001);

        aprobados[1].Nombre.Should().NotBeNullOrWhiteSpace();
        aprobados[1].Subcarpeta.Should().NotBeNullOrWhiteSpace();
        aprobados[1].Lat.Should().BeApproximately(5.5441, 0.0001);
        aprobados[1].Lon.Should().BeApproximately(-72.9058, 0.0001);
    }

    [Fact]
    public void EdicionManualDeNombre_EnFilaInvalida_ActualizaTotalesYPermiteAceptar()
    {
        // CSV donde una fila no tiene nombre asignado
        string csv = "Nombre;Lat;Lon\r\n" +
                     ";4.7788;-74.7641\r\n";
        string path = CrearArchivoTemporal(csv);

        var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 5);
        vm.CargarArchivo(path);

        var fila = vm.FilasPreview[0];
        fila.Nombre.Should().BeEmpty();
        fila.EsValidoParaImportar.Should().BeFalse();
        vm.TotalValidas.Should().Be(0);
        vm.PuedeAceptar.Should().BeFalse();

        // Usuario escribe el nombre en la vista
        fila.Nombre = "Punto Corregido";

        fila.EsValidoParaImportar.Should().BeTrue();
        vm.TotalValidas.Should().Be(1);
        vm.PuedeAceptar.Should().BeTrue();
    }

    #endregion

    #region 8. Pruebas de Editor Tabular, Validación de Municipio y Subcarpeta

    [Fact]
    public void CargarSubproyectosExistentes_CargaFilasYPreservaIds()
    {
        var subs = new List<SubproyectoItemFormulario>
        {
            new() { IdSubproyecto = 101, IdProyecto = 5, Nombre = "Sitio Existente 1", Subcarpeta = "frente_1", Lat = 7.0, Lon = -73.0, MunicipioNombre = "AMBALEMA", MpioCdpmp = "73032" },
            new() { IdSubproyecto = 102, IdProyecto = 5, Nombre = "Sitio Existente 2", Subcarpeta = "frente_2", Lat = 7.1, Lon = -73.1, MunicipioNombre = "HONDA", MpioCdpmp = "73349" }
        };

        var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 5);
        vm.CargarSubproyectosExistentes(subs);

        vm.TotalFilas.Should().Be(2);
        vm.TotalValidas.Should().Be(2);
        vm.MostrarMapeoCsv.Should().BeFalse();

        var fila1 = vm.FilasPreview[0];
        fila1.IdSubproyecto.Should().Be(101);
        fila1.Nombre.Should().Be("Sitio Existente 1");
        fila1.Subcarpeta.Should().Be("frente_1");

        var aprobados = vm.ObtenerSubproyectosAprobados();
        aprobados.Should().HaveCount(2);
        aprobados[0].IdSubproyecto.Should().Be(101);
        aprobados[1].IdSubproyecto.Should().Be(102);
    }

    [Fact]
    public void AgregarNuevaFila_AnadeFilaEditableConValoresPorDefecto()
    {
        var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 8);
        vm.SubcarpetaPredeterminada = "Obras/Fluvial";
        vm.CrearSubcarpetaPorNombre = true;

        vm.TotalFilas.Should().Be(0);

        vm.AgregarFilaCommand.Execute(null);

        vm.TotalFilas.Should().Be(1);
        var nueva = vm.FilasPreview[0];
        nueva.NumeroFila.Should().Be(1);
        nueva.Nombre.Should().Be("Sitio 1");
        nueva.Subcarpeta.Should().Be("Obras/Fluvial/sitio_1");
        nueva.IdSubproyecto.Should().Be(0); // Es nuevo
        nueva.EsValidoParaImportar.Should().BeTrue();
    }

    [Fact]
    public void EliminarFila_RemueveFilaYRenumeracion()
    {
        var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 8);
        vm.AgregarFilaCommand.Execute(null);
        vm.AgregarFilaCommand.Execute(null);
        vm.AgregarFilaCommand.Execute(null);

        vm.TotalFilas.Should().Be(3);
        vm.FilasPreview[1].NumeroFila.Should().Be(2);

        // Eliminar fila 2
        var fila2 = vm.FilasPreview[1];
        vm.EliminarFilaCommand.Execute(fila2);

        vm.TotalFilas.Should().Be(2);
        vm.FilasPreview[0].NumeroFila.Should().Be(1);
        vm.FilasPreview[1].NumeroFila.Should().Be(2);
    }

    [Fact]
    public void ValidacionMunicipio_TextoValido_AsignaCodigoYMarcaValido()
    {
        var munis = new List<FormularioProyectoViewModel.MunicipioItem>
        {
            new("73032", "AMBALEMA"),
            new("68001", "BUCARAMANGA")
        };

        var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 1, municipiosDisponibles: munis);

        // Validación de municipio por nombre
        var res1 = vm.ValidarMunicipioTexto("ambalema");
        res1.Valido.Should().BeTrue();
        res1.Codigo.Should().Be("73032");

        // Validación de municipio por código
        var res2 = vm.ValidarMunicipioTexto("68001");
        res2.Valido.Should().BeTrue();
        res2.Codigo.Should().Be("68001");
    }

    [Fact]
    public void ValidacionMunicipio_TextoInvalido_MarcaInvalidoYBloqueaAprobacion()
    {
        var munis = new List<FormularioProyectoViewModel.MunicipioItem>
        {
            new("73032", "AMBALEMA")
        };

        var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 1, municipiosDisponibles: munis);

        var res = vm.ValidarMunicipioTexto("MunicipioNoExiste123");
        res.Valido.Should().BeFalse();
        res.Codigo.Should().BeNull();

        // En una fila concreta
        var fila = new SubproyectoCsvPreviewItem
        {
            Nombre = "Punto Test",
            Subcarpeta = "frente_test",
            Municipio = "MunicipioNoExiste123",
            ValidadorMunicipio = vm.ValidarMunicipioTexto,
            ValidadorSubcarpeta = vm.ValidarSubcarpetaTexto
        };
        fila.Validar();

        fila.MunicipioEsValido.Should().BeFalse();
        fila.MunicipioEstadoIcono.Should().Be("❌");
        fila.EsValidoParaImportar.Should().BeFalse();
    }

    [Fact]
    public void ValidacionSubcarpeta_CaracteresInvalidos_MarcaError()
    {
        var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 1);

        var res = vm.ValidarSubcarpetaTexto("Carpeta*Con?Caracteres<Invalidos>");
        res.SintaxisValida.Should().BeFalse();

        var fila = new SubproyectoCsvPreviewItem
        {
            Nombre = "Punto Valido",
            Subcarpeta = "Ruta:Invalida*?",
            ValidadorSubcarpeta = vm.ValidarSubcarpetaTexto
        };
        fila.Validar();

        fila.SubcarpetaSintaxisValida.Should().BeFalse();
        fila.SubcarpetaEstadoIcono.Should().Be("❌");
        fila.EsValidoParaImportar.Should().BeFalse();
    }

    [Fact]
    public void ValidacionSubcarpeta_ExistenciaEnDisco_MarcaAdvertenciaSiNoExiste()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"geomatica_test_proj_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string subReal = Path.Combine(tempDir, "Sub_Existente");
        Directory.CreateDirectory(subReal);

        try
        {
            var vm = new ImportarSubproyectosCsvViewModel(idProyecto: 1, rutaProyecto: tempDir);

            // Subcarpeta existente
            var resExiste = vm.ValidarSubcarpetaTexto("Sub_Existente");
            resExiste.SintaxisValida.Should().BeTrue();
            resExiste.ExisteEnDisco.Should().BeTrue();

            // Subcarpeta inexistente (no existe en disco pero sintaxis es válida)
            var resNoExiste = vm.ValidarSubcarpetaTexto("Sub_Fantasma");
            resNoExiste.SintaxisValida.Should().BeTrue();
            resNoExiste.ExisteEnDisco.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FormularioProyecto_AbrirEditorTablaSubproyectos_SincronizaCambiosYDetectaEliminados()
    {
        var mockProy = new Mock<IProyectoRepository>();
        var mockMuni = new Mock<IMunicipioRepository>();
        var crearUseCase = new CrearProyectoUseCase(mockProy.Object);
        var vm = new FormularioProyectoViewModel(
            crearUseCase,
            mockMuni.Object,
            new ProyectoArchivosService(),
            navigateBack: () => { });

        // Estado inicial: 2 subproyectos existentes con ID de base de datos
        vm.Subproyectos.Add(new SubproyectoItemFormulario { IdSubproyecto = 201, Nombre = "Sub 1", Subcarpeta = "sub_1" });
        vm.Subproyectos.Add(new SubproyectoItemFormulario { IdSubproyecto = 202, Nombre = "Sub 2", Subcarpeta = "sub_2" });

        // Simular que el usuario elimina Sub 2, modifica Sub 1 y agrega Sub 3 en la tabla
        vm.DialogoEditorTablaHandler = existentes => new List<SubproyectoItemFormulario>
        {
            new() { IdSubproyecto = 201, Nombre = "Sub 1 Modificado", Subcarpeta = "sub_1_mod" },
            new() { IdSubproyecto = 0, Nombre = "Sub 3 Nuevo", Subcarpeta = "sub_3" }
        };

        // Act
        vm.AbrirEditorTablaSubproyectosCommand.Execute(null);

        // Assert
        vm.Subproyectos.Should().HaveCount(2);
        vm.Subproyectos[0].IdSubproyecto.Should().Be(201);
        vm.Subproyectos[0].Nombre.Should().Be("Sub 1 Modificado");
        vm.Subproyectos[1].IdSubproyecto.Should().Be(0);
        vm.Subproyectos[1].Nombre.Should().Be("Sub 3 Nuevo");
    }

    #endregion
}
