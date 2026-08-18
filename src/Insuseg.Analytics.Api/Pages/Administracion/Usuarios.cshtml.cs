using System.ComponentModel.DataAnnotations;
using Insuseg.Analytics.Data;
using Insuseg.Analytics.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Insuseg.Analytics.Api.Pages.Administracion;

// Solo Admin puede entrar a esta página — es el único rol autorizado a crear/borrar cuentas.
[Authorize(AuthenticationSchemes = "Identity.Application", Roles = "Admin")]
public class UsuariosModel : PageModel
{
    public static readonly string[] RolesDisponibles = ["Admin", "Ejecutivo", "Vendedor"];

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly InsusegAnalyticsDbContext _db;

    public UsuariosModel(UserManager<ApplicationUser> userManager, InsusegAnalyticsDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public List<UsuarioRow> Usuarios { get; set; } = [];

    // Para el selector de vendedor (al invitar y al reasignar) — todos los vendedores conocidos por
    // SalesPersons, no filtrados por período (a diferencia del filtro de Cartera, esto es
    // configuración, no un reporte).
    public List<VendedorOpcion> Vendedores { get; set; } = [];

    [BindProperty]
    [Required]
    [EmailAddress]
    public string NuevoEmail { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    public string NuevaPassword { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    public string NuevoRol { get; set; } = string.Empty;

    [BindProperty]
    public int? NuevoVendedorCodigo { get; set; }

    [TempData]
    public string? InviteSummary { get; set; }

    [TempData]
    public string? InviteError { get; set; }

    public async Task OnGetAsync()
    {
        await LoadUsuariosAsync();
    }

    public async Task<IActionResult> OnPostInvitarAsync()
    {
        if (!RolesDisponibles.Contains(NuevoRol))
        {
            ModelState.AddModelError(nameof(NuevoRol), "Rol inválido.");
        }

        // El código de vendedor solo tiene sentido (y solo se pide en el formulario) para rol
        // Vendedor — para Admin/Ejecutivo se ignora aunque llegue algo en el POST.
        var codigoVendedor = NuevoRol == "Vendedor" ? NuevoVendedorCodigo : null;

        if (!ModelState.IsValid)
        {
            await LoadUsuariosAsync();
            return Page();
        }

        var nuevoUsuario = new ApplicationUser { UserName = NuevoEmail, Email = NuevoEmail, SalesPersonCode = codigoVendedor };
        var result = await _userManager.CreateAsync(nuevoUsuario, NuevaPassword);

        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(nuevoUsuario, NuevoRol);
            InviteSummary = $"Usuario {NuevoEmail} creado con rol {NuevoRol}.";
        }
        else
        {
            InviteError = string.Join(" ", result.Errors.Select(e => e.Description));
        }

        return RedirectToPage();
    }

    // Asigna/cambia/quita (código null) el vendedor de SAP vinculado a una cuenta — separado de
    // Invitar para poder corregirlo después sin recrear la cuenta (ej. alguien invitado antes de
    // tener claro su código, o un vendedor que cambia de código en SAP).
    public async Task<IActionResult> OnPostAsignarVendedorAsync(string userId, int? salesPersonCode)
    {
        var usuario = await _userManager.FindByIdAsync(userId);
        if (usuario is null)
        {
            InviteError = "El usuario ya no existe.";
            return RedirectToPage();
        }

        usuario.SalesPersonCode = salesPersonCode;
        var result = await _userManager.UpdateAsync(usuario);
        InviteSummary = result.Succeeded ? $"Vendedor asignado actualizado para {usuario.Email}." : null;
        if (!result.Succeeded)
        {
            InviteError = string.Join(" ", result.Errors.Select(e => e.Description));
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEliminarAsync(string userId)
    {
        var usuario = await _userManager.FindByIdAsync(userId);
        if (usuario is null)
        {
            InviteError = "El usuario ya no existe.";
            return RedirectToPage();
        }

        if (string.Equals(usuario.Email, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
        {
            InviteError = "No podés eliminar tu propia cuenta.";
            return RedirectToPage();
        }

        if (await _userManager.IsInRoleAsync(usuario, "Admin"))
        {
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            if (admins.Count <= 1)
            {
                InviteError = "No se puede eliminar al último administrador.";
                return RedirectToPage();
            }
        }

        var result = await _userManager.DeleteAsync(usuario);
        InviteSummary = result.Succeeded
            ? $"Usuario {usuario.Email} eliminado."
            : null;
        if (!result.Succeeded)
        {
            InviteError = string.Join(" ", result.Errors.Select(e => e.Description));
        }

        return RedirectToPage();
    }

    private async Task LoadUsuariosAsync()
    {
        var nombresVendedor = await _db.SalesPersons
            .ToDictionaryAsync(sp => sp.SalesEmployeeCode, sp => sp.SalesEmployeeName);
        Vendedores = nombresVendedor
            .Select(kv => new VendedorOpcion(kv.Key, kv.Value))
            .OrderBy(v => v.Nombre)
            .ToList();

        Usuarios = [];
        foreach (var usuario in _userManager.Users.OrderBy(u => u.Email).ToList())
        {
            var roles = await _userManager.GetRolesAsync(usuario);
            var nombreVendedor = usuario.SalesPersonCode.HasValue
                ? nombresVendedor.GetValueOrDefault(usuario.SalesPersonCode.Value, $"código {usuario.SalesPersonCode}")
                : null;
            Usuarios.Add(new UsuarioRow(
                usuario.Id, usuario.Email!, string.Join(", ", roles), roles.Contains("Vendedor"),
                usuario.SalesPersonCode, nombreVendedor));
        }
    }

    public record VendedorOpcion(int Codigo, string Nombre);

    public record UsuarioRow(
        string Id, string Email, string Roles, bool EsVendedor, int? SalesPersonCode, string? NombreVendedor);
}
