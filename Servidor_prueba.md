# Procedimiento: Servidores de prueba gratuitos en Azure (por empresa cotizante)

## Objetivo
Generar, por cada empresa que nos cotice, un entorno de prueba gratuito en Azure con:
- a) Servidor de Base de Datos SQL gratuito.
- b) Servidor de aplicación (App Service) gratuito, compatible con .NET 10 o menor.

## Contexto de facturación
- Los servicios de Microsoft (O365, Azure) se obtienen a través de **Nebulan**, distribuidor CSP de Microsoft en Chile.
- El modelo es **Azure Plan (CSP moderno, self-service)**: se pueden crear suscripciones nuevas directamente desde el Azure Portal, sin que Nebulan tenga que aprovisionarlas manualmente, y sin solicitar tarjeta de crédito (la facturación queda a cargo de Nebulan vía su contrato con Melirrepu).
- Confirmado en la práctica: la suscripción **"Insuseg"** se creó manualmente sin inconvenientes.

## Arquitectura elegida: 1 suscripción Azure por empresa
Motivo: el **free tier de Azure SQL Database (serverless, ~100.000 vCore-seg/mes, 32 GB)** solo aplica **una vez por suscripción**. Para que cada empresa tenga su SQL realmente gratis (y no solo la primera), cada empresa necesita su propia suscripción — no alcanza con separar por Resource Group dentro de una misma suscripción.

El App Service Plan F1 (gratis) no tiene esa restricción de "uno por suscripción" (hasta 10 apps free por región), pero se mantiene el mismo criterio de 1 suscripción por empresa para simplicidad y aislamiento total entre clientes.

## Flujo de trabajo acordado
1. **Creación de la suscripción**: la realiza manualmente el usuario admin de Melirrepu en Azure Portal (no automatizada). Nomenclatura sugerida: `Trial-<NombreEmpresa>`.
2. El usuario informa el **nombre de la empresa/suscripción** creada.
3. A partir de ahí, se automatiza (script Azure CLI o Bicep, parametrizado por nombre de empresa) el despliegue de:
   - Un Resource Group único: `rg-<empresa>-trial`.
   - Azure SQL Database (tier serverless, free offer).
   - App Service Plan F1 (free) + Web App para la aplicación .NET.

## Pendiente / a definir
- Si se automatiza también el paso 1 (creación de la suscripción) vía API bajo el modelo CSP de Nebulan — no evaluado aún.
- Checklist de nomenclatura/tags (empresa, fecha, responsable) para mantener orden entre suscripciones — no definida aún.
- Script de despliegue (Azure CLI / Bicep) para el paso 3 — pendiente de construir cuando se indique.

## Riesgos / advertencias
- Azure tiene controles antifraude: crear muchas suscripciones seguidas bajo la misma identidad/tarjeta puede disparar revisiones o bloqueos temporales si el proceso se vuelve masivo.
- El free tier de SQL (offer gratuito) es distinto del "Azure Free Account" (crédito USD 200/30 días), que sí solo puede reclamarse una vez por persona — no aplica a este flujo porque se usa el modelo CSP de Nebulan.
