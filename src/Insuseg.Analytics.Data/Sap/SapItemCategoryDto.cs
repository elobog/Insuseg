namespace Insuseg.Analytics.Data.Sap;

// Fila de la tabla de usuario (UDT) U_ZCAT — catálogo de categorías de producto. Toda UDT de SAP trae
// como mínimo estos dos campos (Code/Name) — confirmado contra el SAP real (GET U_ZCAT?$top=5, ver
// Insuseg.md).
public class SapItemCategoryDto
{
    public required string Code { get; set; }
    public required string Name { get; set; }
}
