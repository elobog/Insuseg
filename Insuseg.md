# Proyecto Insuseg — Analítica sobre SAP Business One (HANA)

## 1. Objetivo

Aplicación de analítica (ventas, compras, clientes, productos, vendedores, órdenes, stock) que lee datos de **SAP Business One edición HANA** (producción) y los expone en reportes/dashboards, corriendo sobre infraestructura **gratuita de Azure**.

**Equipo:** Eric y Ignacio — cada uno trabaja desde su propia máquina, sobre la misma carpeta de proyecto sincronizada por OneDrive (ver hallazgo operativo sobre `git` en sección 7). Cuando este archivo dice "el usuario" en secciones históricas anteriores a esta nota, se refiere a Eric.

## 2. Decisiones de arquitectura

| Decisión | Elección | Motivo |
|---|---|---|
| Lenguaje / runtime | **C# / .NET 10** | LTS actual (soporte hasta ~2028). .NET 8 sigue siendo LTS pero su soporte termina en nov-2026, muy pronto para arrancar un proyecto nuevo hoy. |
| Origen de datos SAP | **Service Layer** (REST/OData), no HANA SQL directo | Es la forma oficial soportada por SAP para integraciones; el driver ADO.NET de HANA es una alternativa pero el Service Layer ya está confirmado accesible. |
| Acceso a producción | **Solo lectura, siempre** | Regla explícita del proyecto — ver sección de reglas más abajo. |
| Base de datos de la app | **Azure SQL Database — Free tier** (100k vCore-seg/mes, 32GB, gratis permanente) | No usamos HANA como destino de escritura; los datos extraídos/transformados se guardan aquí. |
| Hosting | **Azure Functions (Consumption, gratis)** para ingestión periódica (Timer Trigger) + posible **Azure App Service (F1 Free)** o **Static Web Apps (Free)** para el front/API de analítica | Ajustado a trabajo por lotes (extracción periódica) más que a un servicio siempre encendido. |
| Autenticación de la app | **ASP.NET Core Identity** sobre la misma Azure SQL | Passwords **hasheados** (PBKDF2-HMAC-SHA256 + salt), nunca en texto plano ni reversibles. Estándar de .NET, gratis, control total. Se descartó Entra External ID (login gestionado) por ser más complejo de lo necesario para un equipo interno pequeño; se puede reconsiderar si se necesita SSO corporativo o login social. |
| Horizonte histórico de datos | **Desde el 1 de enero de 2024 en adelante**, para toda entidad que se sincronice desde SAP (Ventas, Compras, Inventario, y cualquier módulo futuro) | Decisión del proyecto (2026-07-26) — no se recolecta ni se analiza historia anterior a esa fecha, sin importar qué tan atrás llegue el dato en SAP. Reemplaza el criterio ad-hoc que se había usado antes por módulo (ej. `SalesIngestionFunction.InitialBackfillStartDate` ya usaba 2024-01-01 para Ventas; Compras usaba todo el historial desde 2000-01-01 por tener muy pocos registros — eso debe ajustarse a este mismo corte). Aplica también al re-sync pendiente contra `INSUSEG` (ver sección 7). |

## 3. Regla crítica del proyecto

> **NUNCA escribir, borrar ni modificar nada en la base de datos de producción de SAP (`INSUSEG`).**
> La aplicación solo lee datos de SAP (GET / SELECT) — nunca escribe, borra ni crea datos ahí, sin excepción. Cualquier prueba de escritura debe hacerse con datos simulados/locales, nunca contra un CompanyDB real de este servidor.

**Cuál CompanyDB es cuál (resuelto 2026-07-24 por el admin de SAP del cliente):** `INSUSEG` es producción. `INSUSEG_PRB` es la base de pruebas (usada por otra empresa para un sistema de órdenes de venta, no relacionada con este proyecto). Esto tuvo idas y vueltas dentro del proyecto los días 2026-07-20 y 2026-07-24 antes de quedar resuelto — ver historial en memoria del proyecto si hace falta.

Aun así, dado que `INSUSEG_PRB` no es responsabilidad de este proyecto y no hay necesidad de escribir en ningún CompanyDB, el acceso se mantiene de solo lectura sobre ambos por defecto.

## 4. Conexión a SAP B1 Service Layer

- URL: `https://159.69.163.254:50003/b1s/v1/`
- CompanyDB: `INSUSEG` (producción — fuente real de datos para este proyecto)
- SAP B1 versión detectada: ~9.3 PL10 (Version 930210), Apache 2.4.34 / SLES
- **Limitación crítica de seguridad detectada:** el servidor **solo acepta TLS 1.0** (protocolo deprecado desde 2020). Confirmado con `openssl s_client` — TLS 1.1 y 1.2 fallan con "unsupported protocol".
  - Windows Schannel y el `HttpClient` por defecto de .NET **rechazan TLS 1.0**. El proyecto C# deberá forzar explícitamente `SslProtocols.Tls` en el `HttpClientHandler`/`SocketsHttpHandler` para poder conectarse.
  - **Recomendación pendiente de comunicar al cliente:** actualizar la configuración TLS del Apache/Service Layer del servidor SAP a TLS 1.2 como mínimo — es un riesgo de seguridad real, expuesto en IP pública, independiente de este proyecto.
- Credenciales: no se documentan en este archivo por seguridad. Ver gestor de contraseñas / variables de entorno del proyecto.

## 5. Esquema de datos SAP

Ver [BDhana.md](BDhana.md) — documenta las entidades del Service Layer relevantes (`BusinessPartners`, `Items`, `SalesPersons`, `Orders`, `PurchaseOrders`, `Invoices`, `PurchaseInvoices`, stock por almacén vía `ItemWarehouseInfoCollection`), sus campos clave, campos personalizados (`U_*`) detectados y relaciones entre entidades.

## 6. Recursos creados en Azure

Suscripción: **Insuseg** (`d811fd57-192a-45fc-954d-be2e3224b3ba`), tenant `Melirrepu.com`.

| Recurso | Nombre | Región | Detalle |
|---|---|---|---|
| Resource Group | `rg-insuseg-analytics` | Central US | — |
| SQL Server (lógico) | `sql-insuseg-centralus` | Central US | Endpoint: `sql-insuseg-centralus.database.windows.net`. Admin user: `sqladmin_insuseg` (password en gestor de contraseñas, no en este repo). |
| Azure SQL Database | `sqldb-insuseg-analytics` | Central US | Edición GeneralPurpose Gen5, Serverless. **Ya no está en free tier** (`useFreeLimit=False` — se dejó de aplicar en algún momento, sin causa confirmada; ahora se factura). **Tamaño máximo reducido a 5 GB** (2026-07-26, antes 32 GB) para reducir el costo de almacenamiento — el uso real (~42 MB al momento del cambio) da margen para años de crecimiento incluso con el nuevo tope. |
| Firewall rule | `AllowAzureServices` | — | `0.0.0.0` (permite servicios de Azure, ej. Functions) |
| Firewall rule | `AllowMyIP` | — | IP pública del equipo de desarrollo usado al momento de crear el recurso — **hay que agregar una regla nueva por cada IP/equipo adicional que necesite conectarse** (ver Pasos Preliminares). |
| Firewall rule | `AllowTerminal-IGANCIO` | — | `200.124.42.167` — IP de salida de la terminal/entorno usado para correr `dotnet ef database update` el 2026-07-22. Puede diferir de la IP del navegador/VS en el mismo equipo. |
| Firewall rule | `AllowMyIP-NB_2026_ELG` | — | `200.29.170.214` — agregada el 2026-07-24 para desarrollo local. |
| Firewall rule | **`AllowAllIPs`** | — | **`0.0.0.0`–`255.255.255.255` — agregada el 2026-07-24, decisión explícita del usuario para permitir que el cliente pruebe el sistema desde cualquier red sin gestionar IPs individuales.** El SQL Server queda expuesto a internet; la única barrera de acceso pasa a ser la password del usuario `sqladmin_insuseg`. Reevaluar (cerrar o migrar a despliegue en Azure App Service) antes de ir a producción real con el cliente. |
| ~~Storage Account~~ | ~~`stinsusegingest`~~ | Central US | **Borrado el 2026-07-26** — solo existía para soportar el Function App, que también se borró. |
| ~~Function App~~ | ~~`func-insuseg-ingestion`~~ | Central US | **Borrado el 2026-07-26** — decisión explícita del usuario para reducir a solo lo necesario (app web local + SQL) mientras se resolvía la pausa por cupo gratuito agotado. Esta era la ruta de sincronización automática por horario, ya shelved/sin uso hacía días (ver sección "Solución alternativa" más abajo) — no se pierde funcionalidad real. |
| ~~Application Insights~~ | ~~`insuseg-ingestion-insights`~~ | Central US | **Borrado el 2026-07-26**, junto con su grupo de alertas huérfano `Application Insights Smart Detection` (recurso automático que Azure crea junto con todo App Insights y no se borra solo). |

**Recursos restantes en `rg-insuseg-analytics` después de la limpieza (2026-07-26):** solo `sql-insuseg-centralus` y `sqldb-insuseg-analytics`. El "Admin de Azure AD" (`info@aitbp.com`) y el usuario `func-insuseg-ingestion` dentro de la base (ambos ligados al Function App borrado) no son recursos de Azure facturables — quedaron como configuración huérfana pero gratis, no se tocaron.

**Azure CLI:** instalado y logueado en esta máquina (2026-07-22) — sesión con la cuenta `info@aitbp.com`. Tuvo que resolverse la intercepción SSL de Norton varias veces (ver Pasos Preliminares, paso 2) — dominios agregados en total: `management.azure.com`, `login.microsoftonline.com`, `graph.windows.net`, `*.database.windows.net`, `*.core.windows.net`, `graph.microsoft.com`, `*.azurewebsites.net`, `aka.ms`, `api.applicationinsights.io`. Cada servicio nuevo de Azure puede requerir agregar su propio dominio.

**⚠️ Gotcha descubierto el 2026-07-26: la suscripción por defecto de esta sesión de `az` NO es "Insuseg".** `az account show` devolvió la suscripción `sosgroup` (`181bad51-59e7-409a-ba4c-df66d1461b55`) como default — la cuenta tiene acceso a dos suscripciones en el mismo tenant (`Melirrepu.com`). Hay que correr `az account set --subscription d811fd57-192a-45fc-954d-be2e3224b3ba` (o `--subscription Insuseg`) **al empezar cada sesión** antes de cualquier comando `az` sobre este proyecto, o se corre el riesgo de operar (o no encontrar nada) en la suscripción equivocada.

**Proveedores de recursos registrados en la suscripción** (paso único por proveedor, ya hecho): `Microsoft.Sql`, `Microsoft.ContainerInstance`, `Microsoft.Storage`, `Microsoft.Web`, `Microsoft.Insights`/`microsoft.insights`, `microsoft.operationalinsights`. Si aparece el error `MissingSubscriptionRegistration` o `SubscriptionNotFound` al crear un recurso de un tipo nuevo, es casi seguro que falta registrar su proveedor (`az provider register --namespace <Microsoft.X>`, esperar a que quede `Registered`).

**Nota sobre la región:** se planeó usar `East US 2` pero Azure no aceptaba provisión de nuevos servidores SQL ahí ni en `East US` en el momento de la creación (restricción temporal de capacidad de Azure). Se usó `Central US` como alternativa funcional más cercana.

## 7. Pendientes / próximos pasos

> **Actualización misma sesión (2026-08-19/20), más tarde todavía: bug real de despliegue — la app se veía "sin estilos" (sin CSS ni logo) en producción.** El cliente reportó que la página de login se veía como HTML puro, sin diseño. Diagnosticado con un endpoint temporal (`/__debug/wwwroot`, ya sacado) que listaba los archivos reales en el servidor: **`env.WebRootPath` daba `null`**, y los archivos de `wwwroot/` aparecían en el disco de Azure con nombres literales como `wwwroot\css\site.css` (con `\` adentro del nombre, no como carpeta real) — Azure interpretaba mal las carpetas anidadas al montar el paquete `.zip` en modo `WEBSITE_RUN_FROM_PACKAGE` (Linux). **Causa raíz encontrada:** los `.zip` armados en Windows (`Compress-Archive` de PowerShell, o `System.IO.Compression.ZipFile.CreateFromDirectory`) marcan sus entradas con atributo de sistema "Windows/FAT" en vez de "Unix" — el montaje de Azure en Linux, al leer ese atributo, no reconstruye bien las carpetas anidadas para esas entradas (los archivos en la raíz del zip sí funcionaban bien, solo los que estaban dentro de subcarpetas se rompían). Se probó primero desactivar `WEBSITE_RUN_FROM_PACKAGE` (que Azure descomprima a disco en vez de montar el zip) — **empeoró las cosas** (quedó la página de bienvenida por defecto de Azure, sin contenido), así que se revirtió a `WEBSITE_RUN_FROM_PACKAGE=1`.
>
> **Fix real: armar el `.zip` marcando explícitamente sus entradas como Unix.** Con PowerShell no se pudo lograr (ambas herramientas de Windows probadas fallan igual) — hay que usar Python (`zipfile`, ya viene con la instalación de Python de esta máquina) seteando `ZipInfo.create_system = 3` en cada entrada. Comando completo para el próximo despliegue (ajustar solo las rutas de origen/destino):
> ```powershell
> dotnet publish "Proyectos\src\Insuseg.Analytics.Api\Insuseg.Analytics.Api.csproj" -c Release -o "$env:TEMP\publish_insuseg"
> & "C:\Users\inlj1\AppData\Local\Programs\Python\Python312\python.exe" -c @"
> import zipfile, os, time
> src = os.path.join(os.environ['TEMP'], 'publish_insuseg')
> dst = os.path.join(os.environ['TEMP'], 'insuseg_deploy.zip')
> if os.path.exists(dst): os.remove(dst)
> with zipfile.ZipFile(dst, 'w', zipfile.ZIP_DEFLATED) as zf:
>     for root, dirs, files in os.walk(src):
>         for f in files:
>             full = os.path.join(root, f)
>             rel = os.path.relpath(full, src).replace(os.sep, '/')
>             zi = zipfile.ZipInfo(rel, date_time=time.localtime(os.path.getmtime(full))[:6])
>             zi.compress_type = zipfile.ZIP_DEFLATED
>             zi.create_system = 3  # Unix — esto es lo que arregla el montaje en Azure Linux
>             zi.external_attr = (0o644 << 16)
>             with open(full, 'rb') as fh: zf.writestr(zi, fh.read())
> "@
> az webapp deploy --resource-group rg-aitbp-app --name app-insuseg --src-path "$env:TEMP\insuseg_deploy.zip" --type zip
> ```
> **Nunca usar `Compress-Archive` ni `ZipFile.CreateFromDirectory` para armar el paquete de este deploy** — ambos reproducen el bug. Se agregó además `builder.Environment.WebRootPath` explícito en `Program.cs` como respaldo defensivo (no depende de que el zip esté bien armado), pero el fix real es el de arriba. Verificado en producción de punta a punta después del fix: CSS, logo y JS cargan (200), login se ve con el diseño de marca completo.
>
> **Actualización misma sesión (2026-08-19), más tarde: primer despliegue real a Azure de todo lo acumulado desde el 2026-08-08** (ItemCategory, ApplicationUser, DeliveryNotes, los dos bugs de arriba) — nunca se había desplegado nada de esto, `app-insuseg` seguía corriendo el build del 2026-08-17. Desplegado a mano con `az webapp deploy` (zip deploy, `rg-aitbp-app`, subscripción `AITBP_APP` — no confundir con la subscripción `Insuseg` donde vive la base de datos, `az account set --subscription` para cambiar). **La app quedó caída (503) las primeras veces**, muriendo siempre en el mismo punto: la primera consulta a la base al sembrar roles (`RoleManager.RoleExistsAsync` → `Program.cs`), con exit code 134 (`Aborted`, sin ningún mensaje de excepción legible en el log — Azure App Service on Linux mata el proceso entero ante una excepción sin capturar en `Main`). Corrida la misma DLL publicada localmente contra la misma base de datos real: **arrancó perfecto, sin error** — descarta un bug de código o de datos, apunta a algo específico del contenedor de Linux en el primer arranque (mismo tipo de fragilidad ya documentado con el Service Layer de SAP, aunque acá es contra Azure SQL, no SAP). **Fix aplicado (además de diagnóstico):** el bloque de sembrado de roles en `Program.cs` ahora tiene `try/catch` — si vuelve a fallar, se registra el error completo en el log y la app sigue levantando igual (antes tumbaba todo el sitio). Redesplegado con el fix — **la app levantó bien y quedó estable** (confirmado con varios `curl` seguidos, HTTP 200). De paso se activó el logging de aplicación en `app-insuseg` (estaba completamente apagado, `az webapp log config --application-logging filesystem --level information`) — sin esto no se hubiera visto ni siquiera el stack trace parcial del error original.
>
> **Siguiente paso al retomar (fin de sesión 2026-08-19):** se encontró y corrigió un segundo bug real, más grave que el de Guías: `SalesSyncService` guardaba la cabecera de cada venta (`Sales`) y sus líneas (`SaleLines`) en **dos `SaveChangesAsync()` separados**. Si el proceso se caía justo entre medio (mismo colgón intermitente de SAP ya documentado, confirmado que volvió a pasar dos veces en esta sesión), la cabecera quedaba guardada sin sus líneas — y como la sincronización incremental usa `MAX(SaleDate)` de `Sales` como watermark, esos documentos nunca se volvían a pedir: quedaban huérfanos para siempre, sin ningún aviso. Encontrados **78 facturas reales** así (\$47.307.903 con IVA), todas entre 2026-08-07 y 2026-08-11 — un solo sync que se cortó a mitad de camino. Para Carlos Cortes puntualmente eran 10 documentos, \$5.064.120 netos — coincide exacto con la diferencia que el usuario reportó al inicio de esta sesión (aunque en ese momento se investigó primero, sin éxito, el lado de Guías). **Corregido:** los dos upserts ahora solo modifican el `ChangeTracker`de EF Core (`StageSalesUpsert`/`StageSaleLinesUpsert`), un único `SaveChangesAsync` al final de `SincronizarFuenteAsync` hace ambos atómicos. Se agregó `SalesSyncService.BackfillRangeAsync(desde, hasta)` (rango puntual, sin recorrer todo el historial como `forceFullResync`) y se usó para reparar los 78 documentos — verificado con `SELECT` directo: 0 documentos huérfanos en toda la base después del backfill, y el neto de Carlos Cortes de agosto ahora coincide exacto con una consulta hecha directo contra SAP (\$23.363.870). **Validación final con el usuario:** facturas netas (\$23.363.870) menos notas de crédito (\$856.650, ya guardadas en negativo) da \$22.507.220 — el total de "Facturas" que muestra Cartera para Carlos Cortes en agosto (sin sumar Guías), confirmado como correcto.
>
> **Siguiente paso al retomar (fin de sesión 2026-08-18):** se encontró y corrigió un bug real: la columna "Guías" de `Ventas/Cartera` (construida en una sesión anterior, ver más abajo "Mostrar guías de despacho en Cartera") sumaba `DeliveryNoteLines`, pero **`DeliveryNoteSyncService` nunca llegó a sincronizar esa tabla** — solo guardaba las cabeceras (`DeliveryNotes`), dejando `DeliveryNoteLines` congelada con datos viejos (692 guías abiertas, solo 54 con alguna línea guardada). `Cartera` mostraba un total de Guías muy por debajo del real sin que nada avisara del problema. **Corregido:** `SapDeliveryNoteDto`/`GetOpenDeliveryNotesAsync` ahora traen `DocumentLines` (mismo patrón que `GetSalesDocumentsAsync`), y `DeliveryNoteSyncService.SyncAsync()` aplica la fórmula validada el 2026-08-14/16 (línea con `LineStatus='bost_Open'`, documento sin texto de no-venta en `Comments`/`NumAtCard`, `LineTotal ≥ $1.000`, agrupado por vendedor de LÍNEA) para reconstruir `DeliveryNoteLines` completa en cada corrida (mismo criterio full-replace que ya usaba `DeliveryNotes`, sin FK/cascada real entre las dos tablas — confirmado contra `sys.foreign_keys` — así que el borrado de líneas huérfanas se hace a mano). Corrido contra SAP + Azure SQL reales para verificar: 692 abiertas, 98 venta real (a nivel documento), **202 líneas reales quedaron sincronizadas** (antes solo 23, y solo para 1 de 7 vendedores). Carlos Cortes: \$1.926.640 en guías reales pendientes hoy (23 líneas) — mismo monto que ya había en la tabla vieja para él puntualmente (sus guías no se movieron en estos días), pero ahora es un número recalculado en vivo, no una sobra vieja. **Pendiente real:** el hallazgo de "INGRESO FALSO" del 2026-08-16 (tarea asignada a Ignacio, ver más abajo) sigue sin resolver — sigue excluido del cálculo hasta confirmar con ventas.
>
> **Siguiente paso al retomar (fin de sesión 2026-08-16):** el cliente entregó una tabla actualizada de "Total Guías" por vendedor (misma planilla que el 2026-08-14, valores nuevos — los montos cambiaron porque guías se van facturando/agregando día a día). Se corrió de nuevo la sincronización real de `DeliveryNotes` contra SAP (681 guías abiertas, 92 venta real — vía `DeliveryNoteSyncService`, la misma clase del botón "Sincronizar ahora", ejecutada desde un runner temporal por conveniencia) y se recalculó la fórmula validada el 2026-08-14 contra los valores nuevos. **Resultado: 6 de 7 vendedores calzan exacto** (antes 4 de 7, con $3,6M de diferencia total) — ver detalle abajo, "✅ Segunda validación". Solo queda Mariana Sánchez con $59.340 de diferencia, y esta vez **se identificó la línea exacta que la explica** (no es un misterio como el 2026-08-14): una guía de Laboratorios Saval con el comentario "INGRESO FALSO" que la fórmula excluye a propósito (por diseño, ver `EsMuestraOCambio`) pero que el cliente sí está contando en su tabla. Pendiente real: confirmar con el equipo de ventas si "INGRESO FALSO" debería contar como pendiente de facturar — si la respuesta es sí, hay que sacar `INGRESO FALSO` del patrón de exclusión (`DeliveryNoteSyncService.PatronNoVenta` e igual en el script de validación).

> **Siguiente paso al retomar (fin de sesión 2026-08-14):** se validó contra una tabla de referencia real del cliente ("Total Guías" por vendedor) la fórmula de guías pendientes de facturar — ver "✅ Fórmula validada" más abajo. **4 de 7 vendedores calzan exacto**; quedan Carlos Cortés, Insuseg y Mariana Sánchez con ~$3.6M de diferencia total que no se pudo reconstruir desde SAP bajo ningún filtro probado (fecha, estado de línea, texto) — siguiente paso es validarlo directo con el equipo de ventas, no seguir ajustando parámetros en SAP. Aparte, se implementó y dejó documentado el sync de `DeliveryNotes` (ver sección más abajo) y se resolvió un conflicto real de trabajo en paralelo con Ignacio (`git` no instalado en esta máquina).

> **Siguiente paso al retomar (fin de sesión 2026-08-08):** sesión larga, todo en `Ventas → Cartera de clientes` (el único módulo activo desde el recorte del 2026-08-07). Cuatro cosas nuevas, ver detalle en las secciones de abajo: (1) los dos gráficos ("Tendencia" y "Margen por mes") ahora tienen puntos de estado por mes comparando contra el año anterior; (2) el detalle por cliente pasó de mostrar productos directo a mostrar **categoría → producto** (dos niveles), lo que llevó a descubrir y modelar bien la tabla de categorías de SAP (`U_ZCAT`); (3) se encontraron y corrigieron **tres bugs reales distintos** de scroll en las tablas anidadas (no uno solo — cada intento de arreglo destapó el siguiente); (4) la tabla "Ventas por cliente" ahora tiene buscador y encabezados ordenables. Todo verificado en vivo con Playwright + credenciales reales (`elobog@Melirrepu.com`), no solo en teoría.
>
> **Pendiente nuevo, real, no resuelto:** la sincronización de Inventario/categorías (`InventorySyncService`, botón "Sincronizar productos") se colgó **2 de 3 veces** esta sesión corriéndola fuera de la app (mismo servicio, sin tocar código de sync) — siempre a mitad de la paginación de `Items` (~25.000 productos, ~255 páginas), nunca en la parte de categorías (rápida, 29 filas). Confirmado con `netstat` que el proceso quedaba sin ninguna conexión de red activa (no es "lento", está realmente trabado) — no se identificó la causa exacta, pero encaja con la fragilidad ya documentada de este servidor (TLS 1.0, Apache viejo, ver sección 4). Cuando el usuario corrió el mismo botón desde el navegador, funcionó bien. Mitigación por ahora: si se cuelga, reintentar.
>
> Pendientes de sesiones anteriores, sin tocar: (1) el módulo de Compras (cuando se reconstruya) probablemente tiene el mismo problema de notas de crédito que tuvo Ventas — nunca investigado. (2) sigue pendiente comunicar al cliente el hallazgo de los 7 documentos con vendedor mal asignado en SAP (ver sesión 2026-08-02). (3) login del papá del usuario — ya se probó y **funciona** (`elobog@Melirrepu.com`, usado esta sesión para verificar todo en vivo) — este pendiente queda cerrado. (4) Nada de esto está desplegado a Azure todavía, todo corre local con `dotnet run`. (5) El sync de Ventas (Facturas) tarda ~31 minutos por el volumen real (16.904 facturas) — si se vuelve un problema de UX, evaluar acotar los campos de `DocumentLines` que se piden a SAP. (6) El detalle por producto de Cartera (AJAX) sigue tardando ~6 segundos pese al índice en `Sales.SaleDate` — mejora real (antes ~9s) pero no resuelto del todo. Nota: el AJAX ahora trae *categorías* primero, no productos directo — puede haber cambiado el tiempo, no vuelto a medir.

### 📋 Tarea asignada a Ignacio: sacar "INGRESO FALSO" del patrón de exclusión (pendiente desde 2026-08-16)

**Quién la hace: Ignacio, no Eric/esta máquina.** Paso a paso completo para que la ejecute él mismo, tocando código por primera vez en esta máquina compartida por OneDrive:

**0. Condición previa — no saltear:** confirmar primero con el equipo de ventas si "INGRESO FALSO" (guías de servicio de estampado sobre tela propia del cliente) debe contar como pendiente de facturar. Sin esa confirmación, no se debe tocar el código todavía. Contexto completo del hallazgo: ver "✅ Segunda validación" más abajo.

**1. Instalar `git` en su máquina**, si todavía no lo tiene (pendiente de infraestructura ya documentado más abajo, sección del hallazgo 2026-08-14). Sin esto no puede versionar nada de forma segura.

**2. Antes de tocar una sola línea, sincronizar y verificar que no hay trabajo a medias:**
```powershell
# Esperar a que OneDrive termine de sincronizar (ícono de OneDrive "al día", no "sincronizando")
git status
git log --oneline -5
```
Si `git status` muestra cambios sin comitear que él no hizo, o `git log` no coincide con lo esperado, **parar y avisar antes de seguir** — puede ser señal de que alguien más está trabajando en paralelo (el mismo problema que ya pasó el 2026-08-14, ver más abajo).

**3. Avisar por chat antes de empezar:** "voy a modificar `DeliveryNoteSyncService.cs`, no toquen nada hasta que avise que ya comiteé" — para que nadie más edite código al mismo tiempo.

**4. Hacer el cambio** — en `Insuseg.Analytics.Data/Sync/DeliveryNoteSyncService.cs`, línea del patrón `PatronNoVenta` (hoy línea 24), sacar `INGRESO FALSO|`:
```csharp
// Antes:
"MUESTRA|CAMBIO|^LOGO|INGRESO FALSO|NO FACTURAR|DONACION|CALIDAD|CONSUMO",
// Después:
"MUESTRA|CAMBIO|^LOGO|NO FACTURAR|DONACION|CALIDAD|CONSUMO",
```

**5. Compilar:**
```powershell
dotnet build "Proyectos\src\Insuseg.Analytics.Api\Insuseg.Analytics.Api.csproj"
```

**6. Probar en vivo** — correr la app local y usar el botón real "Sincronizar ahora" en Ventas/Sincronización, confirmar que la guía de Laboratorios Saval ($59.340) ahora cuenta como venta real:
```powershell
dotnet run --project "Proyectos\src\Insuseg.Analytics.Api\Insuseg.Analytics.Api.csproj"
```

**7. Actualizar este mismo archivo (`Insuseg.md`)** — agregar una línea corta en esta sección diciendo qué cambió y cuándo (ej. "2026-08-XX: confirmado con ventas que INGRESO FALSO sí cuenta, se sacó del patrón de exclusión").

**8. Comitear en un solo commit:**
```powershell
git add Proyectos\src\Insuseg.Analytics.Data\Sync\DeliveryNoteSyncService.cs Proyectos\Insuseg.md
git commit -m "Sacar INGRESO FALSO del patron de exclusion de DeliveryNotes (confirmado con ventas)"
```

**9. Avisar por chat que ya comiteó** — recién ahí el resto del equipo puede volver a tocar código.

No hace falta `git push`: el repo no tiene remoto configurado (todo local, sincronizado solo por OneDrive) — el commit ya queda disponible para el resto en cuanto OneDrive termine de subir/bajar los archivos del `.git`.

### ✅ Segunda validación contra tabla actualizada del cliente: 6 de 7 vendedores exactos (2026-08-16)

El cliente envió una foto nueva de la misma planilla ("Total Guías" por vendedor), con montos distintos a los del 2026-08-14 (es un estado vivo — guías se facturan y se agregan día a día, no es una corrección de la tabla anterior). Se aprovechó para: (1) refrescar `DeliveryNotes` en Azure SQL con la sincronización real de la app (`DeliveryNoteSyncService` — misma clase del botón "Sincronizar ahora" en Ventas/Sincronización, corrida desde un runner de consola temporal por conveniencia, sin agregar ningún endpoint nuevo) y (2) recalcular la fórmula validada el 2026-08-14 (líneas con `LineStatus='bost_Open'`, agrupadas por vendedor de línea, excluyendo muestra/cambio/etc por texto+monto) contra los DocumentLines reales de SAP con un script ad-hoc de solo lectura, igual que la vez anterior.

**Tabla del cliente (2026-08-16):**

| Vendedor | Total Guías (cliente) |
|---|---|
| Karihosqui Calderon | \$62.418.280 |
| Insuseg | \$9.712.730 |
| Carlos Cortes | \$3.798.020 |
| Marcela Espinoza | \$2.575.070 |
| Luz Lacruz | \$1.611.960 |
| Mariana Sanchez | \$1.598.630 |
| Florimar Rodriguez | \$60.640 |
| **Total General** | **\$81.775.330** |

**Resultado (fórmula vs. tabla del cliente):**

| Vendedor | Calculado | Cliente | Diferencia |
|---|---|---|---|
| Karihosqui Calderon | \$62.418.280 | \$62.418.280 | exacto |
| Insuseg | \$9.712.730 | \$9.712.730 | exacto |
| Carlos Cortes | \$3.798.020 | \$3.798.020 | exacto |
| Marcela Espinoza | \$2.575.070 | \$2.575.070 | exacto |
| Luz Lacruz | \$1.611.960 | \$1.611.960 | exacto |
| Florimar Rodriguez | \$60.640 | \$60.640 | exacto |
| Mariana Sanchez | \$1.539.290 | \$1.598.630 | -\$59.340 |
| **Total** | **\$81.715.990** | **\$81.775.330** | **-\$59.340 (0,07%)** |

**Diferencia de Mariana Sánchez, explicada al 100% (a diferencia del 4,4% sin explicar del 2026-08-14):** una sola guía, doc `24824` (cliente Laboratorios Saval S.A.), línea de \$59.340, con el comentario textual *"CLIENTE LAS TRAJO SOLO PARA SERVICIO DE ESTAMPADO. NV PARA HACER GD DE TALLER. INGRESO FALSO DE LAS PARKAS"*. La fórmula la excluye a propósito porque matchea `INGRESO FALSO` en el patrón de no-venta (`DeliveryNoteSyncService.PatronNoVenta`) — el criterio asume que "ingreso falso" significa que no es venta real (el cliente trajo sus propias parkas solo para el servicio de estampado). El cliente, sin embargo, sí la está contando en su total. **No se cambió el código todavía** — antes de sacar `INGRESO FALSO` del patrón de exclusión (lo que también afectaría a cualquier otra guía con ese mismo comentario) hay que confirmar con el equipo de ventas si este tipo de caso (servicio de estampado sobre prenda propia del cliente) debe contar como pendiente de facturar o no.

**Sincronización real ejecutada:** `DeliveryNoteSyncService.SyncAsync()` corrido contra la Azure SQL de producción (`sqldb-insuseg-analytics`) — 681 guías abiertas, 92 clasificadas como venta real, 0 removidas (nada se facturó/canceló desde el último sync). Mismo comportamiento que si se hubiera apretado el botón desde el navegador. El runner temporal (consola .NET referenciando `Insuseg.Analytics.Data` directamente, sin tocar la app) se creó y se borró en esta sesión, no quedó nada nuevo en el repo.

### ✅ Fórmula validada contra tabla real del cliente: "Total Guías" por vendedor (2026-08-14)

El usuario entregó una tabla real (foto de una planilla/reporte interno) con el total de guías pendientes de facturar por vendedor — 7 vendedores + total general \$81,328,355 — pedida para validar/corregir la lógica del hallazgo de arriba. Se hizo ingeniería inversa iterativa contra `INSUSEG` real hasta encontrar la fórmula:

**Fórmula final:** `DeliveryNotes` abiertas y no canceladas → de esas, solo las **líneas** con `LineStatus = 'bost_Open'` (no las que ya se facturaron individualmente aunque el documento completo siga abierto por otra línea) → excluir por texto en `Comments`/`NumAtCard` (`MUESTRA`, `CAMBIO`, `LOGO`, `INGRESO FALSO`, `NO FACTURAR`, `DONACION`, `CALIDAD`, `CONSUMO`) + piso de \$1.000 → agrupar por el `SalesPersonCode` **de la línea**, no de la cabecera (puede diferir, ver `BDhana.md` sección 4) → **sin restricción de fecha** (se probó acotar a 2026 y a 2025+2026, dio peor resultado que no acotar nada).

**Hallazgos en el camino:**
- **`PickStatus`/`BackOrder` no sirven** — vienen con el mismo valor (`tNO`/`NotPicked`/`tYES`) en las 1.973 líneas revisadas; esta empresa no usa el módulo de Pick & Pack de SAP.
- **`OpenAmount` (a nivel de línea) no es confiable en `DeliveryNotes`** — no se reduce a 0 cuando `LineStatus` pasa a `Closed`, sigue mostrando el `LineTotal` completo. El campo confiable es `LineStatus`, no `OpenAmount`.
- **Bug real encontrado en la propia extracción:** la paginación del Service Layer devolvió un documento duplicado (`DocEntry 20388`, \$16.9M) — corregido con `Sort-Object DocEntry -Unique` antes de sumar. Sin este fix, Karihosqui Calderon aparecía con \$23M de exceso en vez de calzar exacto.
- **Se buscó un reporte/consulta nativo de SAP que generara esta tabla — no existe.** Se revisaron las 344 consultas guardadas en `UserQueries` (Query Manager) — las únicas en categorías custom del cliente ("Ventas", "Compras", "General") son sobre `Invoices`/`CreditNotes` (facturación) y un chequeo de stock, nada sobre guías pendientes. Se revisaron también las 21 tablas propietarias `U_Z*` del sistema (`U_ZWHS1`, `U_ZPROVEE`, `U_ZCON1`, `U_ZFOL1`, `U_ZSCH1`, `U_ZTAX1`, etc.) — todas son configuración de facturación electrónica/SII, bodegas, proveedores, impuestos; ninguna tiene lógica de guías-por-vendedor. **Conclusión: la tabla de referencia del cliente viene de un proceso manual/Excel, no de un reporte nativo de SAP.**

**Resultado final (2026-08-14, momento de la consulta):**

| Vendedor | Calculado | Real (cliente) | Diferencia |
|---|---|---|---|
| Karihosqui Calderon | \$57,165,920 | \$57,165,920 | exacto |
| Marcela Espinoza | \$6,944,090 | \$6,944,090 | exacto |
| Florimar Rodriguez | \$287,695 | \$287,695 | exacto |
| Luz Lacruz | \$1,300,880 | \$1,300,880 | exacto |
| Insuseg | \$8,096,330 | \$8,624,730 | -\$528,400 |
| Mariana Sanchez | \$147,020 | \$412,020 | -\$265,000 |
| Carlos Cortes | \$3,798,020 | \$6,593,020 | -\$2,795,000 |
| **Total** | **\$77,739,955** | **\$81,328,355** | **-\$3,588,400 (4.4%)** |

**Pendiente, no resuelto:** el faltante de Carlos Cortés y Mariana Sánchez es **idéntico con o sin filtro de fecha** (probado sin ningún límite de fecha, todo el historial de guías abiertas) — confirma que ese monto no existe en `DeliveryNotes` bajo ningún vendedor ni ninguna fecha, para ninguno de sus clientes históricos reales (se cruzó contra `Sales` real, sin encontrar ningún documento mal codificado que lo explique). Es el mismo tipo de patrón que el hallazgo de vendedor mal asignado del 2026-08-02, pero esta vez el documento **no existe** en SAP como guía, no es solo un código equivocado. Antes de seguir ajustando la fórmula, hay que preguntarle directamente al equipo de ventas de dónde sale esa tabla y qué corrección manual aplican para Carlos/Insuseg/Mariana que SAP no refleja.

**Implementado en código:** la fórmula usada hoy es manual (script ad-hoc contra el Service Layer), todavía **no está reflejada en `DeliveryNoteSyncService`** — ese servicio sincroniza guías abiertas con la clasificación `EsMuestraOCambio`, pero no aplica el filtro de `LineStatus` por línea ni agrupa por vendedor de línea. Pendiente de decidir si vale la pena ese nivel de detalle en el sync automático, dado que el objetivo final (cuadrar con el equipo de ventas) todavía tiene un 4.4% sin explicar.

### Hallazgo (2026-08-14): guías de despacho entregadas y no facturadas — `DeliveryNotes`, no `Orders`

A pedido del usuario, se investigó cómo identificar ventas reales entregadas pero aún no facturadas. La entidad correcta es **`DeliveryNotes`** (guías de despacho), no `Orders` — ver detalle completo en `BDhana.md` sección 8b (entidad explorada por primera vez esta sesión). Resumen:

- `DeliveryNotes` con `DocumentStatus = Open` = mercadería que ya salió de bodega pero sigue sin facturar. Cada línea trae `BaseType`/`BaseEntry` apuntando al `DocEntry` de la `Order` origen (trazabilidad Orden → Guía).
- De 689 guías abiertas en `INSUSEG` al momento de la consulta, **588 (85%) no son venta real** — muestras, cambios, guías internas a bordadoras ("LOGO"), donaciones, devoluciones por calidad, consumo interno. Se identifican por texto libre en `Comments`/`NumAtCard` (sin campo estructurado dedicado en SAP) combinado con un piso de monto (`DocTotal < 1000`, los casos no-venta son consistentemente de \$1–\$815).
- **Resultado real:** 101 guías de venta genuina, entregadas y pendientes de facturar, por **\$123,433,381 CLP** (verificado manualmente, no cargado a Azure SQL todavía — ver plan abajo).

### ⚠️ Hallazgo operativo (2026-08-14): trabajo en paralelo sin `git` disponible en esta máquina — conflicto real con `DeliveryNotes`

Al intentar implementar el plan de sincronización de `DeliveryNotes` (ver más abajo), apareció un problema serio: **Ignacio ya había construido e implementado esto contra la base compartida de Azure SQL** (dos migraciones aplicadas, `AddDeliveryNotes` 2026-08-12 y `AddDeliveryNoteEsMuestraOCambio` 2026-08-13, con **689 filas reales ya sincronizadas**), pero **su código fuente nunca llegó a esta máquina** — ni la entidad, ni el servicio de sync, nada.

**Causa raíz:** este proyecto vive dentro de una carpeta de OneDrive (`01 Melirrepu\...`), que sincroniza archivos entre las máquinas del equipo, incluyendo la carpeta `.git` (existe acá, `git status` la detecta). Pero **el ejecutable `git` no está instalado en esta máquina** — no se puede correr ningún comando `git` para revisar historial, ramas, o si el trabajo de Ignacio quedó comiteado en algún lado recuperable. Es decir: los *archivos* de `.git` llegan por OneDrive, pero sin el binario de `git` no sirven de nada acá. El esquema de Azure SQL sí se compartió (es un recurso en la nube, no un archivo local), por eso la tabla apareció con datos pero sin el código que la generó.

**Resuelto en conjunto con Eric:** se decidió no pisar el trabajo ya hecho. Se adaptó el código nuevo (entidad, DTO, servicio de sync) al esquema **ya existente** en producción (`DocEntry`, `DocNum`, `CardCode`, `CardName`, `DocDate`, `SalesPersonCode`, `EsMuestraOCambio` — sin `DocTotal`/`Comments`/`NumAtCard`, esos campos no se persisten). La migración generada para este código coincidió exactamente con las columnas/tipos/FK ya reales (verificado contra `INFORMATION_SCHEMA.COLUMNS` antes de tocar nada) — se insertó manualmente en `__EFMigrationsHistory` como aplicada, **sin ejecutar el `CREATE TABLE`** (la tabla ya existía), para que el modelo de EF local quede sincronizado sin riesgo de romper la tabla real. Confirmado con `dotnet ef migrations has-pending-model-changes`: sin cambios pendientes.

**Punto sin resolver:** el criterio exacto que usó Ignacio para calcular `EsMuestraOCambio` es desconocido (no hay código de referencia) — su resultado fue 136 guías reales / 553 muestra-cambio, mientras que la heurística de texto+monto de esta sesión (ver hallazgo arriba) da 101/588 sobre los mismos datos. Ambos números son plausibles, no se pudo reconciliar la diferencia. **Recomendación pendiente:** cuando Ignacio pueda instalar `git` en su máquina o compartir su código por otro medio, comparar los dos criterios y quedarse con uno solo.

**Instalar `git` en esta máquina queda como pendiente de infraestructura** — sin eso, este tipo de choque de trabajo en paralelo va a repetirse.

### ✅ Implementado (2026-08-14): sincronización de `DeliveryNotes` en el botón "Sincronizar ahora"

A pedido explícito del usuario: la sincronización de guías vive en `Ventas/Sincronización`, en el **mismo botón** que ya sincroniza Ventas (`OnPostSyncAsync` corre `SalesSyncService` y `DeliveryNoteSyncService` en la misma pasada) — no se creó un botón nuevo. Si Ventas sincroniza bien pero Guías falla, se muestra igual el resumen de Ventas más el error de Guías por separado (no se pierde lo que sí funcionó).

**Diseño final (más simple que el plan original de watermark por `UpdateDate`):** como el usuario pidió explícitamente **no guardar historia, solo el estado actual**, `DeliveryNoteSyncService.SyncAsync()` hace un **reemplazo completo** en cada corrida — trae todas las guías `Open`/no canceladas de SAP (sin filtro de fecha, "abiertas" ya acota el volumen a cientos, no miles), hace upsert de las que siguen viniendo, y **borra** cualquier fila que ya no aparezca (porque se facturó o se canceló desde la última corrida). Resuelve el mismo problema que motivó lo del watermark, sin necesitar lógica de fechas: si una guía dejó de estar abierta, simplemente no vuelve a traerse, y se elimina.

**Clasificación real vs. muestra/cambio:** heurística de texto (`MUESTRA`, `CAMBIO`, `LOGO`, `INGRESO FALSO`, `NO FACTURAR`, `DONACION`, `CALIDAD`, `CONSUMO` en `Comments`/`NumAtCard`) + piso de monto (`DocTotal < 1000`) — mismo criterio validado manualmente en el hallazgo de arriba. Se calcula en `DeliveryNoteSyncService` al sincronizar y se guarda ya resuelta en `EsMuestraOCambio` (bit), siguiendo el esquema que ya existía — el dato crudo (`Comments`/`NumAtCard`/`DocTotal`) **no se persiste**, así que si el criterio cambia más adelante hay que volver a sincronizar (no alcanza con cambiar una consulta de lectura).

**Pendiente:** el esquema actual no permite calcular el monto total pendiente de facturar desde SQL (no hay `DocTotal` guardado) — si se necesita ese número en el dashboard, hay que agregar una columna nueva (migración aditiva, no destructiva) y decidir junto con Ignacio cuál criterio de clasificación usar. Tampoco se construyó todavía ninguna UI que muestre estas guías — solo quedaron sincronizadas en la base.

### Buscador y encabezados ordenables en "Ventas por cliente" (2026-08-08)

Pedido del usuario: con 526 clientes y sin límite de filas (ver sesión 2026-07-29/30), encontrar/comparar clientes a mano era incómodo. Se agregó:
- **Buscador** (`#buscador-cartera`) arriba de la tabla — filtra por nombre de cliente en vivo (evento `input`), sin distinguir mayúsculas ni acentos (normaliza con `texto.normalize('NFD')` + saca las marcas diacríticas, así "Núñez" y "nunez" matchean igual). Colapsa cualquier fila expandida que deje de coincidir. Un contador (`#cartera-contador`) muestra "X de Y cliente(s)" mientras hay texto escrito.
- **Encabezados ordenables** (`th.th-ordenable`) — Cliente, cada mes, Total general, Promedio Mes, Peso Cliente, % Cartera y % MG. Clic ordena, clic de nuevo invierte el sentido; una flecha (▲/▼, naranja de marca) marca la columna activa. El N° de fila se **renumera** después de cada orden nuevo (es la posición visual, no un id fijo). El orden inicial (`Total general` descendente, el mismo que ya traía el servidor) queda marcado con la flecha desde que carga la página, sin necesitar un clic.
- **Por qué no se parsea el texto formateado para ordenar:** los montos se muestran con separador de miles (`"9.911.250"`) en el formato/cultura que tenga configurado el servidor en cada momento (esta app no fija una cultura explícita — corre con la del SO, hoy es-CL en esta máquina, pero podría no serlo en otro entorno) — parsear ese texto sería frágil. En vez de eso, cada celda numérica lleva un `data-valor` con el número crudo en formato invariante (`CultureInfo.InvariantCulture`, siempre con `.` como separador decimal), que es lo que usa el JS para ordenar. El texto visible (`N0`, con separador de miles) sigue siendo el de siempre, sin cambios.
- **Alcance:** solo la tabla principal de clientes — el detalle anidado (categorías/productos, ver más abajo) no tiene buscador ni orden propio todavía; no se pidió y las tablas ahí son mucho más chicas (típicamente <20 filas).

### Tres bugs reales de scroll en tablas anidadas, encontrados y corregidos con Playwright (2026-08-08)

Al agregar el nivel de categoría (ver sección siguiente), el detalle de Cartera pasó a tener **tablas anidadas dos niveles** (cliente → categoría → producto) en vez de uno solo. El usuario reportó que el scroll "se veía mal" y la columna fija no se mantenía — el primer intento (cambiar `border-collapse` de `collapse` a `separate`, por un bug conocido de Chrome con `position:sticky`) no alcanzó. Se terminó usando Playwright con credenciales reales para medir el DOM directamente (no solo mirar capturas) y aparecieron **tres causas distintas**, cada una tapando a la siguiente:

1. **La tabla externa se estiraba sin límite** para acomodar el ancho de las tablas anidadas, en vez de dejar que cada una scrolleara por su cuenta — el truco CSS ya existente (`.fila-detalle td { width: 0 }`, ver sesión 2026-08-02) dejó de alcanzar con dos niveles de anidamiento (el contenido de una tabla anidada tiene un ancho mínimo que ese truco no logra ignorar). Medido con `el.scrollWidth === el.clientWidth` en las mini-tablas (o sea, cero scroll real) mientras el wrapper más externo sí tenía overflow real. **Fix:** `cartera.js` ahora le pone a cada mini-tabla un `max-width` inline en píxeles, medido en runtime contra `.insuseg-container` (un contenedor estable, no afectado por este problema) — función `limitarAnchoContenedor()`.
2. **La celda de la esquina** (fija arriba Y a la izquierda — "N°"/"Cliente" en la tabla principal, "Categoría"/"Producto" en el detalle) tenía el mismo `z-index` que el resto del encabezado (ambos con `position:sticky`, mismo z-index, mismo stacking context) — al scrollear horizontal, las celdas del header que solo son fijas arriba (más adelante en el HTML) se dibujaban encima de la esquina y la tapaban casi por completo. **Fix:** regla nueva `.tabla-vertical-limitada table th.col-sticky { z-index: 4; }` (por encima del header normal, que tiene 3).
3. **Scroll chaining:** al llegar al final del scroll de una mini-tabla, el gesto seguía de largo y movía la página ENTERA de golpe — confirmado con un scroll de mouse de verdad (`page.mouse.wheel`, no `scrollTop` asignado por script): la página saltaba de posición apenas la mini-tabla llegaba a su tope. Es lo que se sentía "incómodo" aunque ya no hubiera nada roto visualmente. **Fix:** `overscroll-behavior: contain;` en `.tabla-vertical-limitada` (aplica a las tres — cliente, categoría, producto — porque comparten esa clase).

**Aprendizaje para la próxima vez que se toque scroll/sticky en esta app:** no alcanza con mirar una captura de pantalla estática — hubo que medir `scrollWidth`/`clientWidth`/`getComputedStyle` en el DOM real y probar con un scroll de mouse de verdad (no programático) para encontrar el bug #3, que no se veía en ninguna captura.

### Detalle de Cartera en dos niveles: categoría → producto (2026-08-08)

Pedido del usuario: que el detalle de un cliente muestre primero las **categorías** de producto, y al hacer clic en una, sus productos — antes iba directo a la lista de productos.

**Hallazgo en el camino: `U_Categoria` en `Items` es un CÓDIGO, no el nombre.** La sesión 2026-08-03 ya había confirmado que `U_Categoria`/`U_Marca`/`U_Familia` tienen datos reales, pero no se había notado que el valor guardado en `Items.U_Categoria` es un código numérico como string (`"1"`, `"4"`...) que apunta a la tabla de usuario `U_ZCAT` (`GET /b1s/v1/U_ZCAT` → `{Code, Name}`, ej. `Code=4, Name="ROPA INDUSTRIAL"`) — el nombre real vive solo ahí. Se armó bien, con una tabla de dimensión nueva (mismo patrón que `SalesPerson`/`SalesPersonCode`):
- Entidad `ItemCategory` (`Code`, `Name`) + `DbSet<ItemCategory> ItemCategories`, migración `AddItemCategories` (aditiva, aplicada).
- `Item.CategoryCode` (antes se había armado como `Item.Category` con el código crudo sin resolver — se corrigió ANTES de comitear nada, no llegó a quedar en el historial de git).
- `SapServiceLayerClient.GetItemCategoriesAsync()` (`U_ZCAT?$select=Code,Name`) + `InventorySyncService` ahora sincroniza categorías primero, después Items (mismo botón "Sincronizar productos" de siempre, sin UI nueva).
- **Backfill corrido con éxito** (ver pendiente nuevo arriba sobre los colgados intermitentes): 25.457 productos, 24.818 con categoría asignada, 29 categorías reales (`CALZADO SEGURIDAD`, `PROTECCION VISUAL`, `ROPA INDUSTRIAL`, etc.).

**Backend (`CarteraModel`):** `OnGetProductosAsync` (primer nivel, ya existía) ahora agrupa por categoría en vez de por producto — devuelve `codigo` + `nombre` de cada categoría (no solo el nombre, para tener una clave estable para el segundo nivel) con los mismos KPIs que antes (Peso Categoría, % Cartera, % MG). Handler nuevo `OnGetProductosPorCategoriaAsync(cardCode, categoriaCodigo)` — mismo cálculo que el viejo detalle por producto, pero acotado a una categoría; **"Peso Producto" ahora es relativo al total de la categoría** (antes era relativo al total del cliente) — se decidió así para seguir el mismo criterio que ya usa el resto de la página ("Peso X" = participación dentro del total del nivel padre inmediato).

**Frontend (`cartera.js`):** reescrito para construir el DOM con `createElement`/`textContent` en vez de strings HTML concatenados (los nombres de categoría/producto son datos, no hay que confiar en que no traigan comillas u otros caracteres). El patrón de fila-clic-para-expandir se generalizó (`construirFilaExpandible`) para que la categoría se comporte igual que el cliente (mismo look de chevron, mismo cacheo por nivel).

### Puntos de comparación año anterior en los gráficos de "Tendencia" y "Margen por mes" (2026-08-08)

Pedido del usuario: ver en los gráficos, no solo en la tabla, si cada mes superó o no al mismo mes del año pasado — con el mes actual resaltado más que los pasados. Se cargó la skill `dataviz` antes de tocar los gráficos (mismo criterio que en sesiones anteriores).

- **Backend:** `CarteraModel` calcula, para cada mes de `TendenciaMensual`/`MargenMensual`, la diferencia contra el mismo mes del año anterior (consulta aparte, mismo criterio que ya usaba el KPI "Comparado con año anterior" pero ahora para *todos* los meses del gráfico, no solo el actual). El horizonte de datos (¿existe año anterior o no?) se resuelve con `MIN(Sales.SaleDate)` en vivo, no hardcodeando `2024-01-01` de nuevo.
- **Diseño de color validado con el validador de la skill (`validate_palette.js`):** verde/naranja-oscuro (los mismos que ya usan las tarjetas KPI `kpi-good`/`kpi-bad`) están en zona límite de daltonismo rojo-verde (ΔE 6.3, banda "legal solo con codificación secundaria") — por eso el estado **nunca depende solo del color**: círculo lleno (verde) = superó, rombo (naranja oscuro) = no alcanzó, anillo hueco (gris) = sin datos de comparación. El mes actual sale más grande y a color pleno; los pasados quedan atenuados.
- Aplicado igual en los dos gráficos (círculos/rombos en el SVG de Tendencia, marcas chicas arriba de cada barra en Margen por mes), con leyenda de texto debajo de cada uno y el detalle exacto en el tooltip/`title`.
- De paso, a pedido del usuario, se sacó la notación `"% Mg"` del gráfico de barras (quedó solo el número con `%`) — las tarjetas KPI de arriba no se tocaron, siguen diciendo `"% Mg"`.

### Hallazgo de seguridad: password de SAP en texto plano, encontrada y limpiada del repo (2026-08-07)

Al revisar qué había quedado comiteado en el primer commit del repo git (recién creado esta sesión), se encontró que la línea de este mismo archivo que documenta la reconfiguración del 2026-07-26 tenía la contraseña real del Service Layer de SAP en texto plano junto al usuario `ADMI1` — el resto del archivo es consistente en decir "password en gestor de contraseñas, no en este repo", pero esa línea puntual se había escapado. No era algo nuevo de esta sesión: la línea existía desde el 26 de julio, simplemente nunca había estado bajo control de versiones hasta que se inicializó git hoy.

**Qué se hizo:**
1. Se redactó la línea en el archivo (ahora dice "password en gestor de contraseñas — no en este repo —").
2. Se reescribió el historial de git desde cero con comandos de bajo nivel (`git commit-tree`, sin tocar ningún archivo del proyecto en disco) para que la contraseña tampoco quede en los commits viejos — los dos commits del historial (`fb4066f`, `c9f3328`) son nuevos, con el mismo contenido y mensajes que los originales salvo esa única línea.
3. Se purgaron los objetos git viejos del disco (`git reflog expire` + `git prune`) — no quedan alcanzables ni physicalmente en `.git`.
4. Verificado con búsqueda binaria directa sobre toda la carpeta `.git` (no solo `git log`) que la contraseña no aparece en ningún lado, y `git fsck --full` confirmó que el repo quedó íntegro.

**Nunca se subió a ningún lado** — este repo no tiene remoto configurado, todo el tiempo fue local. El riesgo real era bajo, pero el hallazgo vale la pena por dos motivos: (1) confirma que el patrón de "nunca escribir secretos en este archivo" no se venía cumpliendo al 100%, hay que ser más cuidadoso al documentar credenciales de acá en adelante; (2) de paso se encontró que `.claude/settings.json` (el cache de permisos de la herramienta de IA usada en el proyecto, no un archivo del proyecto en sí) tiene la contraseña de `sqladmin_insuseg` y la llave `Provisioning:RegistrationKey` en texto plano — nunca estuvo en git (`.gitignore` ya lo excluía), pero queda como advertencia: si alguna vez se comparte o comprime la carpeta completa del proyecto, hay que excluir `.claude/` a mano.

**Recomendación que sigue pendiente:** cambiar el password de `ADMI1` en SAP por algo que no sea 4 dígitos — ya estaba anotado como hallazgo de seguridad del servidor SAP (junto con TLS 1.0 y el certificado autofirmado), independiente de este incidente puntual.

### La app se recortó a solo Cartera de clientes — el resto vive en el historial de git (2026-08-07)

Decisión explícita del usuario: por ahora solo se trabaja `Ventas → Cartera de clientes`, así que se borró todo lo demás para no mantener código que no se está usando. **Se borró, no se ocultó** — pero el proyecto pasó a ser un repositorio git recién en esta sesión (no lo era antes), y el primer commit (`"Estado inicial antes de recortar la app..."`) tiene el snapshot completo de todo antes de tocar nada. **Para recuperar cualquier módulo borrado:** `git log`, buscar ese primer commit, y traer de vuelta los archivos puntuales con `git show <hash>:ruta/al/archivo.cs` (o `git checkout <hash> -- ruta/`) en vez de reescribirlos de cero.

**Qué se borró:**
- `Pages/Ventas/Analisis.cshtml(.cs)`, `Pages/Compras/` completo, `Pages/Inventario/` completo.
- `PurchaseSyncService`, `PurchaseSyncResult`, la entidad `Purchase` y su tabla en Azure SQL (migración `RemovePurchases`, aplicada — la tabla `Purchases` ya no existe en `sqldb-insuseg-analytics`). **Esto no tocó SAP para nada** — `INSUSEG` sigue con todo su historial de compras intacto; si se reconstruye Compras, se vuelve a sincronizar fresco desde ahí sin problema.
- Los links correspondientes del menú lateral en `_Layout.cshtml`.
- `Pages/Index.cshtml.cs` redirigía a `/Ventas/Analisis` (ya borrada) — se cambió para redirigir a `/Ventas/Cartera`.

**Qué se dejó a propósito, aunque también son "otros módulos" a primera vista:**
- **`Ventas/Sincronización`** — sin esto, Cartera se queda sin forma de traer datos nuevos de SAP.
- **`Administración/Usuarios`** (roles) — es gestión de acceso, no un módulo de negocio como Ventas/Compras/Inventario.
- **`InventorySyncService`** (la clase, sin página de Inventario) — el detalle por producto de Cartera (`OnGetProductosAsync`) necesita el nombre de los ítems, que **solo este servicio actualiza** (`SalesSyncService` no toca la tabla `Items` para nada). Se le agregó un botón chico "Sincronizar productos" en `Ventas/Sincronizacion.cshtml`, así el catálogo de productos se sigue actualizando sin resucitar la página de Inventario completa.

**Verificado de punta a punta después del recorte:** compila toda la solución (incluido `Insuseg.Analytics.Ingestion`, que no se tocó), `/` redirige a Cartera, `Compras/Analisis` / `Inventario/Analisis` / `Ventas/Analisis` dan 404, el sidebar quedó limpio (solo Cartera + Sincronización bajo Ventas), y el botón nuevo de "Sincronizar productos" aparece en Ventas/Sincronización.

### Confirmado en SAP real: sí hay marca y categoría/sub-categoría de producto (2026-08-03)

Se verificó en vivo contra el Service Layer (solo lectura — Login/GET/Logout) si los campos custom de `Items` documentados en `BDhana.md` (`U_Marca`, `U_Categoria`, `U_Familia`) tienen datos reales o están vacíos. **Están poblados con datos reales, no es un campo a medio llenar.** Cada uno está enlazado a una tabla de usuario (UDT) propia, consultable vía Service Layer con el prefijo `U_`:

| Campo en `Items` | Tabla enlazada (UDT) | Endpoint Service Layer | Contenido |
|---|---|---|---|
| `U_Marca` | `ZMARC` | `GET /b1s/v1/U_ZMARC` | 20+ marcas: 3M, Master Lock, Ansell, Steelpro, Proseg, Maritex, Segma, Proflex, etc. |
| `U_Categoria` | `ZCAT` | `GET /b1s/v1/U_ZCAT` | 20+ categorías: Calzado Seguridad, Protección Cabeza, Ropa Industrial, Protección Visual, Protección Respiratoria, Elementos Seguridad, etc. |
| `U_Familia` | `ZFAM1` | `GET /b1s/v1/U_ZFAM1` | 20+ sub-categorías: Botín, Bota, Chaleco, Fono, Lente, Cinturón, Candado, etc. — funciona como el nivel debajo de Categoría. |

Verificado con un producto real (`BLUSA VENTURE LEGEND WOMAN...`): `Categoria=ROPA INDUSTRIAL`, `Familia=BLUSA`, `Marca=LEGEND` — coincide exactamente con lo que dice el nombre del producto, confirma que la carga de datos es consistente, no ruido. Con esto, ya se puede construir un módulo de Ventas/Inventario por marca o categoría cuando se pida — no hace falta pedirle nada nuevo al cliente, el dato ya está en SAP.

### Gráfico nuevo: "Margen por mes" en `Ventas/Cartera` (2026-08-03)

Barras verticales, una por mes del período filtrado (respeta el filtro de vendedor si hay uno activo), en una tarjeta nueva debajo de "Tendencia de ventas netas por mes". Antes de construirlo se armó un boceto en un Artifact para acordar la idea con el usuario, iterando sobre notación y jerarquía visual hasta llegar a la versión final:

- **El `% Mg` (margen ÷ venta neta del mes, notación `"21,5 % Mg"` — misma que ya usan las tarjetas KPI) es el dato que más pesa**: arriba de cada barra, en naranja de marca, negrita. El monto en pesos queda como dato de apoyo, chico y en negrita gris debajo del %.
- **La altura de la barra representa el monto ($), no el %** — decisión explícita para no perder la comparación de magnitud entre meses aunque el % sea lo que más salta a la vista en el texto.
- El mes con mayor margen en pesos queda resaltado en naranja oscuro (clase `.destacada`).
- **Bug real encontrado y corregido en el momento:** con un período largo (probado con 32 meses), las columnas se aplastaban y el texto se superponía porque no tenían ancho mínimo. Se envolvió el gráfico en un contenedor con su propio scroll horizontal (`.margen-chart-scroll`) y se le dio a cada columna un ancho mínimo de 56px (`flex: 1 0 56px`) — con muchos meses ahora aparece una barra de scroll en vez de romperse, mismo criterio que ya usa el resto de tablas anchas de la app. Verificado con captura real (Playwright) contra 32 meses de datos.
- Cálculo agregado a `CarteraModel.OnGetAsync` reutilizando las líneas ya cargadas en memoria (`lineasCrudo`) — no agrega ninguna consulta nueva a la base.

### Roles de usuario, montos netos en Análisis, y Cartera de clientes: comparación anual + bug de scroll (2026-08-02)

**Roles (`Admin`, `Ejecutivo`, `Vendedor`).** Se agregó `.AddRoles<IdentityRole>()` en `Program.cs` — no hizo falta migración nueva porque `IdentityDbContext<IdentityUser>` ya traía las tablas `AspNetRoles`/`AspNetUserRoles` sin usar. Los tres roles se siembran al arrancar la app (bloque idempotente en `Program.cs`, después de `app.MapRazorPages()`), y las dos cuentas del equipo ya existentes (`info@aitbp.com`, `elobog@Melirrepu.com`) quedan como **Admin** automáticamente si todavía no lo son. `Pages/Administracion/Usuarios.cshtml(.cs)` ahora: requiere rol Admin para entrar (`[Authorize(Roles = "Admin")]`), pide un rol al invitar un usuario nuevo, muestra el rol de cada usuario en la tabla, y agrega **eliminar cuenta** (no existía antes) con dos protecciones — no podés eliminarte a vos mismo, y no se puede eliminar al último Admin. El link "Usuarios" del menú lateral se oculta para quien no sea Admin. **Alcance deliberado:** por ahora Ejecutivo y Vendedor solo se diferencian de Admin en que no pueden entrar a Usuarios — tienen el mismo acceso que hoy a Ventas/Compras/Inventario, no se definieron restricciones de datos por rol (ej. un Vendedor viendo solo su propia cartera) porque no se pidió.

**Bug de montos brutos vs. netos en `Ventas/Análisis` (mismo patrón que ya se había corregido en Cartera).** `AnalisisModel` calculaba Total Vendido, Ventas por cliente, Ventas por vendedor y el monto histórico de clientes desatendidos desde `Sale.Amount` (el `DocTotal` de SAP, que en Chile incluye IVA) en vez de `SaleLine.LineTotal` (neto). Se migró todo a una sola consulta a nivel de línea (mismo patrón que Cartera), con el conteo de "órdenes" contando documentos distintos (no líneas) para no inflar por tener varias líneas por factura. **Verificado con datos reales:** para agosto 2025, "Total vendido" pasó de $235.062.533 (bruto) a **$197.531.526** — el mismo número exacto ya validado contra la planilla del usuario en la sesión anterior. Se agregó una nota visible ("Todos los montos son netos (sin IVA)") debajo del título, igual que ya tenía Cartera.

**Cartera de clientes — KPI de comparación cambiado de "promedio del período" a "mismo mes, año anterior".** La tarjeta que mostraba "faltan/superado $X" contra el promedio mensual del período ahora compara la venta del mes actual contra la venta real de **ese mismo mes, un año antes** (respeta el filtro de vendedor si hay uno activo; consulta ese mes aparte porque normalmente cae fuera del rango de 12 meses que se ve por defecto). Además se agregó una tarjeta nueva **"Margen del mes actual"**, separada de "Venta del mes actual" (no fusionada), con su propio % destacado (clase nueva `.insuseg-kpi-highlight`: 1.1rem, negrita, naranja de marca — en vez del gris chico que usa el resto de los `%`). "Margen total del período" quedó fusionado en la misma tarjeta de "Venta total del período" (con espacio propio dentro de esa tarjeta), con su `%` en el estilo chico de siempre — la fila completa quedó: **Venta+Margen del período** | Venta promedio por mes | Venta del mes actual | Margen del mes actual | Comparado con año anterior.

**Bug real encontrado y corregido: el header de la mini-tabla de detalle por producto desaparecía al scrollear.** Al expandir el detalle de un cliente en Cartera y bajar el scroll, el encabezado de esa tabla ("Producto", los meses, etc.) se perdía porque solo la tabla principal tenía header fijo (`position:sticky`). Se le dio al detalle su propio recuadro con scroll acotado (`.tabla-detalle-mini`, 300px de alto) y header pegajoso propio — se generalizó la regla CSS `.tabla-vertical-limitada table th` (antes específica a `.insuseg-table`) para que aplique a cualquier tabla anidada. También se generalizó `tablas-expandibles.js` (antes solo corría una vez en `DOMContentLoaded`) para exponer `window.InsusegTablas.aplicarBotonExpandir(contenedor)`, así el botón "Expandir/Compactar" también engancha en contenido cargado después por AJAX — `cartera.js` lo llama justo después de inyectar el detalle. Verificado visualmente con Playwright (capturas antes/después) que el header ya no se pierde al bajar.

**Hallazgo adicional durante la verificación: no había ningún índice en `Sales.SaleDate`.** Todas las páginas de Ventas filtran por rango de fechas — sin índice, cada consulta hacía un scan completo de la tabla. Se agregó `entity.HasIndex(s => s.SaleDate)` en `InsusegAnalyticsDbContext`, migración `AddSaleDateIndex` generada y aplicada a `sqldb-insuseg-analytics`. El detalle por producto de Cartera (el más lento, por su consulta agregada global) bajó de ~9s a ~6s — mejora real pero no completa, ver pendiente arriba.

**Hallazgo de datos en SAP (no un bug de la app): 7 documentos con el vendedor mal asignado.** Investigando por qué el total de Carlos Cortés (código de vendedor 3) en Cartera no le calzaba con su propio seguimiento manual (desfase de $1.907.130 en agosto 2025), se encontró que **SAAM S.A. ($717.840) y Synthon Chile Limitada ($299.500) están asignados a Lilian Novoa (código 24)**, y **AYT Servicios Limitada ($23.970) está asignado a Mariana Sánchez (código 26)** — no a Carlos Cortés como su propio seguimiento asumía. Por otro lado, **Agencia De Aduana Patricio Larrañaga ($1.925.980), Corp Educacional Colegio Teresiano ($626.640), Electrónica Casa Royal ($206.570) y Maestranza Verdugo ($189.250)** sí están bajo Carlos Cortés en SAP pero su planilla no los contaba. La suma de ambas diferencias explica el desfase exacto. La app lee el campo `SalesEmployee` de SAP tal cual viene — el problema es de carga de datos en SAP (alguien facturó con el vendedor equivocado), no algo que se pueda corregir desde el código. Pendiente comunicarlo al cliente/a quien factura.

### Módulo nuevo: `Ventas → Cartera de clientes` (2026-07-28, pendiente de validar indicadores)

Se construyó `Pages/Ventas/Cartera.cshtml(.cs)` — tabla de ventas mensuales por cliente (últimos 12 meses por defecto, filtrable por rango de fechas y por vendedor), con fila expandible por clic que carga el detalle por producto de ese cliente vía AJAX (`OnGetProductosAsync`, mismo patrón de handler que ya usa el resto de la app).

**KPIs y columnas:**
- Venta total del período, venta promedio por mes (**excluye el mes en curso a propósito**, para no comparar contra un promedio inflado por un mes todavía sin cerrar), venta del mes actual, margen total del período (mismo cálculo `LineTotal − GrossBuyPrice × Quantity` validado en el hallazgo de margen de Ventas/Análisis), y una tarjeta que indica cuánto falta o se superó respecto al promedio mensual.
- Por cliente: **Peso Cliente** (% del total de todos los clientes en el período filtrado) y **% Cartera** (% del total de *su vendedor asignado* — esta base siempre se calcula sin el filtro de vendedor activo, para que el % no cambie de significado según qué esté filtrado).
- En el detalle por producto (por cliente): mismo patrón, **Peso Producto** (% del total de ese cliente) y **% Cartera** (% del total vendido de ese producto a *todos* los clientes).

**Estado:** la página carga y responde con autenticación correcta (`[Authorize]`), el JS escapa nombres de producto antes de inyectarlos al DOM (sin riesgo de XSS). **No tiene límite de filas** en la tabla de clientes (nota explícita en la UI) — con 541 clientes en el período por defecto no es un problema de rendimiento hoy, pero vigilar si crece mucho.

### ✅ Cartera de clientes validada + UX de tablas grandes + gráficos, + bug crítico de Notas de Crédito encontrado y corregido (2026-07-29/30)

**Filtro por vendedor y sin tope de filas.** Se agregó un filtro `VendedorCodigo` (select, "Todos los vendedores" por defecto) — filtra los clientes mostrados, pero **`% Cartera` de cada cliente sigue calculándose contra el total real de SU vendedor asignado en todo el período**, no contra el filtro activo (si filtrás por un solo vendedor, Peso Cliente y % Cartera terminan coincidiendo exactamente — es la forma de verificar que el cálculo está bien). Se sacó el límite de filas que tenían las tablas heredado del patrón de `Analisis.cshtml` (Top 15/20) — con datos reales la tabla de Cartera llegó a **541 filas** en el período por defecto, confirmando que el límite había que sacarlo de verdad, no era preventivo.

**UX para tablas grandes (aplicado en Cartera, Análisis de ventas, Análisis de inventario y Usuarios):** las tablas con potencialmente muchas filas quedaron con **alto máximo + scroll interno propio** (clase `.tabla-vertical-limitada`, 420px, header con `position:sticky`) para que no empujen el resto de la página hacia abajo, más un botón **"Expandir tabla / Compactar"** (primer JS "genérico" del proyecto, `wwwroot/js/tablas-expandibles.js`, aplica automático a cualquier tabla con esa clase) para verla completa cuando hace falta. En la tabla de Cartera además las columnas **N°/Cliente** (y **Producto** en el detalle expandido) quedan **fijas al hacer scroll horizontal** (`position:sticky` en el eje X) — hubo que subir el `z-index` del header por encima de las columnas fijas del cuerpo para que no se superpongan mal al scrollear verticalmente (bug real encontrado y corregido en el momento).

**Detalle por producto sin recargar la página:** al hacer clic en un cliente se expande una fila con el detalle por producto de ese cliente, cargado vía `fetch` a un handler AJAX de la misma página (`OnGetProductosAsync`) — primer uso de JS "de verdad" en el proyecto (`wwwroot/js/cartera.js`), con los mismos KPIs/columnas que la tabla principal pero a nivel producto (`Peso Producto` y `% Cartera` con base distinta, documentada en la UI).

**Gráficos agregados (2026-07-29), cargando la skill `dataviz` antes de construirlos, uno por página para darle personalidad a cada análisis sin repetir el mismo tipo de gráfico en todos lados:**
- **Cartera de clientes:** línea/área de tendencia de ventas netas por mes (agregado de todos los clientes filtrados) — con crosshair + tooltip al pasar el mouse (SVG plano, sin librería). Es información que antes solo se podía reconstruir escaneando columnas de la tabla.
- **Análisis de ventas:** barra apilada horizontal (2 segmentos) de Margen vs. Costo como partes del total vendido — patrón "emphasis" (naranja de marca + gris), no categórico.
- **Análisis de inventario:** barra horizontal (mismo patrón ya usado en Ventas/Compras) con el Top 10 de productos por valor inmovilizado — le da a esta página su primer gráfico, antes era 100% tablas.

**Montos netos, no brutos — bug real encontrado por el usuario comparando contra su propia planilla.** `Sale.Amount` viene de `DocTotal` de SAP, que en Chile **incluye IVA**; `SaleLine.LineTotal` es el neto por línea (sin IVA, estándar SAP B1). La tabla principal de Cartera usaba `Sale.Amount` (bruto) mientras que el detalle por producto ya usaba `SaleLine.LineTotal` (neto) — quedaban inconsistentes entre sí, y además mostraban el bruto donde el usuario esperaba neto. Se cambió `CarteraModel` para que **todo** (montos por mes, KPIs, % Cartera, % margen) salga de una sola consulta a nivel línea, eliminando el método separado que calculaba margen aparte.

**⚠️ Bug de fondo encontrado en el proceso, afecta a TODOS los módulos de Ventas, no solo Cartera: las Notas de Crédito de SAP nunca se sincronizaron.** El usuario reportó que el total neto de agosto 2025 no coincidía con su planilla de referencia ($228.103.639 en la app vs. $197.531.526 en su planilla). Se descartaron por orden: duplicados en la base (ninguno), facturas canceladas en SAP (ninguna en ese período, verificado con `Cancelled` vía Service Layer), y recién ahí se encontró la causa real: **la entidad `CreditNotes` de SAP jamás se sincronizó** — el sync solo trae `Orders`/`Invoices`, y `CreditNotes` ni siquiera estaba documentada en `BDhana.md`. Devoluciones y anulaciones quedaban sumadas de más en absolutamente todo lo que usa `Sales`/`SaleLines` (Cartera, Análisis de ventas, rotación de Inventario). Verificado contra SAP real: 46 notas de crédito en agosto 2025 por $36.380.817 (con IVA) → neto ≈ $30.571.275; $228.103.639 − $30.571.275 = $197.532.364, contra los $197.531.526 de la planilla del usuario (diferencia de $838, 0,0004%, redondeo).

**Fix implementado:**
- `SalesSourceDocumentType` ganó un tercer valor, `CreditNote = 2` (ver `BDhana.md` para el detalle de la entidad `CreditNotes` en SAP, agregada ahí también).
- `SalesSyncService` se generalizó para sincronizar **ambas fuentes siempre** (la configurada en `SalesSource` + `CreditNote`, con watermarks independientes) en cada corrida, con un **signo** (+1 normal, −1 para notas de crédito) aplicado a `Amount`/`LineTotal`/`Quantity` al guardar. Con los montos ya en negativo en la base, **ninguna consulta existente tuvo que tocarse** — Cartera, Análisis de ventas y rotación de Inventario ya hacían `.Sum()` directo sobre `Sales`/`SaleLines` sin filtrar por tipo de documento, así que las notas de crédito se netean solas. `GrossBuyPrice` (costo, usado para margen) se confirmó poblado también en líneas de `CreditNotes`, así que el margen también queda correcto sin cambios adicionales.
- Backfill histórico corrido con éxito: **989 notas de crédito, 4.360 líneas**, desde 2024-01-01 hasta hoy — verificado con SQL directo que el total neto de agosto 2025 ahora da exactamente $197.531.526, igual a la planilla del usuario.
- La sincronización automática por horario (`SalesIngestionFunction`) comparte el mismo `SalesSyncService`, así que el fix aplica ahí también sin cambios adicionales.

**Aprendizaje operativo sobre cómo correr el backfill (para la próxima vez que se agregue una fuente nueva):** el primer intento usó "Reprocesar historial completo" (`forceFullResync: true`), que **re-sincroniza las dos fuentes desde cero** — para Facturas eso significa volver a traer los ~17.000 documentos con detalle completo de líneas (~30-45 minutos, cada página de 100 documentos tarda ~11s en SAP por el peso de `DocumentLines`), puro desperdicio ya que Facturas ya estaba al día. La forma correcta para backfillear **una fuente nueva sin tocar las que ya están sincronizadas** es un **sync normal** (sin forzar): como `CreditNote` nunca tuvo watermark previo (`MAX(SaleDate)` da `NULL`), el propio código cae solo a `InitialBackfillStartDate` (2024-01-01) para esa fuente, mientras que Facturas (que sí tiene watermark) solo trae el incremental (rápido). Terminó tardando un par de minutos en vez de 30+.

### ✅ Resync completo contra `INSUSEG` real (2026-07-26)

Con el `CompanyDb` reconfigurado a `INSUSEG` (ver sección 3) y `SapServiceLayer:SalesSource` vuelto a `Invoice` (confirmado que en la base real, a diferencia de `INSUSEG_PRB`, `Invoices` sí está activo), se limpiaron las tablas `Sales`/`SaleLines`/`SalesPersons`/`Purchases`/`Items` (tenían datos de la base equivocada) y se corrieron los tres syncs desde cero, con el horizonte histórico fijado en 2024-01-01 (ver sección 2).

**Escala real, mucho mayor a la usada en las pruebas iniciales contra `INSUSEG_PRB`:**

| Módulo | Antes (`INSUSEG_PRB`) | Ahora (`INSUSEG` real) | Tiempo de sync |
|---|---|---|---|
| Ventas (`Invoices` + líneas) | 53 documentos / 99 líneas | **16.798 facturas / 74.524 líneas** | ~31 minutos |
| Compras (`PurchaseOrders`) | 7 | **12.625** | ~95 segundos |
| Inventario (`Items`) | 25.398 (23 con stock) | **25.287 (856 con stock)** | ~190 segundos |

Rango real de ventas: 2024-01-02 a 2026-07-24, **~$9.600 millones CLP** vendidos en el período — escala de negocio real, muy distinta a los datos de prueba. `SalesPersons` pasó de 2 a **30** vendedores reales.

**Bug real encontrado y corregido durante este resync:** algunas `PurchaseOrders` en `INSUSEG` real vienen **sin proveedor asociado** (`CardCode`/`CardName` nulos) — no era un caso hipotético, rompió el primer intento del sync con una violación de `NOT NULL` en `Purchases.CardName`. `SapPurchaseDocumentDto.CardCode`/`CardName` se volvieron nullable (`string?`), y `PurchaseSyncService` ahora guarda un valor por defecto (`""` / `"(Sin proveedor)"`) en vez de fallar. Confirmado tras el fix: **2 de las 12.625 compras** tienen este caso — datos reales, no ruido.

**Nota de rendimiento:** el sync de Ventas tardó ~31 minutos por el volumen (16.798 documentos, cada uno con el detalle completo de `DocumentLines` sin `$select` — SAP manda ~150 campos por línea aunque solo se persistan 4-5, ver decisión de diseño en el módulo de Inventario). Compras e Inventario, sin ese detalle de líneas, fueron mucho más rápidos pese a volumen similar. Si el sync de Ventas se vuelve un problema de UX (el botón "Sincronizar ahora" quedaría bloqueando 30+ minutos), evaluar acotar `$select` de `DocumentLines` a los campos puntuales necesarios — pendiente, no abordado ahora.

**Pendiente inmediato:** revisar visualmente en el navegador los tres módulos con estos datos reales — no se hizo todavía en esta sesión. Las páginas de Análisis (Ventas/Compras/Inventario) nunca se probaron con este volumen; podrían aparecer casos no vistos con los datos de prueba (ej. más de un vendedor real con ventas, gráficos con muchas más barras, etc.).

**Verificado de nuevo desde otra máquina (2026-07-26, más tarde):** conteo directo con `sqlcmd` confirma los números de arriba (`Sales` 16.798, `SaleLines` 74.524, `Purchases` 12.625, `Items` 25.287, rango 2024-01-02 a 2026-07-24, `Sales.SourceDocType` 100% `Invoice`). Se encontró y corrigió una inconsistencia: los User Secrets locales de esta máquina para `Insuseg.Analytics.Api` tenían `SapServiceLayer:SalesSource` todavía en `Order` (quedó desactualizado de una configuración anterior) — se corrigió a `Invoice` para que un re-sync corrido desde acá no traiga la fuente equivocada.

**Verificación con SQL directo tras el resync:** 893 clientes únicos, top clientes/vendedores con nombres y montos reales y plausibles (ej. WSP Mining S.A., Karihosqui Calderón con $1.546 millones en ventas) — y confirma que el hallazgo anterior "todas las ventas sin vendedor asignado" (`SalesPersonCode = -1` en el 100% de los casos) era específico de `INSUSEG_PRB`; en la base real los vendedores sí están bien asignados. Compras: 156 proveedores únicos, ~$8.007 millones CLP comprados — también coherente.

### Hallazgo (2026-07-26): sí hay margen de venta por producto en `Invoices`, pero no en el campo esperado

Se verificó contra `INSUSEG` real si `Invoices.DocumentLines` trae margen por producto (pregunta directa del usuario). Resultado, tras inspeccionar varias facturas reales:

- Los campos personalizados `U_CtoUnit`/`U_MgenMont`/`U_MgenPorc` (los que sí tenían datos en la Orden de prueba vieja, ver hallazgo de Inventario) **no son confiables en `Invoices`**: en algunas facturas vienen `null`, en otras `0.0`, en otras sí tienen el valor correcto — inconsistente, parece depender de si SAP recalculó esos campos para ese documento puntual.
- **La fuente confiable es distinta**: los campos estándar de SAP `GrossBuyPrice` (costo unitario) y `LineTotal` (monto de venta de la línea) están consistentemente poblados. El margen se calcula como `LineTotal − (GrossBuyPrice × Quantity)` — confirmado que esto coincide exactamente con `U_MgenMont` en las líneas donde ese campo sí está poblado (ej. línea con `LineTotal=87900`, `GrossBuyPrice=19990`, cantidad 3 → costo total 59970 → margen 27930, igual al `U_MgenMont` de esa misma línea). Ojo con el nombre engañoso de `GrossProfitTotalBasePrice`: a pesar del nombre, es el **costo total de la línea** (`GrossBuyPrice × Quantity`), no la utilidad.
- **Implicancia para Inventario:** esto resuelve parcialmente el hallazgo anterior de "costo de producto en $0 para todo el catálogo" (`Items.MovingAveragePrice`/`AvgStdPrice`) — el costo real sí existe, pero vive en `GrossBuyPrice` a nivel de línea de venta, no en la ficha maestra del producto.

**Implementado el mismo día:** `SapDocumentLineDto`/`SaleLine` ahora capturan `GrossBuyPrice` (decimal, precision 18,4). No hizo falta pedirle nada nuevo a SAP — el campo ya venía en cada respuesta de `DocumentLines` (que se pide completo, sin `$select` de línea), solo faltaba deserializarlo y guardarlo. Migración `AddSaleLineGrossBuyPrice` generada y aplicada a `sqldb-insuseg-analytics`. Como las 74.524 líneas ya sincronizadas antes quedaron con `GrossBuyPrice = 0` (columna nueva sin backfill), se corrió **"Reprocesar historial completo"** de nuevo (~31 minutos) para completarlas. Verificado: **74.442 de 74.524 líneas (99,9%) quedaron con costo real** (el resto, ~82 líneas, probablemente ítems gratis/promocionales con costo real en $0 — no investigado en detalle). Muestra de margen calculado (`LineTotal − GrossBuyPrice × Quantity`) sobre datos reales: coherente, sin valores absurdos.

**UI construida el mismo día en `Pages/Ventas/Analisis.cshtml(.cs)`:**
- KPI nuevo "Margen total" ($ y % del total vendido en el período filtrado).
- Tabla nueva "Margen por producto": Top 20 productos por margen (venta − costo) en el período, con unidades, vendido, costo y % de margen. Costo/margen calculados vía join `SaleLines`+`Sales` (mismo patrón de `GroupBy`+`Sum` ya usado en Inventario — sin el pitfall de `GroupBy+First`, acá son solo sumas, traduce bien a SQL). Nombre de producto resuelto contra `Items` con diccionario en memoria (mismo patrón que `vendedorNombres`), con fallback al propio `ItemCode` si no se encuentra.
- Verificado con SQL directo contra la base real antes de mirarlo en el navegador: margen total ~$1.640 millones CLP sobre ~$8.067 millones vendidos en líneas (~20%, plausible), top 5 productos por margen con nombres y montos coherentes.

### ⚠️ CompanyDB equivocado usado hasta ahora — `INSUSEG` es la producción real, no `INSUSEG_PRB` (2026-07-26)

Confirmado por el admin de SAP del cliente: **`INSUSEG` es la base de datos de producción real**, y **`INSUSEG_PRB` es una base de pruebas usada por otra empresa** para un sistema de órdenes de venta no relacionado con este proyecto. Esto se resolvió después de varias idas y vueltas dentro del proyecto (2026-07-20 y 2026-07-24) — ver sección 3 para el detalle.

Todo el desarrollo hecho hasta la fecha (módulos de Ventas, Compras e Inventario, secciones más abajo) usó `INSUSEG_PRB` como fuente, sin saber que era la base equivocada. Esto explica por qué los datos parecían tan escasos y "muertos" (`Invoices` sin actividad desde 2021, `PurchaseOrders`/`PurchaseInvoices` con solo 7/13 registros parados desde 2022): **no es que el cliente use poco SAP, es que estábamos leyendo la base de otra empresa.**

**Verificación rápida en `INSUSEG` (producción real, 2026-07-26):** `Invoices` tiene **29,554** registros totales (vs. 27 en `INSUSEG_PRB`), con **149 facturas nuevas solo en los últimos 7 días** (clientes reales como Prosegur, WSP Mining, ALS Patagonia) — actividad diaria real, muy distinto al patrón congelado que se documentó en el hallazgo de abajo (que en retrospectiva describe la base de pruebas ajena, no el negocio real del cliente).

**Impacto:** hay que revisar si el patrón "Orders activo / Invoices muerto" que motivó cambiar la fuente de ventas a `Orders` (ver hallazgo más abajo) sigue siendo válido en `INSUSEG` — es muy probable que no, dado que `Invoices` ahí sí está vivo. Antes de seguir construyendo, repetir el análisis de actividad por entidad (`Orders`, `Invoices`, `DeliveryNotes`, `Quotations`, `PurchaseOrders`, `PurchaseInvoices`) pero contra `INSUSEG`.

**Reconfigurado el 2026-07-26:** `SapServiceLayer:CompanyDb` actualizado a `INSUSEG` (antes `INSUSEG_PRB`) en los tres lugares donde vive esta configuración — User Secrets de `Insuseg.Analytics.Api`, de `Insuseg.Analytics.Ingestion`, y la copia preparada para el papá del usuario (ver sección de login pendiente más abajo). Usuario/contraseña del Service Layer también actualizados (`ADMI1`, password en gestor de contraseñas — no en este repo —, confirmados por el usuario). `SapServiceLayer:SalesSource` cambiado de `"Order"` a `"Invoice"` en los mismos tres lugares — confirmado que en `INSUSEG` real, `Invoices` sí está activo (a diferencia de `INSUSEG_PRB`), así que la fuente de ventas vuelve a Facturas. Ningún cambio de código: `SalesSource` se diseñó justo para este caso (ver sección 2 y el módulo de Ventas más abajo). **Pendiente:** aún no se corrió ninguna sincronización contra `INSUSEG` — los datos en `sqldb-insuseg-analytics` siguen siendo los de la base equivocada.

### Módulo de Administración — gestión de usuarios (2026-07-23)

Se construyó el último placeholder del dashboard, acotado deliberadamente a **solo gestión de usuarios del equipo** — el otro tema que se había mencionado (un toggle de configuración para la fuente de ventas, Órdenes vs Facturas) quedó fuera de alcance a propósito: requeriría mover esa configuración de un archivo estático a algo editable en runtime (cambio de arquitectura más grande), y la decisión de negocio de cuál fuente es la correcta ni siquiera está confirmada con el cliente todavía.

**Qué se construyó:** `Pages/Administracion/Usuarios.cshtml(.cs)` (reemplaza el placeholder `Index.cshtml`) — lista los usuarios existentes en `AspNetUsers` y tiene un formulario para crear uno nuevo directo desde el dashboard (`UserManager<IdentityUser>.CreateAsync`, ya registrado en DI por Identity, sin configuración adicional). La contraseña inicial la escribe quien invita y se la pasa a la persona por fuera del sistema (no hay envío de correo configurado) — mismo criterio que se usó para crear la cuenta real existente.

**No se tocó el mecanismo existente:** `/register` + la llave de aprovisionamiento (`X-Provisioning-Key`) siguen intactos como vía alternativa/de script — la UI nueva es un camino adicional, no un reemplazo. Tampoco se agregó ningún sistema de roles/permisos: cualquier usuario logueado en el dashboard ya es "de confianza" (mismo modelo que el resto de la app, ej. cualquiera puede apretar los botones de sync).

**Probado de punta a punta el 2026-07-23** (real Azure SQL, vía un endpoint temporal descartable ya que no hay forma de loguearse por `curl` sin la contraseña real): se creó un usuario de prueba (`test-debug@insuseg.local`), se confirmó con SQL directo que apareció en `AspNetUsers` junto a la cuenta real, y se borró después — no queda rastro en la base. Sidebar (`_Layout.cshtml`) actualizado: Administración ahora apunta a `/Administracion/Usuarios` con el texto "Usuarios" (antes "General").

### Módulo de Compras (2026-07-23)

Se sincronizó `PurchaseOrders` por primera vez y se construyó el módulo de Compras completo (antes un placeholder "Próximamente"), con un alcance deliberadamente acotado dado lo que ya sabíamos de una sesión anterior: **`PurchaseOrders` tiene solo 7 registros y `PurchaseInvoices` 13, ambos sin actividad desde 2022-07-06** (más de 4 años). Confirmado con el usuario: construir igual, con lo que hay, en vez de pausar a preguntarle al cliente primero.

**Decisiones de diseño (distintas a Ventas/Inventario, justificadas por lo poco/viejo del dato):**
- **Solo `PurchaseOrders` como fuente**, sin el mecanismo de "fuente intercambiable" (`SourceDocType`) que sí tiene `Sale` — en Ventas esa complejidad se justificaba porque `Invoices` murió en 2021 mientras `Orders` seguía activo; acá **ambas entidades de compra murieron el mismo día** (2022-07-06), no hay una "activa" candidata que justifique la abstracción todavía. Nueva entidad `Purchase.cs` con PK simple (`DocEntry`, sin discriminador).
- **Sin detalle de líneas** — el módulo es de gasto/proveedor, no de costeo por producto (nota a futuro: las líneas de compra sí tendrían costo real, a diferencia del `Items.MovingAveragePrice` en $0 que encontramos en Inventario — posible mejora futura, no implementada).
- **`PurchaseSyncService` (`src/Insuseg.Analytics.Data/Sync/`): full upsert cada corrida, sin incremental por fecha** — si se hubiera copiado el mismo backfill de Sales (desde 2024-01-01), el sync nunca habría traído nada, porque **todos** los datos de compra son de 2022 o antes. Se trae siempre todo el rango (2000-01-01 a hoy) — con 7 documentos, no hay costo real en hacerlo así siempre (mismo criterio que Items en Inventario).
- **Sin tabla "proveedores desatendidos"** (paralelo de "clientes desatendidos"/"productos sin movimiento") — con 7 órdenes en total y ninguna después de 2022, esa tabla mostraría básicamente todos los proveedores, puro ruido sin valor. Queda fuera de alcance; tendría sentido si el volumen de compras se reactiva.

**Construido y probado de punta a punta (real SAP + real Azure SQL, 2026-07-23):**
- `Pages/Compras/Sincronizacion.cshtml(.cs)`: lista de compras + botón "Sincronizar ahora".
- `Pages/Compras/Analisis.cshtml(.cs)` (reemplaza el placeholder `Index.cshtml`): filtro de fechas, KPIs (total comprado en el período, cantidad de órdenes, proveedor top) y gráfico de barras "Compras por proveedor" — con una nota visible en la página aclarando que SAP no tiene actividad de compras desde 2022.
- Sync corrido contra el SAP real: **7 órdenes de compra**, verificado con SQL directo contra `sqldb-insuseg-analytics` que coinciden exactamente con lo ya conocido (Comercial Kuppel SpA, Segusa Chile SpA, Sociedad Importadora Maritex Spa, etc., fechas 2020-12-11 y 2022-07-06).
- Sidebar (`_Layout.cshtml`) actualizado: Compras ahora tiene los links "Análisis" y "Sincronización", igual que Ventas/Inventario.
- Migración `AddPurchases` generada y aplicada a `sqldb-insuseg-analytics`.

### Módulo de Inventario / rotación de stock (2026-07-23)

Se sincronizó por primera vez el catálogo de productos (`Items`) y el detalle de líneas de las Órdenes (`DocumentLines`), y se construyó el módulo de Inventario completo (antes un placeholder "Próximamente").

**Alcance de sincronización (decisiones de diseño):**
- Solo **stock agregado** (`Items.QuantityOnStock`), no detalle por almacén (`ItemWarehouseInfoCollection`) — evita depender del catálogo de `Warehouses` (nombres de los 7 almacenes, aún sin confirmar). Se puede agregar después si hace falta.
- Nueva entidad `Item` (`src/Insuseg.Analytics.Data/Entities/Item.cs`): `ItemCode` (PK), `ItemName`, `ItemsGroupCode`, `QuantityOnStock`, `MovingAveragePrice`. Sincronizada por `InventorySyncService` (`src/Insuseg.Analytics.Data/Sync/`), **full upsert cada corrida** (no incremental — a diferencia de Sales, Items no tiene una fecha útil para acotar el pull).
- Nueva entidad `SaleLine` (línea de detalle de una Orden/Factura): PK compuesta `(DocEntry, SourceDocType, LineNum)`, `ItemCode` como string plano **sin FK forzada** hacia `Item` — el sync de Items (botón Inventario) y el de Sales/líneas (botón Ventas) se disparan por separado, y una FK dura podría fallar si las líneas llegan antes que el catálogo. El join se hace en memoria en la página de Análisis.
- Migración `AddInventory` generada y **aplicada** a `sqldb-insuseg-analytics` el 2026-07-23 (tablas `Items`, `SaleLines`).

**Riesgo técnico verificado contra el SAP real:** `SapODataResponse` usa el envelope de **OData V2** (`"odata.nextLink"`, sin `@`). En V2, un dot-path en `$select` (ej. `DocumentLines/ItemCode`) no funciona sobre una colección de tipo complejo incluida por defecto — hay que pedir `DocumentLines` a secas. Confirmado en vivo (`Orders?$top=1&$select=DocEntry,DocumentLines`, solo GET descartable): SAP devuelve las líneas completas y pobladas así. `GetSalesDocumentsAsync` ahora incluye `DocumentLines` en su `$select`, y `SapSalesDocumentDto` trae una lista de `SapDocumentLineDto`.

**Backfill de líneas para las órdenes ya existentes:** las órdenes sincronizadas antes de este cambio ya habían avanzado el watermark incremental (`MAX(SaleDate)`), así que un sync normal nunca les iba a completar el detalle de líneas. Se agregó `SalesSyncService.SyncAsync(ct, forceFullResync: true)` — ignora el watermark y vuelve a pedir todo desde `InitialBackfillStartDate` (2024-01-01). Expuesto como botón secundario **"Reprocesar historial completo"** en `Ventas/Sincronizacion` (acción puntual, no de uso diario). Corrido el 2026-07-23 contra los datos reales: **53 documentos, 99 líneas** — verificado idempotente (corrido dos veces seguidas, mismo resultado, sin duplicar).

**Hallazgo real de datos (no un bug): el costo de producto viene en $0 para todo el catálogo.** Tanto `MovingAveragePrice` como `AvgStdPrice` de `Items` vienen en `0.0` directo desde SAP para los 25.398 productos (confirmado con una consulta GET directa) — este SAP no tiene cargado el costo en la ficha maestra del producto. El costo real parece vivir en el campo personalizado `U_CtoUnit` de cada línea de venta (ej. `824.0` para el producto A00001), no en `Items`. **Decisión (2026-07-23): se deja el KPI "Valor total de inventario" y la columna "Valor inmovilizado" en $0 por ahora**, mostrando el dato real de SAP — no se usa `U_CtoUnit` como sustituto todavía (quedaría fuera de alcance para productos nunca vendidos, y es una mejora a evaluar después, no ahora).

**Otro hallazgo real de datos:** de los 25.398 `Items` en el catálogo de SAP (la enorme mayoría histórico/descontinuado), solo **23 tienen stock actual (`QuantityOnStock > 0`)**. Por eso el KPI "SKUs con stock" (antes iba a llamarse "SKUs totales") cuenta solo esos 23, no las 25.398 filas del catálogo — mostrar el total real hubiera sido un número sin sentido de negocio. Mismo criterio se aplicó al listado de `Inventario/Sincronizacion`, ordenado por stock descendente en vez de alfabético (alfabético hubiera mostrado 200 productos muertos).

**Nota de rendimiento:** sincronizar el catálogo completo de Items tomó **~198 segundos** contra el SAP real (25.398 productos, paginado de a 100, servidor lento por el TLS 1.0/Apache viejo). El botón "Sincronizar ahora" de Inventario tarda bastante más que el de Ventas — normal, no es un error.

**Construido y probado de punta a punta (real SAP + real Azure SQL, 2026-07-23):**
- `Pages/Inventario/Sincronizacion.cshtml(.cs)`: lista de productos (ordenados por stock) + botón "Sincronizar ahora".
- `Pages/Inventario/Analisis.cshtml(.cs)` (reemplaza el placeholder `Index.cshtml`): KPIs (SKUs con stock, unidades en stock, valor de inventario), tabla **"productos sin movimiento"** (mismo patrón que "clientes desatendidos" en Ventas — stock > 0 sin ventas en los últimos 60 días, sobre todo el historial) y tabla de **índice de rotación** (unidades vendidas en el período ÷ stock actual, peor a mejor, excluye productos sin stock).
- Verificado con SQL directo contra la base real que la lógica de rotación y última venta da resultados coherentes antes de mirarlo en el navegador (no se pudo loguear como el usuario real para probarlo por curl — la contraseña no vive en este equipo a propósito).
- **Aclaración de negocio importante sobre el índice de rotación:** puede dar valores > 1 (ej. un producto con stock actual = 1 pero 355 unidades vendidas en el período) — esto es correcto, no un error: "unidades vendidas" es acumulado sobre *todo* el período filtrado (por defecto, 2+ años de historial), mientras que "stock" es una foto del inventario *hoy*. Un producto se puede vender y reponer muchas veces en ese lapso. Índice > 1 es una señal de buena rotación, no un error de datos.
- Sidebar (`_Layout.cshtml`) actualizado: Inventario ahora tiene los links "Análisis" y "Sincronización", igual que Ventas.

- [ ] Confirmar catálogo de `Warehouses` (nombres de los 7 almacenes 01–07).
- [ ] Confirmar con el cliente el significado de negocio de los campos custom de costo/margen (`U_CtoUnit`, `U_MgenPorc`, etc.) y de categorización de productos (`U_Categoria`, `U_Familia`, `U_Marca`, etc.).
- [x] Diseñar y crear el primer modelo de tablas en Azure SQL — módulo de venta detallada: entidades `Sale` (`DocEntry`, `DocNum`, `CardCode`, `CardName`, `Amount`, `SaleDate`, `SalesPersonCode`) y `SalesPerson` (`SalesEmployeeCode`, `SalesEmployeeName`) en `Insuseg.Analytics.Data`. Migración `InitialCreate` generada y **aplicada** a `sqldb-insuseg-analytics` el 2026-07-22.
- [x] Crear el proyecto .NET 10 (solución `Insuseg.Analytics.slnx` — VS2026 migró el `.sln` clásico automáticamente — con `src/Insuseg.Analytics.Data` (EF Core), `src/Insuseg.Analytics.Api` (Web API) y `src/Insuseg.Analytics.Ingestion` (Azure Functions, Timer Trigger) creados y referenciados en la solución).
- [x] Configurar **.NET User Secrets** en `Insuseg.Analytics.Ingestion` para desarrollo local: `SapServiceLayer:BaseUrl/CompanyDb/Username/Password` y `ConnectionStrings:InsusegAnalyticsDb` guardados en `secrets.json` (fuera del repo, `%APPDATA%\Microsoft\UserSecrets\`). `Program.cs` los carga vía `AddUserSecrets<Program>()` + `IOptions<SapServiceLayerOptions>` (`src/Insuseg.Analytics.Ingestion/Configuration/SapServiceLayerOptions.cs`) y `AddDbContext<InsusegAnalyticsDbContext>`.
- [x] **Fuente cambiada de `Invoices` a `Orders`** (2026-07-22, tras confirmar que `Invoices` está parado desde 2021 — ver hallazgo abajo). Backfill inicial fijo: **2024-01-01 hasta hoy** (`SalesIngestionFunction.InitialBackfillStartDate`), sincronización incremental después basada en `MAX(SaleDate)` ya almacenado.
- [x] **Preparado para volver a cambiar de fuente sin tocar código** (aún se está esperando confirmación del cliente sobre si `INSUSEG_PRB` tiene o no un ambiente de pruebas separado — ver hallazgo abajo, es probable que haya que revertir a `Invoices`): se agregó `SalesSourceDocumentType` (`Order`/`Invoice`) como parte de la clave compuesta de `Sale` (`DocEntry` + `SourceDocType`) porque `DocEntry` **no es único entre tipos de documento** en SAP (una Orden y una Factura pueden compartir número por coincidencia — sin esto, cambiar de fuente podía mezclar filas de un documento con otro sin darse cuenta). `SapServiceLayerOptions.SalesSource` (User Secret `SapServiceLayer:SalesSource`, valores `"Order"` o `"Invoice"`) es ahora el único punto de cambio — `SapServiceLayerClient.GetSalesDocumentsAsync` y `SalesIngestionFunction` resuelven todo a partir de ahí. Migración `AddSalesSourceDocType` generada y **aplicada** a `sqldb-insuseg-analytics` el 2026-07-22 (las 53 filas existentes quedaron marcadas `SourceDocType=Order` vía `defaultValue`, sin pérdida de datos — verificado). El endpoint `GET /api/sales` ahora también expone `sourceDocType` en la respuesta.
- [x] Implementar y probar de punta a punta `SalesIngestionFunction` (`src/Insuseg.Analytics.Ingestion/SalesIngestionFunction.cs` + `Sap/SapServiceLayerClient.cs`): login/GET de solo lectura contra el Service Layer (nunca POST/PATCH/DELETE sobre entidades de negocio — ver regla sección 3), paginación vía `odata.nextLink`, upsert por `DocEntry`/`SalesEmployeeCode` hacia `Sales`/`SalesPersons`. Probado localmente con Azure Functions Core Tools + Azurite contra el SAP y el Azure SQL **reales** el 2026-07-22 — **53 órdenes de venta reales cargadas en `sqldb-insuseg-analytics`** (rango 2024-01-01 a 2026-07-22), sin errores (ver sección de bugs resueltos abajo).
- [x] Endpoint de lectura `GET /api/sales` (`src/Insuseg.Analytics.Api/Sales/SalesController.cs` + `SaleDto.cs`) sobre `Sales`/`SalesPersons` en Azure SQL — nunca consulta SAP directamente. Soporta filtro opcional `?desde=&hasta=`. User Secrets configurados también en `Insuseg.Analytics.Api` (`ConnectionStrings:InsusegAnalyticsDb`, mismo valor que en Ingestion). Probado localmente el 2026-07-22 contra los 53 registros reales — funciona, incluye el join a `SalesPersons` para el nombre del vendedor.
- [x] Implementar **ASP.NET Core Identity** sobre `sqldb-insuseg-analytics`: `InsusegAnalyticsDbContext` ahora hereda de `IdentityDbContext<IdentityUser>` (tablas `AspNetUsers`/`AspNetRoles`/etc., migración `AddIdentity` aplicada el 2026-07-22 sin tocar `Sales`/`SalesPersons`). Se usó `AddIdentityApiEndpoints<IdentityUser>()` + `MapIdentityApi<IdentityUser>()` (endpoints nativos de .NET 8+: `/register`, `/login`, `/refresh`, etc., autenticación Bearer). `SalesController` ahora tiene `[Authorize]`. Probado de punta a punta: `/api/sales` sin token → 401; registro + login + `/api/sales` con token Bearer → 200 con los datos reales. Usuario de prueba (`test@insuseg.local`) usado para la prueba, luego borrado de `AspNetUsers`.
- [x] **`/register` protegido con llave de aprovisionamiento** (2026-07-22): `MapIdentityApi` no permite excluir una sola ruta del grupo, así que se agregó un middleware en `Program.cs` que intercepta `POST /register` y exige el header `X-Provisioning-Key` — sin la llave correcta (o si no está configurada), responde `404` antes de llegar al endpoint real, como si la ruta no existiera. La llave vive en User Secrets (`Provisioning:RegistrationKey`, generada con `RandomNumberGenerator`, no está en este archivo). `/login`/`/refresh` no se tocaron. Probado: sin header → 404; con header correcto → 200 y la cuenta se crea normal. Para dar de alta un usuario nuevo del equipo: llamar `POST /register` con ese header.
- [x] Cuenta real del equipo creada en `AspNetUsers`: `info@aitbp.com` (password gestionada por el usuario, no se documenta acá).
- [x] **Confirmado: el firewall de SAP acepta conexiones desde la red de Azure** (2026-07-22). Prueba: contenedor temporal (`az container create`, imagen `mcr.microsoft.com/azure-cli`, región `eastus2`) haciendo `curl` a `https://159.69.163.254:50003/b1s/v1/` — se completó el TCP connect y el servidor SAP respondió el "Server Hello" del handshake TLS; el error final (`unsupported protocol`) fue solo porque el OpenSSL de esa imagen no soporta TLS 1.0 en absoluto (limitación de la herramienta de prueba, no de la red). Contenedor borrado después de la prueba, sin costo persistente. De-riesga el futuro despliegue del Function App: la conectividad de red no es un bloqueante.
- [x] **Managed Identity para Azure SQL** — implementada en el despliegue real (ver sección "⚠️ Pendiente" abajo para el detalle completo y el problema sin resolver).
- [ ] **Azure Key Vault** para las credenciales del Service Layer de SAP en producción (siguen como App Settings planas por ahora — más seguras que un archivo en el repo, pero no es el diseño final). No abordado todavía.
- [ ] (Resuelto por ahora) Se quitó `Microsoft.AspNetCore.OpenApi` de `Insuseg.Analytics.Api` — traía `Microsoft.OpenApi 2.0.0` con vulnerabilidad alta (`NU1903`) sin fix compatible con .NET 10 preview (la serie 3.x corrige la vulnerabilidad pero rompe el generador de código de ASP.NET Core 10.0.9). No era un requisito funcional, solo generación de spec OpenAPI/Swagger. Reevaluar agregarlo de nuevo cuando ASP.NET Core publique una versión estable compatible con OpenApi.NET 3.x.
- [ ] Implementar ASP.NET Core Identity (login + roles) sobre `sqldb-insuseg-analytics`.
- [ ] Comunicar al cliente el riesgo de seguridad del TLS 1.0 expuesto en el servidor SAP, adicionalmente que el password del usuario `ADMI1` del Service Layer es de solo 4 dígitos, y que el certificado del servidor es autofirmado (`SEC_E_UNTRUSTED_ROOT`) — tres hallazgos de seguridad para el mismo servidor expuesto en IP pública.

### ⚠️ Hallazgo importante: `Invoices` está "muerto" desde 2021, pero el sistema sí está activo

Al probar la ingesta contra el Service Layer real (2026-07-22), `GET /Invoices/$count` devuelve solo **27** facturas en total, la más reciente de `2021-01-18`. Para confirmar si esto era el sistema completo inactivo o solo esa entidad, se revisó `UpdateDate`/`CreationDate` (no solo `DocDate`) de las 6 entidades documentadas en `BDhana.md`:

| Entidad | Registro más reciente | Fecha (`UpdateDate`) | Total de registros |
|---|---|---|---|
| **Items** | `A10641` "ZAPATO TP 3041, NEGRO NUM 35" | **2026-07-21 (hoy)** | — |
| `Orders` (venta, no facturado) | DocEntry 157 / DocNum 55 — Victor Hugo Gonzalez Palma | 2026-04-10 | 55 |
| `PurchaseOrders` | DocEntry 22 / DocNum 7 — Comercial Kuppel SpA | 2022-07-06 | 7 |
| `PurchaseInvoices` | DocEntry 30 / DocNum 12 — Segusa Chile SpA | 2022-07-06 | 13 |
| `BusinessPartners` | `10260237-4C` Marcela Rebeca Moraga Ramirez | 2021-01-18 | — |
| `Invoices` (facturado) | DocEntry 51 / DocNum 10004 | 2021-01-18 | 27 |

**Conclusión: el sistema está activo** (productos actualizados hoy, órdenes de venta hasta abril 2026), pero específicamente `Invoices` — la entidad elegida como fuente del módulo de ventas (ver sección 2, decisión "Invoices (facturado)") — dejó de recibir documentos nuevos en 2021, mientras que `Orders` (55 documentos, el doble que `Invoices`) sigue activo. Esto sugiere que la facturación real podría no estarse registrando en este SAP (¿otro sistema? ¿atraso operativo?), o que el flujo del negocio simplemente no cierra las órdenes como factura dentro de SAP.

**Ampliación (2026-07-22):** se revisaron también las otras dos entidades del ciclo de venta (Cotización → Orden → Entrega → Factura):

| Entidad | Total | Más reciente (`UpdateDate`) |
|---|---|---|
| `Quotations` (cotización) | 3 | 2025-03-18 (uno de los 3 registros es literalmente cliente "**Cliente prueba**") |
| **`Orders` (orden de venta)** | **55** | **2026-04-10** |
| `DeliveryNotes` (entrega) | 14 | 2021-01-04 |
| `Invoices` (factura) | 27 | 2021-01-18 |

El ciclo completo de venta (Orden → Entrega → Factura) se corta justo después de la Orden: **`DeliveryNotes` e `Invoices` dejaron de usarse casi el mismo día (enero 2021)**, mientras que las Órdenes de venta se han seguido creando con normalidad hasta abril 2026. Es decir, desde 2021 nada se está entregando ni facturando *dentro de SAP*, aunque las órdenes sí se siguen registrando.

**Pendiente de decidir con el cliente antes de seguir con el módulo de ventas:** dado este patrón, la fuente correcta de "monto de venta" probablemente sea `Orders.DocTotal` (actividad real y reciente) en vez de `Invoices.DocTotal` (parado desde 2021) — a menos que el cliente confirme que la facturación real vive en otro sistema y no debe considerarse parte de este análisis. Esto puede requerir ajustar el modelo ya creado (`Sale`/`SalesPerson` en `Insuseg.Analytics.Data`) y la lógica de `SalesIngestionFunction`.

### Bugs resueltos durante la prueba end-to-end de `SalesIngestionFunction` (2026-07-22)

Tres problemas reales encontrados y corregidos al probar contra el SAP y el Azure SQL de verdad (no se hubieran visto sin probar, `curl -k` los enmascaraba):
1. **Certificado autofirmado del servidor SAP** (`SEC_E_UNTRUSTED_ROOT`) — el `HttpClient` de .NET valida la cadena de certificados por defecto (a diferencia de `curl -k`). Se aceptó explícitamente solo en `SapServiceLayerClient` vía `RemoteCertificateValidationCallback`, documentado como riesgo a comunicar al cliente.
2. **`Expect: 100-continue` / `Transfer-Encoding: chunked`** — `HttpClient.PostAsJsonAsync` no calcula `Content-Length` de antemano, así que manda el POST de `/Login` chunked; el Apache viejo que expone el Service Layer respondía 500 genérico (página HTML de Apache, no error JSON de SAP) ante eso. Se cambió a `StringContent` explícito (sí calcula `Content-Length`) y se desactivó `ExpectContinue`.
3. **Auto-pausa de Azure SQL Serverless** — la primera conexión tras inactividad agota el tiempo de espera mientras la base "despierta" (`Error Number:-2`, timeout). Se agregó `EnableRetryOnFailure()` al `UseSqlServer(...)` para que EF Core reintente automáticamente en vez de fallar la corrida completa.

### ⚠️ Pendiente sin resolver: ejecución del Function App en la nube (2026-07-22)

Se desplegó `Insuseg.Analytics.Ingestion` a Azure por primera vez. Lo que quedó **confirmado funcionando**:
- Recursos creados: `stinsusegingest` (storage), `func-insuseg-ingestion` (Function App, Consumption/Windows), `insuseg-ingestion-insights` (Application Insights).
- Managed Identity habilitada en el Function App, con usuario creado en `sqldb-insuseg-analytics` (`FROM EXTERNAL PROVIDER`, roles `db_datareader`+`db_datawriter`) — requirió antes configurar a `info@aitbp.com` como Azure AD Admin del servidor SQL (no había ninguno).
- App Settings configurados: `ConnectionStrings__InsusegAnalyticsDb` (con `Authentication=Active Directory Managed Identity`, **sin password**) y `SapServiceLayer__*` (BaseUrl/CompanyDb/Username/Password/SalesSource — estos sí en texto plano por ahora, pendiente Key Vault).
- El código compila y se sube sin error (`func azure functionapp publish`), la función queda registrada (`isDisabled: false`) y el host reporta `"state":"Running"`.
- Confirmado por prueba con contenedor descartable: el firewall de SAP acepta conexiones desde la red de Azure (ver hallazgo más arriba).

**Lo que NO se pudo confirmar (primera pasada):** que `SalesIngestionFunction` realmente se ejecute en la nube. Después de múltiples disparos manuales (`POST /admin/functions/...`) y de cambiar el horario a "cada 2 minutos" para forzar una ejecución natural, **nunca apareció ninguna conexión a Azure SQL, ningún registro en Application Insights, ni ningún log de ejecución** — pero tampoco ningún error nuevo. El horario se revertió a `0 0 * * * *` (cada hora).

**Bug real que sí se encontró y corrigió en el camino:** el Function App se había creado originalmente con `--runtime-version 10` (.NET 10, todavía preview). Aunque el CLI aceptó el valor, el runtime nunca llegó a arrancar — el Event Log de Windows mostró `System.IO.FileNotFoundException: Could not load file or assembly 'System.Runtime, Version=10.0.0.0...'` y el propio nombre interno del sitio en IIS quedó fijado como `..._DOTNET-ISOLATED_10.0_X64` desde la creación, sin que cambiar `netFrameworkVersion` después alcanzara para corregirlo. **Se resolvió bajando `Insuseg.Analytics.Data` e `Insuseg.Analytics.Ingestion` a .NET 9** (LTS, con soporte confirmado — `Insuseg.Analytics.Api` se dejó en .NET 10 ya que aún no se despliega) y **recreando el Function App desde cero** ya con `--runtime-version 9`. Después de este fix, los errores de arranque desaparecieron del Event Log.

**Continuación del diagnóstico (misma noche, más tarde):** revisando directamente los blobs internos que usa el Timer Trigger para llevar el horario (cuenta `stinsusegingest`, contenedor `azure-webjobs-hosts`, blob `timers/func-insuseg-ingestion/Host.Functions.SalesIngestionFunction/status`), apareció `{"Last":"2026-07-22T23:00:00...","Next":"2026-07-23T00:00:00Z",...}` — es decir, **el mecanismo del Timer Trigger sí calculó y "marcó" el horario de las 23:00 correctamente**. Pero cruzando esto con los logs de host descargados (`LogFiles/Application/Functions/Host/*.log`), se ve que:
- Una instancia del host arrancó a las 22:37:46 y calculó bien el próximo horario (23:00:00Z).
- La app se quedó sin actividad y **el plan Consumption la apagó por inactividad** (`alwaysOn: false`) antes de llegar a las 23:00.
- Recién volvió a arrancar una instancia nueva a las **23:38:15** (probablemente por nuestras propias consultas de diagnóstico) — y para ese momento, el cálculo de "próximos horarios" ya no incluía las 23:00 (saltó directo a las 00:00), consistente con que el horario de las 23:00 se marcó como "manejado" sin que la función realmente se haya ejecutado.

**Hipótesis actual (no 100% confirmada, pero coherente con toda la evidencia):** en el plan Consumption de Windows, sin tráfico HTTP que mantenga la app despierta, el mecanismo que debería "despertar" la app específicamente para el Timer Trigger no está siendo lo suficientemente confiable — la app se duerme, y si nadie/nada la despierta a tiempo, el horario se pierde en vez de ejecutarse tarde. Esto es una limitación conocida de Timer Triggers sin tráfico HTTP en Consumption/Windows.

**Prueba pendiente para la próxima sesión:** la app quedó despierta desde las 23:38:15. El próximo horario natural es **00:00 UTC** — si nadie la deja dormir antes de esa hora, sería una prueba justa. Si tampoco dispara ahí, confirma la hipótesis y hay que migrar de plan/SO.

**Próximos pasos sugeridos si la hipótesis se confirma:**
- Plan **Premium** (tiene instancias "Always Ready", pensado justo para este caso) en vez de Consumption — tiene costo, ya no es gratis.
- Plan **Flex Consumption** (que Azure recomienda activamente) — mantiene el modelo pay-per-use pero con otro mecanismo de scaling, podría comportarse distinto.
- Recrear en **Linux** en vez de Windows — no probado, podría tener un mecanismo de wake-up más confiable.
- Como último recurso: un "keep-alive" — otro Timer Trigger HTTP-ping cada varios minutos, o habilitar algo que genere tráfico regular, para evitar que la app se duerma del todo (workaround, no soluciona la causa de fondo).

### Solución alternativa: dashboard web con sincronización manual (2026-07-23)

Dado que la confiabilidad del Timer Trigger en la nube quedó sin resolver, se construyó un camino alternativo que **no depende de Azure Functions en absoluto**: convertir `Insuseg.Analytics.Api` en una aplicación web completa (no solo JSON), con login y un botón que sincroniza SAP→Azure SQL al toque, mostrando el resultado en pantalla. Probado de punta a punta contra SAP y Azure SQL reales — funciona.

**Cambios de arquitectura:**
- El cliente SAP y la lógica de sincronización (`SapServiceLayerClient`, DTOs, `SapServiceLayerOptions`) se movieron de `Insuseg.Analytics.Ingestion` a **`Insuseg.Analytics.Data`** (namespaces `Insuseg.Analytics.Data.Sap` / `.Configuration`), para que sean compartidos.
- Se extrajo la lógica de sincronización a una clase nueva, **`SalesSyncService`** (`Insuseg.Analytics.Data/Sync/SalesSyncService.cs`), con un método `SyncAsync()` que devuelve un `SalesSyncResult` (resumen: fuente, cantidad de vendedores/documentos, rango de fechas).
- `SalesIngestionFunction` (la Function App) ahora es un envoltorio delgado que solo llama a `SalesSyncService.SyncAsync()` — sigue existiendo y compilando, no se borró nada, solo dejó de tener la lógica duplicada.
- `Insuseg.Analytics.Api` ahora registra `SapServiceLayerClient`/`SalesSyncService` también, y tiene sus propios User Secrets `SapServiceLayer:*` (mismos valores que Ingestion).

**App web nueva (dentro de `Insuseg.Analytics.Api`):**
- Se agregó **Razor Pages** (`AddRazorPages()`/`MapRazorPages()`) y **autenticación por cookie** (`ConfigureApplicationCookie`, esquema `"Identity.Application"`) — conviven con el sistema de token Bearer que ya tenía la API (`AddIdentityApiEndpoints`) para `/api/sales`; son dos mecanismos de auth independientes para dos consumidores distintos (navegador vs. API programática).
- `Pages/Account/Login.cshtml(.cs)` — formulario de usuario/contraseña, `SignInManager.PasswordSignInAsync(...)`.
- `Pages/Account/Logout.cshtml(.cs)` — cierra sesión.
- `Pages/Ventas/Sincronizacion.cshtml(.cs)` (antes `Pages/Index`) — la lista de últimas 200 ventas + el botón **"Sincronizar ahora"** (`OnPostSyncAsync`) que llama a `SalesSyncService.SyncAsync()` directo (sin pasar por Azure Functions) y muestra el resumen vía `TempData` tras un redirect.
- `Pages/Shared/_Layout.cshtml` — header con logo/usuario/logout; body sin layout propio en Login (pantalla completa).
- Identidad visual tomada de **insuseg.cl** (analizado con WebFetch + `curl` del CSS real del sitio, no una impresión general): naranja de marca `#ef6000`, texto oscuro `#303030`, gris claro `#eaeaea`, tipografía `Open Sans`, bordes rectos sin sombras. Logo descargado y guardado localmente en `wwwroot/img/insuseg-logo.png` (no enlazado en vivo al sitio del cliente). CSS en `wwwroot/css/site.css`.

**Probado de punta a punta el 2026-07-23** (local, `dotnet run` + `curl` con cookies): `/` sin sesión redirige a Login (302); login con la cuenta real `info@aitbp.com` funciona; el botón "Sincronizar ahora" tardó ~8s (login a SAP + query + upsert reales) y devolvió el resumen correcto.

### Navegación por secciones + módulo de Análisis de ventas (2026-07-23)

Se agregó una **barra lateral fija** con 4 secciones (Ventas, Compras, Inventario, Administración) — pensada para que cada una vaya acumulando módulos de análisis con el tiempo. Reorganización de páginas por carpeta: `Pages/Ventas/`, `Pages/Compras/`, `Pages/Inventario/`, `Pages/Administracion/`. Compras/Inventario/Administración quedaron con una pantalla "Próximamente" — sin contenido real todavía. `Pages/Index.cshtml` ahora es solo un redirect a `/Ventas/Analisis` (landing page post-login).

**Primer módulo de análisis real: `Pages/Ventas/Analisis.cshtml(.cs)`.** Se cargó la skill de `dataviz` antes de construirlo. Contenido:
- Filtro de fechas (Desde/Hasta) en una fila arriba de todo — por defecto cubre **todo el historial disponible** (no una ventana fija de días), para que la primera vista no aparezca vacía si las ventas más recientes son viejas.
- KPIs (stat tiles, no gráfico): total vendido, cantidad de órdenes, cliente top, vendedor top del período.
- **Ventas por cliente** y **ventas por vendedor**: gráfico de barras horizontal en HTML/CSS plano (sin librería de gráficos) + valor exacto al lado de cada barra. Una sola tonalidad de marca (naranja `#ef6000`) — es una comparación de magnitud (un solo valor por categoría), no de identidad, así que no aplica paleta categórica ni corresponde correrle el validador de esa skill (eso es solo para paletas categóricas). Contraste del naranja sobre blanco verificado igual (3.31:1) con el validador de la skill.
- **Clientes desatendidos**: tabla (no gráfico — son varios atributos por fila). Definición usada: cliente que compró antes pero no en los últimos **60 días** (constante `DiasParaDesatendido`, ajustable). Se calcula sobre *todo* el historial, no sobre el filtro de fechas de arriba. Muestra cliente, vendedor responsable (el de su venta más reciente), última compra, días sin comprar, monto histórico — ordenado por monto histórico para priorizar.
- Pendiente explícitamente NO construido todavía: análisis de **rotación de stock** (Inventario) — requiere sincronizar `Items` y el detalle de líneas de las Órdenes desde SAP, que no se ha tocado. Documentado como próximo paso grande cuando se retome esa sección.

**Dos bugs de EF Core encontrados y corregidos al probar** (ambos son la misma familia de problema: LINQ que no se puede traducir a SQL):
1. Proyectar directo a un `record` (constructor) dentro de un `.Select()` después de `GroupBy` no traduce — solución: agrupar a un tipo anónimo (sí traduce) y convertir al `record` después, en memoria.
2. Encadenar `.Where()`/`.Select()` justo después de `GroupBy(...).Select(g => g.OrderBy().First())` rompe la traducción (`KeyNotFoundException: 'EmptyProjectionMember'`) — solución: materializar (`ToListAsync`) el resultado del `GroupBy+First` primero, y hacer el filtro/proyección siguiente en memoria (LINQ-to-Objects), no en la misma consulta SQL.

**Probado de punta a punta el 2026-07-23** (local, `curl` con cookies): `/Ventas/Analisis` devuelve 200 con datos reales — $527.107.991 total, 53 órdenes, cliente top "Victor Hugo Gonzalez Palma", vendedor top "-Ningún empleado del departamento de ventas-" (esperable, la mayoría de las ventas no tienen vendedor asignado en SAP), tabla de clientes desatendidos con filas.

**Revisión visual en navegador real, misma noche:** confirmado — login y dashboard se ven y funcionan bien en un navegador de verdad (Chrome/Edge del usuario), no solo por `curl`.

**Confirmado al 100% (no solo "esperable"): las 53 órdenes tienen `SalesPersonCode = -1`.** Se consultó directo `sqldb-insuseg-analytics` (`SELECT SalesPersonCode, COUNT(*) FROM Sales GROUP BY SalesPersonCode`) y **ninguna** orden tiene vendedor real asignado — no es una mayoría, es el total. No es un bug de la app (el código lee y muestra bien el dato tal cual viene de SAP); es que en SAP nunca se llenó el campo de vendedor responsable al crear las Órdenes. Mientras esto no cambie del lado de SAP, "ventas por vendedor" y la columna de vendedor en clientes desatendidos van a mostrar siempre el mismo valor sin aportar información real. **Decisión (2026-07-23): se deja el módulo tal cual está** (mostrando el dato real de SAP) — no se oculta ni se avisa al cliente todavía, queda pendiente decidir eso más adelante si se retoma el tema.

**Pendiente:** ya no incluye la revisión visual (hecha). Sigue pendiente: no se ha desplegado nada de esto a Azure.

### ⚠️ Pendiente sin resolver: no se pudo iniciar sesión desde otra computadora (2026-07-23/24)

El repo se compartió y se corrió (`dotnet run`) en una segunda computadora (la del papá del usuario), y no dejó iniciar sesión con la cuenta `info@aitbp.com` que sí funciona en esta máquina. **Diagnóstico en curso, no resuelto** — no se llegó a ver el mensaje de error exacto que le apareció a él. Dos causas candidatas, ninguna descartada todavía:

1. **Faltan los User Secrets en esa máquina.** La cadena de conexión a `sqldb-insuseg-analytics` y las credenciales de SAP viven en `%APPDATA%\Microsoft\UserSecrets\` de *esta* máquina, no en el repo (a propósito, ver regla de seguridad). Si él solo bajó el código, la app en su compu no tiene con qué conectarse a la base — probablemente daría un error 500 al intentar loguear, no un "contraseña incorrecta".
2. **El firewall de `sql-insuseg-centralus` no tiene la IP de su computadora.** Solo estas IPs están permitidas hoy: `AllowAzureServices` (0.0.0.0, servicios de Azure), `AllowMyIP` (la de este equipo al crear el recurso), `AllowTerminal-IGANCIO` (la de esta terminal). Si su IP no está, cualquier intento de conexión a Azure SQL desde su máquina se rechaza de plano.

**Confirmado (2026-07-24): sí va a ser parte del equipo.** Pero se deja la resolución pendiente por ahora — el usuario no puede hablar con él en este momento para coordinar (compartirle credenciales, confirmar qué error vio exactamente, etc.). Cuando se retome: crear su cuenta real en `AspNetUsers` (vía `POST /register` con la llave de aprovisionamiento), agregar su IP al firewall de `sql-insuseg-centralus`, y asegurarse de que su máquina tenga los User Secrets configurados (no vienen con el repo, a propósito).

**Adelantado el 2026-07-23 (mismo día, más tarde, con el correo confirmado): lo que no depende de hablar con él ya está listo.**
- Su cuenta real ya existe en `AspNetUsers`: `elobog@Melirrepu.com` (creada vía `UserManager.CreateAsync` a través de un endpoint temporal descartable, mismo mecanismo de prueba usado para el módulo de Administración — confirmado con SQL directo, contraseña inicial acordada con el usuario, no documentada en este archivo).
- Los User Secrets de `Insuseg.Analytics.Api` (SAP + cadena de conexión) se copiaron a una carpeta de preparación fuera del repo, junto con instrucciones paso a paso, listas para pasarle a su computador cuando se coordine con él.
- **Sigue bloqueado, no se puede avanzar sin hablar con él:** agregar su IP pública al firewall de `sql-insuseg-centralus` (regla nueva tipo `AllowMyIP`) — se necesita su IP actual, y copiarle físicamente el archivo de secrets a su máquina.

**Nota (2026-07-23, misma sesión): el usuario cree que su papá ya tiene los User Secrets configurados** de un intento anterior — sin confirmar al 100%, pero si es así, la causa #1 (secrets faltantes) queda descartada y el firewall (causa #2) pasa a ser el sospechoso principal y probablemente el único paso que falta. Sigue haciendo falta su IP pública actual para poder agregar la regla.

---

## Pasos preliminares (para replicar la preparación del entorno en otro PC)

Esta sección documenta, en orden, todo lo que se hizo para dejar el entorno de desarrollo y los recursos de Azure listos. Sirve para repetir el proceso en otra máquina o por otra persona del equipo.

### 1. Instalar Azure CLI

```powershell
winget install --id Microsoft.AzureCLI --source winget --silent --accept-package-agreements --accept-source-agreements
```

Verificar instalación:
```powershell
az version
```

### 2. Si hay un antivirus con inspección SSL/TLS (ej. Norton), agregar excepciones

Algunos antivirus (Norton en este caso) interceptan el tráfico HTTPS para escanearlo y generan certificados que Azure CLI (basado en Python) rechaza por validación estricta, aunque el certificado real de Microsoft sea válido. Síntoma: `SSL: CERTIFICATE_VERIFY_FAILED` al ejecutar cualquier comando `az`.

**Solución:** en la configuración del antivirus, agregar como excepción de escaneo SSL los dominios:
- `management.azure.com`
- `login.microsoftonline.com`
- `graph.windows.net`
- `*.database.windows.net`
- `*.core.windows.net`

(En Norton: Configuración → Firewall/Web Shield → Exclusiones de escaneo SSL. Si la versión no permite excluir por dominio, excluir el proceso `C:\Program Files\Microsoft SDKs\Azure\CLI2\python.exe`.)

### 3. Login en Azure

```powershell
az login
```
Se abre el navegador para autenticarse. Confirmar que la suscripción activa es **Insuseg**:
```powershell
az account show
```

### 4. Registrar el resource provider de SQL (solo la primera vez por suscripción)

```powershell
az provider register --namespace Microsoft.Sql
# Esperar hasta que quede "Registered":
az provider show --namespace Microsoft.Sql --query "registrationState" -o tsv
```

### 5. Crear el Resource Group

```powershell
az group create --name rg-insuseg-analytics --location eastus2
```

### 6. Crear el servidor SQL lógico y la base de datos

> Nota: `East US` y `East US 2` estuvieron bloqueadas para nuevos servidores SQL por Azure al momento de crear este proyecto (`RegionDoesNotAllowProvisioning`, restricción temporal de capacidad). Si vuelve a pasar, probar otras regiones (`centralus`, `southcentralus`, `westus2`, `brazilsouth`, etc.) — **cada intento fallido en una región reserva el nombre**, así que hay que usar un nombre de servidor distinto en cada intento.

```powershell
# Generar una password fuerte para el admin del servidor y guardarla en un gestor de contraseñas (NO commitear a git)
$pwd = "<password-generada-segura>"
$pwdPath = "$env:TEMP\sql_admin_password.txt"
Set-Content -Path $pwdPath -Value $pwd -Encoding utf8 -NoNewline

az sql server create `
    --name sql-insuseg-<region> `
    --resource-group rg-insuseg-analytics `
    --location <region> `
    --admin-user sqladmin_insuseg `
    --admin-password "@$pwdPath"
```

Crear la base de datos con el free tier:
```powershell
az sql db create `
    --resource-group rg-insuseg-analytics `
    --server sql-insuseg-<region> `
    --name sqldb-insuseg-analytics `
    --edition GeneralPurpose `
    --family Gen5 `
    --capacity 2 `
    --compute-model Serverless `
    --use-free-limit `
    --free-limit-exhaustion-behavior AutoPause
```

### 7. Configurar el firewall

```powershell
# Permitir servicios de Azure (necesario para que Functions/App Service se conecten)
az sql server firewall-rule create `
    --resource-group rg-insuseg-analytics `
    --server sql-insuseg-<region> `
    --name AllowAzureServices `
    --start-ip-address 0.0.0.0 `
    --end-ip-address 0.0.0.0

# Permitir la IP publica de la maquina de desarrollo actual
$myIp = (Invoke-RestMethod -Uri "https://api.ipify.org?format=json").ip
az sql server firewall-rule create `
    --resource-group rg-insuseg-analytics `
    --server sql-insuseg-<region> `
    --name "AllowMyIP-$env:COMPUTERNAME" `
    --start-ip-address $myIp `
    --end-ip-address $myIp
```

### 8. Verificar

```powershell
az sql db show --resource-group rg-insuseg-analytics --server sql-insuseg-<region> --name sqldb-insuseg-analytics --output json
```
Confirmar `useFreeLimit: true` y `status: Online`.

### Notas de seguridad para replicar esto correctamente

- La password del admin SQL y las credenciales del Service Layer de SAP **nunca deben quedar en archivos versionados en git** — usar un gestor de contraseñas o Azure Key Vault.
- Cada PC nuevo que necesite conectarse directo a la base de datos (fuera de Azure) necesita su propia regla de firewall con su IP pública.
- Recordar la regla de solo-lectura sobre SAP producción (sección 3) al escribir cualquier script de prueba.
