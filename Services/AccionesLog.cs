namespace PickingApp.Services
{
    // Nombres de acción del LOG que escriben tanto las pantallas como la demostración: deben coincidir para que
    // los filtros del LOG encuentren igual lo real y lo simulado
    public static class AccionesLog
    {
        public const string AsignarPedido = "Asignar Pedido";
        public const string ReasignarPedido = "Reasignar Pedido";
        public const string CompletarPedido = "Completar Pedido";
        public const string CancelarPedido = "Cancelar Pedido";
        public const string DividirPedido = "Dividir Pedido";
        public const string CargaExcelPedidos = "Carga Excel Pedidos";
        public const string RegistrarNovedad = "Registrar Novedad";
        public const string AtenderNovedad = "Atender Novedad";
        public const string AprobarSolicitud = "Aprobar Solicitud";
        public const string RechazarSolicitud = "Rechazar Solicitud";
        public const string CrearObservacion = "Crear Observación";
        public const string ActivarObservacion = "Activar Observación";
        public const string DesactivarObservacion = "Desactivar Observación";
        public const string SolicitudRegistroEmpleado = "Solicitud Registro Empleado";
        public const string SolicitudEdicionEmpleado = "Solicitud Edición Empleado";
        public const string SolicitudInactivarEmpleado = "Solicitud Inactivar Empleado";
        public const string SolicitudTraslado = "Solicitud Traslado";
    }
}
