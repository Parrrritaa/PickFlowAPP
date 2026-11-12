namespace PickingApp.Models.ViewModels
{
    // Filtros de la lista de solicitudes: viajan por GET en la URL (?tipo=...&estado=...)
    public class FiltrosSolicitudes
    {
        public string? Busqueda { get; set; }
        public int? Tipo { get; set; }
        public string? Estado { get; set; }
        public int? SolicitanteId { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }

        public bool HayFiltros => !string.IsNullOrWhiteSpace(Busqueda) || Tipo.HasValue || !string.IsNullOrEmpty(Estado)
            || SolicitanteId.HasValue || FechaDesde.HasValue || FechaHasta.HasValue;
    }

    // Views/Solicitudes/Index.cshtml: la bandeja para revisar (Administrador y Supervisor) o las solicitudes propias
    public class SolicitudesListaViewModel
    {
        public List<Solicitud> Solicitudes { get; set; } = new();
        public FiltrosSolicitudes Filtros { get; set; } = new();

        // Solicitudes de cada estado con los demás filtros: el número de cada pestaña
        public Dictionary<string, int> ConteoPorEstado { get; set; } = new();

        // true: solicitudes de otros para revisar; false: "Mis solicitudes"
        public bool EsBandeja { get; set; }

        // El Supervisor revisa registros de empleados y también envía sus propias solicitudes: ve las dos pestañas
        public bool MostrarPestanas { get; set; }
        public int PendientesPorRevisar { get; set; }

        // Pendientes que abre el botón "Revisar pendientes" (respeta el filtro de tipo)
        public int PendientesEnCadena { get; set; }

        // Opciones de los filtros: solo los tipos y solicitantes que aparecen en la lista sin filtrar
        public List<TipoSolicitud> Tipos { get; set; } = new();
        public List<Usuario> Solicitantes { get; set; } = new();
    }

    public class CambioDetalle
    {
        public string Campo { get; set; } = string.Empty;
        public string Anterior { get; set; } = string.Empty;
        public string Nuevo { get; set; } = string.Empty;

        // Valor de hoy, solo si ya no es el "anterior" (alguien lo cambió mientras la solicitud esperaba)
        public string? Actual { get; set; }
    }

    // Views/Solicitudes/Revisar.cshtml: lo arma SolicitudesController según el tipo de solicitud
    public class SolicitudDetalleViewModel
    {
        public Solicitud Solicitud { get; set; } = null!;

        // "Empleado a registrar", "Ubicación a eliminar"...
        public string TituloDatos { get; set; } = string.Empty;
        public List<(string Etiqueta, string Valor)> Datos { get; set; } = new();
        public List<CambioDetalle> Cambios { get; set; } = new();
        public bool EsEdicion { get; set; }

        // null cuando el tipo no lleva motivo (registro de empleado)
        public string? Motivo { get; set; }

        // Algo que impide aprobarla o que conviene saber antes (ya no existe, tiene un pedido en curso...)
        public string? Aviso { get; set; }

        // Administrador (o Supervisor en registros) con la solicitud pendiente
        public bool PuedeResponder { get; set; }

        // Opciones activas del catálogo para este tipo de solicitud, separadas por decisión
        public List<ObservacionSolicitud> ObservacionesAprobar { get; set; } = new();
        public List<ObservacionSolicitud> ObservacionesRechazar { get; set; } = new();

        // Volver a la bandeja (Index) o a "Mis solicitudes"
        public string AccionVolver { get; set; } = "Index";

        // Revisión en cadena ("Revisar pendientes"): al decidir se abre la siguiente pendiente
        public bool EnCadena { get; set; }
        public int? TipoCadena { get; set; }
        public int PendientesEnCadena { get; set; }
        public int? SiguienteId { get; set; }
    }

    // Views/Observaciones/Index.cshtml: catálogo de observaciones para responder solicitudes
    public class ObservacionesIndexViewModel
    {
        public List<ObservacionSolicitud> Observaciones { get; init; } = new();

        // Solicitudes respondidas con cada observación, por Id
        public Dictionary<int, int> Usos { get; init; } = new();

        public int? TipoFiltro { get; init; }
    }
}
