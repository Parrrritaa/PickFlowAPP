namespace PickingApp.Models
{
    public static class Roles
    {
        public const string Administrador = "Administrador";
        public const string Supervisor = "Supervisor";
        public const string Auxiliar = "Auxiliar";

        // Para [Authorize(Roles = ...)], que recibe los roles separados por coma
        public const string AdministradorOSupervisor = Administrador + "," + Supervisor;

        public static readonly string[] Todos = { Administrador, Supervisor, Auxiliar };
    }
}
