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

        // Stałe do obsługi klawiatury
        private const byte VK_RETURN = 0x0D;
        private const int KEYEVENTF_KEYDOWN = 0x0000;
        private const int KEYEVENTF_KEYUP = 0x0002;

        /// <summary>
        /// Metoda pomocnicza do przesuwania kursora i krótkiej pauzy.
        /// </summary>
        private static void MoveCursor(int x, int y, int delayMs)
        {
            SetCursorPos(x, y);
            Thread.Sleep(delayMs);
        }

        /// <summary>
        /// Metoda pomocnicza do naciśnięcia klawisza Enter.
        /// </summary>
        private static void PressEnter()
        {
            keybd_event(VK_RETURN, 0, KEYEVENTF_KEYDOWN, 0);
            Thread.Sleep(20); // krótsza pauza
            keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, 0);
            Thread.Sleep(20);
        }

        /// <summary>
        /// Główna metoda wywołująca całą sekwencję ruchu kursora i kliknięć.
        /// Zmniejszono czasy między ruchami, a kroki są drobniejsze,
        /// aby było szybciej i płynniej.
        /// </summary>
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

            // 7. Przesuwamy kursor o 180px w dół (teoretycznie +100px od poprzedniego,
            //    ale w kodzie jest 180). Róbmy krok co 5px, 15ms.
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

            // 10. Ruch w dół o 5px
            //     Oryginalnie co 10px i 5000ms (!), teraz 5px jednorazowo i 800ms (wydłużona pauza, żeby coś było widać)
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

          

            Console.WriteLine("Zakończono akcje kursora.");
        }
    }
}






//// 12. Ruch w górę o 150px (od downY do upY) w krokach co 5px, 15ms
//Console.WriteLine("Przesuwam kursor w górę...");
//int upY = downY - 150;
//for (int y = downY; y >= upY; y -= 5)
//{
//    MoveCursor(newX, y, 15);
//}

//// 13. Kliknięcie prawym
//Console.WriteLine("Klikam prawym przyciskiem myszy...");
//mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
//mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
//Thread.Sleep(200);

//// 14. W prawo (kopiowanie) – w oryginale błąd logiczny, zostawiamy
//Console.WriteLine("Przesuwam kursor w prawo...");
//int rightX = currentPos.X + 70;
//for (int x = currentPos.X; x >= newX; x -= 5)
//{
//    MoveCursor(x, newY, 15);
//}

//// 15. Kliknięcie lewym
//Console.WriteLine("Klikam lewym przyciskiem myszy...");
//mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
//mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

//// 16. Ruch w górę o 180px (od newY w górę)
//Console.WriteLine("Przesuwam kursor w górę o 180px...");
//int upNetY = newY - 180;
//for (int y = newY; y >= upNetY; y -= 5)
//{
//    MoveCursor(currentPos.X, y, 15);
//}

//// 17. Kliknięcie lewym
//Console.WriteLine("Klikam lewym przyciskiem myszy...");
//mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
//mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

////
//// 18. Ruch w dół o 15px (w kodzie było +40, bo tak oryginalnie)
////
//Console.WriteLine("Przesuwam kursor w dół o 15px...");
//GetCursorPos(out POINT pos18);
//int target18 = pos18.Y + 40;
//for (int y = pos18.Y; y <= target18; y += 5)
//{
//    Thread.Sleep(100);
//    MoveCursor(pos18.X, y, 20);
//}

////
//// 19. Kliknięcie prawym przyciskiem myszy
////
//Console.WriteLine("Klikam prawym przyciskiem myszy...");
//Thread.Sleep(200);
//mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
//mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);

////
//// 20. Ruch w dół o 50px (w kodzie 180)
////
//Console.WriteLine("Przesuwam kursor w dół o 50px...");
//GetCursorPos(out POINT pos20);
//int target20 = pos20.Y + 180;
//for (int y = pos20.Y; y <= target20; y += 5)
//{
//    MoveCursor(pos20.X, y, 15);
//}

////
//// 21. Kliknięcie lewym przyciskiem
////
//Console.WriteLine("Klikam lewym przyciskiem myszy...");
//mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
//mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

////
//// 22. Naciśnięcie klawisza Enter
////
//Console.WriteLine("Naciskam klawisz ENTER...");
//PressEnter();