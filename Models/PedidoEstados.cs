namespace PickingApp.Models
{
    public static class PedidoEstados
    {
        // Estados en los que el pedido sigue en manos del empleado (RN-01-CU26: un solo pedido simultáneo).
        public static readonly string[] EnCurso = { "Asignado", "Reasignado", "EnProceso" };

        // Pedido original repartido en partes; no se asigna: se asignan sus partes.
        public const string Dividido = "Dividido";

        // Estados finales: el pedido ya no admite cambios de asignación.
        public static readonly string[] Finalizados = { "Completado", "Cancelado" };
    }
}
