using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoFix.Mvc.Controllers;

[Authorize]
public class TallerController(Api api) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Dashboard() => View(await api.Obtener<Resumen>("api/dashboard"));

    [HttpGet("/clientes"), Authorize(Roles = Roles.Oficina)]
    public async Task<IActionResult> Clientes() => View(await api.Obtener<List<ClienteFila>>("api/clientes"));

    [HttpGet("/vehiculos"), Authorize(Roles = Roles.Oficina + "," + Roles.Cliente)]
    public async Task<IActionResult> Vehiculos()
    {
        var filas = await api.Obtener<List<VehiculoFila>>("api/vehiculos");
        return View(new VehiculosVm(filas, User.IsInRole(Roles.Cliente)
            ? [] : await api.Obtener<List<ClienteFila>>("api/clientes")));
    }

    [HttpGet("/ordenes")]
    public async Task<IActionResult> Ordenes()
    {
        if (Tiene(Permisos.Crear)) ViewBag.Vehiculos = await api.Obtener<List<VehiculoFila>>("api/vehiculos");
        return View(await api.Obtener<List<OrdenFila>>("api/ordenes"));
    }

    [HttpGet("/ordenes/{id:int}")]
    public async Task<IActionResult> Detalle(int id)
    {
        var orden = await api.Obtener<OrdenDetalle>($"api/ordenes/{id}");
        var mecanicos = Tiene(Permisos.Asignar) && orden.Orden.Estado < Estado.EnReparacion
            ? await api.Obtener<List<MecanicoFila>>("api/mecanicos") : [];
        return View(new DetalleVm(orden, mecanicos, []));
    }

    [HttpGet("/ordenes/{id:int}/presupuesto")]
    public async Task<IActionResult> Presupuesto(int id)
    {
        var orden = await api.Obtener<OrdenDetalle>($"api/ordenes/{id}");
        var repuestos = PuedePresupuestar(orden) ? await api.Obtener<List<RepuestoFila>>("api/repuestos") : [];
        return View(new DetalleVm(orden, [], repuestos));
    }

    [HttpGet("/mecanicos"), Authorize(Roles = Roles.Oficina)]
    public async Task<IActionResult> Mecanicos() => View(await api.Obtener<List<MecanicoFila>>("api/mecanicos"));

    [HttpGet("/inventario"), Authorize(Roles = Roles.Personal)]
    public async Task<IActionResult> Inventario() => View(await api.Obtener<List<RepuestoFila>>("api/repuestos"));

    [HttpGet("/movimientos"), Authorize(Roles = Roles.Admin + "," + Roles.Supervisor)]
    public async Task<IActionResult> Movimientos() => View(new MovimientosVm(
        await api.Obtener<List<MovimientoFila>>("api/movimientos"),
        Tiene(Permisos.Inventario) ? await api.Obtener<List<RepuestoFila>>("api/repuestos") : []));

    [HttpGet("/ordenes/{id:int}/evidencias")]
    public async Task<IActionResult> Evidencias(int id) => View(await api.Obtener<OrdenDetalle>($"api/ordenes/{id}"));

    [HttpGet("/reportes"), Authorize(Roles = Roles.Admin + "," + Roles.Supervisor)]
    public async Task<IActionResult> Reportes(DateOnly? desde, DateOnly? hasta)
    {
        ViewBag.Desde = desde?.ToString("yyyy-MM-dd");
        ViewBag.Hasta = hasta?.ToString("yyyy-MM-dd");
        return View(await api.Obtener<Resumen>($"api/reportes?desde={ViewBag.Desde}&hasta={ViewBag.Hasta}"));
    }

    [HttpGet("/imagen/{nombre}")]
    public async Task<IActionResult> Imagen(string nombre)
    {
        var (bytes, mime) = await api.Imagen(nombre);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(bytes, mime);
    }

    [HttpPost("/clientes/guardar")]
    public Task<IActionResult> GuardarCliente(int? id, ClienteDatos datos) =>
        Guardar("clientes", id, datos, nameof(Clientes));
    [HttpPost("/vehiculos/guardar")]
    public Task<IActionResult> GuardarVehiculo(int? id, VehiculoDatos datos) =>
        Guardar("vehiculos", id, datos, nameof(Vehiculos));
    [HttpPost("/mecanicos/guardar")]
    public Task<IActionResult> GuardarMecanico(int? id, MecanicoDatos datos) =>
        Guardar("mecanicos", id, datos, nameof(Mecanicos));
    [HttpPost("/inventario/guardar")]
    public Task<IActionResult> GuardarRepuesto(int? id, RepuestoDatos datos) =>
        Guardar("repuestos", id, datos, nameof(Inventario));
    [HttpPost("/clientes/{id:int}/activar")]
    public Task<IActionResult> Activar(int id, ClaveDatos datos) =>
        Operar(() => api.Cambiar(HttpMethod.Post, $"api/clientes/{id}/activar", datos), nameof(Clientes));
    [HttpPost("/clientes/{id:int}/eliminar")]
    public Task<IActionResult> BorrarCliente(int id) => Borrar("clientes", id, nameof(Clientes));
    [HttpPost("/vehiculos/{id:int}/eliminar")]
    public Task<IActionResult> BorrarVehiculo(int id) => Borrar("vehiculos", id, nameof(Vehiculos));
    [HttpPost("/inventario/{id:int}/eliminar")]
    public Task<IActionResult> BorrarRepuesto(int id) => Borrar("repuestos", id, nameof(Inventario));
    [HttpPost("/vehiculos/{id:int}/foto")]
    public Task<IActionResult> FotoVehiculo(int id, IFormFile? archivo) =>
        Operar(() => api.Foto($"api/vehiculos/{id}/foto", archivo), nameof(Vehiculos));
    [HttpPost("/inventario/{id:int}/foto")]
    public Task<IActionResult> FotoRepuesto(int id, IFormFile? archivo) =>
        Operar(() => api.Foto($"api/repuestos/{id}/foto", archivo), nameof(Inventario));
    [HttpPost("/movimientos")]
    public Task<IActionResult> Movimiento(MovimientoDatos datos) =>
        Operar(() => api.Cambiar(HttpMethod.Post, "api/movimientos", datos), nameof(Movimientos));

    [HttpPost("/ordenes")]
    public async Task<IActionResult> CrearOrden(OrdenNueva datos)
    {
        try
        {
            Validar();
            var orden = await api.Enviar<OrdenCreada>("api/ordenes", datos with
            { FinEstimado = HoraTaller.Utc(datos.FinEstimado) });
            TempData["Exito"] = "Orden creada.";
            return RedirectToAction(nameof(Detalle), new { id = orden.Id });
        }
        catch (FalloApi e) when (e.Codigo != 401)
        {
            TempData["Error"] = e.Message;
            return RedirectToAction(nameof(Ordenes));
        }
    }

    [HttpPost("/ordenes/{id:int}/diagnostico")]
    public Task<IActionResult> Diagnostico(int id, DiagnosticoDatos datos) =>
        Orden(HttpMethod.Put, id, "diagnostico", datos, nameof(Detalle));

    [HttpPost("/ordenes/{id:int}/asignar")]
    public Task<IActionResult> Asignar(int id, AsignacionDatos datos) =>
        Orden(HttpMethod.Post, id, "asignar", datos with
        { Inicio = HoraTaller.Utc(datos.Inicio), Fin = HoraTaller.Utc(datos.Fin) }, nameof(Detalle));

    [HttpPost("/ordenes/{id:int}/estado")]
    public Task<IActionResult> EstadoOrden(int id, EstadoDatos datos, bool presupuesto = false) =>
        Orden(HttpMethod.Post, id, "estado", datos, presupuesto ? nameof(Presupuesto) : nameof(Detalle));

    [HttpPost("/ordenes/{id:int}/presupuesto")]
    public Task<IActionResult> GuardarPresupuesto(int id, decimal manoObra, string? observaciones, Guid version,
        List<LineaForm> lineas)
    {
        var datos = new PresupuestoDatos(manoObra, observaciones,
            lineas.Where(x => x.RepuestoId > 0 || x.Cantidad > 0)
                .Select(x => new LineaDatos(x.RepuestoId, x.Cantidad)).ToList(), version);
        return Orden(HttpMethod.Post, id, "presupuesto", datos, nameof(Presupuesto));
    }

    [HttpPost("/ordenes/{id:int}/aprobar")]
    public Task<IActionResult> Aprobar(int id, VersionDatos datos) =>
        Orden(HttpMethod.Post, id, "aprobar", datos, nameof(Presupuesto));
    [HttpPost("/ordenes/{id:int}/finalizar")]
    public Task<IActionResult> Finalizar(int id, VersionDatos datos) =>
        Orden(HttpMethod.Post, id, "finalizar", datos, nameof(Detalle));
    [HttpPost("/ordenes/{id:int}/evidencias")]
    public Task<IActionResult> SubirEvidencia(int id, TipoEvidencia tipo, IFormFile? archivo) =>
        Operar(() => api.Foto($"api/ordenes/{id}/evidencias", archivo, tipo), nameof(Evidencias), id);

    private bool Tiene(string permiso) => User.HasClaim("permiso", permiso);
    private bool PuedePresupuestar(OrdenDetalle d) => Tiene(Permisos.Editar) && !User.IsInRole(Roles.Recepcion) &&
        d.Orden.Estado is Estado.EnDiagnostico or Estado.Presupuestado && !string.IsNullOrWhiteSpace(d.Diagnostico);
    private Task<IActionResult> Guardar(string recurso, int? id, object datos, string vista) =>
        Operar(() => api.Cambiar(id.HasValue ? HttpMethod.Put : HttpMethod.Post,
            "api/" + recurso + (id.HasValue ? $"/{id}" : ""), datos), vista);
    private Task<IActionResult> Borrar(string recurso, int id, string vista) =>
        Operar(() => api.Cambiar(HttpMethod.Delete, $"api/{recurso}/{id}"), vista);
    private Task<IActionResult> Orden(HttpMethod metodo, int id, string accion, object datos, string vista) =>
        Operar(() => api.Cambiar(metodo, $"api/ordenes/{id}/{accion}", datos), vista, id);

    private async Task<IActionResult> Operar(Func<Task> accion, string vista, int? id = null)
    {
        try
        {
            Validar();
            await accion();
            TempData["Exito"] = "Cambios guardados.";
        }
        catch (FalloApi e) when (e.Codigo != 401)
        {
            TempData["Error"] = e.Message;
        }
        return RedirectToAction(vista, new { id });
    }

    private void Validar()
    {
        if (!ModelState.IsValid)
            throw new FalloApi(400, string.Join(" ", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage)));
    }

    private record OrdenCreada(int Id);
    public class LineaForm
    {
        public int RepuestoId { get; set; }
        public int Cantidad { get; set; }
    }
}
