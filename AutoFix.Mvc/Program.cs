using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AutoFix;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add<AutoValidateAntiforgeryTokenAttribute>();
    o.Filters.Add<FiltroApi>();
});
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
    o.IdleTimeout = TimeSpan.FromHours(2);
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.LoginPath = "/cuenta/login";
    o.AccessDeniedPath = "/cuenta/acceso-denegado";
    o.ExpireTimeSpan = TimeSpan.FromHours(2);
    o.SlidingExpiration = false;
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<Api>(c => c.BaseAddress =
    new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5080/"));
var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

namespace AutoFix
{
    public sealed class FalloApi(int codigo, string mensaje) : Exception(mensaje)
    {
        public int Codigo { get; } = codigo;
    }

    public class Api(HttpClient http, IHttpContextAccessor accessor)
    {
        private HttpContext Contexto => accessor.HttpContext!;
        public async Task<T> Obtener<T>(string ruta)
        {
            using var respuesta = await Enviar(HttpMethod.Get, ruta);
            return (await respuesta.Content.ReadFromJsonAsync<T>(Contexto.RequestAborted))!;
        }

        public async Task<T> Enviar<T>(string ruta, object datos)
        {
            using var respuesta = await Enviar(HttpMethod.Post, ruta, JsonContent.Create(datos));
            return (await respuesta.Content.ReadFromJsonAsync<T>(Contexto.RequestAborted))!;
        }

        public async Task Cambiar(HttpMethod metodo, string ruta, object? datos = null)
        {
            using var respuesta = await Enviar(metodo, ruta, datos is null ? null : JsonContent.Create(datos));
        }

        public async Task Foto(string ruta, IFormFile? archivo, TipoEvidencia? tipo = null)
        {
            if (archivo is null || archivo.Length == 0 || archivo.Length > 5 * 1024 * 1024)
                throw new FalloApi(400, "Selecciona una foto JPG, JPEG o PNG de hasta 5 MB.");
            using var formulario = new MultipartFormDataContent();
            formulario.Add(new StreamContent(archivo.OpenReadStream()), "archivo", Path.GetFileName(archivo.FileName));
            if (tipo.HasValue) formulario.Add(new StringContent(tipo.Value.ToString()), "tipo");
            using var respuesta = await Enviar(HttpMethod.Post, ruta, formulario);
        }

        public async Task<(byte[] Bytes, string Mime)> Imagen(string nombre)
        {
            using var respuesta = await Enviar(HttpMethod.Get, "api/imagenes/" + Uri.EscapeDataString(nombre));
            return (await respuesta.Content.ReadAsByteArrayAsync(Contexto.RequestAborted),
                respuesta.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
        }

        private async Task<HttpResponseMessage> Enviar(HttpMethod metodo, string ruta, HttpContent? contenido = null)
        {
            using var peticion = new HttpRequestMessage(metodo, ruta) { Content = contenido };
            if (Contexto.Session.GetString("jwt") is { } jwt)
                peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            var correlacion = Guid.NewGuid().ToString();
            peticion.Headers.Add("X-Correlation-ID", correlacion);
            HttpResponseMessage respuesta;
            try { respuesta = await http.SendAsync(peticion, Contexto.RequestAborted); }
            catch (HttpRequestException) { throw new FalloApi(503, "La API no está disponible. Revisa que esté ejecutándose."); }
            catch (TaskCanceledException) when (!Contexto.RequestAborted.IsCancellationRequested)
            {
                throw new FalloApi(503, "La API tardó demasiado en responder.");
            }
            if (respuesta.IsSuccessStatusCode) return respuesta;
            using (respuesta)
            {
                var codigo = (int)respuesta.StatusCode;
                var mensaje = respuesta.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Tu sesión expiró. Inicia sesión nuevamente.",
                    HttpStatusCode.Forbidden => "Tu usuario no tiene permiso para esta operación.",
                    HttpStatusCode.NotFound => "El registro no está disponible.",
                    _ => "No se pudo completar la operación."
                };
                if (respuesta.Content.Headers.ContentType?.MediaType?.Contains("json") == true)
                {
                    using var json = await JsonDocument.ParseAsync(await respuesta.Content.ReadAsStreamAsync(),
                        cancellationToken: Contexto.RequestAborted);
                    if (json.RootElement.TryGetProperty("mensaje", out var detalle))
                        mensaje = detalle.GetString() ?? mensaje;
                    else if (json.RootElement.TryGetProperty("errors", out var errors))
                        mensaje = string.Join(" ", errors.EnumerateObject().SelectMany(x =>
                            x.Value.EnumerateArray().Select(v => v.GetString())));
                }
                throw new FalloApi(codigo, mensaje + $" [ID: {correlacion}]");
            }
        }
    }

    public class FiltroApi : IAsyncExceptionFilter
    {
        public async Task OnExceptionAsync(ExceptionContext context)
        {
            if (context.Exception is not FalloApi e) return;
            if (e.Codigo == 401)
            {
                context.HttpContext.Session.Clear();
                await context.HttpContext.SignOutAsync();
                context.Result = new RedirectToActionResult("Login", "Cuenta", null);
            }
            else
                context.Result = new ViewResult
                {
                    ViewName = "Error", StatusCode = e.Codigo,
                    ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary<string>(
                        new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), context.ModelState)
                        { Model = e.Message }
                };
            context.ExceptionHandled = true;
        }
    }

    public static class HoraTaller
    {
        public static DateTime Local(DateTime fecha) => fecha.AddHours(-6);
        public static DateTime Utc(DateTime fecha) => DateTime.SpecifyKind(fecha.AddHours(6), DateTimeKind.Utc);
        public static string Texto(DateTime fecha) => Local(fecha).ToString("dd/MM/yyyy HH:mm");
        public static string Campo(DateTime fecha) => Local(fecha).ToString("yyyy-MM-ddTHH:mm");
    }

    public record DetalleVm(OrdenDetalle Datos, List<MecanicoFila> Mecanicos, List<RepuestoFila> Repuestos);
    public record VehiculosVm(List<VehiculoFila> Vehiculos, List<ClienteFila> Clientes);
    public record MovimientosVm(List<MovimientoFila> Movimientos, List<RepuestoFila> Repuestos);
}
