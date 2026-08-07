# Esquema de datos — SAP Business One (HANA) — INSUSEG_PRB (PRODUCCIÓN)

> Origen: SAP B1 Service Layer (`https://159.69.163.254:50003/b1s/v1/`), no acceso SQL directo a HANA.
> Versión SAP B1 detectada: 9.3 PL10 (Version 930210).
> **Este es el esquema del ambiente de PRODUCCIÓN. No existe ambiente de pruebas.**
> Extraído por lectura (GET, `$top=1`) el 2026-07-20. Solo se documentan nombres de campos, no datos reales.
> Prefijo `U_` = campo de usuario (custom field) agregado en esta instalación — el resto son campos estándar de SAP B1.

---

## 1. Clientes → `BusinessPartners` (filtrado `CardType eq 'C'`)

Entidad estándar de Business Partners (también incluye proveedores con `CardType eq 'S'` y leads `CardType eq 'L'`).

**Clave primaria:** `CardCode`

**Campos clave para análisis:**
- `CardCode`, `CardName`, `CardType`, `GroupCode`
- `Currency`, `CreditLimit`, `PayTermsGrpCode`, `PriceListNum`
- `SalesPersonCode` (vendedor asignado al cliente)
- `Address`, `City`, `Country`, `Phone1`, `EmailAddress`
- `CurrentAccountBalance`, `OpenOrdersBalance`, `OpenDeliveryNotesBalance`
- `Valid`, `Frozen` (estado activo/congelado)
- `Territory`, `Industry`

**Sub-colecciones (navegación):**
- `BPAddresses` — direcciones de envío/facturación
- `ContactEmployees` — contactos
- `BPAccountReceivablePaybleCollection`
- `BPBankAccounts`

**Campos personalizados (U_) detectados:**
- `U_EXX_FE_Cesionario`
- `U_Tipo`

---

## 2. Productos → `Items`

**Clave primaria:** `ItemCode`

**Campos clave para análisis:**
- `ItemCode`, `ItemName`, `ForeignName`, `BarCode`
- `ItemsGroupCode` (grupo/categoría de artículo)
- `SalesItem`, `PurchaseItem`, `InventoryItem` (flags de uso)
- `QuantityOnStock`, `QuantityOrderedFromVendors`, `QuantityOrderedByCustomers` (totales, no por almacén)
- `MovingAveragePrice`, `AvgStdPrice`
- `DefaultWarehouse`, `ManageStockByWarehouse`
- `Valid`, `Frozen`
- `Manufacturer`

**Sub-colecciones (navegación):**
- `ItemWarehouseInfoCollection` — **stock por almacén** (ver sección 8)
- `ItemPrices` — precios por lista de precios
- `ItemPreferredVendors`

**Campos personalizados (U_) detectados:**
- `U_Currency`
- `U_Origin`
- `U_Categoria`
- `U_Genero`
- `U_Marca`
- `U_Mix`
- `U_Proveedor`
- `U_Familia`

---

## 3. Vendedores → `SalesPersons`

**Clave primaria:** `SalesEmployeeCode`

**Campos (entidad pequeña, sin campos custom):**
- `SalesEmployeeCode`, `SalesEmployeeName`
- `CommissionForSalesEmployee`, `CommissionGroup`
- `Locked`, `Active`
- `EmployeeID`

Se referencia desde `BusinessPartners.SalesPersonCode`, y desde el header y las líneas de cada documento de venta (`SalesPersonCode` en Orders/Invoices/DocumentLines).

---

## 4. Órdenes de Venta → `Orders`

Documento de tipo "Sales Order" (SAP interno: ORDR/RDR1).

**Clave primaria:** `DocEntry` (interno) / `DocNum` (correlativo visible)

**Campos de cabecera clave:**
- `DocEntry`, `DocNum`, `DocDate`, `DocDueDate`, `DocTime`
- `CardCode`, `CardName` (cliente)
- `SalesPersonCode` (vendedor)
- `DocTotal`, `DocCurrency`, `DocRate`, `VatSum`
- `DocumentStatus` (abierto/cerrado), `Cancelled`
- `Comments`, `NumAtCard` (referencia del cliente)

**Líneas → `DocumentLines`:**
- `LineNum`, `ItemCode`, `ItemDescription`, `Quantity`
- `Price`, `PriceAfterVAT`, `LineTotal`, `DiscountPercent`
- `WarehouseCode`, `SalesPersonCode` (puede diferir del header, por línea)
- `TaxCode`, `TaxTotal`
- `BaseType`, `BaseEntry`, `BaseLine` (trazabilidad a documento origen: cotización, etc.)
- `LineStatus` (abierta/cerrada — clave para saber pendientes de entrega)

**Campos personalizados (U_) en cabecera:**
`U_TotalCtoUnit`, `U_SumCtoTotal`, `U_SumMgTotal`, `U_SumPorcTotal`, `U_NUMFACT`, `U_Bol_Ini`, `U_Bol_Fin`, más ~25 campos `U_EXX_FE_*` (facturación electrónica — DTE/SII, formato usado en Chile).

**Campos personalizados (U_) en líneas:**
`U_CtoUnit`, `U_CtoTotal`, `U_MgenMont`, `U_MgenPorc`, `U_porcentaje` (parecen ser costo/margen calculado por línea — muy relevante para análisis de rentabilidad).

---

## 5. Órdenes de Compra → `PurchaseOrders`

Misma estructura que `Orders` (documento de compras, SAP interno: OPOR/POR1). Comparte prácticamente el mismo set de campos de cabecera y de `DocumentLines`, mismos campos `U_EXX_FE_*` y `U_TotalCtoUnit`/`U_SumCtoTotal`/etc.

**Clave primaria:** `DocEntry` / `DocNum`

**Diferencia relevante:** `CardCode`/`CardName` referencian al **proveedor** (`CardType='S'` en BusinessPartners), no al cliente.

---

## 6. Ventas (facturado) → `Invoices`

Factura de venta (A/R Invoice, SAP interno: OINV/INV1). Misma estructura de cabecera/líneas que `Orders`, con adicionales:
- `PaidToDate`, `PaidToDateFC`, `PaidToDateSys` (seguimiento de pago)
- `DownPayment`, `ReserveInvoice`

**Relación con Órdenes de Venta:** las líneas de `Invoices.DocumentLines` tienen `BaseType`/`BaseEntry`/`BaseLine` apuntando al `DocEntry`/línea de la `Order` origen — así se traza Orden → Factura.

Mismos campos custom `U_*` que Orders (incluye todo el bloque de facturación electrónica `U_EXX_FE_*`).

---

## 7. Compras (facturado) → `PurchaseInvoices`

Factura de compra (A/P Invoice, SAP interno: OPCH/PCH1). Misma estructura que Invoices pero para proveedores. Incluye además `TaxInvoiceNo`, `TaxInvoiceDate`.

---

## 8. Notas de Crédito de venta → `CreditNotes`

Nota de crédito de venta (A/R Credit Memo, SAP interno: ORIN/RIN1) — devoluciones y anulaciones contra Facturas. **No se sincronizaba hasta el 2026-07-30**: el sync original solo traía `Orders`/`Invoices`, y esta entidad ni siquiera se había explorado — causó que todos los montos de Ventas (Cartera, Análisis, márgenes) quedaran inflados por el monto bruto de las devoluciones no netadas (ver hallazgo y fix en `Insuseg.md`, sección del módulo Cartera de clientes).

Misma estructura de cabecera/líneas que `Invoices` para los campos que usa este proyecto (`DocEntry`, `DocNum`, `DocDate`, `DocTotal`, `CardCode`, `CardName`, `SalesPersonCode`, `DocumentLines`). Confirmado con una consulta real (2026-07-30): `Cancelled` existe igual que en `Invoices`, y **`GrossBuyPrice` viene poblado en las líneas** igual que en `Invoices.DocumentLines` — no hizo falta ningún campo adicional para calcular margen sobre las devoluciones.

**Cómo se sincroniza (ver `SalesSyncService`):** se trae siempre, además de la fuente principal (`SalesSource`, hoy `Invoice`), con watermark independiente. Al guardarse en `Sale`/`SaleLine`, `Amount`/`LineTotal`/`Quantity` se guardan **negativos** (signo −1) — así cualquier `.Sum()` existente sobre `Sales`/`SaleLines` neta las devoluciones automáticamente, sin que las páginas de análisis necesiten saber que esta fuente existe. `SourceDocType = CreditNote` (valor `2`) en la clave compuesta, mismo motivo que `Order`/`Invoice`: `DocEntry` no es único entre tipos de documento.

---

## 9. Stock por almacén

No es una entidad top-level independiente; se obtiene expandida dentro de `Items` vía la colección `ItemWarehouseInfoCollection` (ya viene incluida por defecto al consultar `Items`, no requiere `$expand` — se detectaron **7 almacenes activos: códigos `01` a `07`**).

**Campos por almacén (uno por cada `WarehouseCode`):**
- `WarehouseCode`, `ItemCode`
- `InStock` (stock físico), `Committed` (comprometido en órdenes), `Ordered` (en órdenes de compra pendientes)
- `MinimalStock`, `MaximalStock`, `MinimalOrder`
- `StandardAveragePrice`
- `Locked`, `WasCounted`, `CountedQuantity`

**Campo personalizado detectado:** `U_Lock_fg`

> Nota: para el catálogo real de almacenes (nombre, código, ubicación) falta consultar la entidad `Warehouses` — no se hizo en esta pasada, se puede agregar si se necesita.

---

## Relaciones clave para el modelo analítico

```
BusinessPartners (CardCode) ──┬── Orders.CardCode (Órdenes de Venta)
   CardType='C' Clientes      ├── Invoices.CardCode (Ventas facturadas)
   CardType='S' Proveedores   ├── PurchaseOrders.CardCode (Órdenes de Compra)
                               └── PurchaseInvoices.CardCode (Compras facturadas)

SalesPersons (SalesEmployeeCode) ── referenciado en BusinessPartners y en
                                     header/líneas de Orders/Invoices/PurchaseOrders

Items (ItemCode) ──┬── DocumentLines.ItemCode (en los 4 tipos de documento)
                    └── ItemWarehouseInfoCollection (stock por almacén 01-07)

Orders.DocumentLines ── BaseType/BaseEntry/BaseLine ──> Invoices.DocumentLines
                         (trazabilidad Orden de Venta → Factura)
PurchaseOrders.DocumentLines ── BaseType/BaseEntry/BaseLine ──> PurchaseInvoices.DocumentLines
```

---

## Pendiente / siguiente paso sugerido

- Confirmar catálogo de `Warehouses` (nombres de los 7 almacenes 01-07).
- Confirmar significado de negocio de los campos custom clave para el análisis:
  - `U_CtoUnit`, `U_CtoTotal`, `U_MgenMont`, `U_MgenPorc`, `U_porcentaje` (parecen costo/margen por línea — importante para reportes de rentabilidad).
  - `U_Categoria`, `U_Familia`, `U_Marca`, `U_Genero`, `U_Mix`, `U_Origin`, `U_Proveedor` en Items (dimensiones de análisis de productos).
- Decidir qué subconjunto de estos campos se replica hacia Azure SQL (no hace falta traer las ~250 columnas de cada documento; para analítica probablemente basta con 20-30 campos por entidad).

### Hallazgo (2026-07-23): `MovingAveragePrice`/`AvgStdPrice` en $0 para todo el catálogo de `Items`

Al sincronizar `Items` por primera vez (25.398 productos), se confirmó con un GET directo contra el SAP real que **tanto `MovingAveragePrice` como `AvgStdPrice` vienen en `0.0` para absolutamente todo el catálogo**, incluidos los 23 productos con stock actual > 0. No es un problema de nuestra consulta — este SAP simplemente no tiene costo cargado en la ficha maestra del producto. El costo real de cada venta parece vivir en el campo custom **`U_CtoUnit` de cada línea de `DocumentLines`** (visto con valores reales, ej. `824.0` para el producto A00001), no en `Items`. Si en el futuro se necesita un "valor de inventario" o "valor inmovilizado" confiable, la fuente de costo tendría que ser `U_CtoUnit` de la venta más reciente de cada producto (con la limitación de que productos nunca vendidos seguirían sin costo conocido) — pendiente de decidir si vale la pena, no implementado todavía.

### Hallazgo (2026-07-23): de 25.398 `Items`, solo 23 tienen stock actual

La enorme mayoría del catálogo de `Items` es histórico/descontinuado (`QuantityOnStock = 0`). Cualquier análisis o listado sobre `Items` debería filtrar por `QuantityOnStock > 0` antes de mostrarlo — mostrar o contar el catálogo completo sin ese filtro da números sin sentido de negocio (ver módulo de Inventario en `Insuseg.md`).
