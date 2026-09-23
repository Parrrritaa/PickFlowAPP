namespace PickingApp.Models
{
    public static class PedidoEstados
    {
        // Estados en los que el pedido sigue en manos del empleado (RN-01-CU26: un solo pedido simultáneo).
        public static readonly string[] EnCurso = { "Asignado", "Reasignado", "EnProceso" };
    }
}
