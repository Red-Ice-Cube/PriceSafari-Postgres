document.addEventListener("DOMContentLoaded", function () {
    var selectedPriceChanges = [];

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

        var summaryText = document.getElementById("summaryText");
        if (summaryText) {
            summaryText.textContent = "Wybrane produkty: " + totalCount +
                " | Podwyższone: " + increasedCount +
                " | Obniżone: " + decreasedCount;
        }

        var simulateButton = document.getElementById("simulateButton");
        if (simulateButton) {
            simulateButton.disabled = totalCount === 0;
            simulateButton.style.opacity = totalCount === 0 ? '0.5' : '1';
        }
    }

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

    document.addEventListener('priceBoxChangeRemove', function (event) {
        const { productId } = event.detail;
        console.log("Usunięto zmianę ceny dla produktu:", productId);
        selectedPriceChanges = selectedPriceChanges.filter(function (item) {
            return item.productId !== productId;
        });
        updatePriceChangeSummary();
    });

    function openSimulationModal() {
        var simulationData = selectedPriceChanges.map(function (item) {
            return {
                ProductId: item.productId,
                CurrentPrice: item.currentPrice,
                NewPrice: item.newPrice
            };
        });

        fetch('/PriceHistory/GetPriceChangeDetails?productIds=' +
            encodeURIComponent(JSON.stringify(selectedPriceChanges.map(function (item) {
                return item.productId;
            }))))
            .then(function (response) {
                return response.json();
            })
            .then(function (productDetails) {
                fetch('/PriceHistory/SimulatePriceChange', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(simulationData)
                })
                    .then(function (response) {
                        return response.json();
                    })
                    .then(function (data) {
                        // Zakładamy, że backend zwraca obiekt z właściwością simulationResults
                        var simulationResults = data.simulationResults;

                        var modalContent = '<table class="table table-sm">';
                        modalContent += '<thead><tr>';
                        modalContent += '<th>Grafika</th>';
                        modalContent += '<th>Produkt</th>';
                        modalContent += '<th style="white-space: nowrap;">Cena aktualna<br/><small>(Google / Ceneo)</small></th>';
                        modalContent += '<th style="white-space: nowrap;">Nowa cena<br/><small>(Google / Ceneo)</small></th>';
                        modalContent += '<th style="white-space: nowrap;">Różnica</th>';
                        modalContent += '</tr></thead><tbody>';

                        selectedPriceChanges.forEach(function (item) {
                            var prodDetail = productDetails.find(function (x) {
                                return x.productId == item.productId;
                            });
                            var name = prodDetail ? prodDetail.productName : item.productName;
                            var imageUrl = prodDetail ? prodDetail.imageUrl : "";
                            var diff = item.newPrice - item.currentPrice;
                            var arrow = diff > 0 ? '<span style="color: red;">▲</span>' :
                                diff < 0 ? '<span style="color: green;">▼</span>' :
                                    '<span style="color: gray;">●</span>';

                            var simResult = simulationResults.find(function (x) {
                                return x.productId == item.productId;
                            });

                            // Przygotowujemy wizualizację danych kanałów.
                            // Jeśli dla Google mamy oferty, wyświetlamy ikonę, ranking i liczbę ofert.
                            var googleDisplay = "";
                            if (simResult && simResult.totalGoogleOffers) {
                                googleDisplay = '<span class="data-channel">' +
                                    '<img src="/images/GoogleShopping.png" alt="Google Icon" style="width:15px; height:15px;" /> ' +
                                    simResult.currentGoogleRanking + ' / ' + simResult.totalGoogleOffers +
                                    '</span>';
                            }
                            // Analogicznie dla Ceneo.
                            var ceneoDisplay = "";
                            if (simResult && simResult.totalCeneoOffers) {
                                ceneoDisplay = '<span class="data-channel">' +
                                    '<img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:15px; height:15px;" /> ' +
                                    simResult.currentCeneoRanking + ' / ' + simResult.totalCeneoOffers +
                                    '</span>';
                            }

                            // Podobnie przygotowujemy dane dla nowej ceny.
                            var googleNewDisplay = "";
                            if (simResult && simResult.totalGoogleOffers) {
                                googleNewDisplay = '<span class="data-channel">' +
                                    '<img src="/images/GoogleShopping.png" alt="Google Icon" style="width:15px; height:15px;" /> ' +
                                    simResult.newGoogleRanking + ' / ' + simResult.totalGoogleOffers +
                                    '</span>';
                            }
                            var ceneoNewDisplay = "";
                            if (simResult && simResult.totalCeneoOffers) {
                                ceneoNewDisplay = '<span class="data-channel">' +
                                    '<img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:15px; height:15px;" /> ' +
                                    simResult.newCeneoRanking + ' / ' + simResult.totalCeneoOffers +
                                    '</span>';
                            }

                            modalContent += '<tr>';
                            modalContent += '<td>' + (imageUrl ? '<img src="' + imageUrl + '" alt="' + name + '" style="width:50px; height:auto;">' : '') + '</td>';
                            modalContent += '<td>' + name + '</td>';
                            modalContent += '<td style="white-space: nowrap;">' +
                                item.currentPrice.toFixed(2) + ' PLN' +
                                '<br/><small>' +
                                googleDisplay +
                                (googleDisplay && ceneoDisplay ? ' &nbsp; ' : '') +
                                ceneoDisplay +
                                '</small></td>';
                            modalContent += '<td style="white-space: nowrap;">' +
                                item.newPrice.toFixed(2) + ' PLN' +
                                '<br/><small>' +
                                googleNewDisplay +
                                (googleNewDisplay && ceneoNewDisplay ? ' &nbsp; ' : '') +
                                ceneoNewDisplay +
                                '</small></td>';
                            modalContent += '<td style="white-space: nowrap;">' + arrow + ' ' + Math.abs(diff).toFixed(2) + ' PLN</td>';
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
                        console.error("Błąd symulacji zmian:", err);
                    });
            })
            .catch(function (err) {
                console.error("Błąd pobierania danych produktu:", err);
            });
    }

    var simulateButton = document.getElementById("simulateButton");
    if (simulateButton) {
        simulateButton.addEventListener("click", function () {
            openSimulationModal();
        });
    }

    updatePriceChangeSummary();
});
