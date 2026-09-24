namespace PickingApp.Models
{
    public static class EstadosUsuario
    {
        public const string Activo = "Activo";
        public const string Inactivo = "Inactivo";
        public const string Bloqueado = "Bloqueado";

        public static readonly string[] Todos = { Activo, Inactivo, Bloqueado };
    }
}
