using Microsoft.AspNetCore.Identity;

namespace Insuseg.Analytics.Data.Entities;

// Extiende IdentityUser (antes se usaba directo) para poder vincular una cuenta con un vendedor real
// de SAP. Se usa para restringir qué cartera puede ver un usuario con rol Vendedor — ver
// CarteraModel.AplicarRestriccionVendedorAsync. null para Admin/Ejecutivo (no representan a un
// vendedor puntual) o para un Vendedor todavía sin asignar (ver Administracion/Usuarios).
public class ApplicationUser : IdentityUser
{
    public int? SalesPersonCode { get; set; }
}
