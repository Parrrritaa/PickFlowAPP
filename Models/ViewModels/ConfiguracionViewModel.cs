namespace PickingApp.Models.ViewModels
{
    // Valores vigentes de Configuración (CU-39); la definición de cada parámetro está en ParametrosSistema
    public class ConfiguracionViewModel
    {
        public bool LogHabilitado { get; init; }
        public IReadOnlyDictionary<string, double> Valores { get; init; } = new Dictionary<string, double>();
    }
}
