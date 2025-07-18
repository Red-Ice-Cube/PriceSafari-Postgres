namespace PriceSafari.ScrapersControllers
{
    using System.Collections.Concurrent;

    // Definiujemy możliwe stany dla zadania
    public enum ScrapingStatus { Pending, Running, Cancelled }

    public static class AllegroTaskManager
    {
        // Słownik przechowuje stan dla każdego zadania.
        // Klucz: nazwa użytkownika Allegro (string)
        // Wartość: aktualny stan zadania (ScrapingStatus)
        public static readonly ConcurrentDictionary<string, ScrapingStatus> ActiveTasks = new();
    }
}
