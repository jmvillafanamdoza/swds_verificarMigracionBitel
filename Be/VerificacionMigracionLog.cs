namespace Aiwara.Scheduler.Be.VerificacionMigracionBitel
{
    public class VerificacionMigracionLog
    {
        public string celular          { get; set; } = string.Empty;
        public string estado           { get; set; } = string.Empty;  // "S" = OK, "E" = Error, "T" = Otra tienda
        public string paso             { get; set; } = string.Empty;
        public string mensaje          { get; set; } = string.Empty;
        public string fechaRegistro    { get; set; } = string.Empty;
    }
}
