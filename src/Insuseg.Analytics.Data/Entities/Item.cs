namespace Insuseg.Analytics.Data.Entities;

// Réplica local de SAP B1 Items — ver BDhana.md sección 2. Solo stock agregado (QuantityOnStock), no
// detalle por almacén.
public class Item
{
    public required string ItemCode { get; set; }
    public required string ItemName { get; set; }
    public int? ItemsGroupCode { get; set; }
    public decimal QuantityOnStock { get; set; }
    public decimal MovingAveragePrice { get; set; }

    // Código de categoría de producto (campo custom U_Categoria en SAP) — apunta a ItemCategory.Code,
    // no trae el nombre directo (ver esa entidad). Usado para agrupar el detalle por producto de
    // Ventas/Cartera en dos niveles (categoría → producto). Ver BDhana.md sección 2.
    public string? CategoryCode { get; set; }
}
