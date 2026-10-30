# Revisión independiente de 7 hallazgos de alto riesgo — Auditoría Geomática

* **Fecha:** 2026-10-16
* **Revisor:** Antigravity (Claude Opus)
* **Alcance:** solo lectura. Sin modificar código, sin ejecutar pruebas ni compilar. Lectura de código, `git ls-files`/`git grep` y consulta web sobre la semántica de Npgsql/PostgreSQL.
* **Documento base:** [CONTEXTO_AUDITORIA_GEOMATICA.md](CONTEXTO_AUDITORIA_GEOMATICA.md)

> [!CAUTION]
> Durante la revisión, una búsqueda con `Select-String` mostró en claro la API key de ArcGIS en el registro de herramientas de la sesión (no en este documento). Considérese al rotar la clave. Este documento no contiene secretos.

**Correspondencia de ID:** ARCH-04 = CRIT-08 · ARCGIS-01 = CRIT-01 · WPF-01 = CRIT-02 · DB-01 = CRIT-04 · DB-02 = CRIT-05 · DB-03 = HIGH-09 · GIS-01 = CRIT-06.

**Convención:** **[H]** = hecho comprobado en código/configuración · **[I]** = inferencia no verificada en ejecución.

Rutas relativas a `Geomatica.Desktop/` salvo indicación.

---

## ARCH-04: secretos en `appsettings.json` y archivos de salida

**Estado: confirmado** (con matices).

**Evidencia**
- **[H]** `Geomatica.Desktop/appsettings.json` L2-L4: cadena de conexión con contraseña en claro y `ArcGIS:ApiKey` real (293 caracteres).
- **[H]** El archivo **está versionado**; `main` coincide con `origin/main` en GitHub. Visibilidad del repositorio no verificada.
- **[H]** `Geomatica.Desktop.csproj` L14-L16: `CopyToOutputDirectory=PreserveNewest`. Copias con secretos en `bin/Debug` (Desktop y UnitTests) y en `publish/Geomatica-Desktop/` (ignoradas por Git, pero viajan con cualquier artefacto distribuido).
- **[H]** `App.xaml.cs` L65-L71 ya carga variables de entorno y `AddUserSecrets<App>()`; el `.csproj` tiene `UserSecretsId`. La alternativa segura existe pero no se usa.
- **[H]** `.gitignore` ignora `appsettings.local.json`, pero la aplicación nunca carga ese archivo: la regla no protege nada.

**Matices frente a la auditoría inicial**
- **[H]** `Include Error Detail=true` **no está** en el archivo actual.
- "Credenciales de producción" no verificable: **[H]** host en IP privada y usuario dedicado (no `postgres`). **[I]** Privilegios reales del rol desconocidos.
- **[H]** `CONTEXTO_AUDITORIA_GEOMATICA.md` L59 repite la contraseña en texto plano (archivo aún no versionado): segunda copia del secreto.
- **[I]** Una app de escritorio con conexión directa a PostgreSQL siempre deja credenciales en el cliente. User-secrets resuelve el repositorio, no la distribución. Mitigación real: rotar, rol de privilegios mínimos, restricción por red.

**Consecuencias:** quien lea el repositorio o un `publish/` obtiene acceso a la BD (si alcanza la red interna) y a la cuota de ArcGIS. Está en el historial de Git (4 commits), borrar el archivo no basta.

**Prueba mínima:** `gitleaks detect` sobre el historial; verificar visibilidad en GitHub; revisar alcance/restricciones de la key en ArcGIS; en BD, `\du <rol_app>` y `has_table_privilege(...)`.

**Prioridad: P0 crítica.** Independiente. Rotar antes de cualquier refactorización.

---

## ARCGIS-01: borrado de `OperationalLayers` al filtrar o invalidar caché

**Estado: confirmado.**

**Evidencia [H]**
- `ViewModels/MapaViewModel.Proyectos.cs` L48: `InvalidarCache()` ejecuta `Map?.OperationalLayers.Clear()`.
- L131 y L184: `ActualizarCapasConFiltroAsync` llama a `Clear()` dos veces y solo vuelve a agregar Municipios y Proyectos.
- Las capas del usuario se agregan al mismo `Map.OperationalLayers` (`MapaViewModel.cs` L370, L568, L653); nada las reinserta.
- Disparadores: cada `BuscarSolicitado` (vía `Views/MapaView.xaml.cs` L726), incluido el debounce de `FiltrosViewModel.cs` L86-L100; y `InvalidarCache()` en `MainViewModel` L99, L126, L159 tras crear/editar/eliminar.

**Matices**
- **[H]** Solo afecta 2D; `Scene.OperationalLayers` (SLPK, LiDAR) y `GraphicsOverlays` no se tocan.
- **[H]** El cambio de mapa base (`MapaViewModel.cs` L1778-L1783) sí conserva las capas: el `Clear()` de filtros es un descuido.
- **[I]** Las capas huérfanas siguen referenciadas por `CapaUsuarioItem` con `GeoPackage`/`Geodatabase` abiertos: recursos y bloqueos retenidos, sin pérdida de datos.
- Inconsistencia grave de UI pero recuperable; propongo Alta, no Crítica.

**Prueba mínima:** prueba STA con `MapaViewModel` y `Map` reales: agregar un `FeatureLayer` a `OperationalLayers` y `CapasAdicionales`, llamar `ActualizarCapasConFiltroAsync([])`, verificar que la capa persiste. Manual: cargar un `.shp` y escribir en el filtro.

**Prioridad: Alta (P1).** Relacionado con WPF-01 y HIGH-07.

---

## WPF-01: consultas concurrentes que sobrescriben `Filtros.ResultadosLista`

**Estado: confirmado** (descripción incompleta).

**Evidencia [H]**
- Escritor 1: `Views/MapaView.xaml.cs` L203-L219 y L764-L788. Usa `CancellationTokenSource`, descarta resultados obsoletos, `ProyectoItem` con 12 campos.
- Escritor 2: `ViewModels/ArchivosViewModel.cs` L364 y L521-L552. **Sin cancelación ni control de generación**, nunca desuscribe, 5 argumentos (`Fecha`, `PalabrasClave`, `Responsable`, `MinX..MaxY` por defecto; ver `FiltrosViewModel.cs` L246-L259).
- Hasta **3** manejadores: MapaView, el `ArchivosViewModel` capturado por el singleton `MapaViewModel` (`App.xaml.cs` L285) y el creado por `_filesFactory()` (`MainViewModel` L88).

**Matices omitidos por la auditoría**
- **[H]** No hay mezcla: cada escritor hace `Clear()`+`Add` en un solo callback del Dispatcher. Gana el último, de forma no determinista.
- **[I]** Peor caso: al no cancelar, una consulta antigua de `ArchivosViewModel` puede pintar **resultados de un filtro anterior** mientras el mapa muestra el actual.
- **[H]** Consultas no equivalentes: `BuscarAsync` (municipio) filtra por código **y** envolvente; `ListarPorMunicipioAsync` solo por código. Conteos pueden diferir.
- **[H]** Con **departamento** seleccionado, `ListarPorDepartamentoAsync` falla (DB-03) y el `catch` no toca la lista: DB-03 enmascara este hallazgo.
- **[H]** Efecto visible: "Sin fecha", sin palabras clave, `TieneExtentValido=false` (no se dibuja la huella).

**Prueba mínima:** prueba STA con `IProyectoRepository` falso controlado por `TaskCompletionSource`; completar primero MapaView y luego Archivos y verificar `Fecha == null`; repetir con dos disparos seguidos para provocar resultados obsoletos.

**Prioridad: Alta (P1).** Depende de DB-03 y HIGH-08; relacionado con ARCGIS-01.

---

## DB-01: coordenadas opcionales en la UI frente a `geom NOT NULL`

**Estado: parcialmente confirmado** (solo creación).

**Evidencia [H]**
- `Documentos/Documento Final/ddl_geovisor_schema.sql` L132: `geom geometry(Point,4686) NOT NULL` (volcado `pg_dump` de PostgreSQL 16.4).
- `ViewModels/FormularioProyectoViewModel.cs` L550-L575 permite ambas coordenadas vacías → `geom = null`. `CrearProyectoUseCase` no valida.
- `Geomatica.Data/Repositories/ProyectoRepository.cs` L330-L332 inserta `NULL` → error 23502.

**Matices**
- **[H]** **Editar no falla**: `ActualizarAsync` usa `ELSE geom END` (L528-L530).
- **[H]** No es silencioso: rollback y mensaje "Error guardando proyecto" (L663-L666).
- **[H]** Omitido: texto no numérico en ambos campos se trata como vacío (`TryParse` falla sin aviso); `GestionarEstructuraCarpetas` (L584-L595) crea carpetas **antes** del `INSERT` fallido → carpetas huérfanas.
- **[I]** La BD desplegada podría diferir del volcado; el código asume `geom` anulable (`WHERE p.geom IS NOT NULL` en 4 consultas).

**Prueba mínima:** `SELECT is_nullable FROM information_schema.columns WHERE table_schema='geovisor' AND table_name='proyecto' AND column_name='geom'`; integración con PostGIS desechable (Testcontainers): `InsertarAsync(..., geom: null, ...)` → `PostgresException` con `SqlState == "23502"`.

**Prioridad: Alta (P1).** Requiere la decisión de §5.1 del contexto. Relacionado con HIGH-10 y DB-02.

---

## DB-02: errores de auditoría dentro de transacciones PostgreSQL

**Estado: confirmado** (consecuencia mal descrita; peor en Insertar/Actualizar).

**Evidencia [H]:** `ProyectoRepository.cs` `InsertarAsync` L385-L400, `ActualizarAsync` L591-L606, `EliminarAsync` L652-L680: `try/catch` de C# alrededor del `INSERT` de auditoría dentro de `tran`, sin `SAVEPOINT`.

**Corrección a la auditoría**
- **[I, semántica documentada de PostgreSQL]** `COMMIT` sobre una transacción abortada **no lanza error**: PostgreSQL ejecuta `ROLLBACK` y Npgsql no lo trata como fallo. En Insertar/Actualizar la consecuencia probable es **pérdida silenciosa**: la UI muestra "exitosamente" y no se guardó nada. La auditoría afirma lo contrario ("falla `CommitAsync()`").
- **[H]** En Eliminar, el `DELETE` siguiente lanza 25P02, rollback y excepción propagada: borrado fallido con mensaje engañoso.

**Condiciones de disparo (omitidas)**
- **[H]** Tabla y columnas existen en el DDL; `AsegurarTablaAuditoriaAsync` (L728-L756) coincide; sin desbordes en uso normal.
- **[I]** Disparadores realistas: falta de `INSERT` sobre `auditoria_proyecto` o `USAGE` sobre su secuencia para el rol, o despliegue sin la tabla y sin privilegio `CREATE`. El volcado no incluye privilegios. Fallo latente: baja probabilidad, alta gravedad.

**Prueba mínima:** en BD desechable, `ALTER TABLE geovisor.auditoria_proyecto ADD CONSTRAINT t CHECK (accion <> 'CREACION')` o revocar `INSERT`; verificar (1) `InsertarAsync` no lanza y el proyecto no existe; (2) `EliminarAsync` lanza `SqlState == "25P02"`. En BD real: `has_table_privilege('<rol_app>','geovisor.auditoria_proyecto','INSERT')` y `has_sequence_privilege('<rol_app>','geovisor.auditoria_proyecto_id_auditoria_seq','USAGE')`.

**Prioridad: Alta (P1); Crítica si falla la verificación de privilegios.** Corregir junto con DB-01 (mismo método).

---

## DB-03: discrepancias entre vistas, DDL y columnas

**Estado: parcialmente confirmado** (defectos reales, impacto acotado; una discrepancia adicional).

**Evidencia [H]**
1. `ListarPorDepartamentoAsync` (`ProyectoRepository.cs` L213) hace `JOIN geovisor.vw_proyecto_departamento`, ausente del DDL (un `pg_dump` la incluiría). Único llamador: `ArchivosViewModel.cs` L532, cuyo `catch` silencia el error. **El mapa y la lista principal no la usan** (`BuscarAsync` resuelve departamento con `m.dpto_ccdgo`). Impacto: solo pierde una de las dos consultas redundantes de WPF-01.
2. Trigger `municipio_sync_codes` (DDL L37-L42) asigna `NEW.mpio_ccdgo`, columna inexistente en `municipio` (L106-L112). **[H]** `mpio_cdpmp` es `NOT NULL`, así que la rama 1 se ejecuta siempre; **[I]** todo `INSERT`/`UPDATE` en `municipio` falla con 42703. **[H]** La app nunca escribe en `municipio`: impacto limitado a cargas DIVIPOLA externas.
3. **No reportado:** `AsegurarColumnasProyectoAsync` (L758-L778) agrega solo 4 columnas; omite `sistema_referencia`, `formato_datos`, `linaje`, `fecha_actualizacion`, que sí se usan en `INSERT`/`UPDATE`/`SELECT`. **[I]** Una BD creada con un DDL anterior fallaría al crear/editar. Las demás referencias `alias.columna` coinciden con el DDL.
4. Nota de método: el cuerpo del trigger tiene finales de línea `CR CR CR LF`; las herramientas numeran distinto (`Select-String` ubica `geom NOT NULL` en L162; el visor en L132). Puede explicar discrepancias de líneas entre auditorías.

**Prueba mínima:** `SELECT to_regclass('geovisor.vw_proyecto_departamento')` (esperado `NULL`); en BD desechable `UPDATE geovisor.municipio SET mpio_cnmbr = mpio_cnmbr WHERE mpio_cdpmp = '<código>'` (esperado 42703); integración: `ListarPorDepartamentoAsync` lanza 42P01.

**Prioridad:** Media (vista; se resuelve eliminando el escritor redundante de WPF-01), Baja (trigger), Media (migración incompleta).

---

## GIS-01: stdout/stderr, cancelación y procesos Python

**Estado: parcialmente confirmado** (4 rutas con perfiles distintos).

| Ruta | Drena stdout | Drena stderr | Kill al cancelar | Timeout |
|---|---|---|---|---|
| `FileGdbImporterService.ExportarConArcPyAsync` L348-L390 | ✅ asíncrono | ✅ asíncrono | ✅ `Kill(true)` | 10 min |
| `ExtraerAdjuntosConArcPyAsync` L879-L899 | ✅ | ❌ | ❌ | 25 s |
| `GenerarMetadatosGdbAsync` L962-L978 | ❌ | ❌ | ❌ | 30 s |
| `CadImporterService.ExportarConArcPyAsync` L371-L380 | secuencial (stdout luego stderr) | | ❌ | **ninguno** |

(Archivos en `Geomatica.Infrastructure.Gis/Services/`.)

**Matices [H]**
- La exportación principal de GDB está **bien implementada**; la auditoría no lo reconoce. Sirve de patrón para corregir las demás.
- `GenerarMetadatosGdbAsync`: el script **escribe el JSON en disco antes de imprimirlo** (L1252-L1254). Con tubería llena, el archivo ya está completo; tras 30 s el llamador (L461-L466) lo usa. Consecuencia real: **espera de ~30 s la primera vez por GDB**, no fallo funcional.
- CAD: lectura secuencial → bloqueo clásico si stderr se llena antes de cerrar stdout; sin timeout, espera hasta cancelar `ct`. La `OperationCanceledException` cae en `catch (Exception)` (L192) y **degrada a ACadSharp aunque el usuario canceló**. El `Directory.Delete(tempGdb)` del `finally` falla en silencio si el proceso sigue vivo.

**Inferencias [I]**
- "Huérfanos con `.lock` indefinidos" es exagerado para tubería llena: al desechar `Process` se cierran los extremos de lectura y Python debería terminar con BrokenPipe.
- El huérfano persistente ocurre cuando ArcPy **está trabajando** al vencer timeout/cancelación: sigue hasta terminar y retiene bloqueos de lectura sobre la `.gdb` (impiden borrar, mover o editar esquema).
- En adjuntos, el proceso huérfano puede escribir en el mismo `attachmentsDir` que el respaldo GDAL (L726-L737).
- El búfer de ~4 KB es el valor típico de Windows; no verificado.
- **[H]** Todo requiere `IsArcPyAvailable` (equipos con ArcGIS Pro).

**Prueba mínima:** apuntar `_pythonExecutablePath` a un stub que escriba ~100 KB en stderr/stdout y duerma. Esperado: metadatos ~30 s; adjuntos con stderr saturado ~25 s; CAD no termina sin cancelación. Tras cancelar, enumerar procesos hijos supervivientes; en CAD, verificar que la cancelación no degrada a ACadSharp.

**Prioridad: Media-Alta.** Orden: CAD → adjuntos → metadatos. Relacionado con HIGH-06.

---

## Tabla consolidada

| ID | Estado | Severidad inicial | Severidad propuesta | Dependencias |
|---|---|---|---|---|
| ARCH-04 | Confirmado | Crítico | **P0 Crítico** | Ninguna |
| ARCGIS-01 | Confirmado | Crítico | **Alto** | WPF-01, HIGH-07 |
| WPF-01 | Confirmado (con matices) | Crítico | **Alto** | DB-03, HIGH-08, ARCGIS-01 |
| DB-01 | Parcialmente (solo creación) | Crítico | **Alto** | Decisión §5.1, HIGH-10, DB-02 |
| DB-02 | Confirmado (consecuencia distinta) | Crítico | **Alto → Crítico** si faltan privilegios | DB-01 |
| DB-03 | Parcialmente (impacto acotado) | Alto | **Medio / Bajo** | WPF-01 |
| GIS-01 | Parcialmente (4 rutas distintas) | Crítico | **Medio-Alto** | HIGH-06 |

## Discrepancias con la auditoría inicial

1. **ARCH-04:** `Include Error Detail=true` no aparece en el archivo actual; "producción" no verificable. Omite la publicación en GitHub, la réplica del secreto en el archivo de contexto y la regla inútil de `appsettings.local.json`.
2. **DB-02:** no falla `CommitAsync()`; lo esperado es `COMMIT` silencioso = `ROLLBACK`, con **éxito aparente y pérdida de datos** en crear/editar. No identifica cuándo fallaría el `INSERT` de auditoría (privilegios del rol).
3. **DB-01:** "al guardar" es impreciso; editar sin coordenadas no falla. Omite carpetas huérfanas y entrada no numérica tratada como vacía.
4. **WPF-01:** no hay intercalado destructivo sino "gana el último". Omite el peor caso (resultados obsoletos) y que DB-03 neutraliza al segundo escritor con departamento seleccionado.
5. **DB-03:** ni la vista ni el trigger afectan el flujo principal. Omite la migración incompleta de columnas ISO en `AsegurarColumnasProyectoAsync`.
6. **GIS-01:** no reconoce que la exportación GDB es correcta; en metadatos el bloqueo solo cuesta tiempo; el huérfano "indefinido" está exagerado para tubería llena; omite que CAD no tiene timeout y que la cancelación activa ACadSharp.
7. **ARCGIS-01:** correcto, pero limitado a 2D, recuperable y sin pérdida de datos: Alto, no Crítico.
