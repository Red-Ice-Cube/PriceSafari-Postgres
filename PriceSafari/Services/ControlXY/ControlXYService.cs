using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PriceSafari.Services.ControlXY
{
    public class ControlXYService
    {
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

        // Struktura do pobierania współrzędnych kursora
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        // Stałe do obsługi zdarzeń myszy
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;


        private static void MoveCursor(int x, int y, int delayMs)
        {
            SetCursorPos(x, y);
            Thread.Sleep(delayMs);
        }

      
        public void StartControlXY()
        {
            Console.WriteLine("Rozpoczynam przesuwanie kursora...");

            // 1. Czekamy 2 sekundy przed rozpoczęciem (zamiast 4s)
            Thread.Sleep(2000);

            // 2. Ustawiamy kursor w lewym górnym rogu (0, 0)
            //    Krótsza pauza zamiast 500
            MoveCursor(0, 0, 200);

            // 3. Przesuwamy kursor o 35px w dół (w krokach co 5px, co 15ms)
            for (int y = 0; y <= 35; y += 5)
            {
                MoveCursor(0, y, 15);
            }

            // 4. Przesuwamy kursor o 430px w prawo (co 5px, co 15ms)
            for (int x = 0; x <= 430; x += 5)
            {
                MoveCursor(x, 35, 15);
            }

            // 5. Kliknięcie prawym przyciskiem myszy
            Console.WriteLine("Klikam prawym przyciskiem myszy...");
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);

            // Krótsza przerwa zamiast 500
            Thread.Sleep(200);

            // 6. Pobranie aktualnej pozycji kursora
            GetCursorPos(out POINT currentPos);

          
            int newY = currentPos.Y + 180;
            for (int y = currentPos.Y; y <= newY; y += 5)
            {
                MoveCursor(currentPos.X, y, 15);
            }

            // 8. Kliknięcie lewym
            Console.WriteLine("Klikam lewym przyciskiem myszy...");
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

            // 9. Ruch w lewo o 70px (co 5px, 15ms)
            Console.WriteLine("Przesuwam kursor w lewo...");
            int newX = currentPos.X - 70;
            for (int x = currentPos.X; x >= newX; x -= 5)
            {
                MoveCursor(x, newY, 15);
            }

            
            Console.WriteLine("Przesuwam kursor w dół o 5px...");
            int downY = newY + 5;
            for (int y = newY; y <= downY; y += 5)
            {
                MoveCursor(newX, y, 800);
            }

            // 11. Dwukrotne kliknięcie lewym
            Console.WriteLine("Klikam dwukrotnie lewym przyciskiem myszy...");
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(100); // krótszy odstęp
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

            // Dodajemy 3-sekundową przerwę
            Thread.Sleep(3000);

            // 12. Trzecie kliknięcie lewym
            Console.WriteLine("Klikam trzeci raz lewym przyciskiem myszy (po 2s)...");
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);




            Console.WriteLine("Zakończono akcje kursora.");
        }
    }
}





