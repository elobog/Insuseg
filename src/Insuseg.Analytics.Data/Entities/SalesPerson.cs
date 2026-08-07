namespace Insuseg.Analytics.Data.Entities;

// Réplica local de SAP B1 SalesPersons (SalesEmployeeCode/SalesEmployeeName) — ver BDhana.md sección 3.
public class SalesPerson
{
    public int SalesEmployeeCode { get; set; }
    public required string SalesEmployeeName { get; set; }
}
