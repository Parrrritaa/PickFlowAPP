namespace PickingApp.Models
{
    // Valores permitidos de las columnas que en MySQL son ENUM (ver EnumMySql en ApplicationDbContext).
    // Agregar un valor aquí necesita una migración nueva: el ENUM de la base de datos no se amplía solo.

    public static class Prioridades
    {
        public const string Alta = "Alta";
        public const string Media = "Media";
        public const string Baja = "Baja";

        public static readonly string[] Todas = { Alta, Media, Baja };
    }

    public static class TiposNovedad
    {
        public const string Faltante = "Faltante";
        public const string Averia = "Avería";
        public const string UbicacionIncorrecta = "Ubicación Incorrecta";
        public const string DiferenciaCantidad = "Diferencia Cantidad";
        public const string Otro = "Otro";
        // La deja la carga de Excel: la bodega del pedido no tiene ese producto y la línea no entró
        public const string NoDisponible = "No disponible";

        public static readonly string[] Todos = { Faltante, Averia, UbicacionIncorrecta, DiferenciaCantidad, Otro, NoDisponible };
    }

    // Valores del filtro ?novedad= de la lista de pedidos (además de un tipo de TiposNovedad)
    public static class FiltrosNovedad
    {
        public const string SinAtender = "pendientes";
        public const string Todas = "todas";
    }

    public static class TiposNotificacion
    {
        public const string Info = "Info";
        public const string Warning = "Warning";
        public const string Success = "Success";
        public const string Danger = "Danger";

        public static readonly string[] Todos = { Info, Warning, Success, Danger };
    }
}
