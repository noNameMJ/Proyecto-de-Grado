# Geomatica.Desktop

## Arquitectura del Sistema
El proyecto emplea una Arquitectura en Capas (N-Tier Architecture), integrando conceptos de Clean Architecture. Su estructura principal está dividida en cuatro subproyectos clave, lo cual facilita la separación de responsabilidades:

- **Geomatica.Domain (Capa de Dominio)**: Contiene la lógica de negocio pura, modelos, entidades y abstracciones sin dependencias externas o de infraestructura.
- **Geomatica.Data (Capa de Infraestructura/Datos)**: Aloja la implementación de acceso a datos, repositorios concretos y la comunicación con bases de datos. Referencia a la capa de dominio.
- **Geomatica.AppCore (Capa de Aplicación o Business Logic)**: Orquesta la lógica del negocio mediante casos de uso, servicios e interfaces de repositorios. Actúa como mediador entre la capa de presentación (Desktop) y el Dominio/Data.
- **Geomatica.Desktop (Capa de Presentación)**: Implementa la interfaz de usuario para Windows. Utiliza el patrón **MVVM** (Model-View-ViewModel) para separar estrictamente la lógica de la UI de su presentación y enlazar datos. Emplea la librería `CommunityToolkit.Mvvm` para simplificar la implementación de MVVM.

## Flujo de Datos Principal
El ciclo de vida de una petición o de los datos dentro del sistema típicamente sigue esta secuencia:

1. **Ingreso desde la UI**: El usuario interactúa con la interfaz gráfica construida en WPF (`Geomatica.Desktop`). La Interfaz de Usuario emite comandos (Commands) o notifica cambios a través del `ViewModel`.
2. **Procesamiento en ViewModel**: El `ViewModel` en `Geomatica.Desktop` recibe el mando y se comunica con un servicio en `Geomatica.AppCore`.  
3. **Orquestación en la Capa de Aplicación**: En `Geomatica.AppCore`, los servicios de negocio procesan los requerimientos invocando abstracciones del dominio y estructuran las reglas. Si se necesita persistir u obtener datos, se invocan las interfaces de acceso a datos (por ejemplo, `IRepository`).
4. **Acceso a Datos**: La implementación del repositorio en `Geomatica.Data` toma la solicitud y traduce los comandos en consultas de Entity Framework o consultas SQL nativas a la base de datos correspondiente, leyendo o modificado registros físicos.
5. **Retorno de la Información**: La base de datos responde a la capa de datos. `Geomatica.Data` retorna modelos de dominio a `Geomatica.AppCore`, los cuales son devueltos, posiblemente transformados en Data Transfer Objects (DTOs), hacia `Geomatica.Desktop`. 
6. **Actualización de la UI**: El `ViewModel` actualiza sus propiedades, las cuales están ligadas mediante Binding (`INotifyPropertyChanged`), y la Vista de la UI notifica los cambios al usuario de inmediato de forma reactiva.

## Tecnologías, Frameworks y Dependencias Clave
- **.NET 8.0**: El runtime y SDK unificado que garantiza altísimo rendimiento, features C# modernos y dependencias estables.
- **WPF (Windows Presentation Foundation) & UseWPF**: Estándar para UI rica en Windows, usado para interfaces gráficas intensivas usando XAML.
- **CommunityToolkit.Mvvm**: Implementación de MVVM moderna de Microsoft, facilitando código conciso mediante generadores de código de C# (ObservableObjects, RelayCommands).
- **Esri.ArcGISRuntime.WPF & Esri.ArcGISRuntime.Toolkit.WPF** (v300.0.0): SDK potente para implementar capacidades de Sistemas de Información Geográfica (SIG / GIS), mapas ricos en 2D/3D y procesamiento espacial en la UI y backend.
- **GDAL** (v3.12.1): Librería crítica y estándar de la industria geospacial para leer y escribir transformaciones y traducciones de formatos raster y vectoriales cartográficos.
- **Microsoft.Extensions.DependencyInjection**: Abstracción e inyección modular de dependencias de Microsoft (Inversion of Control - IoC).
- **Microsoft.Web.WebView2**: Integración de Edge/Chromium para presentar aplicaciones web o mapas online dentro de la ventana de WPF.

## Contexto Semántico para Agentes IA
Optimizado para LLMs que interactúen con el proyecto:

- **Convenciones de Nombres y Estilo**: 
  - Archivos e Interfaces usando `PascalCase`. Variables privadas usan prefijo `_` con `camelCase`. Interfaces inician con la letra `I` (ej. `IUserRepository`).
  - Utilización extensiva de características modernas de C# (.NET 8): Nullable Reference Types (`<Nullable>enable</Nullable>`) e Immutable Types (Records) preferidos en Dominio/DTOs.
- **Lógica de Negocio y Data**: 
  - La lógica pura **siempre** reside en `Geomatica.Domain` y los orquestadores (servicios) residen en `Geomatica.AppCore`.
  - NUNCA introduzcas queries nativas o lógicas de negocio en los archivos ocultos (Code-Behind `.xaml.cs`) de la interfaz WPF en `Geomatica.Desktop`.
  - Las dependencias hacia capas de base de datos se abstraen mediante interfaces e Inyección de Dependencias.
- **Deuda Técnica Intencionada / Detalles Locales**:
  - El proyecto utiliza configuraciones flexibles como `appsettings.json` o Modelos Híbridos (Variables de Entorno y User Secrets). 
  - Considera el uso de `CommunityToolkit.Mvvm` limitando código "boiler-plate"; la IA debe sugerir preferentemente los atributos `[ObservableProperty]` y `[RelayCommand]` para ViewModels, en lugar de implementar `INotifyPropertyChanged` a mano.
  - La arquitectura utiliza una compilación condicional para Benchmarks (InternalsVisibleTo -> `BenchmarkSuite2`, `BenchmarkSuite3`), que la IA no debe modificar al realizar arreglos de negocio.

## Guía de Despliegue y Configuración de Entorno

### Entorno de Desarrollo (Local)
1. **Prerrequisitos**:
   - Visual Studio 2022 o IDE compatible (versiones 17.8+ para dar soporte correcto a .NET 8). Instalar la carga de trabajo de `.NET Desktop Development` (WPF/WinForms).
   - .NET 8.0 SDK instalado localmente.
2. **Clasificación y Restauración**:
   - Clonar el proyecto y abrir la solución principal en el IDE.
   - Restablecer (Restore) los paquetes NuGet configurados, dado que utiliza dependencias potentes (ArcGIS o GDAL).
   - Verificar si existen pre-requisitos nativos para GDAL (dependiendo del OS y CPU binario, a veces requerirá distribuciones de runtimes de C++ en Windows).
3. **Configuración Local (User Secrets & AppSettings)**:
   - Configurar los valores sensibles (cadenas de conexión o APIs keys) usando User Secrets de .NET vinculados a `9d9ff670-48ce-4e68-a981-f3c515fb8697`.
   - Modificar las variables locales definidas en `appsettings.json` de acuerdo a su entorno.
4. **Ejecución**: 
   - Seleccionar `Geomatica.Desktop` como el Proyecto de Inicio (Startup Project).
   - Iniciar en el modo de depuración (`F5` o `dotnet run`).

### Despliegue a Producción (Distribución WPF)
1. **Publicación y Compilación**:
   - Usar el comando `dotnet publish Geomatica.Desktop\Geomatica.Desktop.csproj -c Release -r win10-x64 --self-contained true` o el asistente visual de Visual Studio para empaquetar una aplicación independiente del runtime o con ClickOnce.
2. **Embalaje (Packaging) y Variables de Entorno**:
   - Para producción, inyectar los valores confidenciales mediante **Variables de Entorno del Sistema** en lugar del `appsettings.json` local.
3. **Distribución**:
   - Compartir el ejecutable compilado junto con las dependencias satálite originadas por GDAL o WebView2 y distribuir a los clientes correspondientes.