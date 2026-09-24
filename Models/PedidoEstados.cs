namespace PickingApp.Models
{
    public static class PedidoEstados
    {
        public const string Pendiente = "Pendiente";
        public const string Asignado = "Asignado";
        public const string Reasignado = "Reasignado";
        public const string EnProceso = "EnProceso";
        public const string Completado = "Completado";
        public const string Cancelado = "Cancelado";

        // Pedido original repartido en partes; no se asigna: se asignan sus partes.
        public const string Dividido = "Dividido";

        // Estados en los que el pedido sigue en manos del empleado (RN-01-CU26: un solo pedido simultáneo).
        public static readonly string[] EnCurso = { Asignado, Reasignado, EnProceso };

        // Estados finales: el pedido ya no admite cambios de asignación.
        public static readonly string[] Finalizados = { Completado, Cancelado };
    }
}
