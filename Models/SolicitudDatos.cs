namespace PickingApp.Models
{
    public static class TiposSolicitud
    {
        public const string RegistroEmpleado = "RegistroEmpleado";
        public const string EdicionEmpleado = "EdicionEmpleado";
        public const string InactivacionEmpleado = "InactivacionEmpleado";
        public const string EdicionUbicacion = "EdicionUbicacion";
        public const string EliminacionUbicacion = "EliminacionUbicacion";

        public static string Nombre(string tipo) => tipo switch
        {
            RegistroEmpleado => "Registro de empleado",
            EdicionEmpleado => "Editar empleado",
            InactivacionEmpleado => "Inactivar empleado",
            EdicionUbicacion => "Editar ubicación",
            EliminacionUbicacion => "Eliminar ubicación",
            _ => tipo
        };
    }

    public static class EstadosSolicitud
    {
        public const string Pendiente = "Pendiente";
        public const string Aprobada = "Aprobada";
        public const string Rechazada = "Rechazada";
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
        public string Motivo { get; set; } = string.Empty;
        public List<CambioSolicitud> Cambios { get; set; } = new();
        public DatosUbicacionSolicitud NuevosDatos { get; set; } = new();
    }

    public class SolicitudEliminacionUbicacion
    {
        public int UbicacionId { get; set; }
        public string CodigoUbicacion { get; set; } = string.Empty;
        public string? BodegaCodigo { get; set; }
        public string Motivo { get; set; } = string.Empty;
    }
}
