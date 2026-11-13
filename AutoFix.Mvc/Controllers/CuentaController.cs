using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoFix.Mvc.Controllers;

[Route("cuenta")]
public class CuentaController(Api api) : Controller
{
    [HttpGet("login"), AllowAnonymous]
    public IActionResult Login() => User.Identity?.IsAuthenticated == true ? Redirect("/") : View();

    [HttpPost("login"), AllowAnonymous]
    public Task<IActionResult> Login(Login datos) => Acceder("login", datos, "Login");

    [HttpGet("registro"), AllowAnonymous]
    public IActionResult Registro() => View();

    [HttpPost("registro"), AllowAnonymous]
    public Task<IActionResult> Registro(Registro datos) => Acceder("registro", datos, "Registro");

    [HttpPost("salir"), Authorize]
    public async Task<IActionResult> Salir()
    {
        HttpContext.Session.Clear();
        await HttpContext.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("acceso-denegado")]
    public IActionResult Denegado() => View("Error", "Tu usuario no tiene permiso para esta página.");

    private async Task<IActionResult> Acceder(string ruta, object datos, string vista)
    {
        if (!ModelState.IsValid) return View(vista);
        try
        {
            var sesion = await api.Enviar<Sesion>("api/auth/" + ruta, datos);
            HttpContext.Session.SetString("jwt", sesion.Token);
            var claims = new List<Claim> { new(ClaimTypes.Name, sesion.Nombre), new(ClaimTypes.Email, sesion.Email),
                new(ClaimTypes.Role, sesion.Rol) };
            claims.AddRange(sesion.Permisos.Select(p => new Claim("permiso", p)));
            await HttpContext.SignInAsync(new ClaimsPrincipal(
                new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
            return Redirect("/");
        }
        catch (FalloApi e)
        {
            ModelState.AddModelError("", e.Message);
            return View(vista);
        }
    }
}
