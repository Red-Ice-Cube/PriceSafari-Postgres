
    document.addEventListener("DOMContentLoaded", function () {
    // Używamy unikalnego klucza dla danego sklepu
    const localStorageKey = 'selectedPriceChanges_' + storeId;
    var selectedPriceChanges = [];
    const storedChanges = localStorage.getItem(localStorageKey);
    if (storedChanges) {
        try {
        selectedPriceChanges = JSON.parse(storedChanges);
        } catch (err) {
        console.error("Błąd parsowania danych z localStorage:", err);
        }
    }

    function updatePriceChangeSummary() {
        var increasedCount = selectedPriceChanges.filter(function (item) {
            return item.newPrice > item.currentPrice;
        }).length;
    var decreasedCount = selectedPriceChanges.filter(function (item) {
            return item.newPrice < item.currentPrice;
        }).length;
    var totalCount = selectedPriceChanges.length;

    var summaryText = document.getElementById("summaryText");
    if (summaryText) {
        summaryText.innerHTML =
        "<div class='price-change-up' style='display: inline-block;'><span style='color: red;'>▲</span> " + increasedCount + "</div>" +
        "<div class='price-change-down' style='display: inline-block;'><span style='color: green;'>▼</span> " + decreasedCount + "</div>";
        }

    var simulateButton = document.getElementById("simulateButton");
    if (simulateButton) {
        simulateButton.disabled = (totalCount === 0);
    simulateButton.style.opacity = (totalCount === 0 ? '0.5' : '1');
        }
    }

    // Zapis do LS z użyciem unikalnego klucza
    function savePriceChanges() {
        localStorage.setItem(localStorageKey, JSON.stringify(selectedPriceChanges));
    }

    // Funkcja czyszcząca wszystkie zmiany dla sklepu
    function clearPriceChanges() {
        selectedPriceChanges = [];
    savePriceChanges();
    updatePriceChangeSummary();
    // Jeśli modal symulacji jest otwarty, można też wyczyścić jego zawartość
    const tableContainer = document.getElementById("simulationModalBody");
    if (tableContainer) {
        tableContainer.innerHTML = "";
        }
    }

    // Listener globalnego przycisku czyszczącego (upewnij się, że przycisk ma ID "clearChangesButton")
    const clearChangesButton = document.getElementById("clearChangesButton");
    if (clearChangesButton) {
        clearChangesButton.addEventListener("click", function () {
            clearPriceChanges();
        });
    }

    document.addEventListener('priceBoxChange', function (event) {
        const {productId, productName, currentPrice, newPrice} = event.detail;
    console.log("Dodano zmianę ceny:", {productId, productName, currentPrice, newPrice});
    var existingIndex = selectedPriceChanges.findIndex(function (item) {
            return item.productId === productId;
        });
        if (existingIndex > -1) {
        selectedPriceChanges[existingIndex] = { productId, productName, currentPrice, newPrice };
        } else {
        selectedPriceChanges.push({ productId, productName, currentPrice, newPrice });
        }
    savePriceChanges();
    updatePriceChangeSummary();
    });

    document.addEventListener('priceBoxChangeRemove', function (event) {
        const {productId} = event.detail;
    console.log("Usunięto zmianę ceny dla produktu:", productId);
    selectedPriceChanges = selectedPriceChanges.filter(function (item) {
            return parseInt(item.productId) !== parseInt(productId);
        });
    savePriceChanges();
    updatePriceChangeSummary();
    });

    // -------------------------------
    // 1) Funkcja otwierająca modal z symulacją
    // -------------------------------
    function openSimulationModal() {
        var simulationData = selectedPriceChanges.map(function (item) {
            return {
        ProductId: item.productId,
    CurrentPrice: item.currentPrice,
    NewPrice: item.newPrice,
    StoreId: storeId  // Upewnij się, że zmienna storeId zawiera ID aktualnego sklepu
            };
        });

    // Pobieramy dane dodatkowe (np. EAN, obrazki) na podstawie ID produktów
    fetch('/PriceHistory/GetPriceChangeDetails?productIds=' +
    encodeURIComponent(JSON.stringify(selectedPriceChanges.map(function (item) { return item.productId; })))
    )
    .then(function (response) {
                return response.json();
            })
    .then(function (productDetails) {
                var hasImage = productDetails.some(function (prod) {
                    return prod.imageUrl && prod.imageUrl.trim() !== "";
                });

    // Wywołujemy endpoint symulacji
    fetch('/PriceHistory/SimulatePriceChange', {
        method: 'POST',
    headers: {'Content-Type': 'application/json' },
    body: JSON.stringify(simulationData)
                })
    .then(function (response) {
                        return response.json();
                    })
    .then(function (data) {
                        var simulationResults = data.simulationResults;

    // Budowa nowej tabeli ze zmienionym układem
    var modalContent = '<table class="table-orders"><thead><tr>';
        if (hasImage) {
            modalContent += '<th>Produkt</th>';
                        }
        modalContent += '<th></th>';  // Nazwa i EAN
        modalContent += '<th>Obecne</th>';
        modalContent += '<th>Zmiana</th>';
        modalContent += '<th>Po zmianie</th>';
        modalContent += '<th>Usuń</th>';
        modalContent += '</tr></thead><tbody>';

            selectedPriceChanges.forEach(function (item) {
                            var prodDetail = productDetails.find(function (x) {
                                return x.productId == item.productId;
                            });
            var name = prodDetail ? prodDetail.productName : item.productName;
            var imageUrl = prodDetail ? prodDetail.imageUrl : "";
                            var simResult = simulationResults.find(x => x.productId == item.productId);
            var ean = (simResult && simResult.ean) ? simResult.ean : (prodDetail ? prodDetail.ean : "");
            var diff = item.newPrice - item.currentPrice;
                            var arrow = diff > 0
            ? '<span style="color: red;">▲</span>'
            : diff < 0
            ? '<span style="color: green;">▼</span>'
            : '<span style="color: gray;">●</span>';

            // Rankingi dla Google i Ceneo (obecne)
            var googleDisplay = "";
            if (simResult && simResult.totalGoogleOffers) {
                googleDisplay = '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
                + ' Pozycja cenowa | '
                + '<img src="/images/GoogleShopping.png" alt="Google Icon" style="width:16px; height:16px;" /> '
                + simResult.currentGoogleRanking + ' / ' + simResult.totalGoogleOffers
                + '</div>';
                            }
            var ceneoDisplay = "";
            if (simResult && simResult.totalCeneoOffers) {
                ceneoDisplay = '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
                + ' Pozycja cenowa | '
                + '<img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:16px; height:16px;" /> '
                + simResult.currentCeneoRanking + ' / ' + simResult.totalCeneoOffers
                + '</div>';
                            }

            // Rankingi dla Google i Ceneo (po zmianie)
            var googleNewDisplay = "";
            if (simResult && simResult.totalGoogleOffers) {
                googleNewDisplay = '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
                + ' Pozycja cenowa | '
                + '<img src="/images/GoogleShopping.png" alt="Google Icon" style="width:16px; height:16px;" /> '
                + simResult.newGoogleRanking + ' / ' + simResult.totalGoogleOffers
                + '</div>';
                            }
            var ceneoNewDisplay = "";
            if (simResult && simResult.totalCeneoOffers) {
                ceneoNewDisplay = '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
                + ' Pozycja cenowa | '
                + '<img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:16px; height:16px;" /> '
                + simResult.newCeneoRanking + ' / ' + simResult.totalCeneoOffers
                + '</div>';
                            }

            // Przygotowanie bloku "Obecne"
            let currentBlock = '<div class="price-info-box">';
                currentBlock += '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
                    + 'Cena produktu | ' + item.currentPrice.toFixed(2) + ' PLN</div>';
                if (googleDisplay) currentBlock += googleDisplay;
                if (ceneoDisplay) currentBlock += ceneoDisplay;
                if (simResult && simResult.currentMargin != null && simResult.currentMarginValue != null) {
                    let currMarginValue = parseFloat(simResult.currentMarginValue).toFixed(2);
                let currMarginPercentage = parseFloat(simResult.currentMargin).toFixed(2);
                                let currSign = parseFloat(simResult.currentMarginValue) >= 0 ? "+" : "";
                                let currClass = parseFloat(simResult.currentMarginValue) >= 0
                ? "priceBox-diff-margin"
                : "priceBox-diff-margin-minus";

                currentBlock += `
                <div class="price-info-item">
                    <div class="price-box-diff-margin ${currClass}">
                        <p>Marża: ${currSign}${currMarginValue} PLN (${currSign}${currMarginPercentage}%)</p>
                    </div>
                </div>`;
                            }
                currentBlock += '</div>';

            // Przygotowanie bloku "Po zmianie"
            let newBlock = '<div class="price-info-box">';
                newBlock += '<div class="price-info-item" style="padding: 4px 12px; background: rgb(245, 245, 245); border: 1px solid #e3e3e3; border-radius: 5px;">'
                    + 'Cena produktu | ' + item.newPrice.toFixed(2) + ' PLN</div>';
                if (googleNewDisplay) newBlock += googleNewDisplay;
                if (ceneoNewDisplay) newBlock += ceneoNewDisplay;
                if (simResult && simResult.newMargin != null && simResult.newMarginValue != null) {
                    let newMarginValue = parseFloat(simResult.newMarginValue).toFixed(2);
                let newMarginPercentage = parseFloat(simResult.newMargin).toFixed(2);
                                let newSign = parseFloat(simResult.newMarginValue) >= 0 ? "+" : "";
                                let newClass = parseFloat(simResult.newMarginValue) >= 0
                ? "priceBox-diff-margin"
                : "priceBox-diff-margin-minus";

                newBlock += `
                <div class="price-info-item">
                    <div class="price-box-diff-margin ${newClass}">
                        <p>Marża: ${newSign}${newMarginValue} PLN (${newSign}${newMarginPercentage}%)</p>
                    </div>
                </div>`;
                            }
                newBlock += '</div>';

            // Generowanie wiersza tabeli
            modalContent += '<tr>';

                if (hasImage) {
                                if (imageUrl && imageUrl.trim() !== "") {
                    modalContent += `
                                    <td>
                                        <div class="price-info-item" style="padding:4px;">
                                            <img
                                                data-src="${imageUrl}"
                                                alt="${name}"
                                                class="lazy-load loaded"
                                                style="width: 114px; height: 114px; margin-right: 5px; margin-left: 5px; background-color: #fff; border: 1px solid #e3e3e3; border-radius: 4px; padding: 10px; display: block;"
                                                src="${imageUrl}"
                                            >
                                        </div>
                                    </td>`;
                                } else {
                    modalContent += '<td></td>';
                                }
                            }

                // Nazwa produktu + EAN
                modalContent += '<td>' +
                    '<div class="price-info-item" style="font-size:125%;">' + name + '</div>' +
                    (ean ? '<div class="price-info-item">EAN: ' + ean + '</div>' : '') +
                    '</td>';

                // Obecne dane
                modalContent += '<td>' + currentBlock + '</td>';

                // Zmiana
                modalContent += '<td style="font-size:16px; white-space: nowrap;">' +
                    arrow + ' ' + Math.abs(diff).toFixed(2) + ' PLN</td>';

                // Dane po zmianie
                modalContent += '<td>' + newBlock + '</td>';

                // Przycisk usuwający
                modalContent += '<td>' +
                    '<button class="remove-change-btn" data-product-id="' + item.productId + '" style="background: none; border: none; padding: 5px; display: flex; align-items: center; margin-left:8px; justify-content: center;">' +
                        '<i class="fa fa-trash-o" style="font-size: 19px; color: red;"></i>' +
                        '</button>' +
                    '</td>';

                modalContent += '</tr>';
                        });

            modalContent += '</tbody></table>';

    var tableContainer = document.getElementById("simulationModalBody");
    if (tableContainer) {
        tableContainer.innerHTML = modalContent;
                        }

    // Obsługa przycisków usuwających
    document.querySelectorAll('.remove-change-btn').forEach(function (btn) {
        btn.addEventListener('click', function (e) {
            e.stopPropagation();
            var prodId = this.getAttribute('data-product-id');
            const removeEvent = new CustomEvent('priceBoxChangeRemove', {
                detail: { productId: prodId }
            });
            document.dispatchEvent(removeEvent);
            openSimulationModal();
        });
                        });

    var simulationModal = document.getElementById("simulationModal");
    simulationModal.style.display = 'block';
    simulationModal.classList.add('show');
                    })
    .catch(function (err) {
        console.error("Błąd symulacji zmian:", err);
                    });
            })
    .catch(function (err) {
        console.error("Błąd pobierania danych produktu:", err);
            });
    }

    // -------------------------------
    // 2) Eksport do CSV
    // -------------------------------
    function exportToCsv() {
        // Najpierw pobieramy EAN-y z backendu (jak w openSimulationModal),
        // aby mieć parę (ean, nowaCena).
        const productIds = selectedPriceChanges.map(function (item) { return item.productId; });

    fetch('/PriceHistory/GetPriceChangeDetails?productIds=' + encodeURIComponent(JSON.stringify(productIds)))
            .then(response => response.json())
            .then(productDetails => {
                // Mapa productId -> ean
                const eanMap = { };
                productDetails.forEach(prod => {
        eanMap[prod.productId] = prod.ean || "";
                });

    // Budujemy CSV: nagłówek i kolejne wiersze
    let csvContent = "EAN;Nowa cena\n";

                selectedPriceChanges.forEach(item => {
                    const ean = eanMap[item.productId] || "";
    const newPrice = item.newPrice != null ? item.newPrice.toFixed(2) : "";
    csvContent += `${ean};${newPrice}\n`;
                });

    // Tworzymy blob i generujemy link do pobrania
    const blob = new Blob([csvContent], {type: "text/csv;charset=utf-8;" });
    const fileName = `PriceSafari_${storeId}_changes.csv`;

    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = fileName;
    link.click();

                setTimeout(() => {
        URL.revokeObjectURL(link.href);
                }, 1000);
            })
            .catch(err => {
        console.error("Błąd pobierania danych produktu do CSV:", err);
            });
    }

    // -------------------------------
    // 3) Eksport do Excela (ExcelJS)
    // -------------------------------
    async function exportToExcelXLSX() {
        // Najpierw, tak samo jak w CSV, pobieramy dane o EAN:
        const productIds = selectedPriceChanges.map(item => item.productId);

    try {
            const response = await fetch('/PriceHistory/GetPriceChangeDetails?productIds=' +
    encodeURIComponent(JSON.stringify(productIds)));
    const productDetails = await response.json();

    // Mapa productId -> ean
    const eanMap = { };
            productDetails.forEach(prod => {
        eanMap[prod.productId] = prod.ean || "";
            });

    // Tworzymy workbook i arkusz
    const workbook = new ExcelJS.Workbook();
    const worksheet = workbook.addWorksheet("Zmiany Cen");

    // Nagłówek
    worksheet.addRow(["EAN", "Nowa Cena"]);

            // Uzupełnienie wierszy
            selectedPriceChanges.forEach(item => {
                const ean = eanMap[item.productId] || "";
    const newPrice = item.newPrice != null ? item.newPrice.toFixed(2) : "";
    worksheet.addRow([ean, newPrice]);
            });

    // Style kolumn (opcjonalne)
    worksheet.columns = [
    {key: "ean", width: 18 },
    {key: "newPrice", width: 15 }
    ];

    // Eksport do pliku xlsx w postaci buffer, a następnie blob
    const buffer = await workbook.xlsx.writeBuffer();
    const blob = new Blob([buffer], {type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" });

    const fileName = `PriceSafari_${storeId}_changes.xlsx`;

    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = fileName;
    link.click();

            setTimeout(() => {
        URL.revokeObjectURL(link.href);
            }, 1000);

        } catch (err) {
        console.error("Błąd w eksporcie do Excela:", err);
        }
    }

    // -------------------------------
    // Podpinamy eventy do przycisków
    // -------------------------------
    var simulateButton = document.getElementById("simulateButton");
    if (simulateButton) {
        simulateButton.addEventListener("click", function () {
            openSimulationModal();
        });
    }

    // Przycisk "Eksport do CSV" (ID w HTML: exportToCsvButton)
    const exportToCsvButton = document.getElementById("exportToCsvButton");
    if (exportToCsvButton) {
        exportToCsvButton.addEventListener("click", function () {
            exportToCsv();
        });
    }

    // Przycisk "Eksport do Excela" (ID w HTML: exportToExcelButton)
    const exportToExcelButton = document.getElementById("exportToExcelButton");
    if (exportToExcelButton) {
        exportToExcelButton.addEventListener("click", function () {
            exportToExcelXLSX();
        });
    }

    updatePriceChangeSummary();

    var closeButtons = document.querySelectorAll('#simulationModal .close, #simulationModal [data-dismiss="modal"]');
    closeButtons.forEach(function (btn) {
        btn.addEventListener('click', function () {
            var simulationModal = document.getElementById("simulationModal");
            simulationModal.style.display = 'none';
            simulationModal.classList.remove('show');
            // Po zamknięciu modala odświeżamy widok cen
            loadPrices();
        });
    });
});

