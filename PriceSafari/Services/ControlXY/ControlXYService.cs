//using System.Diagnostics;
//using System.Runtime.InteropServices;

//namespace PriceSafari.Services.ControlXY
//{
//    public class ControlXYService
//    {
//        [DllImport("user32.dll")]
//        private static extern bool SetCursorPos(int x, int y);

//        [DllImport("user32.dll")]
//        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

//        [DllImport("user32.dll")]
//        private static extern bool GetCursorPos(out POINT lpPoint);

//        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
//        private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

//        [StructLayout(LayoutKind.Sequential)]
//        public struct POINT
//        {
//            public int X;
//            public int Y;
//        }

//        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
//        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
//        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
//        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

//        private static void MoveCursor(int x, int y, int delayMs)
//        {
//            SetCursorPos(x, y);
//            Thread.Sleep(delayMs);
//        }

//        public void StartControlXY()
//        {
//            Console.WriteLine("Rozpoczynam przesuwanie kursora...");

//            Thread.Sleep(2000);

//            MoveCursor(0, 0, 200);

//            for (int y = 0; y <= 35; y += 5)
//            {
//                MoveCursor(0, y, 15);
//            }

//            for (int x = 0; x <= 430; x += 5)
//            {
//                MoveCursor(x, 35, 15);
//            }

//            Console.WriteLine("Klikam prawym przyciskiem myszy...");
//            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
//            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);

//            Thread.Sleep(200);

//            GetCursorPos(out POINT currentPos);

//            int newY = currentPos.Y + 180;
//            for (int y = currentPos.Y; y <= newY; y += 5)
//            {
//                MoveCursor(currentPos.X, y, 15);
//            }

//            Console.WriteLine("Klikam lewym przyciskiem myszy...");
//            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
//            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

//            Console.WriteLine("Przesuwam kursor w lewo...");
//            int newX = currentPos.X - 70;
//            for (int x = currentPos.X; x >= newX; x -= 5)
//            {
//                MoveCursor(x, newY, 15);
//            }

//            Console.WriteLine("Przesuwam kursor w dół o 5px...");
//            int downY = newY + 5;
//            for (int y = newY; y <= downY; y += 5)
//            {
//                MoveCursor(newX, y, 800);
//            }

//            Console.WriteLine("Klikam dwukrotnie lewym przyciskiem myszy...");
//            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
//            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
//            Thread.Sleep(1527);
//            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
//            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

//            Thread.Sleep(4873);

//            Console.WriteLine("Klikam trzeci raz lewym przyciskiem myszy (po 2s)...");
//            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
//            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

//            // --- NOWY FRAGMENT KODU ---
//            Thread.Sleep(9724); 

//            Console.WriteLine("Klikam ostatni raz lewym przyciskiem myszy (po 9,7s)...");
//            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
//            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
//            // --- KONIEC NOWEGO FRAGMENTU KODU ---

//            Console.WriteLine("Zakończono akcje kursora.");
//        }
//    }
//}



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

        // --- NOWY IMPORT ---
        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int nIndex);

        // Identyfikatory dla GetSystemMetrics
        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;
        // --- KONIEC NOWEGO IMPORTU ---


        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        // --- NOWE STAŁE: BAZOWA ROZDZIELCZOŚĆ ---
        // Ustaw rozdzielczość, na której oryginalny skrypt działał poprawnie (prawdopodobnie Full HD)
        private const double BASE_WIDTH = 1920.0;
        private const double BASE_HEIGHT = 1080.0;
        // --- KONIEC NOWYCH STAŁYCH ---

        private static void MoveCursor(int x, int y, int delayMs)
        {
            SetCursorPos(x, y);
            Thread.Sleep(delayMs);
        }

        public void StartControlXY()
        {
            Console.WriteLine("Rozpoczynam przesuwanie kursora...");

            // --- POBIERANIE I SKALOWANIE ---
            // 1. Pobierz aktualną rozdzielczość ekranu
            int currentWidth = GetSystemMetrics(SM_CXSCREEN);
            int currentHeight = GetSystemMetrics(SM_CYSCREEN);

            // 2. Oblicz współczynniki skalowania
            double scaleX = currentWidth / BASE_WIDTH;
            double scaleY = currentHeight / BASE_HEIGHT;

            Console.WriteLine($"Bazowa rozdzielczość: {BASE_WIDTH}x{BASE_HEIGHT}");
            Console.WriteLine($"Aktualna rozdzielczość: {currentWidth}x{currentHeight}");
            Console.WriteLine($"Współczynniki skalowania: X={scaleX:F4}, Y={scaleY:F4}");

            // 3. Przeskaluj wszystkie "magiczne liczby"
            int targetY_Abs_1 = (int)(35 * scaleY);
            int targetX_Abs_1 = (int)(430 * scaleX);

            int targetY_Rel_1 = (int)(180 * scaleY);
            int targetX_Rel_1 = (int)(70 * scaleX);
            int targetY_Rel_2 = (int)(5 * scaleY);
            // --- KONIEC POBIERANIA I SKALOWANIA ---


            Thread.Sleep(2000);

            MoveCursor(0, 0, 200);

            // Użyj przeskalowanej wartości docelowej
            for (int y = 0; y <= targetY_Abs_1; y += 5)
            {
                MoveCursor(0, y, 15);
            }
            MoveCursor(0, targetY_Abs_1, 15); // Upewnij się, że kursor jest na dokładnej pozycji

            // Użyj przeskalowanej wartości docelowej
            for (int x = 0; x <= targetX_Abs_1; x += 5)
            {
                MoveCursor(x, targetY_Abs_1, 15);
            }
            MoveCursor(targetX_Abs_1, targetY_Abs_1, 15); // Upewnij się

            Console.WriteLine("Klikam prawym przyciskiem myszy...");
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);

            Thread.Sleep(200);

            GetCursorPos(out POINT currentPos);

            // Użyj przeskalowanej wartości WZGLĘDNEJ
            int newY = currentPos.Y + targetY_Rel_1;
            for (int y = currentPos.Y; y <= newY; y += 5)
            {
                MoveCursor(currentPos.X, y, 15);
            }
            MoveCursor(currentPos.X, newY, 15); // Upewnij się

            Console.WriteLine("Klikam lewym przyciskiem myszy...");
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

            Console.WriteLine("Przesuwam kursor w lewo...");
            // Użyj przeskalowanej wartości WZGLĘDNEJ
            int newX = currentPos.X - targetX_Rel_1;
            for (int x = currentPos.X; x >= newX; x -= 5)
            {
                MoveCursor(x, newY, 15);
            }
            MoveCursor(newX, newY, 15); // Upewnij się

            Console.WriteLine("Przesuwam kursor w dół o 5px (przeskalowane)...");
            // Użyj przeskalowanej wartości WZGLĘDNEJ
            int downY = newY + targetY_Rel_2;

            // Pętla może się nie wykonać, jeśli przeskalowana wartość < 5, 
            // więc dla pewności po prostu przenieśmy kursor
            MoveCursor(newX, downY, 800);

            Console.WriteLine("Klikam dwukrotnie lewym przyciskiem myszy...");
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(1527);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

            Thread.Sleep(4873);

            Console.WriteLine("Klikam trzeci raz lewym przyciskiem myszy (po 2s)...");
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

            Thread.Sleep(10724);

            Console.WriteLine("Klikam ostatni raz lewym przyciskiem myszy (po 10,7s)...");
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

            Console.WriteLine("Zakończono akcje kursora.");
        }
    }
}