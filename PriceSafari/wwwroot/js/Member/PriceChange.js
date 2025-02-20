document.addEventListener("DOMContentLoaded", function () {
    // Tablica przechowująca zmiany cen
    var selectedPriceChanges = [];

    // Funkcja aktualizująca pasek podsumowania zmian cen
    function updatePriceChangeSummary() {
        var increasedCount = selectedPriceChanges.filter(function (item) {
            return item.newPrice > item.currentPrice;
        }).length;
        var decreasedCount = selectedPriceChanges.filter(function (item) {
            return item.newPrice < item.currentPrice;
        }).length;
        var totalCount = selectedPriceChanges.length;

        console.log("Podsumowanie: Dodano zmiany dla " + totalCount + " produktów (" +
            "Podwyższone: " + increasedCount + ", Obniżone: " + decreasedCount + ")");

        var summaryElement = document.getElementById("priceChangeSummary");
        if (summaryElement) {
            summaryElement.textContent = "Wybrane produkty: " + totalCount +
                " | Podwyższone: " + increasedCount +
                " | Obniżone: " + decreasedCount;
        }
    }

    // Nasłuchujemy zdarzenia dodania/aktualizacji zmiany ceny
    document.addEventListener('priceBoxChange', function (event) {
        const { productId, currentPrice, newPrice } = event.detail;
        console.log("Dodano zmianę ceny:", { productId, currentPrice, newPrice });

        // Jeśli produkt już istnieje, aktualizujemy wpis; w przeciwnym razie dodajemy nowy
        var existingIndex = selectedPriceChanges.findIndex(function (item) {
            return item.productId === productId;
        });
        if (existingIndex > -1) {
            selectedPriceChanges[existingIndex] = { productId, currentPrice, newPrice };
        } else {
            selectedPriceChanges.push({ productId, currentPrice, newPrice });
        }
        updatePriceChangeSummary();
    });

    // Nasłuchujemy zdarzenia usunięcia zmiany ceny
    document.addEventListener('priceBoxChangeRemove', function (event) {
        const { productId } = event.detail;
        console.log("Usunięto zmianę ceny dla produktu:", productId);

        selectedPriceChanges = selectedPriceChanges.filter(function (item) {
            return item.productId !== productId;
        });
        updatePriceChangeSummary();
    });
});
