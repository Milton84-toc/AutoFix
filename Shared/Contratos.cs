using System.ComponentModel.DataAnnotations;

namespace AutoFix;

public static class Roles
{
    public const string Admin = "Administrador", Recepcion = "Recepcionista",
        Mecanico = "Mecánico", Supervisor = "Supervisor", Cliente = "Cliente";
    public const string Personal = Admin + "," + Recepcion + "," + Mecanico + "," + Supervisor;
    public const string Oficina = Admin + "," + Recepcion + "," + Supervisor;
    public const string Taller = Admin + "," + Supervisor + "," + Mecanico;
}

public static class Permisos
{
    public const string Crear = "Ordenes.Crear", Editar = "Ordenes.Editar",
        Asignar = "Ordenes.Asignar", Aprobar = "Ordenes.Aprobar",
        Finalizar = "Ordenes.Finalizar", Inventario = "Inventario.Gestionar",
        Reportes = "Reportes.Consultar";
    public static string[] De(string rol) => rol switch
    {
        Roles.Admin => [Crear, Editar, Asignar, Aprobar, Finalizar, Inventario, Reportes],
        Roles.Recepcion => [Crear, Editar],
        Roles.Supervisor => [Editar, Asignar, Aprobar, Finalizar, Reportes],
        Roles.Mecanico => [Editar],
        _ => []
    };
}

public enum Estado
{
    Recibido, EnDiagnostico, Presupuestado, PendienteAprobacion, Aprobado,
    EnReparacion, Finalizado, Entregado, Cancelado
}

public enum TipoEvidencia { Recepcion, Dano, Repuesto, Finalizada }

public static class Etiquetas
{
    public static string De(Estado estado) => estado switch
    {
        Estado.EnDiagnostico => "En diagnóstico",
        Estado.PendienteAprobacion => "Pendiente de aprobación",
        Estado.EnReparacion => "En reparación",
        _ => estado.ToString()
    };
    public static string De(TipoEvidencia tipo) => tipo switch
    {
        TipoEvidencia.Recepcion => "Recepción",
        TipoEvidencia.Dano => "Daño",
        TipoEvidencia.Finalizada => "Reparación finalizada",
        _ => tipo.ToString()
    };
}

public record Login([Required, EmailAddress, StringLength(120)] string Email,
    [Required, StringLength(100)] string Password);
public record Registro([Required, EmailAddress, StringLength(120)] string Email,
    [Required, StringLength(100, MinimumLength = 8)] string Password,
    [Required, StringLength(120)] string Nombre, [Required, StringLength(30)] string Dui,
    [Required, StringLength(30)] string Telefono, [Required, StringLength(200)] string Direccion);
public record Sesion(string Token, string Email, string Nombre, string Rol, string[] Permisos);
public record ClienteDatos([Required, StringLength(120)] string Nombre,
    [Required, StringLength(30)] string Dui, [Required, StringLength(30)] string Telefono,
    [Required, EmailAddress, StringLength(120)] string Email, [Required, StringLength(200)] string Direccion);
public record VehiculoDatos([Range(1, int.MaxValue)] int ClienteId, [Required, StringLength(20)] string Placa,
    [Required, StringLength(80)] string Marca, [Required, StringLength(80)] string Modelo,
    [Range(1900, 2100)] int Anio, [Required, StringLength(40)] string Color,
    [Required, StringLength(50)] string Chasis);
public record MecanicoDatos([Required, StringLength(120)] string Nombre,
    [Required, EmailAddress, StringLength(120)] string Email,
    [Required, StringLength(80)] string Especialidad, bool Activo = true,
    [StringLength(100, MinimumLength = 8)] string? Password = null);
public record RepuestoDatos([Required, StringLength(40)] string Codigo, [Required, StringLength(100)] string Nombre,
    [Required, StringLength(250)] string Descripcion, [Required, StringLength(60)] string Categoria,
    [Required, StringLength(60)] string Marca, [Range(typeof(decimal), "0", "1000000")] decimal Precio,
    [Range(0, 1000000)] int StockMinimo, [Range(0, 1000000)] int Existencias = 0);
public record OrdenNueva([Range(1, int.MaxValue)] int VehiculoId,
    [Required, StringLength(100)] string Servicio, [Required, StringLength(1000)] string Problema,
    DateTime FinEstimado);
public record DiagnosticoDatos([Required, StringLength(1000)] string Diagnostico, Guid Version);
public record AsignacionDatos([Range(1, int.MaxValue)] int MecanicoId, DateTime Inicio, DateTime Fin, Guid Version);
public record EstadoDatos(Estado Estado, Guid Version);
public record MovimientoDatos([Range(1, int.MaxValue)] int RepuestoId, bool Entrada,
    [Range(1, 1000000)] int Cantidad, [Required, StringLength(180)] string Motivo);
public record LineaDatos([Range(1, int.MaxValue)] int RepuestoId, [Range(1, 1000000)] int Cantidad);
public record PresupuestoDatos([Range(typeof(decimal), "0", "1000000")] decimal ManoObra,
    [StringLength(600)] string? Observaciones, [Required, MaxLength(50)] List<LineaDatos> Lineas, Guid Version);
public record VersionDatos(Guid Version);
public record ClaveDatos([Required, StringLength(100, MinimumLength = 8)] string Password);

public record ClienteFila(int Id, string Nombre, string Dui, string Telefono, string Email, string Direccion);
public record VehiculoFila(int Id, int ClienteId, string Cliente, string Placa, string Marca, string Modelo,
    int Anio, string Color, string Chasis, string? Foto);
public record MecanicoFila(int Id, string Nombre, string Email, string Especialidad, bool Activo);
public record RepuestoFila(int Id, string Codigo, string Nombre, string Descripcion, string Categoria,
    string Marca, decimal Precio, int Existencias, int StockMinimo, string? Foto);
public record MovimientoFila(int Id, int RepuestoId, string Repuesto, int Cantidad, string Motivo,
    DateTime Fecha, string Usuario, int? OrdenId);
public record OrdenFila(int Id, string Numero, string Cliente, string Placa, string Servicio,
    DateTime Ingreso, DateTime FinEstimado, Estado Estado, string? Mecanico);
public record EvidenciaFila(int Id, TipoEvidencia Tipo, string Foto, DateTime Fecha);
public record LineaFila(int RepuestoId, string Repuesto, int Cantidad, decimal Precio, decimal Subtotal);
public record PresupuestoFila(decimal ManoObra, decimal Total, string? Observaciones,
    List<LineaFila> Lineas, string? AprobadoPor, DateTime? AprobadoEn);
public record OrdenDetalle(OrdenFila Orden, string Problema, string? Diagnostico, Guid Version,
    int? MecanicoId, DateTime? Inicio, DateTime? Fin, PresupuestoFila? Presupuesto,
    List<EvidenciaFila> Evidencias);
public record Resumen(int Ordenes, int Activas, int Finalizadas, int Clientes, int StockBajo,
    decimal TotalAprobado, List<OrdenFila> Recientes, Dictionary<Estado, int> PorEstado);
