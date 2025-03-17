//document.addEventListener("DOMContentLoaded", function () {
//    var selectedPriceChanges = [];

//    function updatePriceChangeSummary() {
//        var increasedCount = selectedPriceChanges.filter(function (item) {
//            return item.newPrice > item.currentPrice;
//        }).length;
//        var decreasedCount = selectedPriceChanges.filter(function (item) {
//            return item.newPrice < item.currentPrice;
//        }).length;
//        var totalCount = selectedPriceChanges.length;

//        var summaryText = document.getElementById("summaryText");
//        if (summaryText) {
//            summaryText.innerHTML =
//                "<div class='price-change-up'>" +
//                "<span style='color: red;'>▲</span> " + increasedCount +
//                "</div>" +
//                "<div class='price-change-down'>" +
//                "<span style='color: green;'>▼</span> " + decreasedCount +
//                "</div>";
//        }

//        var simulateButton = document.getElementById("simulateButton");
//        if (simulateButton) {
//            simulateButton.disabled = (totalCount === 0);
//            simulateButton.style.opacity = (totalCount === 0 ? '0.5' : '1');
//        }
//    }

//    document.addEventListener('priceBoxChange', function (event) {
//        const { productId, productName, currentPrice, newPrice } = event.detail;
//        console.log("Dodano zmianę ceny:", { productId, productName, currentPrice, newPrice });
//        var existingIndex = selectedPriceChanges.findIndex(function (item) {
//            return item.productId === productId;
//        });
//        if (existingIndex > -1) {
//            selectedPriceChanges[existingIndex] = { productId, productName, currentPrice, newPrice };
//        } else {
//            selectedPriceChanges.push({ productId, productName, currentPrice, newPrice });
//        }
//        updatePriceChangeSummary();
//    });

//    document.addEventListener('priceBoxChangeRemove', function (event) {
//        const { productId } = event.detail;
//        console.log("Usunięto zmianę ceny dla produktu:", productId);
//        selectedPriceChanges = selectedPriceChanges.filter(function (item) {
//            return item.productId !== productId;
//        });
//        updatePriceChangeSummary();
//    });

//    function openSimulationModal() {
//        var simulationData = selectedPriceChanges.map(function (item) {
//            return {
//                ProductId: item.productId,
//                CurrentPrice: item.currentPrice,
//                NewPrice: item.newPrice
//            };
//        });
//        fetch('/PriceHistory/GetPriceChangeDetails?productIds=' +
//            encodeURIComponent(JSON.stringify(selectedPriceChanges.map(function (item) { return item.productId; })))
//        )
//            .then(function (response) {
//                return response.json();
//            })
//            .then(function (productDetails) {
//                // Sprawdzamy, czy przynajmniej jeden produkt ma obrazek
//                var hasImage = productDetails.some(function (prod) {
//                    return prod.imageUrl && prod.imageUrl.trim() !== "";
//                });

//                fetch('/PriceHistory/SimulatePriceChange', {
//                    method: 'POST',
//                    headers: { 'Content-Type': 'application/json' },
//                    body: JSON.stringify(simulationData)
//                })
//                    .then(function (response) {
//                        return response.json();
//                    })
//                    .then(function (data) {
//                        var simulationResults = data.simulationResults;
//                        // Budujemy nagłówek tabeli – kolumna z obrazkiem tylko, jeśli jest choć jeden obrazek
//                        var modalContent = '<table class="table table-sm"><thead><tr>';
//                        if (hasImage) {
//                            modalContent += '<th>Obrazek</th>';
//                        }
//                        modalContent += '<th>Produkt</th><th>Cena aktualna</th><th>Nowa cena</th><th>Różnica</th>';
//                        modalContent += '</tr></thead><tbody>';

//                        selectedPriceChanges.forEach(function (item) {
//                            var prodDetail = productDetails.find(function (x) { return x.productId == item.productId; });
//                            var name = prodDetail ? prodDetail.productName : item.productName;
//                            var imageUrl = prodDetail ? prodDetail.imageUrl : "";
//                            var diff = item.newPrice - item.currentPrice;
//                            var arrow = diff > 0 ? '<span style="color: red;">▲</span>' : diff < 0 ? '<span style="color: green;">▼</span>' : '<span style="color: gray;">●</span>';
//                            var simResult = simulationResults.find(function (x) { return x.productId == item.productId; });
//                            var googleDisplay = "";
//                            if (simResult && simResult.totalGoogleOffers) {
//                                googleDisplay = '<span class="data-channel"><img src="/images/GoogleShopping.png" alt="Google Icon" style="width:15px; height:15px;" /> ' +
//                                    simResult.currentGoogleRanking + ' / ' + simResult.totalGoogleOffers + '</span>';
//                            }
//                            var ceneoDisplay = "";
//                            if (simResult && simResult.totalCeneoOffers) {
//                                ceneoDisplay = '<span class="data-channel"><img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:15px; height:15px;" /> ' +
//                                    simResult.currentCeneoRanking + ' / ' + simResult.totalCeneoOffers + '</span>';
//                            }
//                            var googleNewDisplay = "";
//                            if (simResult && simResult.totalGoogleOffers) {
//                                googleNewDisplay = '<span class="data-channel"><img src="/images/GoogleShopping.png" alt="Google Icon" style="width:15px; height:15px;" /> ' +
//                                    simResult.newGoogleRanking + ' / ' + simResult.totalGoogleOffers + '</span>';
//                            }
//                            var ceneoNewDisplay = "";
//                            if (simResult && simResult.totalCeneoOffers) {
//                                ceneoNewDisplay = '<span class="data-channel"><img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:15px; height:15px;" /> ' +
//                                    simResult.newCeneoRanking + ' / ' + simResult.totalCeneoOffers + '</span>';

//                            }
//                            modalContent += '<tr>';
//                            // Jeśli nagłówek zawiera obrazek, to wiersz musi mieć komórkę z obrazkiem (lub pustą, jeśli brak)
//                            if (hasImage) {
//                                if (imageUrl && imageUrl.trim() !== "") {
//                                    modalContent += '<td><img src="' + imageUrl + '" alt="' + name + '" style="width:auto; height:100px;"></td>';
//                                } else {
//                                    modalContent += '<td></td>';
//                                }
//                            }
//                            modalContent += '<td>' + name + '</td>';
//                            modalContent += '<td>' + item.currentPrice.toFixed(2) + ' PLN<br/><small>' + googleDisplay + ceneoDisplay + '</small></td>';
//                            modalContent += '<td>' + item.newPrice.toFixed(2) + ' PLN<br/><small>' + googleNewDisplay + ceneoNewDisplay + '</small></td>';
//                            modalContent += '<td>' + arrow + ' ' + Math.abs(diff).toFixed(2) + ' PLN</td>';
//                            modalContent += '</tr>';
//                        });
//                        modalContent += '</tbody></table>';
//                        var tableContainer = document.getElementById("simulationModalBody");
//                        if (tableContainer) {
//                            tableContainer.innerHTML = modalContent;
//                        }
//                        var simulationModal = document.getElementById("simulationModal");
//                        simulationModal.style.display = 'block';
//                        simulationModal.classList.add('show');
//                    })
//                    .catch(function (err) {
//                        console.error("Błąd symulacji zmian:", err);
//                    });
//            })
//            .catch(function (err) {
//                console.error("Błąd pobierania danych produktu:", err);
//            });
//    }

//    var simulateButton = document.getElementById("simulateButton");
//    if (simulateButton) {
//        simulateButton.addEventListener("click", function () {
//            openSimulationModal();
//        });
//    }

//    updatePriceChangeSummary();

//    var closeButtons = document.querySelectorAll('#simulationModal .close, #simulationModal [data-dismiss="modal"]');
//    closeButtons.forEach(function (btn) {
//        btn.addEventListener('click', function () {
//            var simulationModal = document.getElementById("simulationModal");
//            simulationModal.style.display = 'none';
//            simulationModal.classList.remove('show');
//        });
//    });
//});

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

        var summaryText = document.getElementById("summaryText");
        if (summaryText) {
            summaryText.innerHTML =
                "<div class='price-change-up'>" +
                "<span style='color: red;'>▲</span> " + increasedCount +
                "</div>" +
                "<div class='price-change-down'>" +
                "<span style='color: green;'>▼</span> " + decreasedCount +
                "</div>";
        }

        var simulateButton = document.getElementById("simulateButton");
        if (simulateButton) {
            simulateButton.disabled = (totalCount === 0);
            simulateButton.style.opacity = (totalCount === 0 ? '0.5' : '1');
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

        // Pobieramy dane o produktach (w tym ewentualnie EAN) na podstawie ich ID
        fetch('/PriceHistory/GetPriceChangeDetails?productIds=' +
            encodeURIComponent(JSON.stringify(selectedPriceChanges.map(function (item) { return item.productId; })))
        )
            .then(function (response) {
                return response.json();
            })
            .then(function (productDetails) {
                // Sprawdzamy, czy przynajmniej jeden produkt ma obrazek
                var hasImage = productDetails.some(function (prod) {
                    return prod.imageUrl && prod.imageUrl.trim() !== "";
                });

                // Wywołujemy endpoint symulacji
                fetch('/PriceHistory/SimulatePriceChange', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(simulationData)
                })
                    .then(function (response) {
                        return response.json();
                    })
                    .then(function (data) {
                        var simulationResults = data.simulationResults;

                        // Budujemy tabelę – kolumna z obrazkiem tylko, jeśli jest choć jeden obrazek
                        var modalContent = '<table class="table table-sm"><thead><tr>';
                        if (hasImage) {
                            modalContent += '<th>Obrazek</th>';
                        }
                        modalContent += '<th>Produkt</th><th>Cena aktualna</th><th>Nowa cena</th><th>Różnica</th>';
                        modalContent += '</tr></thead><tbody>';

                        selectedPriceChanges.forEach(function (item) {
                            var prodDetail = productDetails.find(function (x) { return x.productId == item.productId; });
                            var name = prodDetail ? prodDetail.productName : item.productName;
                            var imageUrl = prodDetail ? prodDetail.imageUrl : "";
                            // Jeśli w obiekcie prodDetail jest ean, pobierzemy go do zmiennej
                            var ean = prodDetail && prodDetail.ean ? prodDetail.ean : "";

                            var diff = item.newPrice - item.currentPrice;
                            var arrow = diff > 0 ? '<span style="color: red;">▲</span>' :
                                diff < 0 ? '<span style="color: green;">▼</span>' :
                                    '<span style="color: gray;">●</span>';

                            var simResult = simulationResults.find(function (x) { return x.productId == item.productId; });

                            // Przygotowujemy wyświetlanie rankingów
                            var googleDisplay = "";
                            if (simResult && simResult.totalGoogleOffers) {
                                googleDisplay = '<span class="data-channel">' +
                                    '<img src="/images/GoogleShopping.png" alt="Google Icon" style="width:15px; height:15px;" /> ' +
                                    simResult.currentGoogleRanking + ' / ' + simResult.totalGoogleOffers + '</span>';
                            }

                            var ceneoDisplay = "";
                            if (simResult && simResult.totalCeneoOffers) {
                                ceneoDisplay = '<span class="data-channel">' +
                                    '<img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:15px; height:15px;" /> ' +
                                    simResult.currentCeneoRanking + ' / ' + simResult.totalCeneoOffers + '</span>';
                            }

                            var googleNewDisplay = "";
                            if (simResult && simResult.totalGoogleOffers) {
                                googleNewDisplay = '<span class="data-channel">' +
                                    '<img src="/images/GoogleShopping.png" alt="Google Icon" style="width:15px; height:15px;" /> ' +
                                    simResult.newGoogleRanking + ' / ' + simResult.totalGoogleOffers + '</span>';
                            }

                            var ceneoNewDisplay = "";
                            if (simResult && simResult.totalCeneoOffers) {
                                ceneoNewDisplay = '<span class="data-channel">' +
                                    '<img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:15px; height:15px;" /> ' +
                                    simResult.newCeneoRanking + ' / ' + simResult.totalCeneoOffers + '</span>';
                            }

                            modalContent += '<tr>';
                            // Komórka z obrazkiem (jeśli jest w nagłówku)
                            if (hasImage) {
                                if (imageUrl && imageUrl.trim() !== "") {
                                    modalContent += '<td><img src="' + imageUrl + '" alt="' + name + '" style="width:auto; height:100px;"></td>';
                                } else {
                                    modalContent += '<td></td>';
                                }
                            }

                            // Tytuł większą czcionką + EAN poniżej
                            modalContent += '<td>' +
                                '<span style="font-size:120%; font-weight:bold;">' + name + '</span>' +
                                '<br />' +
                                '<span>' + ean + '</span>' +
                                '</td>';

                            // Cena aktualna
                            modalContent += '<td>' +
                                item.currentPrice.toFixed(2) + ' PLN<br />' +
                                '<small>' + googleDisplay + ceneoDisplay + '</small>' +
                                '</td>';

                            // Nowa cena
                            modalContent += '<td>' +
                                item.newPrice.toFixed(2) + ' PLN<br />' +
                                '<small>' + googleNewDisplay + ceneoNewDisplay + '</small>' +
                                '</td>';

                            // Różnica
                            modalContent += '<td>' + arrow + ' ' + Math.abs(diff).toFixed(2) + ' PLN</td>';

                            modalContent += '</tr>';
                        });

                        modalContent += '</tbody></table>';

                        var tableContainer = document.getElementById("simulationModalBody");
                        if (tableContainer) {
                            tableContainer.innerHTML = modalContent;
                        }

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

    var simulateButton = document.getElementById("simulateButton");
    if (simulateButton) {
        simulateButton.addEventListener("click", function () {
            openSimulationModal();
        });
    }

    updatePriceChangeSummary();

    var closeButtons = document.querySelectorAll('#simulationModal .close, #simulationModal [data-dismiss="modal"]');
    closeButtons.forEach(function (btn) {
        btn.addEventListener('click', function () {
            var simulationModal = document.getElementById("simulationModal");
            simulationModal.style.display = 'none';
            simulationModal.classList.remove('show');
        });
    });
});