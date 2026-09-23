using System.Globalization;

namespace PickingApp.Services
{
    public static class Formato
    {
        // Duración legible: "45 min", "2 h 15 min", "3 días 4 h" (los pedidos grandes pueden sumar miles de minutos)
        public static string Duracion(double minutos)
        {
            if (minutos < 1) return "menos de 1 min";

            var total = (long)Math.Round(minutos);
            if (total < 60) return $"{total} min";

            var horas = total / 60;
            var mins = total % 60;
            if (horas < 24) return mins == 0 ? $"{horas} h" : $"{horas} h {mins} min";

            var dias = horas / 24;
            var horasResto = horas % 24;
            var textoDias = dias == 1 ? "1 día" : $"{dias} días";
            return horasResto == 0 ? textoDias : $"{textoDias} {horasResto} h";
        }

        // Lee decimales escritos con coma o con punto sin depender de la cultura del servidor (es-CO lee "0.3" como 3)
        public static bool TryParseDecimal(string? texto, out double valor)
        {
            valor = 0;
            if (string.IsNullOrWhiteSpace(texto)) return false;
            var normalizado = texto.Trim().Replace(',', '.');
            return double.TryParse(normalizado, NumberStyles.Float, CultureInfo.InvariantCulture, out valor);
        }

        // Valor para un <input type="number">: siempre con punto decimal
        public static string ParaInput(double valor) => valor.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
