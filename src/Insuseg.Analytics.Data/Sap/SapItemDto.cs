namespace Insuseg.Analytics.Data.Sap;

// Subconjunto de campos de Items via $select — ver BDhana.md sección 2. Solo stock agregado
// (QuantityOnStock), no detalle por almacén (ItemWarehouseInfoCollection) — ver Insuseg.md, decisión de
// alcance v1 del módulo de Inventario.
public class SapItemDto
{
    public required string ItemCode { get; set; }
    public required string ItemName { get; set; }
    public int? ItemsGroupCode { get; set; }
    public decimal QuantityOnStock { get; set; }
    public decimal MovingAveragePrice { get; set; }
}
