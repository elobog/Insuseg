namespace Insuseg.Analytics.Data.Entities;

// Réplica local de la tabla de usuario (UDT) U_ZCAT de SAP — el catálogo de categorías de producto.
// Items.CategoryCode apunta acá por código; el nombre real vive solo en esta tabla (mismo patrón que
// SalesPerson/SalesPersonCode). Ver BDhana.md sección 2.
public class ItemCategory
{
    public required string Code { get; set; }
    public required string Name { get; set; }
}
