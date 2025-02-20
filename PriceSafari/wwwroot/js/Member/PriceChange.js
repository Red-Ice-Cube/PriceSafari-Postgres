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
            // Zakładamy, że tekst podsumowania jest pierwszym dzieckiem elementu
            summaryElement.firstChild.textContent = "Wybrane produkty: " + totalCount +
                " | Podwyższone: " + increasedCount +
                " | Obniżone: " + decreasedCount;
        }

        var simulateButton = document.getElementById("simulateButton");
        if (simulateButton) {
            simulateButton.disabled = totalCount === 0;
            simulateButton.style.opacity = totalCount === 0 ? '0.5' : '1';
        }
    }

    // Obsługa zdarzenia dodania/aktualizacji zmiany ceny
    document.addEventListener('priceBoxChange', function (event) {
        const { productId, productName, currentPrice, newPrice } = event.detail;
        console.log("Dodano zmianę ceny:", { productId, productName, currentPrice, newPrice });
        var existingIndex = selectedPriceChanges.findIndex(function (item) {
            return item.productId === productId;
        });
        if (existingIndex > -1) {
            selectedPriceChanges[existingIndex] = { productId, productName, currentPrice, newPrice };
        } else {
            selectedPriceChanges.push({ productId, productName, currentPrice, newPrice });
        }
        updatePriceChangeSummary();
    });

    // Obsługa zdarzenia usunięcia zmiany ceny
    document.addEventListener('priceBoxChangeRemove', function (event) {
        const { productId } = event.detail;
        console.log("Usunięto zmianę ceny dla produktu:", productId);
        selectedPriceChanges = selectedPriceChanges.filter(function (item) {
            return item.productId !== productId;
        });
        updatePriceChangeSummary();
    });

    // Funkcja otwierająca modal symulacji
    function openSimulationModal() {
        // Pobieramy listę ID produktów z lokalnej tablicy
        var productIds = selectedPriceChanges.map(function (item) {
            return item.productId;
        });

        // Wywołujemy backend, aby pobrać dodatkowe informacje (nazwa, grafika)
        fetch('/PriceHistory/GetPriceChangeDetails?productIds=' + encodeURIComponent(JSON.stringify(productIds)))
            .then(function (response) { return response.json(); })
            .then(function (data) {
                // data – tablica obiektów, np. { productId, productName, imageUrl }
                var modalContent = '<table class="table table-sm">';
                modalContent += '<thead><tr>';
                modalContent += '<th>Grafika</th>';
                modalContent += '<th>Produkt</th>';
                modalContent += '<th style="white-space: nowrap;">Cena aktualna</th>';
                modalContent += '<th style="white-space: nowrap;">Różnica</th>';
                modalContent += '<th style="white-space: nowrap;">Nowa cena</th>';
                modalContent += '</tr></thead><tbody>';
                selectedPriceChanges.forEach(function (item) {
                    var productDetail = data.find(function (x) {
                        return x.productId == item.productId;
                    });
                    var name = productDetail ? productDetail.productName : item.productName;
                    var imageUrl = productDetail ? productDetail.imageUrl : "";
                    var diff = item.newPrice - item.currentPrice;
                    var arrow = "";
                    if (diff > 0) {
                        // Podwyżka: czerwona strzałka
                        arrow = '<span style="color: red;">▲</span>';
                    } else if (diff < 0) {
                        // Obniżka: zielona strzałka
                        arrow = '<span style="color: green;">▼</span>';
                    } else {
                        // Brak zmiany: szare kółko
                        arrow = '<span style="color: gray;">●</span>';
                    }
                    modalContent += '<tr>';
                    // Pierwsza kolumna: grafika
                    modalContent += '<td>' + (imageUrl ? '<img src="' + imageUrl + '" alt="' + name + '" style="width:50px; height:auto;">' : '') + '</td>';
                    // Druga kolumna: nazwa produktu
                    modalContent += '<td>' + name + '</td>';
                    // Trzecia kolumna: cena aktualna
                    modalContent += '<td style="white-space: nowrap;">' + item.currentPrice.toFixed(2) + ' PLN</td>';
                    // Czwarta kolumna: różnica
                    modalContent += '<td style="white-space: nowrap;">' + arrow + ' ' + Math.abs(diff).toFixed(2) + ' PLN</td>';
                    // Piąta kolumna: nowa cena
                    modalContent += '<td style="white-space: nowrap;">' + item.newPrice.toFixed(2) + ' PLN</td>';
                    modalContent += '</tr>';
                });
                modalContent += '</tbody></table>';

                var modalBody = document.getElementById("simulationModalBody");
                if (modalBody) {
                    modalBody.innerHTML = modalContent;
                }
                $('#simulationModal').modal('show');
            })
            .catch(function (err) {
                console.error("Błąd pobierania szczegółów zmian cen:", err);
            });
    }

    // Przycisk "Symuluj" – kliknięcie otwiera modal
    var simulateButton = document.getElementById("simulateButton");
    if (simulateButton) {
        simulateButton.addEventListener("click", function () {
            openSimulationModal();
        });
    }

    // Inicjalne uaktualnienie podsumowania (przycisk zostanie zablokowany, jeśli lista jest pusta)
    updatePriceChangeSummary();
});
