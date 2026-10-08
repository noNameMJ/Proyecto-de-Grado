# Segunda Auditoría Crítica del Proyecto de Grado

Como auditor académico independiente, asumo la total responsabilidad por los sesgos heurísticos de mi diagnóstico anterior. Mi error principal fue equiparar la **ausencia de evidencia en el repositorio Git** con la **evidencia de ausencia de un proceso**. El control de versiones es una herramienta técnica, no una herramienta de gestión de proyectos, por lo que inferir que "no hubo Scrum" o "no hubo validación" solo porque no existen en el código fuente fue una conclusión excesiva y metodológicamente incorrecta.

A continuación, presento la **Segunda Auditoría Crítica**, ajustada estrictamente a la evidencia objetiva, diferenciando claramente entre lo demostrable, lo inferible y lo no verificable.

---

### 1. REVISAR NUEVAMENTE EL PLAN

De la lectura estricta del documento `Plan_TrabajoDeGrado_Geomatica_v2.docx`, se extraen los siguientes compromisos y la evidencia necesaria para demostrarlos:

*   **Objetivos (General y Específicos):** Requieren evidencia de software funcional (interfaz cartográfica), esquemas de base de datos y repositorios de código.
*   **Metodología (Scrum adaptado, Sprints de 2 semanas):** Requiere evidencia de un Product Backlog, Sprint Backlogs, registros de reuniones (planning, reviews) o seguimiento temporal de tareas. Esta evidencia *normalmente* vive fuera del código (Jira, Trello, Excel, actas).
*   **Instrumentos y Validación (Caja negra, Encuesta SUS, entrevistas):** Requiere documentos externos (guiones de entrevista, formatos de encuesta diligenciados, tablas de resultados, matrices de casos de prueba manuales).
*   **Tecnologías (C#, WPF, ArcGIS SDK, PostGIS):** Requiere evidencia directa en el código fuente (archivos `.cs`, `.xaml`, referencias a paquetes NuGet).

---

### 2. REVISIÓN CRÍTICA DE MI DIAGNÓSTICO ANTERIOR

Corrijo explícitamente mis conclusiones anteriores:

*   **"El proyecto NO aplicó Scrum" / "No hubo sprints / backlog / reuniones / retrospectivas"** 
    👉 **INCORRECTA / EXCESIVA:** Git no guarda reuniones ni tableros externos. Que no haya un archivo `BACKLOG.md` no significa que el backlog no exista en un Excel o en Trello. La afirmación correcta es: **PENDIENTE DE EVIDENCIA EXTERNA**.
*   **"Aplicó desarrollo iterativo en solitario"** 
    👉 **PARCIALMENTE CONFIRMADA:** El log de Git muestra un solo autor de código, pero no descarta la participación del Tutor (Carlos García) u otros miembros de GEOMÁTICA como *Product Owners* o *Stakeholders* durante las iteraciones.
*   **"No hubo validación con usuarios / La encuesta SUS no existe"** 
    👉 **INCORRECTA / EXCESIVA:** Las encuestas de usabilidad rara vez se versionan en Git. Es un documento académico/externo. Su estado real es **PENDIENTE DE EVIDENCIA EXTERNA**.
*   **"Las pruebas funcionales son solamente unitarias"** 
    👉 **INCORRECTA / EXCESIVA:** El repositorio contiene pruebas unitarias automatizadas. Sin embargo, las "pruebas funcionales de caja negra" descritas en el plan probablemente se hicieron de manera *manual* sobre la interfaz. Que no estén automatizadas no significa que no existieron.
*   **"El objetivo específico de validación está parcialmente cumplido"** 
    👉 **INCORRECTA:** Evaluar esto requiere ver los anexos documentales (las encuestas). No puedo calificarlo de parcial solo mirando el código.
*   **"El sistema tiene 147/192 pruebas automatizadas"** 
    👉 **CONFIRMADA:** La evidencia de ejecución real del motor `.NET Test` confirma 192 pruebas exitosas.
*   **Afirmaciones técnicas (Clean Architecture, GDAL, LiDAR, PostGIS, ArcGIS, ISO 19115)** 
    👉 **CONFIRMADA:** Existe evidencia absoluta y contundente en el código fuente.

---

### 3. ESPECIALMENTE: SCRUM (Límites de Git)

| Afirmación metodológica | ¿Git puede demostrarla? | Evidencia encontrada en repo | Conclusión correcta |
| :--- | :--- | :--- | :--- |
| **Product Backlog / User Stories** | Rara vez (salvo GitHub Issues) | Inexistente en el código | **No verificable** |
| **Sprint Planning / Review / Retro** | No (son reuniones) | Inexistente en el código | **No verificable** |
| **Sprints (Ciclos de tiempo)** | Parcialmente (mirando fechas de commits) | Commits continuos, agrupables empíricamente en fases | **No verificable formalmente** |
| **Incrementos funcionales** | Sí (entregables funcionales sumativos) | Sí, las funcionalidades se añadieron incrementalmente | **Sólidamente demostrado** |
| **Roles (Scrum Master, PO)** | No | Un solo committer técnico (`main`) | **No verificable** |

---

### 4. METODOLOGÍA REAL VS METODOLOGÍA PLANIFICADA

*   **Lo planificado (A):** Scrum adaptado, ciclos de 2 semanas, entrevistas ágiles, feedback estructurado.
*   **Lo demostrado (B):** Desarrollo altamente iterativo e incremental. El software fue sumando capas de complejidad funcionales verificables en el historial de Git.
*   **Defensa académicamente honesta:** Salvo que existan actas, tickets o un tablero Trello/Jira que demuestre los rituales, la forma más segura e inatacable de describirlo en el documento de grado es como un **"Enfoque de desarrollo iterativo e incremental basado en principios ágiles"**. Esto valida la evolución del software sin obligarte a presentar papelería burocrática de Scrum (Dailies, Retrospectivas) que un jurado estricto podría exigir.

---

### 5. VALIDACIÓN Y PRUEBAS (Desambiguación)

*   **Pruebas unitarias/integración (Caja Blanca automatizada):** Existen, son reales y están en `Geomatica.UnitTests`.
*   **Pruebas de Caja Negra (Funcionales operativas):** No existen como *scripts automatizados* (ej. no hay Selenium o WinAppDriver). El plan indica "Verificación de casos de uso". Esto es totalmente válido si se hizo mediante **ejecución manual** guiada por una matriz de pruebas. Es **Pendiente de evidencia externa**.
*   **Pruebas de Usabilidad / Usuarios (SUS):** **Pendiente de evidencia externa**.

---

### 6. VERIFICAR LAS 147 VS 192 PRUEBAS

1.  **Origen del 147:** En documentación anterior del proyecto y artefactos metodológicos (como el archivo `estructura_monografia.md`), se planificó y registró la existencia de "147 casos de prueba".
2.  **Origen del 192:** Durante mi revisión anterior, el sistema ejecutó en segundo plano el comando `dotnet test`. El motor de pruebas compiló y arrojó: *Total: 192, Superado: 192, Duración: 37 s*.
3.  **Conclusión:** Ambos números son reales, pero el 147 corresponde a documentación antigua o a un hito anterior. **Actualmente existen 192 pruebas, todas se ejecutaron realmente y pasaron sin errores.**

---

### 7. AUDITORÍA TÉCNICA

| Afirmación Técnica | Evidencia concreta en código | ¿La evidencia permite afirmarla? | Confianza |
| :--- | :--- | :--- | :--- |
| **Clean Architecture / MVVM** | Proyectos separados (`.Domain`, `.Data`, `.Desktop`), Inyección de dependencias (`App.xaml.cs`), ViewModels y Views | Sí. Las abstracciones y reglas de dependencia (Core no depende de UI) se cumplen. | **ALTA** |
| **PostgreSQL / PostGIS** | `ProyectoRepository.cs` usa `Npgsql`. Se evidencian comandos SQL con ST_Intersects y SRID 4686. | Sí. | **ALTA** |
| **ArcGIS Maps SDK** | `MapaView.xaml` y `MapaView.xaml.cs` usan referencias a `Esri.ArcGISRuntime`. | Sí. | **ALTA** |
| **GDAL / GeoTIFF** | `GeoTiffSidecarResolver.cs` importa y usa `MaxRev.Gdal.Core`. | Sí. | **ALTA** |
| **LiDAR / Background Workers** | `LidarBackgroundWorker.cs` maneja proyecciones geodésicas desacopladas del hilo principal (UI). | Sí. Hay multihilo real. | **ALTA** |
| **ISO 19115** | Clase `Iso19115MetadataExtractor.cs` extrae y mapea campos al estándar. | Sí. | **ALTA** |
| **Migraciones de BD formales** | Se usan validaciones en código (`AsegurarTablaAuditoriaAsync`) en lugar de migraciones EF Core o Flyway. | Sí. Es funcional pero menos estructurado. | **ALTA** |

---

### 8. MATRIZ FINAL DE CUMPLIMIENTO

| Elemento del Plan | Evidencia real | Estado | Qué podemos afirmar | Qué NO podemos afirmar | Evidencia externa necesaria |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Obj 1: Arquitectura | Código fuente (Estructura de proyectos) | **CUMPLIDO** | Diseño por capas, patrones MVVM. | Que sea la única arquitectura viable. | Ninguna |
| Obj 2: Base de Datos Espacial | Código en `.Data` y repositorios SQL | **CUMPLIDO** | Uso de PostGIS, índices espaciales. | - | Ninguna |
| Obj 3: Interfaz Cartográfica | WPF, ArcGIS SDK | **CUMPLIDO** | Búsqueda, mapas interactivos. | - | Ninguna |
| Obj 4: Carga de Archivos | Servicios GDAL, LiDAR, FileGDB | **CUMPLIDO** | Soporte para formatos pesados. | - | Ninguna |
| Obj 5: Validación y Retroalimentación | 192 Unit Tests exitosos | **NO VERIFICABLE** | Estabilidad algorítmica y de backend. | Que los usuarios validaron la herramienta. | Resultados Encuesta SUS / Reportes usuarios |
| Metodología: Scrum | Git no lo demuestra | **NO VERIFICABLE** | Software se construyó incrementalmente. | Que hubo Sprints rígidos o Scrum Master formal. | Tableros Trello/Jira, Actas de revisión |

---

### 9. RIESGO PARA LA SUSTENTACIÓN

*   🔴 **CRÍTICO:** Afirmar la aplicación de metodologías formales (Scrum o encuestas SUS) en el documento de grado **sin anexar la evidencia correspondiente**. Un jurado metodológico exigirá ver los resultados del SUS o el Backlog. (Solución: Adjuntar los anexos, o suavizar la redacción en el texto).
*   🟠 **ALTO:** Afirmar que se hizo "validación de caja negra" sin tener un anexo que liste qué casos de uso manuales se probaron y cuáles fueron los resultados.
*   🟡 **MEDIO:** Que el jurado cuestione cómo se administra la base de datos sin un ORM o motor de migraciones formales (usaste raw Npgsql). (Se defiende argumentando rendimiento y control directo sobre consultas espaciales).
*   🟢 **BAJO:** Riesgos técnicos funcionales. El software evidencia una solidez inusual para un proyecto de pregrado.

---

### 11. RESULTADO FINAL

#### A. Lo que está sólidamente demostrado
*   El desarrollo íntegro, robusto y funcional del sistema a nivel arquitectónico, de base de datos (PostGIS), de interfaz cartográfica (ArcGIS) y procesamiento geoespacial avanzado (GDAL, LiDAR).
*   La existencia y éxito de **192 pruebas automatizadas** que garantizan la estabilidad del código base.

#### B. Lo que probablemente ocurrió pero actualmente no está suficientemente documentado
*   **Pruebas funcionales manuales (caja negra):** Es evidente que la interfaz funciona y tuvo que ser probada por ti iterativamente, pero no hay un documento formal de "Casos de Prueba Manuales" en el código.
*   **Gestión del Proyecto (Metodología Ágil):** Hubo planificación y entregas continuas, pero no están registrados los rituales metodológicos (backlog, reuniones).

#### C. Lo que realmente parece no haberse cumplido
*   (Nada en este rubro por ahora. Todo lo ausente cae en "No Verificable" o "Requiere evidencia externa"). No hay código que demuestre un rotundo fracaso en ningún objetivo técnico del plan.

#### D. Lo que necesitamos comprobar antes de modificar el documento de grado
Para poder redactar de forma segura los capítulos metodológicos y de validación, necesito que me confirmes si cuentas con:
1.  Un registro (Trello, Excel, Github Issues) de las **Historias de Usuario / Backlog** que usaste para guiarte.
2.  Los resultados (hoja de cálculo, PDF o capturas) de la **Encuesta SUS** o validaciones aplicadas a los integrantes de GEOMÁTICA.
3.  Matrices o tablas de **casos de prueba funcionales** que hayas ejecutado manualmente.
