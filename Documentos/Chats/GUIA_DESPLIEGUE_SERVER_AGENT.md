# Guía de Despliegue y Configuración: Geomatica.ServerAgent

> **Agente de Auditoría de Archivos en Segundo Plano para Windows Server**  
> Proyecto de Grado - Escuela de Ingeniería de Sistemas e Informática / Grupo GEOMÁTICA UIS  
> Fecha de actualización: 29 de octubre de 2026

---

## 1. Propósito y Arquitectura

[`Geomatica.ServerAgent`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.ServerAgent/Geomatica.ServerAgent.csproj) es un servicio de Windows (.NET 8 Worker Service) diseñado para ejecutarse directamente en la máquina virtual o servidor físico de **Windows Server** donde residen los discos de almacenamiento de los proyectos y el Directorio Activo (Active Directory) de la UIS.

### Flujo de Funcionamiento
```text
[Ingeniero/Usuario] (AutoCAD, ArcGIS, Windows Explorer, etc.)
       │
       ▼ Guarda, edita, renombra o elimina archivo en recurso compartido SMB (\\10.x.x.x\pruebas)
[Windows Server - NTFS Local + Canal de Seguridad Windows] (G:\PRUEBAS\...)
       │
       ├──► Notificación física inmediata (FileSystemWatcher local en 0 ms)
       └──► Evento de Seguridad Windows (EventID 4663 / 4660 con SubjectUserName y AccessMask)
       │
       ▼ Correlación en memoria (_recentWriters vs _recentDeleters) y EventLogReader
[Geomatica.ServerAgent (FileAuditorWorker)]
       │ 1. Resuelve id_proyecto y ruta relativa normalizada (limpiando ADS :Zone.Identifier).
       │ 2. Atribuye el usuario autenticado mediante Eventos 4663/4660 (o "No identificado" si no hay evento).
       │ 3. Clasifica la operación: CREACION, MODIFICACION (tamaño/fecha), RENOMBRAR, MOVER o ELIMINAR.
       ▼ 4. Envía INSERT a PostgreSQL con poda automática FIFO (máx. 5 registros por archivo).
[VM PostgreSQL / PostGIS] (Tabla: geovisor.auditoria_archivo)
       │
       ▼ Consulta unificada instantánea
[Clientes de Escritorio: Geomatica.Desktop]
```

---

## 2. Ventajas del Agente en el Servidor

1. **Independiente de la app cliente:** El usuario no necesita tener abierta la aplicación de escritorio; cualquier cambio hecho desde el Explorador de Windows, scripts o software GIS/CAD queda registrado.
2. **Cero impacto de red para monitoreo:** El monitoreo se hace en el disco local NTFS del servidor, eliminando los desbordamientos de búfer (`InternalBufferOverflowException`) típicos de monitorear por red SMB.
3. **Identidad auditable y sin falsos culpables:** El usuario se obtiene directamente de los **Eventos de Seguridad de Windows (`4663` y `4660`)** generados por el kernel al validar la sesión de Active Directory (`DOMAIN\usuario`), separando estrictamente las cachés de escritura (`_recentWriters`) y eliminación (`_recentDeleters`) y evitando atribuir falsamente acciones al propietario NTFS o al creador original del archivo.
4. **Poda FIFO automática:** Mantiene el límite estricto de máximo 5 registros históricos por archivo para no inflar la base de datos.

---

## 3. Estructura de Archivos del Agente Publicado

Al publicar el proyecto para el servidor se generan los siguientes elementos en `Geomatica.Desktop/publish_server/`:

| Archivo | Descripción |
| :--- | :--- |
| `Geomatica.ServerAgent.exe` | Ejecutable auto-contenido (incluye el runtime de .NET 8; **no requiere instalar .NET en el servidor**). |
| `appsettings.json` | Archivo de configuración principal (cadena de conexión y mapeo de rutas). |
| `appsettings.Development.json` | Configuración opcional para depuración detallada. |

---

## 4. Configuración (`appsettings.json`)

Edita el archivo [`appsettings.json`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.ServerAgent/appsettings.json) en el servidor con los valores reales de tu infraestructura:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "ConnectionStrings": {
    "PostgresConnection": "Host=10.x.x.postgres;Port=5432;Database=geovisor;Username=postgres;Password=<REDACTED>"
  },
  "AgentSettings": {
    "IntervaloRecargaProyectosSegundos": 60,
    "TiempoDebounceMilisegundos": 1000,
    "MapeoRutas": {
      "\\\\10.x.x.x\\pruebas": "G:\\PRUEBAS",
      "\\\\10.x.x.x\\embarcaderos": "E:\\Shares\\Embarcaderos"
    }
  }
}
```

### Parámetros Explicados:
* **`PostgresConnection`**: Dirección IP de la máquina virtual donde corre PostgreSQL y credenciales de acceso.
* **`IntervaloRecargaProyectosSegundos`**: Cada cuántos segundos el agente consulta la tabla `geovisor.proyecto` para detectar si se crearon nuevos proyectos en la base de datos (por defecto: 60 s).
* **`TiempoDebounceMilisegundos`**: Ventana de espera para agrupar múltiples eventos de guardado rápido de software como Word/Excel/ArcGIS en una única entrada de auditoría (por defecto: 1000 ms).
* **`MapeoRutas`**: Diccionario que traduce la ruta de red registrada en el proyecto (`\\10.x.x.x\recurso`) a la unidad de disco física local del Windows Server (`G:\CARPETA`).

---

## 5. Procedimiento de Despliegue Paso a Paso

### Paso 1: Compilación Auto-Contenida
Desde la máquina de desarrollo, en la carpeta raíz del repositorio:
```powershell
dotnet publish Geomatica.Desktop\Geomatica.ServerAgent\Geomatica.ServerAgent.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o Geomatica.Desktop\publish_server
```

### Paso 2: Copiar al Windows Server
Copia el contenido de `Geomatica.Desktop\publish_server\` hacia una carpeta del servidor, por ejemplo:  
`C:\Servicios\GeomaticaServerAgent\`

### Paso 3: Probar en Modo Consola (Interactivo)
Antes de instalarlo como servicio, puedes verificar que todo funcione abriendo una terminal como Administrador o ejecutando:  
`C:\Servicios\GeomaticaServerAgent\Geomatica.ServerAgent.exe`

Verás la salida en vivo:
```text
info: Geomatica.ServerAgent.FileAuditorWorker[0]
      ======================================================
        Geomatica.ServerAgent - Agente de Auditoría en Servidor
        Iniciando servicio de monitoreo en segundo plano...
      ======================================================
info: Geomatica.ServerAgent.FileAuditorWorker[0]
      [BD] Conexión establecida y tabla 'geovisor.auditoria_archivo' verificada.
info: Geomatica.ServerAgent.FileAuditorWorker[0]
      [Seguridad 4663] Escucha activa en el canal 'Security' para el Evento 4663.
info: Geomatica.ServerAgent.FileAuditorWorker[0]
      [Vigilante] Monitoreando carpeta física local: G:\PRUEBAS
```
Al crear, modificar, renombrar o eliminar un archivo en esa carpeta, la consola imprimirá el evento capturado y lo enviará a PostgreSQL. Presiona <kbd>Ctrl</kbd> + <kbd>C</kbd> para detenerlo.

### Paso 4: Instalar como Servicio de Windows (Definitivo)
Abre **Símbolo del sistema (cmd)** o **PowerShell como Administrador** en el Windows Server y ejecuta:

```cmd
sc create "GeomaticaAuditoria" binPath= "C:\Servicios\GeomaticaServerAgent\Geomatica.ServerAgent.exe" start= auto displayname= "Geomática - Agente de Auditoría de Archivos"
```

Configurar descripción y recuperación ante fallos (opcional pero recomendado):
```cmd
sc description "GeomaticaAuditoria" "Monitorea cambios en archivos de proyectos GIS y registra auditoría en PostgreSQL."
sc failure "GeomaticaAuditoria" reset= 86400 actions= restart/60000/restart/60000/restart/60000
```

Iniciar el servicio:
```cmd
sc start "GeomaticaAuditoria"
```

---

## 6. Comandos de Administración del Servicio

| Acción | Comando (Ejecutar como Administrador) |
| :--- | :--- |
| **Iniciar:** | `sc start "GeomaticaAuditoria"` (o `Start-Service GeomaticaAuditoria`) |
| **Detener:** | `sc stop "GeomaticaAuditoria"` (o `Stop-Service GeomaticaAuditoria`) |
| **Consultar estado:** | `sc query "GeomaticaAuditoria"` |
| **Desinstalar:** | `sc stop "GeomaticaAuditoria"` seguido de `sc delete "GeomaticaAuditoria"` |

---

## 7. Diagnóstico y Logs

* En modo consola, todos los registros se imprimen en pantalla con marcas de tiempo.
* Cuando corre como servicio de Windows, los eventos del ciclo de vida se registran en el **Visor de Eventos de Windows** (`eventvwr.msc`) bajo `Registros de Windows` ➔ `Aplicación`.

---

## 8. Comportamiento ante Rutas No Resueltas, Servidores Externos y Respaldo Híbrido

Una duda operativa fundamental es: **¿Qué ocurre si un proyecto registrado en PostgreSQL tiene una ruta inválida, inexistente o que apunta a otro servidor/NAS diferente al Windows Server donde corre el agente?**

### 1. Aislamiento y Resiliencia en el Servidor (Cero Colapsos, Cero Bloqueos)
* **Detección sin bloqueo de red SMB:**  
  Cuando el agente recarga la lista de proyectos desde PostgreSQL (cada 60 s), analiza el campo `ruta_archivos`. Si la ruta es de red UNC (`\\otro-servidor\recurso`) y no coincide con ninguna clave configurada en `AgentSettings:MapeoRutas`, el agente **no intenta resolverla por red**. De este modo se evitan congelamientos de hilos por timeouts de conexión SMB (que pueden tardar hasta 30-60 segundos por cada máquina apagada).
* **Registro de diagnóstico limpio:**  
  El agente emite un log informativo indicando claramente que el proyecto pertenece a otro nodo:
  ```text
  info: [Mapeo] Proyecto 14 ('Cartografía Piedecuesta') omitido en este servidor: la ruta '\\10.0.0.8\historico\...' no pertenece a los discos locales configurados o resuelve a otro servidor.
  ```
* **Continuidad ininterrumpida:**  
  Los demás proyectos configurados en el disco local (`G:\...`) continúan siendo vigilados normalmente sin ninguna interrupción.

### 2. Detección de Carpetas Inexistentes o Pendientes de Creación
* Si un proyecto tiene una ruta local mapeada pero la carpeta física aún no ha sido creada en el disco del servidor (o el disco fue desconectado temporalmente), el agente registra una advertencia preventiva:
  ```text
  warn: [Mapeo] Proyecto 22 ('Nuevo Levantamiento'): la ruta local resuelta 'G:\PRUEBAS\2026\Lev22' no existe físicamente en el disco. Se reintentará en el próximo ciclo.
  ```
* **Auto-recuperación sin reinicio:**  
  Tan pronto como un usuario o administrador cree la carpeta física en Windows, en el siguiente ciclo automático (60 s) el agente la detectará viva, asegurará su regla SACL e iniciará el `FileSystemWatcher` automáticamente sin necesidad de reiniciar el servicio ni tocar la configuración.

### 3. Delegación Total al Servidor (Cliente Ultraliviano)
La auditoría de archivos físicos en tiempo real ha sido delegada **100% al Windows Server (`Geomatica.ServerAgent`)**:
* Se eliminó el escaneo y rastreo recurrente de directorios para auditoría en el cliente de escritorio ([`ArchivosViewModel.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Desktop/ViewModels/ArchivosViewModel.cs)), lo que hace que la aplicación cliente sea sumamente rápida y no sobrecargue la red SMB al navegar o refrescar carpetas.
* La aplicación de escritorio `Geomatica.Desktop` se enfoca exclusivamente en:
  1. Registrar las acciones realizadas directamente dentro del aplicativo (`geovisor.auditoria_proyecto` y `geovisor.auditoria_archivo`).
  2. Consultar y presentar el historial de auditoría de archivos (`geovisor.auditoria_archivo`) de forma instantánea desde PostgreSQL cuando el usuario presiona **"📜 Historial de cambios (5 últimos)..."**, **"🗑️ Eliminados"** o consulta la pestaña **"📁 Actividad de Archivos"** en la ficha del proyecto.

### 4. Soporte Multi-Servidor (Escalabilidad Horizontal)
Si la institución dispone de múltiples servidores de archivos Windows (por ejemplo: `SRV-ARCHIVOS-01` con proyectos activos en `G:\` y `SRV-ARCHIVOS-02` con proyectos históricos en `E:\`):
* Se puede desplegar una copia idéntica de `Geomatica.ServerAgent` en cada servidor Windows.
* Cada instancia se configura con su propio `MapeoRutas` apuntando a sus unidades físicas locales.
* Ambas instancias apuntan a la misma base de datos PostgreSQL centralizada (`PostgresConnection`) y reportan de manera concurrente a la tabla `geovisor.auditoria_archivo` sin conflictos.

---

## 9. Auditoría Avanzada de Usuarios: Integración con Eventos de Seguridad Windows 4663, 4660, 4656 y 5145

### 1. ¿Por qué el Propietario NTFS (`Owner`) no es suficiente para saber quién modificó o eliminó un archivo?
En el sistema de archivos NTFS de Windows:
1. **La propiedad del archivo (`Owner`) solo indica quién creó el archivo**, NO quién lo modificó por última vez. Si un usuario A crea un archivo y un usuario B lo edita o elimina, NTFS sigue reportando como dueño al usuario A.
2. Si el creador o modificador pertenece al grupo Administradores, Windows Server por defecto asigna como propietario genérico a `BUILTIN\Administradores`.
3. Cuando un archivo es eliminado, ya no existe en el disco y su descriptor de seguridad NTFS desaparece.

### 2. La Solución Empresarial: Eventos de Seguridad 4663, 4660, 4656 y 5145 + Cachés Separadas
[`FileAuditorWorker.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.ServerAgent/FileAuditorWorker.cs) incorpora un oyente en tiempo real (`EventLogWatcher`) y un lector inverso de respaldo (`EventLogReader`) sobre el canal **`Security`** de Windows utilizando la consulta XPath estandarizada:
```xpath
*[System[(EventID=4663 or EventID=4660 or EventID=4656 or EventID=5145)]]
```
Cuando un usuario interactúa con los archivos por red SMB o de forma local:
* El kernel de Windows emite el evento con los campos:
  * `SubjectUserName`: Nombre del usuario real autenticado (ej. `marbin.arevalo`), **incluso si es administrador** (filtrando cuentas de máquina `$` y cuentas de sistema `SYSTEM`/`LOCAL SERVICE`).
  * `SubjectDomainName`: Dominio de Active Directory (ej. `GEOMATICAAD0`).
  * `ObjectName` (o `ShareLocalPath + RelativeTargetName` en el Evento `5145` SMB): Ruta física en disco (normalizada por el agente mediante `QueryDosDevice` de `\Device\HarddiskVolumeX\` a letra de unidad y limpiando flujos alternativos ADS como `:Zone.Identifier` o `:$DATA` al copiar y pegar con `Ctrl+C`/`Ctrl+V`).
  * `HandleId`: Identificador hexadecimal normalizado en `_handleToObjectMap` para vincular aperturas de archivo (`4656`/`4663`) con eventos de borrado de objeto (`4660`).
  * `AccessMask`: Máscara binaria del acceso:
    * **Escritura (`WRITE_MASK`):** `0x2` (`WriteData`), `0x4` (`AppendData`), `0x100` (`WriteAttributes`), `0x40000`, `0x80000` ➔ almacenados en `_recentWriters` (retención 3 minutos).
    * **Eliminación (`DELETE_MASK`):** `0x10000` (`DELETE`), `0x40` (`DeleteChild`) ➔ almacenados exclusivamente en `_recentDeleters` (retención 45 segundos). En los eventos de solicitud de handle o recurso compartido (`4656` y `5145`), se filtra que no contengan bits de escritura (`(accessMask & (0x2 | 0x4)) == 0`) para distinguir una eliminación/renombrado real (`0x110080`, `0x10080`, `0x10000`) de una apertura con permisos completos durante la creación/copia de un archivo (`0x17019F`, `0x12019F`).
* **Política de Atribución Estricta:** Los Eventos de Seguridad de Windows son la **única fuente válida** para identificar quién realizó la acción. Si el evento no está disponible, el agente **nunca culpa falsamente al creador ni al propietario NTFS**: registra el movimiento físico en la base de datos (`CREACION`, `MODIFICACION`, `RENOMBRAR`, `MOVER`, `ELIMINAR`) y asigna como autor `"No identificado"`.

---

### 3. Configuración Requerida en Windows Server 2016 / 2019 / 2022

Para que Windows Server registre los Eventos 4663, 4660, 4656 y 5145 en el log de Seguridad, se requieren dos pasos (los cuales `Geomatica.ServerAgent` intenta aplicar automáticamente al iniciar como Administrador mediante `AsegurarSubcategoriasAuditpol()` y `AsegurarSaclAuditoriaCarpeta()`):

#### Paso 1: Habilitar las 3 subcategorías de auditoría (`auditpol`)
Abre una consola **CMD o PowerShell como Administrador** en el servidor y ejecuta:

```cmd
auditpol /set /subcategory:"{0CCE921D-69AE-11D9-BED3-505054503030}" /success:enable
auditpol /set /subcategory:"{0CCE9223-69AE-11D9-BED3-505054503030}" /success:enable
auditpol /set /subcategory:"{0CCE9244-69AE-11D9-BED3-505054503030}" /success:enable
```
*(Nota: Los GUIDs corresponden universalmente a "File System", "Handle Manipulation" y "Detailed File Share" en cualquier idioma de Windows Server).*

#### Paso 2: Configurar la regla de auditoría (SACL) en las carpetas compartidas
Windows únicamente genera eventos 4663/4656/4660 para las carpetas y archivos que tengan una regla SACL definida con los **4 permisos completos** (`WriteData, AppendData, Delete, DeleteSubdirectoriesAndFiles`).
`Geomatica.ServerAgent` invoca automáticamente `AsegurarSaclAuditoriaCarpeta(rutaDir)` al arrancar con privilegios elevados. También puedes aplicarla manualmente en **PowerShell como Administrador**:

```powershell
$carpetas = @("E:\EJEMPLO", "G:\GRUPO5\PASEO_ESPAÑA", "G:\GRUPO5\EMBARCADEROS", "F:\GRUPO4\PROYECTOS RECUPERADOS\NorOriente")
$everyone = New-Object System.Security.Principal.SecurityIdentifier([System.Security.Principal.WellKnownSidType]::WorldSid, $null)
$rights = [System.Security.AccessControl.FileSystemRights]"WriteData, AppendData, Delete, DeleteSubdirectoriesAndFiles"
$inherit = [System.Security.AccessControl.InheritanceFlags]"ContainerInherit, ObjectInherit"
$prop = [System.Security.AccessControl.PropagationFlags]::None
$rule = New-Object System.Security.AccessControl.FileSystemAuditRule($everyone, $rights, $inherit, $prop, [System.Security.AccessControl.AuditFlags]::Success)

foreach ($ruta in $carpetas) {
    if (Test-Path $ruta) {
        $acl = Get-Acl -Path $ruta -Audit
        $acl.SetAuditRule($rule)
        Set-Acl -Path $ruta -AclObject $acl
        Write-Host "SACL actualizada en: $ruta" -ForegroundColor Green
    }
}
```

*(Opcional - Vía Gráfica):*
1. Clic derecho en la carpeta compartida ➔ **Propiedades** ➔ pestaña **Seguridad** ➔ botón **Opciones avanzadas**.
2. Ve a la pestaña **Auditoría** y pulsa **Agregar**.
3. Selecciona la entidad de seguridad: `Todos` (o `Everyone`).
4. Tipo: `Acierto` (Success). Se aplica a: `Esta carpeta, subcarpetas y archivos`.
5. En Permisos avanzados marca únicamente: *Crear archivos / escribir datos*, *Crear carpetas / anexar datos*, *Eliminar* y *Eliminar subcarpetas y archivos*.
6. Pulsa **Aceptar** y **Aplicar**.

---

### 4. Detección Nativa de Renombrado (`RENOMBRAR` / `MOVER`), Reasignación de Historial y Archivos sin Historial Previo
* **Renombrado y Movimiento Nativo (`OnFileSystemRenamed`) con Reasignación de Historial (`ReasignarRutaAuditoriaArchivoAsync`):**
  * Cuando un usuario renombra o mueve un archivo dentro del proyecto, el agente cancela eventos pendientes sobre la ruta antigua, migra los registros previos de `geovisor.auditoria_archivo` a la nueva ruta relativa (manteniendo intacta la línea de tiempo de hasta 5 cambios) y genera un **único registro** con acción **`RENOMBRAR`** (icono naranja 🏷️) o **`MOVER`** (icono azul 🔄).
  * **Cero registros falsos:** No genera un falso `ELIMINAR` (🗑️) seguido de un falso `CREACION` (✨).
* **Eliminación de Archivos Preexistentes:**
  * Si se elimina cualquier archivo que fue creado antes de iniciar el agente (`estadoPrevio == null`), siempre se audita e inserta el evento `ELIMINAR` en `geovisor.auditoria_archivo` indicando `"Archivo (sin historial previo en BD) eliminado del servidor"`.
* **Verificación de Tabla sin Permisos DDL (`AsegurarTablaAuditoriaArchivoAsync`):**
  * Al iniciar, el repositorio verifica primero con `SELECT to_regclass('geovisor.auditoria_archivo')::text;` si la tabla ya existe antes de intentar ejecutar `CREATE TABLE`, evitando errores `42501: permission denied for schema geovisor` cuando la cuenta de PostgreSQL tiene permisos DML pero no DDL sobre el esquema.

---

### 5. Script DDL para PostgreSQL: Tabla, Índices y Función de Purga por Vejez
Aunque `Geomatica.ServerAgent` invoca `AsegurarTablaAuditoriaArchivoAsync()` al iniciar, si deseas ejecutar o verificar manualmente la estructura en PostgreSQL (vía pgAdmin o psql), este es el script oficial e idempotente:

```sql
-- 1. Tabla de auditoría de archivos
CREATE TABLE IF NOT EXISTS geovisor.auditoria_archivo (
    id_auditoria_archivo SERIAL PRIMARY KEY,
    id_proyecto INT NOT NULL REFERENCES geovisor.proyecto(id_proyecto) ON DELETE CASCADE,
    ruta_relativa VARCHAR(1000) NOT NULL,
    nombre_archivo VARCHAR(255) NOT NULL,
    accion VARCHAR(50) NOT NULL,
    usuario VARCHAR(150) NOT NULL,
    equipo VARCHAR(100),
    fecha_hora TIMESTAMPTZ DEFAULT NOW(),
    tamano_bytes BIGINT,
    fecha_archivo TIMESTAMPTZ,
    detalles TEXT
);

-- 2. Índices de rendimiento
CREATE INDEX IF NOT EXISTS idx_auditoria_archivo_proyecto_ruta 
    ON geovisor.auditoria_archivo (id_proyecto, lower(ruta_relativa));

CREATE INDEX IF NOT EXISTS idx_auditoria_archivo_proyecto_fecha 
    ON geovisor.auditoria_archivo (id_proyecto, fecha_hora DESC);

CREATE INDEX IF NOT EXISTS idx_auditoria_archivo_eliminados 
    ON geovisor.auditoria_archivo (id_proyecto, accion, fecha_hora DESC);

-- 3. Función almacenada para purga periódica por vejez (7 días con piso de 5)
CREATE OR REPLACE FUNCTION geovisor.fn_purgar_auditoria_archivo_vejez(
    p_dias_retencion integer DEFAULT 7,
    p_minimo_registros integer DEFAULT 5
)
RETURNS integer AS $$
DECLARE
    v_eliminados integer := 0;
BEGIN
    WITH protegidos AS (
        SELECT id_auditoria_archivo
        FROM (
            SELECT id_auditoria_archivo,
                   ROW_NUMBER() OVER (
                       PARTITION BY id_proyecto, lower(ruta_relativa)
                       ORDER BY fecha_hora DESC, id_auditoria_archivo DESC
                   ) AS ranking
            FROM geovisor.auditoria_archivo
        ) ranked
        WHERE ranked.ranking <= p_minimo_registros
    ),
    borrados AS (
        DELETE FROM geovisor.auditoria_archivo a
        WHERE a.fecha_hora < (NOW() - (p_dias_retencion || ' days')::interval)
          AND a.id_auditoria_archivo NOT IN (SELECT id_auditoria_archivo FROM protegidos)
        RETURNING a.id_auditoria_archivo
    )
    SELECT count(*) INTO v_eliminados FROM borrados;

    RETURN v_eliminados;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION geovisor.fn_purgar_auditoria_archivo_vejez(integer, integer) 
IS 'Purga registros de auditoría de archivos con más de N días de antigüedad conservando un piso de al menos M registros por archivo.';
```

### 6. Barrido Inicial Condicional (Línea Base) y Poda Diaria
1. **Barrido inicial automático de proyectos nuevos:**
   * Al descubrir carpetas físicas de proyectos, el agente verifica si `id_proyecto` tiene registros en `geovisor.auditoria_archivo`.
   * Si tiene 0 registros, lanza un barrido en segundo plano registrando el estado inicial de todos los archivos físicos (`CREACION`, `Sistema (Línea Base)`).
   * Si ya tiene registros, **no toca el disco**, evitando saturación I/O en reinicios del servicio.
2. **Poda periódica automática cada 24 horas:**
   * El servicio ejecuta diariamente `SELECT geovisor.fn_purgar_auditoria_archivo_vejez(7, 5);`.
   * Registros con más de 7 días de antigüedad son purgados automáticamente, manteniendo siempre intactos los 5 registros más recientes de cada archivo.

---

> **Nota de autoría y continuidad:** Verificado, depurado y actualizado por **Antigravity** el 30 de octubre de 2026. Sincronizado al 100% con [`FileAuditorWorker.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.ServerAgent/FileAuditorWorker.cs), [`ProyectoRepository.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Data/Repositories/ProyectoRepository.cs), [`ddl_geovisor_schema.sql`](file:///D:/Repositorios/Proyecto-de-Grado/Documentos/Documento%20Final/ddl_geovisor_schema.sql) y [`.agents/rules/auditoria_archivos_windows.md`](file:///D:/Repositorios/Proyecto-de-Grado/.agents/rules/auditoria_archivos_windows.md).
