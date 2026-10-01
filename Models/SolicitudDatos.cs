namespace PickingApp.Models
{
    // Id fijos de la tabla TiposSolicitud (el catálogo se siembra con estas mismas filas)
    public static class TiposSolicitud
    {
        public const int RegistroEmpleado = 1;
        public const int EdicionEmpleado = 2;
        public const int InactivacionEmpleado = 3;
        public const int EdicionUbicacion = 4;
        public const int EliminacionUbicacion = 5;

        public static readonly TipoSolicitud[] Catalogo =
        {
            new() { Id = RegistroEmpleado, Nombre = "Registro de empleado" },
            new() { Id = EdicionEmpleado, Nombre = "Editar empleado" },
            new() { Id = InactivacionEmpleado, Nombre = "Inactivar empleado" },
            new() { Id = EdicionUbicacion, Nombre = "Editar ubicación" },
            new() { Id = EliminacionUbicacion, Nombre = "Eliminar ubicación" }
        };
    }

    public static class EstadosSolicitud
    {
        public const string Pendiente = "Pendiente";
        public const string Aprobada = "Aprobada";
        public const string Rechazada = "Rechazada";

        public static readonly string[] Todos = { Pendiente, Aprobada, Rechazada };
    }

    // Nombres de los campos en CambioSolicitud.Campo. Quien crea la solicitud los escribe y al aprobarla
    // se aplican solo los campos que aparecen en la lista de cambios.
    public static class CamposEmpleado
    {
        public const string Nombre = "Nombre completo";
        public const string Correo = "Correo electrónico";
        public const string FechaIngreso = "Fecha de ingreso";
        public const string HoraEntrada = "Hora de entrada";
        public const string HoraSalida = "Hora de salida";
    }

    public static class CamposUbicacion
    {
        public const string Codigo = "Código de ubicación";
        public const string Estado = "Estado";
        public const string Producto = "Producto";
        public const string CategoriaProductoNuevo = "Categoría del producto nuevo";
        public const string UnidadesProductoNuevo = "Cantidad máx. por caja del producto nuevo";
    }

    // Contenido de Solicitud.DatosJson según el tipo. Los nombres de las propiedades coinciden con el JSON
    // que ya está guardado, así las solicitudes pendientes creadas antes se siguen leyendo igual.

    public class CambioSolicitud
    {
        public string Campo { get; set; } = string.Empty;
        public string Anterior { get; set; } = string.Empty;
        public string Nuevo { get; set; } = string.Empty;
    }

    // RegistroEmpleado guarda estos datos en la raíz; EdicionEmpleado los guarda en NuevosDatos
    public class DatosEmpleadoSolicitud
    {
        public int Id { get; set; }
        public string Identificacion { get; set; } = string.Empty;
        public string NombreCompleto { get; set; } = string.Empty;
        public string? Correo { get; set; }
        public TimeSpan HorarioEntrada { get; set; }
        public TimeSpan HorarioSalida { get; set; }
        public DateTime FechaIngreso { get; set; }

        public static DatosEmpleadoSolicitud Desde(Empleado empleado) => new()
        {
            Id = empleado.Id,
            Identificacion = empleado.Identificacion,
            NombreCompleto = empleado.NombreCompleto,
            Correo = empleado.Correo,
            HorarioEntrada = empleado.HorarioEntrada,
            HorarioSalida = empleado.HorarioSalida,
            FechaIngreso = empleado.FechaIngreso
        };
    }

    public class SolicitudEdicionEmpleado
    {
        public string Motivo { get; set; } = string.Empty;
        public List<CambioSolicitud> Cambios { get; set; } = new();
        public DatosEmpleadoSolicitud NuevosDatos { get; set; } = new();
    }

    public class SolicitudInactivacionEmpleado
    {
        public int EmpleadoId { get; set; }
        public string Identificacion { get; set; } = string.Empty;
        public string NombreCompleto { get; set; } = string.Empty;
        public string Motivo { get; set; } = string.Empty;
    }

    public class DatosUbicacionSolicitud
    {
        public string Pasillo { get; set; } = string.Empty;
        public string Estante { get; set; } = string.Empty;
        public string Nivel { get; set; } = string.Empty;
        public string CodigoUbicacion { get; set; } = string.Empty;
        public bool Activa { get; set; }

        // Producto existente elegido para la ubicación; null si se propone un producto nuevo
        public int? ProductoId { get; set; }

        // true si la solicitud registra un producto nuevo: su código no debe existir al aprobarla.
        // Las solicitudes anteriores a esta opción no traen ni ProductoId ni ProductoNuevo.
        public bool ProductoNuevo { get; set; }

        public string CodigoProducto { get; set; } = string.Empty;
        public string? NombreProducto { get; set; }
        public int? CategoriaProductoId { get; set; }

        // Ausente en solicitudes anteriores a esta funcionalidad: se conserva el valor actual del producto
        public int? UnidadesPorCaja { get; set; }
    }

    public class SolicitudEdicionUbicacion
    {
        public int UbicacionId { get; set; }
        public int BodegaId { get; set; }
        public string? BodegaCodigo { get; set; }

        // Cómo estaba la ubicación al pedir el cambio; ausentes en solicitudes anteriores
        public string? CodigoUbicacion { get; set; }
        public string? Producto { get; set; }

        public string Motivo { get; set; } = string.Empty;
        public List<CambioSolicitud> Cambios { get; set; } = new();
        public DatosUbicacionSolicitud NuevosDatos { get; set; } = new();
    }

    public class SolicitudEliminacionUbicacion
    {
        public int UbicacionId { get; set; }
        public string CodigoUbicacion { get; set; } = string.Empty;
        public string? BodegaCodigo { get; set; }

        // Producto que guardaba al pedir la eliminación; ausente en solicitudes anteriores
        public string? Producto { get; set; }
        public string Motivo { get; set; } = string.Empty;
    }
}
