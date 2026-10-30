# Contexto de continuidad: Monografía y Trabajo de Grado

## 1. Identificación y objetivo
* **Título del proyecto:** Sistema de gestión y visualización geoespacial para el acceso y filtrado de datos espaciales relacionados con los proyectos del grupo de investigación GEOMÁTICA, gestión y optimización de sistemas.
* **Autor:** Marbin Javier Arévalo Guerrero (Código estudiantil: 2210053).
* **Director:** Jhon Jairo Cáceres Jiménez (Profesor Escuela de Ingeniería Civil, Doctor en Ingeniería Civil y Costera).
* **Tutor:** Carlos Horacio García Castrillón (Profesional Grupo GEOMÁTICA, Mag. en Desarrollo de Aplicaciones sobre Dispositivos Móviles).
* **Entidad / Institución:** Universidad Industrial de Santander (UIS) — Facultad de Ingenierías Fisicomecánicas — Escuela de Ingeniería de Sistemas e Informática — Modalidad: Práctica Social.
* **Propósito:** Consolidar, redactar y auditar la monografía de grado en formato APA 7 a partir del plan de trabajo aprobado (`Plan_TrabajoDeGrado_Geomatica_v2.docx`), respaldando técnicamente el software desarrollado (`Geomatica.Desktop`), la arquitectura, la base de datos PostGIS, la metodología ágil adaptada y la validación con usuarios operativos.
* **Resultado final esperado:** Documento formal de grado (`Documento de Grado Marbin.docx`) completo, estructurado conforme a la guía de la escuela/institución, listo para revisión del director/tutor y sustentación ante el comité de trabajos de grado.

---

## 2. Estado actual
* **Documento maestro vigente:** `D:\Repositorios\Proyecto-de-Grado\Documentos\Documento Final\Documento de Grado Marbin.docx`.
* **Capítulos técnicos ya redactados e integrados:**
  * **Capítulo 5:** Fase 1 — Diseño e Implementación de la Arquitectura Base (Clean Architecture, MVVM, WPF, ArcGIS Maps SDK for .NET).
  * **Capítulo 6:** Fase 2 — Implementación de la Base de Datos Espacial (PostGIS, SRID 4686 MAGNA-SIRGAS, tablas, vistas, triggers DIVIPOLA, índices GiST, GIN y B-tree, sustentado en `ddl_geovisor_schema.sql`).
  * **Capítulo 7:** Fase 3 — Desarrollo del Visor Cartográfico y Filtrado Espaciotemporal (interacción bidireccional, `GeoViewTapped`, filtros en cascada, mapas base offline/online, medición SIG).
  * **Capítulo 8:** Fase 4 — Gestión y Acceso a Archivos Especializados (explorador virtual de carpetas, importadores de FileGDB, CAD DWG/DXF, ráster GeoTIFF con GDAL y sidecars, nubes LiDAR LAS/LAZ en 3D, exportación ZIP y justificación técnica de la ampliación de alcance).
  * **Capítulo 9:** Fase 5 — Pruebas y Validación Operativa (batería automatizada de pruebas, protocolo en etapas, resultados de usabilidad con $n=3$ usuarios basados en `Encuesta1.csv`, trazabilidad de mejoras al Sprint 8).
  * **Conclusiones y Referencias:** 24 referencias bibliográficas formateadas rigurosamente en APA 7.
* **Capítulo 4 (Metodología del Proyecto y Marco Ágil):**
  * Se elaboró el borrador completo y refinado (subcapítulos 4.1 a 4.5), integrando los testimonios reales del autor y la reconstrucción retrospectiva de los Sprints S0 a S8 y las 8 Historias de Usuario (INVEST).
  * *Estado:* El borrador está aprobado en conversación y listo para ser insertado formalmente en `Documento de Grado Marbin.docx`.
* **Automatización de Listas y Tablas en Word:**
  * Se corrigieron los estilos internos de la Tabla de Contenido (`toc 1`, `toc 2`, `toc 3`) a Times New Roman 12 pt, interlineado doble, sin sangría escalonada (0 cm) y desactivando la propiedad oculta `All Caps`.
  * Se transformaron los títulos de tablas y figuras a campos automáticos de Word (`SEQ Tabla`, `SEQ Figura`) y se inyectaron los campos automáticos de Lista de Tablas y Lista de Figuras con el script `fix_tocs.py`.
* **Auditoría Técnica y Metodológica:**
  * Documentada en `auditoria_critica.md`. Se desmintió la afirmación prematura de "no cumplimiento de Scrum" y se estableció la base testimonial y técnica para defender el marco ágil como una adaptación empírica de Scrum a la investigación aplicada.
* **Estado del Software:**
  * Totalmente funcional, compilando en .NET 8 sin errores (7 proyectos en `Geomatica.sln`, incluyendo el servicio de Windows `Geomatica.ServerAgent`) y con **368 pruebas unitarias superadas** (0 fallos, depuradas sin falsos positivos ni duplicados). Incluye catálogo canónico GIS unificado, indexación progresiva en tiempo real validada sobre el repositorio real `\\10.0.29.10\grupo5\EMBARCADEROS` (`1.282.597` archivos en `32.835` carpetas indexados en `33,14 s` y filtrados en `< 1 ms` a `22 ms`), búsqueda global de archivos, sincronización bidireccional thread-safe entre la Ficha de Proyecto y el explorador de archivos, gestión integral de subproyectos y frentes de trabajo con editor tabular (`ImportarSubproyectosCsvWindow` / `EditarSubproyectoWindow`) y visualización cartográfica global, auditoría granular de archivos (`geovisor.auditoria_archivo` con retención por vejez a 7 días con piso de 5 registros, barrido inicial condicional de línea base para proyectos nuevos, purga periódica diaria de fondo, reasignación de historial al renombrar/mover y agente de servidor `Geomatica.ServerAgent` integrado con Eventos de Seguridad Windows `4663`, `4660`, `4656` y `5145`), y arquitectura de metadatos ISO 19115 modular y plegable bajo demanda.
  * El sistema está en estado *Deployment-Ready* (listo para puesta en producción); el cargue masivo del archivo histórico total está a la espera de la activación del servidor central de almacenamiento por parte de la infraestructura institucional de GEOMÁTICA.

---

## 3. Hechos confirmados
1. **Pruebas Automatizadas:** Existen **368 pruebas unitarias** funcionales y deterministas en el proyecto `Geomatica.UnitTests` (~1–3 s de ejecución total). Todas pasaron exitosamente al ejecutar `dotnet test Geomatica.Desktop\Geomatica.UnitTests\Geomatica.UnitTests.csproj -m:1` (net8.0-windows10.0.19041.0). La cifra antigua de "147 pruebas" o "285 pruebas" pertenecía a hitos documentales previos.
2. **Esquema de Base de Datos:** El esquema oficial es `geovisor` sobre PostgreSQL/PostGIS. Las geometrías están restringidas a `SRID 4686` mediante restricciones CHECK. Incluye las **6 tablas** `departamento`, `municipio`, `proyecto`, `proyecto_municipio`, `subproyecto`, `auditoria_proyecto` y `auditoria_archivo` (7 tablas en total en el esquema `geovisor`). El script DDL completo está verificado en [`ddl_geovisor_schema.sql`](file:///D:/Repositorios/Proyecto-de-Grado/Documentos/Documento%20Final/ddl_geovisor_schema.sql).
3. **Muestra y Validación con Usuarios:** La evaluación temprana de usabilidad se ejecutó con 3 usuarios clave del grupo GEOMÁTICA (docente, maestría y doctorado), utilizando una versión compilada distribuida (`publish/Geomatica-Desktop`) vinculada a rutas locales reales. Los resultados están registrados en `Documentos/Reportes de Usuarios/Encuesta1.csv` y complementados con el reporte técnico de "Roberto" (`Documento reporte_Roberto.pdf`).
4. **Roles Reales en el Desarrollo:**
   * *Product Owner inicial / Visión Estratégica:* Dr. Jhon Jairo Cáceres Jiménez (Director).
   * *Tutor / Revisor Técnico Continuo:* Carlos Horacio García Castrillón (reuniones y feedback semanal).
   * *Desarrollador / Scrum Master adaptado:* Marbin Javier Arévalo Guerrero.
   * *Especialistas de Dominio / Stakeholders:* Miembros de GEOMÁTICA (ej. Roberto para nubes de puntos LiDAR).
5. **Origen de la Ampliación de Alcance:**
   * El soporte nativo para GeoTIFF, FileGDB, CAD (.dwg/.dxf) y LiDAR LAS no fue una desviación no planificada; surgió de las necesidades operativas reales de proyectos de extensión e interventoría ejecutados por la UIS (destacando el proyecto real **"Embarcaderos"**). Se priorizó la fidelidad de ingeniería de escritorio sobre mapas web ligeros.
6. **Definition of Done (DoD) Aplicada:**
   * Nivel 1 (Técnico): Compilación limpia, procesamiento asíncrono no bloqueante en UI, pruebas unitarias y verificación espacial en PostGIS.
   * Nivel 2 (Dominio): Inspección visual con el especialista temático correspondiente.

---

## 4. Decisiones y restricciones
* **Tono del Documento (Regla Inquebrantable):** El proyecto compila avances consolidados. **ESTÁ PROHIBIDO REDACTAR EN TONO DE CARENCIA** o insinuar que faltan cosas por hacer (evitar: "aún no se ha realizado", "queda pendiente", "está incompleto"). Se redacta en positivo lo que ya está construido, probado y estructurado. Los pasos futuros se describen formalmente como la fase de *despliegue y puesta en marcha institucional*.
* **Secciones Aprobadas Intocables:** El planteamiento del problema, la justificación y los objetivos (general y específicos) deben trasladarse textualmente del plan de trabajo aprobado; no se permite alterar su redacción.
* **Formato Normativo:** Norma **APA 7** (autor-año). Letra Times New Roman 12 pt, interlineado doble (2.0), márgenes institucionales (2.54 cm).
* **Formato de la Tabla de Contenido:** Alineación plana (sangría izquierda = 0 cm para todos los niveles TOC 1, TOC 2, TOC 3), sin mayúscula sostenida forzada (Title Case).
* **Manejo de Tiempos de Búsqueda:** Redacción cualitativa sustentada en la percepción de eficiencia (5,0/5 en ubicación cartográfica) frente a la dispersión tradicional en carpetas locales. No inventar mediciones cronometradas en segundos o minutos.
* **Copias de Seguridad Obligatorias:** Antes de realizar cualquier modificación sobre archivos Word (`.docx`), es obligatorio generar una copia de seguridad con fecha o sufijo descriptivo.
* **Restricción de Modificación de Código:** En esta etapa documental NO se debe alterar el código fuente de `Geomatica.Desktop` ni alterar paquetes o dependencias sin orden expresa.

---

## 5. Hipótesis y cuestiones sin resolver
1. **Fecha de Activación del Servidor:** La fecha exacta en que la administración de infraestructura de GEOMÁTICA / UIS habilitará el servidor central para la ingesta masiva del repositorio histórico no está fijada; se maneja en el documento como una actividad de la etapa de puesta en marcha.
2. **Inserción Gráfica de la Figura 1 (Modelo ER):** El código Mermaid del diagrama entidad-relación está completo en `estructura_monografia.md`, pero aún debe exportarse a imagen (.png) e insertarse en el espacio asignado en el Capítulo 6 del documento Word.
3. **Revisión del Capítulo 3 (Marco de Referencia):** Falta verificar con el autor si los antecedentes y fundamentos teóricos de la plantilla actual requieren ampliación o si se consideran suficientes para el comité de grado.

---

## 6. Historial de actuaciones relevantes
* **Corrección de la Tabla de Contenido (TOC) en Word:** Se descubrió que el estilo `TOC 1` de la plantilla original tenía activa la propiedad binaria `all_caps = True`. Se ejecutó un script en Python que modificó el XML de estilos desactivando `all_caps`, estableciendo Times New Roman 12 pt y `left_indent = 0`.
* **Automatización de Listas de Tablas y Figuras:** La plantilla original solo tenía texto estático de guía. Se implementó `fix_tocs.py`, que convirtió los encabezados del documento a campos de secuencia `SEQ Tabla` y `SEQ Figura` e insertó los códigos de campo `TOC \h \z \c "Tabla"` y `TOC \h \z \c "Figura"`.
* **Auditoría Técnica Imparcial (`auditoria_critica.md`):** Inicialmente se planteó erróneamente que "no hubo Scrum" por falta de commits en ramas Git. En la segunda auditoría se corrigió la postura, distinguiendo la ausencia de registros de tickets con la existencia de un desarrollo ágil adaptado con iteraciones semanales, validación con usuarios y prototipado rápido.
* **Entrevista de Reconstrucción Metodológica:** Se formularon 5 preguntas clave al autor, cuyas respuestas revelaron la participación del Director como PO, la existencia del proyecto Embarcaderos y la validación con Roberto.
* **Redacción de Capítulos Técnicos:** Se redactaron con éxito e integraron en el documento Word los capítulos 5 a 9, conclusiones y referencias, con respaldo en copias de seguridad.

---

## 7. Archivos y recursos

### Archivos Inspeccionados y Activos
* **Documento Maestro Actual:**
  * [`D:\Repositorios\Proyecto-de-Grado\Documentos\Documento Final\Documento de Grado Marbin.docx`](file:///D:/Repositorios/Proyecto-de-Grado/Documentos/Documento%20Final/Documento%20de%20Grado%20Marbin.docx) (408 párrafos, capítulos 5 a 9 y referencias consolidadas).
* **Plan de Trabajo de Grado Aprobado:**
  * [`D:\Repositorios\Proyecto-de-Grado\Documentos\Documento Final\Plan_TrabajoDeGrado_Geomatica_v2.docx`](file:///D:/Repositorios/Proyecto-de-Grado/Documentos/Documento%20Final/Plan_TrabajoDeGrado_Geomatica_v2.docx).
* **Respaldos de Seguridad Generados:**
  * `APA_Plantilla_respaldo_pre_cap6.docx`
  * `APA_Plantilla_respaldo_pre_referencias.docx`
  * `APA_Plantilla_respaldo_final.docx`
  * `APA_Plantilla_respaldo_pre_toc_fix.docx`
  * `APA_Plantilla_respaldo_pre_toc_indent.docx`
  * `APA_Plantilla_respaldo_pre_captions.docx`
* **Insumos de Datos y Evidencias:**
  * Script DDL: [`D:\Repositorios\Proyecto-de-Grado\Documentos\Documento Final\ddl_geovisor_schema.sql`](file:///D:/Repositorios/Proyecto-de-Grado/Documentos/Documento%20Final/ddl_geovisor_schema.sql).
  * Encuesta de Usabilidad: [`D:\Repositorios\Proyecto-de-Grado\Documentos\Reportes de Usuarios\Encuesta1.csv`](file:///D:/Repositorios/Proyecto-de-Grado/Documentos/Reportes%20de%20Usuarios/Encuesta1.csv).
  * Reporte de Usuario Roberto: `D:\Repositorios\Proyecto-de-Grado\Documentos\Reportes de Usuarios\Documento reporte_Roberto.pdf`.
* **Artefactos y Scripts del Asistente:**
  * Diagnóstico de Auditoría: [`C:\Users\marbin.arevalo\.gemini\antigravity\brain\6197f4b1-a290-41d3-b007-6698726c899b\auditoria_critica.md`](file:///C:/Users/marbin.arevalo/.gemini/antigravity/brain/6197f4b1-a290-41d3-b007-6698726c899b/auditoria_critica.md).
  * Estructura y Diagrama Mermaid: [`C:\Users\marbin.arevalo\.gemini\antigravity\brain\6197f4b1-a290-41d3-b007-6698726c899b\estructura_monografia.md`](file:///C:/Users/marbin.arevalo/.gemini/antigravity/brain/6197f4b1-a290-41d3-b007-6698726c899b/estructura_monografia.md).
  * Script de automatización de listas: [`D:\Repositorios\Proyecto-de-Grado\fix_tocs.py`](file:///D:/Repositorios/Proyecto-de-Grado/fix_tocs.py).

### Archivos de Código Fuente Clave Verificados
* Repositorio de Datos: [`Geomatica.Data/Repositories/ProyectoRepository.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Data/Repositories/ProyectoRepository.cs) y [`MunicipioRepository.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Data/Repositories/MunicipioRepository.cs) (ADO.NET puro con `Npgsql` `9.0.3`, sin ORM ni Dapper).
* Capa de Dominio: [`Geomatica.Domain/Entities/ProyectoGeomatico.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Domain/Entities/ProyectoGeomatico.cs), [`AuditoriaProyectoDto.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Domain/Entities/AuditoriaProyectoDto.cs), [`AuditoriaArchivoDto.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Domain/Entities/AuditoriaArchivoDto.cs), [`SubproyectoDto.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Domain/Entities/SubproyectoDto.cs), [`ProyectoDtos.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Domain/Entities/ProyectoDtos.cs) y [`CatalogoFormatosGis.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Domain/Catalogos/CatalogoFormatosGis.cs).
* Servicios GIS: [`LidarBackgroundWorker.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Infrastructure.Gis/Services/LidarBackgroundWorker.cs), [`GeoTiffSidecarResolver.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Infrastructure.Gis/Services/GeoTiffSidecarResolver.cs), [`Iso19115MetadataExtractor.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Infrastructure.Gis/Services/Iso19115MetadataExtractor.cs), [`FileGdbImporterService.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Infrastructure.Gis/Services/FileGdbImporterService.cs), [`CadImporterService.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Infrastructure.Gis/Services/CadImporterService.cs).
* Agente de Servidor: [`Geomatica.ServerAgent/FileAuditorWorker.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.ServerAgent/FileAuditorWorker.cs) y [`Program.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.ServerAgent/Program.cs).
* Vistas y ViewModels: [`MapaView.xaml.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Desktop/Views/MapaView.xaml.cs), [`FormularioProyectoView.xaml`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Desktop/Views/FormularioProyectoView.xaml), [`FiltrosViewModel.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Desktop/ViewModels/FiltrosViewModel.cs), [`FormularioProyectoViewModel.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Desktop/ViewModels/FormularioProyectoViewModel.cs), [`FichaProyectoViewModel.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Desktop/ViewModels/FichaProyectoViewModel.cs), [`ArchivosViewModel.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Desktop/ViewModels/ArchivosViewModel.cs) e [`HistorialArchivoViewModel.cs`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.Desktop/ViewModels/HistorialArchivoViewModel.cs).
* Pruebas Unitarias: [`Geomatica.UnitTests`](file:///D:/Repositorios/Proyecto-de-Grado/Geomatica.Desktop/Geomatica.UnitTests/Geomatica.UnitTests.csproj) (**365 pruebas deterministas** pasando al 100%).

---

## 8. Instrucciones de continuidad
Para el siguiente asistente que retome el trabajo:
1. **Fuente de verdad:** Trabajar exclusivamente sobre el archivo `Documento de Grado Marbin.docx` ubicado en `Documentos\Documento Final\`.
2. **Generar respaldo antes de tocar Word:** Cualquier edición en `.docx` debe ir precedida por una copia con sufijo (ej. `_respaldo_pre_cap4.docx`).
3. **Respetar la redacción acordada para el Capítulo 4:** El texto del Capítulo 4 ya fue acordado detalladamente en esta conversación (secciones 4.1 a 4.5). Su inserción debe reemplazar cualquier contenido provisional existente de metodología en el documento de grado.
4. **No modificar código fuente:** La solución `Geomatica.Desktop` está estable, compilada y con pruebas pasando. Salvo orden explícita del usuario, no editar archivos `.cs`, `.xaml` o `.csproj`.
5. **No romper estilos de Word:** Si se insertan tablas o encabezados mediante scripts con `python-docx`, asegurarse de asignar el estilo `Normal` o `Heading` respetando Times New Roman 12 pt e interlineado doble.

---

## 9. Próximas acciones (Ordenadas por prioridad)
1. **Acción 1 (Prioridad Alta):** Insertar el texto definitivo del **Capítulo 4 (Metodología del Proyecto y Marco Ágil)** en `Documento de Grado Marbin.docx` (previo respaldo).
2. **Acción 2 (Prioridad Alta):** Redactar e integrar el **Resumen (Español) y Abstract (Inglés)** y la **Introducción** final en el documento de grado, sintetizando los aportes del sistema y su estado actual.
3. **Acción 3 (Prioridad Media):** Incorporar la **Figura 1 (Modelo Entidad-Relación)** en el Capítulo 6 exportando el código Mermaid de `estructura_monografia.md` e insertándola con el epígrafe formal en Word.
4. **Acción 4 (Prioridad Media):** Estructurar y anexar los **Apéndices:**
   * Apéndice A: Script DDL del esquema geovisor (`ddl_geovisor_schema.sql`).
   * Apéndice B: Instrumento de encuesta de usabilidad y reporte cualitativo.
   * Apéndice C: Matriz de pruebas automatizadas y funcionales.
   * Apéndice D: Guía de despliegue y puesta en marcha (`LEEME.txt`).
5. **Acción 5 (Prioridad Baja):** Revisión final de coherencia editorial, ortografía, numeración de páginas y actualización de la Tabla de Contenido, Lista de Tablas y Lista de Figuras en Word con F9.

---

## 10. Riesgos y errores que evitar
* **NO usar tono de carencia ni justificaciones defensivas:** El proyecto está consolidado y en fase de despliegue. Evitar frases como "no se alcanzó a implementar" o "queda como trabajo futuro por falta de tiempo". Presentar la carga masiva como la fase de *puesta en marcha institucional* sujeta a la infraestructura del servidor central.
* **NO inventar métricas cuantitativas no registradas:** No afirmar tiempos de búsqueda en segundos (ej. "redujo un 80% el tiempo de búsqueda") porque `Encuesta1.csv` mide escala Likert de usabilidad (5,0/5 en ubicación espacial), no cronometrajes exactos.
* **NO afirmar que se ejecutó Scrum corporativo estricto:** Describirlo con honestidad como Scrum adaptado a investigación aplicada, donde el Product Owner fue el director y el seguimiento fue semanal directo sin burocracia de actas diarias.
* **NO editar el documento de Word mientras esté abierto por el usuario:** Verificar siempre permisos de escritura o pedir al usuario que lo cierre para evitar excepciones de bloqueo (`PermissionError`).

---

## 11. Referencias de origen
* `Plan_TrabajoDeGrado_Geomatica_v2.docx`: Planteamiento, objetivos aprobados, Tabla 1 de actividades metodológicas.
* `auditoria_critica.md`: Evaluación detallada de cumplimiento técnico y desambiguación metodológica.
* `estructura_monografia.md`: Plano de capítulos aprobado, tabla de sprints reconstruida y modelo ER en Mermaid.
* Historial de commits de Git: Fechas y evolución técnica desde el commit inicial en julio 2025 hasta octubre 2026.
* Respuestas testimoniales del autor (2026-10-13): Validación de roles (director como PO, Carlos García como tutor semanal, proyecto Embarcaderos, Roberto para LiDAR y sesión de prueba con versión compilada).

---

## 12. Prompt de continuación

```markdown
Hola. Estoy trabajando en la redacción y consolidación final de mi monografía de trabajo de grado en formato APA 7 para la carrera de Ingeniería de Sistemas (Universidad Industrial de Santander - UIS).

Por favor, lee y carga inmediatamente como contexto de continuidad el archivo:
`Documentos\Chats\CONTEXTO_TRABAJO_GRADO.md`

### Contexto y restricciones clave que debes asumir:
1. El documento maestro de trabajo es `Documentos\Documento Final\Documento de Grado Marbin.docx`. Antes de cualquier edición, genera una copia de seguridad.
2. NO modifiques código fuente ni dependencias del proyecto `Geomatica.Desktop`. El software está completo, compilando en .NET 8 (7 proyectos en la solución) y con 365 pruebas unitarias superadas al 100%.
3. Mantén estrictamente el TONO POSITIVO Y CONSOLIDADO: está prohibido redactar en tono de carencia o decir que faltan cosas por hacer. El software está en estado *Deployment-Ready* y la carga masiva de datos históricos se describe como la fase planificada de puesta en producción una vez se active el servidor institucional de GEOMÁTICA.
4. El estilo normativo es APA 7 estricto (Times New Roman 12 pt, interlineado doble, sangrías 0 cm en la tabla de contenidos).

### Primer paso concreto:
Revisa el estado actual de `Documentos\Documento Final\Documento de Grado Marbin.docx` y procede a insertar el texto definitivo del **Capítulo 4 (Metodología del Proyecto y Marco Ágil)** que ya fue acordado y estructurado en el contexto de continuidad, creando previamente el respaldo `Documento de Grado Marbin_respaldo_pre_cap4.docx`. Confírmame cuando esté listo para continuar con el Resumen e Introducción.
```

---

> **Nota de autoría y continuidad:** Verificado y actualizado por **Antigravity (Claude Opus 4.6)** el 29 de octubre de 2026. Sincronizado con los 7 proyectos de `Geomatica.sln`, las 7 tablas del esquema `geovisor` (`ddl_geovisor_schema.sql`), el servicio `Geomatica.ServerAgent` y las **365 pruebas unitarias** verificadas.
