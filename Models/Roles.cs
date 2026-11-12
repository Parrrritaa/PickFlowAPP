namespace PickingApp.Models
{
    public static class Roles
    {
        public const string Administrador = "Administrador";
        public const string Supervisor = "Supervisor";
        public const string Auxiliar = "Auxiliar";

        // Id de cada rol en la tabla Roles; se siembran con estos valores fijos
        public const int IdAdministrador = 1;
        public const int IdSupervisor = 2;
        public const int IdAuxiliar = 3;

        // Para [Authorize(Roles = ...)], que recibe los roles separados por coma
        public const string AdministradorOSupervisor = Administrador + "," + Supervisor;
        public const string AdministradorSupervisorOAuxiliar = AdministradorOSupervisor + "," + Auxiliar;

        public static readonly string[] Todos = { Administrador, Supervisor, Auxiliar };

        // Filas de la tabla Roles
        public static readonly Rol[] Catalogo =
        {
            new() { Id = IdAdministrador, Nombre = Administrador },
            new() { Id = IdSupervisor, Nombre = Supervisor },
            new() { Id = IdAuxiliar, Nombre = Auxiliar }
        };

        public static int IdDe(string nombre) => Catalogo.First(r => r.Nombre == nombre).Id;
    }
}
