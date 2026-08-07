using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Insuseg.Analytics.Api.Pages.Administracion;

// Solo Admin puede entrar a esta página — es el único rol autorizado a crear/borrar cuentas.
[Authorize(AuthenticationSchemes = "Identity.Application", Roles = "Admin")]
public class UsuariosModel : PageModel
{
    public static readonly string[] RolesDisponibles = ["Admin", "Ejecutivo", "Vendedor"];

    private readonly UserManager<IdentityUser> _userManager;

    public UsuariosModel(UserManager<IdentityUser> userManager)
    {
        _userManager = userManager;
    }

    public List<UsuarioRow> Usuarios { get; set; } = [];

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

        if (!ModelState.IsValid)
        {
            await LoadUsuariosAsync();
            return Page();
        }

        var nuevoUsuario = new IdentityUser { UserName = NuevoEmail, Email = NuevoEmail };
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
        Usuarios = [];
        foreach (var usuario in _userManager.Users.OrderBy(u => u.Email).ToList())
        {
            var roles = await _userManager.GetRolesAsync(usuario);
            Usuarios.Add(new UsuarioRow(usuario.Id, usuario.Email!, string.Join(", ", roles)));
        }
    }

    public record UsuarioRow(string Id, string Email, string Roles);
}
