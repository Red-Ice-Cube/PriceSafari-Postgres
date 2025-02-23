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







//function renderPrices(data) {
//    const container = document.getElementById('priceContainer');
//    const currentProductSearchTerm = document.getElementById('productSearch').value.trim();
//    const currentStoreSearchTerm = document.getElementById('storeSearch').value.trim();
//    container.innerHTML = '';

//    data.forEach(item => {
//        const isRejected = item.isRejected;

//        const highlightedProductName = highlightMatches(item.productName, currentProductSearchTerm);
//        const highlightedStoreName = highlightMatches(item.storeName, currentStoreSearchTerm);
//        const isBidding = item.isBidding === "1";
//        const deliveryClass = getDeliveryClass(item.delivery);

//        let percentageDifference = null;
//        let priceDifference = null;
//        let savings = null;
//        let myIsBidding = null;
//        let myDeliveryClass = null;
//        let marginAmount = null;
//        let marginPercentage = null;
//        let marginSign = '';
//        let marginClass = '';
//        let marginPrice = null;
//        let myPrice = null;
//        let myPosition = null;
//        let lowestPrice = null;

//        if (!isRejected) {
//            percentageDifference = item.percentageDifference != null ? item.percentageDifference.toFixed(2) : "N/A";
//            priceDifference = item.priceDifference != null ? item.priceDifference.toFixed(2) : "N/A";
//            savings = item.savings != null ? item.savings.toFixed(2) : "N/A";

//            myIsBidding = item.myIsBidding === "1";
//            myDeliveryClass = getDeliveryClass(item.myDelivery);
//            marginAmount = item.marginAmount;
//            marginPercentage = item.marginPercentage;
//            marginSign = item.marginSign;
//            marginClass = item.marginClass;
//            marginPrice = item.marginPrice;
//            myPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;
//            myPosition = item.myPosition;
//            lowestPrice = item.lowestPrice != null ? parseFloat(item.lowestPrice) : null;
//        }

//        const box = document.createElement('div');
//        box.className = 'price-box ' + item.colorClass;
//        box.dataset.detailsUrl = '/PriceHistory/Details?scrapId=' + item.scrapId + '&productId=' + item.productId;
//        box.dataset.productId = item.productId;

//        box.addEventListener('click', function () {
//            window.open(this.dataset.detailsUrl, '_blank');
//        });

//        const priceBoxSpace = document.createElement('div');
//        priceBoxSpace.className = 'price-box-space';

//        const priceBoxColumnName = document.createElement('div');
//        priceBoxColumnName.className = 'price-box-column-name';
//        priceBoxColumnName.innerHTML = highlightedProductName;

//        const priceBoxColumnCategory = document.createElement('div');
//        priceBoxColumnCategory.className = 'price-box-column-category';

//        if (item.externalId) {
//            const apiBox = document.createElement('span');
//            apiBox.className = 'ApiBox';
//            apiBox.innerHTML = 'API ID ' + item.externalId;
//            priceBoxColumnCategory.appendChild(apiBox);
//        }

//        const flagsContainer = createFlagsContainer(item);

//        const assignFlagButton = document.createElement('button');
//        assignFlagButton.className = 'assign-flag-button';
//        assignFlagButton.dataset.productId = item.productId;
//        assignFlagButton.innerHTML = '+ Przypisz flagi';
//        assignFlagButton.style.pointerEvents = 'auto';

//        assignFlagButton.addEventListener('click', function (event) {
//            event.stopPropagation();
//            selectedProductId = this.dataset.productId;
//            modal.style.display = 'block';

//            fetch(`/ProductFlags/GetFlagsForProduct?productId=${selectedProductId}`)
//                .then(response => response.json())
//                .then(flags => {
//                    document.querySelectorAll('.flagCheckbox').forEach(checkbox => {
//                        checkbox.checked = flags.includes(parseInt(checkbox.value));
//                    });
//                })
//                .catch(error => console.error('Błąd pobierania flag dla produktu:', error));
//        });

//        priceBoxSpace.appendChild(priceBoxColumnName);
//        priceBoxSpace.appendChild(flagsContainer);
//        priceBoxSpace.appendChild(assignFlagButton);
//        priceBoxSpace.appendChild(priceBoxColumnCategory);

//        const externalInfoContainer = document.createElement('div');
//        externalInfoContainer.className = 'price-box-externalInfo';

//        const priceBoxColumnStoreCount = document.createElement('div');
//        priceBoxColumnStoreCount.className = 'price-box-column-offers';
//        priceBoxColumnStoreCount.innerHTML =
//            (
//                (item.sourceGoogle || item.sourceCeneo) ?
//                    '<span class="data-channel">' +
//                    (item.sourceGoogle ? '<img src="/images/GoogleShopping.png" alt="Google Icon" style="width:15px; height:15px;" />' : '') +
//                    (item.sourceCeneo ? '<img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:15px; height:15px;" />' : '') +
//                    '</span>'
//                    : ''
//            ) +
//            '<div class="offer-count-box">' + item.storeCount + ' Ofert</div>';

//        externalInfoContainer.appendChild(priceBoxColumnStoreCount);

//        if (marginPrice != null) {
//            const formattedMarginPrice = marginPrice.toLocaleString('pl-PL', {
//                minimumFractionDigits: 2,
//                maximumFractionDigits: 2
//            }) + ' PLN';

//            const purchasePriceBox = document.createElement('div');
//            purchasePriceBox.className = 'price-box-diff-margin ' + marginClass;
//            purchasePriceBox.innerHTML = '<p>Cena zakupu: ' + formattedMarginPrice + '</p>';

//            externalInfoContainer.appendChild(purchasePriceBox);

//            if (myPrice != null) {
//                const formattedMarginAmount = marginSign + Math.abs(marginAmount).toLocaleString('pl-PL', {
//                    minimumFractionDigits: 2,
//                    maximumFractionDigits: 2
//                }) + ' PLN';
//                const formattedMarginPercentage = '(' + marginSign + Math.abs(marginPercentage).toLocaleString('pl-PL', {
//                    minimumFractionDigits: 2,
//                    maximumFractionDigits: 2
//                }) + '%)';

//                const marginBox = document.createElement('div');
//                marginBox.className = 'price-box-diff-margin ' + marginClass;
//                marginBox.innerHTML = '<p>Marża: ' + formattedMarginAmount + ' ' + formattedMarginPercentage + '</p>';

//                externalInfoContainer.appendChild(marginBox);
//            }
//        }

//        priceBoxSpace.appendChild(externalInfoContainer);

//        const priceBoxData = document.createElement('div');
//        priceBoxData.className = 'price-box-data';

//        const colorBar = document.createElement('div');
//        colorBar.className = 'color-bar ' + item.colorClass;

//        const priceBoxColumnLowestPrice = document.createElement('div');
//        priceBoxColumnLowestPrice.className = 'price-box-column';

//        const priceBoxLowestText = document.createElement('div');
//        priceBoxLowestText.className = 'price-box-column-text';

//        const priceLine = document.createElement('div');
//        priceLine.style.display = 'flex';

//        const priceSpan = document.createElement('div');
//        priceSpan.style.fontWeight = '500';
//        priceSpan.style.fontSize = '17px';
//        priceSpan.textContent = item.lowestPrice.toFixed(2) + ' PLN';
//        priceLine.appendChild(priceSpan);

//        // Sprawdzamy externalBestPriceCount i dodajemy odpowiedni box obok ceny
//        if (typeof item.externalBestPriceCount !== 'undefined' && item.externalBestPriceCount !== null) {
//            if (item.externalBestPriceCount === 1) {
//                const uniqueBox = document.createElement('div');
//                uniqueBox.className = 'uniqueBestPriceBox';
//                uniqueBox.textContent = '★ TOP';
//                uniqueBox.style.marginLeft = '8px';
//                priceLine.appendChild(uniqueBox);
//            } else if (item.externalBestPriceCount > 1) {
//                const shareBox = document.createElement('div');
//                shareBox.className = 'shareBestPriceBox';
//                shareBox.textContent = item.externalBestPriceCount + ' TOP';
//                shareBox.style.marginLeft = '8px';
//                priceLine.appendChild(shareBox);
//            }
//        }
//        if (item.singleBestCheaperDiffPerc !== null && item.singleBestCheaperDiffPerc !== undefined) {
//            const diffBox = document.createElement('div');
//            diffBox.style.marginLeft = '4px';
//            diffBox.className = item.singleBestCheaperDiffPerc > 25 ? 'singlePriceDiffBoxHigh' : 'singlePriceDiffBox';
//            const span = document.createElement('span');
//            span.textContent = item.singleBestCheaperDiffPerc.toFixed(2) + '%';
//            diffBox.appendChild(span);
//            priceLine.appendChild(diffBox);
//        }

//        priceBoxLowestText.appendChild(priceLine);

//        const storeNameDiv = document.createElement('div');
//        storeNameDiv.textContent = highlightedStoreName;
//        priceBoxLowestText.appendChild(storeNameDiv);

//        const priceBoxLowestDetails = document.createElement('div');
//        priceBoxLowestDetails.className = 'price-box-column-text';
//        priceBoxLowestDetails.innerHTML =
//            (item.isGoogle != null ?
//                '<span class="data-channel"><img src="' +
//                (item.isGoogle ? '/images/GoogleShopping.png' : '/images/Ceneo.png') +
//                '" alt="Channel Icon" style="width:20px; height:20px; margin-right:4px;" /></div>'
//                : '') +
//            (item.position !== null ?
//                (item.isGoogle ?
//                    '<span class="Position-Google">Poz. Google ' + item.position + '</span>' :
//                    '<span class="Position">Poz. Ceneo ' + item.position + '</span>')
//                :
//                '<span class="Position" style="background-color: #414141;">Schowany</span>') +
//            (isBidding ? '<span class="Bidding">Bid</span>' : '') +
//            (item.delivery != null ? '<span class="' + deliveryClass + '">Wysyłka w ' + (item.delivery == 1 ? '1 dzień' : item.delivery + ' dni') + '</span>' : '');

//        priceBoxColumnLowestPrice.appendChild(priceBoxLowestText);
//        priceBoxColumnLowestPrice.appendChild(priceBoxLowestDetails);

//        const priceBoxColumnMyPrice = document.createElement('div');
//        priceBoxColumnMyPrice.className = 'price-box-column';

//        if (!isRejected && myPrice != null) {
//            const priceBoxMyText = document.createElement('div');
//            priceBoxMyText.className = 'price-box-column-text';

//            if (item.externalPrice !== null) {
//                const externalPriceDifference = (item.externalPrice - myPrice).toFixed(2);
//                const isPriceDecrease = item.externalPrice < myPrice;

//                const priceChangeContainer = document.createElement('div');
//                priceChangeContainer.style.display = 'flex';
//                priceChangeContainer.style.justifyContent = 'space-between';
//                priceChangeContainer.style.alignItems = 'center';

//                const storeName = document.createElement('span');
//                storeName.style.fontWeight = '500';
//                storeName.style.marginRight = '20px';
//                storeName.textContent = myStoreName;

//                const priceDifference = document.createElement('span');
//                priceDifference.style.fontWeight = '500';
//                const arrow = '<span class="' + (isPriceDecrease ? 'arrow-down' : 'arrow-up') + '"></span>';
//                priceDifference.innerHTML = arrow + (isPriceDecrease ? '-' : '+') + Math.abs(externalPriceDifference) + ' PLN ';

//                priceChangeContainer.appendChild(storeName);
//                priceChangeContainer.appendChild(priceDifference);

//                const priceContainer = document.createElement('div');
//                priceContainer.style.display = 'flex';
//                priceContainer.style.justifyContent = 'space-between';
//                priceContainer.style.alignItems = 'center';

//                const oldPrice = document.createElement('span');
//                oldPrice.style.fontWeight = '500';
//                oldPrice.style.textDecoration = 'line-through';
//                oldPrice.style.marginRight = '10px';
//                oldPrice.textContent = myPrice.toFixed(2) + ' PLN';

//                const newPrice = document.createElement('span');
//                newPrice.style.fontWeight = '500';
//                newPrice.textContent = item.externalPrice.toFixed(2) + ' PLN';

//                priceContainer.appendChild(oldPrice);
//                priceContainer.appendChild(newPrice);

//                priceBoxMyText.appendChild(priceContainer);
//                priceBoxMyText.appendChild(priceChangeContainer);
//            } else {
//                priceBoxMyText.innerHTML =
//                    '<span style="font-weight: 500; font-size:17px;">' + myPrice.toFixed(2) + ' PLN</span><br>' + myStoreName;
//            }

//            const priceBoxMyDetails = document.createElement('div');
//            priceBoxMyDetails.className = 'price-box-column-text';
//            priceBoxMyDetails.innerHTML =
//                (item.myIsGoogle != null ?
//                    '<span class="data-channel"><img src="' +
//                    (item.myIsGoogle ? '/images/GoogleShopping.png' : '/images/Ceneo.png') +
//                    '" alt="Channel Icon" style="width:20px; height:20px; margin-right:4px;" /></div>'
//                    : '') +
//                (myPosition !== null ?
//                    (item.myIsGoogle ?
//                        '<span class="Position-Google">Poz. Google ' + myPosition + '</span>' :
//                        '<span class="Position">Poz. Ceneo ' + myPosition + '</span>')
//                    :
//                    '<span class="Position" style="background-color: #414141;">Schowany</span>') +
//                (myIsBidding ? '<span class="Bidding">Bid</span>' : '') +
//                (item.myDelivery != null ? '<span class="' + myDeliveryClass + '">Wysyłka w ' + (item.myDelivery == 1 ? '1 dzień' : item.myDelivery + ' dni') + '</span>' : '');

//            priceBoxColumnMyPrice.appendChild(priceBoxMyText);
//            priceBoxColumnMyPrice.appendChild(priceBoxMyDetails);
//        } else {
//            const priceBoxMyText = document.createElement('div');
//            priceBoxMyText.className = 'price-box-column-text';
//            priceBoxMyText.innerHTML = '<span style="font-weight: 500;">Brak ceny</span><br>' + myStoreName;
//            priceBoxColumnMyPrice.appendChild(priceBoxMyText);
//        }

//        const priceBoxColumnInfo = document.createElement('div');
//        priceBoxColumnInfo.className = 'price-box-column-action';
//        priceBoxColumnInfo.innerHTML = '';

//        // Helper: dodaje listener, który przy kliknięciu wysyła zdarzenie z danymi (productId, currentPrice, newPrice)
//        // Dodatkowo – zamiast zmieniać kolory – tworzymy badge z napisem "Dodano | usun"
//        function attachPriceChangeListener(element, suggestedPrice) {
//            element.addEventListener('click', function (e) {
//                e.stopPropagation();
//                const box = this.closest('.price-box');
//                const productId = box ? box.dataset.productId : null;
//                const currentPriceValue = myPrice;
//                console.log("PriceChange kliknięcie:", { productId, currentPrice: currentPriceValue, newPrice: suggestedPrice });
//                const priceChangeEvent = new CustomEvent('priceBoxChange', {
//                    detail: { productId, currentPrice: currentPriceValue, newPrice: suggestedPrice }
//                });
//                document.dispatchEvent(priceChangeEvent);
//                if (box) {
//                    // Sprawdzamy czy badge już istnieje
//                    let badge = box.querySelector('.price-change-badge');
//                    if (!badge) {
//                        badge = document.createElement('div');
//                        badge.className = 'price-change-badge';
//                        badge.style.backgroundColor = "#333333";
//                        badge.style.color = "#f5f5f5";
//                        badge.style.padding = "2px 4px";
//                        badge.style.marginTop = "4px";
//                        badge.style.fontSize = "12px";
//                        badge.style.display = "inline-block";
//                        badge.style.cursor = "default";
//                        badge.innerHTML = "Dodano | <span class='remove-price-change' style='text-decoration:underline; cursor:pointer;'>usun</span>";
//                        // Wstawiamy badge za elementem, w obrębie tej samej kolumny
//                        this.parentNode.insertBefore(badge, this.nextSibling);
//                        // Listener usuwający zmianę
//                        badge.querySelector('.remove-price-change').addEventListener('click', function (ev) {
//                            ev.stopPropagation();
//                            badge.remove();
//                            const removeEvent = new CustomEvent('priceBoxChangeRemove', {
//                                detail: { productId }
//                            });
//                            document.dispatchEvent(removeEvent);
//                        });
//                    }
//                }
//            });
//        }

//        // Obsługa zmian cenowych w zależności od colorClass
//        if (!isRejected) {
//            if (item.colorClass === "prOnlyMe") {
//                const diffClass = item.colorClass + ' ' + 'priceBox-diff-top';
//                priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Brak ofert konkurencji</div>';
//            } else if (item.colorClass === "prToLow" || item.colorClass === "prIdeal") {
//                if (myPrice != null && savings != null) {
//                    const savingsValue = parseFloat(savings.replace(',', '.'));
//                    const upArrowClass = item.colorClass === 'prToLow' ? 'arrow-up-black' : 'arrow-up-turquoise';

//                    const suggestedPrice1 = myPrice + savingsValue;
//                    const amountToSuggestedPrice1 = savingsValue;
//                    const percentageToSuggestedPrice1 = (amountToSuggestedPrice1 / myPrice) * 100;

//                    let suggestedPrice2, amountToSuggestedPrice2, percentageToSuggestedPrice2;
//                    let arrowClass2 = upArrowClass;

//                    if (usePriceDifference) {
//                        if (savingsValue < 1) {
//                            suggestedPrice2 = suggestedPrice1 - setStepPrice;
//                            amountToSuggestedPrice2 = suggestedPrice2 - myPrice;
//                            percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;
//                            if (amountToSuggestedPrice2 < 0) { arrowClass2 = 'arrow-down-turquoise'; }
//                        } else {
//                            suggestedPrice2 = suggestedPrice1 - setStepPrice;
//                            amountToSuggestedPrice2 = amountToSuggestedPrice1 - setStepPrice;
//                            percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;
//                            if (amountToSuggestedPrice2 < 0) { arrowClass2 = 'arrow-down-turquoise'; }
//                        }
//                    } else {
//                        const percentageStep = setStepPrice / 100;
//                        if (savingsValue < 1) {
//                            suggestedPrice2 = suggestedPrice1 * (1 - percentageStep);
//                            amountToSuggestedPrice2 = suggestedPrice2 - myPrice;
//                            percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;
//                            if (amountToSuggestedPrice2 < 0) { arrowClass2 = 'arrow-down-turquoise'; }
//                        } else {
//                            suggestedPrice2 = suggestedPrice1 * (1 - percentageStep);
//                            amountToSuggestedPrice2 = amountToSuggestedPrice1 - (myPrice * percentageStep);
//                            percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;
//                            if (amountToSuggestedPrice2 < 0) { arrowClass2 = 'arrow-down-turquoise'; }
//                        }
//                    }

//                    const amount1Formatted = (amountToSuggestedPrice1 >= 0 ? '+' : '') + amountToSuggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
//                    const percentage1Formatted = '(' + (percentageToSuggestedPrice1 >= 0 ? '+' : '') + percentageToSuggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
//                    const newSuggestedPrice1Formatted = suggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

//                    const amount2Formatted = (amountToSuggestedPrice2 >= 0 ? '+' : '') + amountToSuggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
//                    const percentage2Formatted = '(' + (percentageToSuggestedPrice2 >= 0 ? '+' : '') + percentageToSuggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
//                    const newSuggestedPrice2Formatted = suggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

//                    const matchPriceBox = document.createElement('div');
//                    matchPriceBox.className = 'price-box-column';
//                    const matchPriceLine = document.createElement('div');
//                    matchPriceLine.className = 'price-action-line';

//                    const upArrow = document.createElement('span');
//                    upArrow.className = upArrowClass;
//                    const increaseText = document.createElement('span');
//                    increaseText.innerHTML = amount1Formatted + ' ' + percentage1Formatted;
//                    const newPriceText = document.createElement('div');
//                    newPriceText.innerHTML = '= ' + newSuggestedPrice1Formatted;
//                    const colorSquare = document.createElement('span');
//                    colorSquare.className = 'color-square-green';

//                    matchPriceLine.appendChild(upArrow);
//                    matchPriceLine.appendChild(increaseText);
//                    matchPriceLine.appendChild(newPriceText);
//                    matchPriceLine.appendChild(colorSquare);
//                    // Dodaj listener – przekazujemy suggestedPrice1 jako nową cenę
//                    attachPriceChangeListener(matchPriceLine, suggestedPrice1);
//                    matchPriceBox.appendChild(matchPriceLine);

//                    const strategicPriceBox = document.createElement('div');
//                    strategicPriceBox.className = 'price-box-column';
//                    const strategicPriceLine = document.createElement('div');
//                    strategicPriceLine.className = 'price-action-line';

//                    const arrow2 = document.createElement('span');
//                    arrow2.className = arrowClass2;
//                    const increaseText2 = document.createElement('span');
//                    increaseText2.innerHTML = amount2Formatted + ' ' + percentage2Formatted;
//                    const newPriceText2 = document.createElement('div');
//                    newPriceText2.innerHTML = '= ' + newSuggestedPrice2Formatted;
//                    const colorSquare2 = document.createElement('span');
//                    colorSquare2.className = 'color-square-turquoise';

//                    strategicPriceLine.appendChild(arrow2);
//                    strategicPriceLine.appendChild(increaseText2);
//                    strategicPriceLine.appendChild(newPriceText2);
//                    strategicPriceLine.appendChild(colorSquare2);
//                    // Dodaj listener – przekazujemy suggestedPrice2
//                    attachPriceChangeListener(strategicPriceLine, suggestedPrice2);
//                    strategicPriceBox.appendChild(strategicPriceLine);

//                    priceBoxColumnInfo.appendChild(matchPriceBox);
//                    priceBoxColumnInfo.appendChild(strategicPriceBox);
//                } else {
//                    const diffClass = item.colorClass + ' ' + 'priceBox-diff';
//                    priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Podnieś: N/A</div>';
//                }
//            } else if (item.colorClass === "prMid") {
//                if (myPrice != null && lowestPrice != null) {
//                    const amountToMatchLowestPrice = myPrice - lowestPrice;
//                    const percentageToMatchLowestPrice = (amountToMatchLowestPrice / myPrice) * 100;

//                    let strategicPrice;
//                    if (usePriceDifference) {
//                        strategicPrice = lowestPrice - setStepPrice;
//                    } else {
//                        strategicPrice = lowestPrice * (1 - setStepPrice / 100);
//                    }
//                    const amountToBeatLowestPrice = myPrice - strategicPrice;
//                    const percentageToBeatLowestPrice = (amountToBeatLowestPrice / myPrice) * 100;

//                    const amountMatchFormatted = amountToMatchLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
//                    const percentageMatchFormatted = '(-' + percentageToMatchLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
//                    const newSuggestedPriceMatch = lowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

//                    const amountBeatFormatted = amountToBeatLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
//                    const percentageBeatFormatted = '(-' + percentageToBeatLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
//                    const newSuggestedPriceBeat = strategicPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

//                    const matchPriceBox = document.createElement('div');
//                    matchPriceBox.className = 'price-box-column';
//                    const matchPriceLine = document.createElement('div');
//                    matchPriceLine.className = 'price-action-line';

//                    const downArrow = document.createElement('span');
//                    downArrow.className = 'arrow-down-yellow';
//                    const reduceText = document.createElement('span');
//                    reduceText.innerHTML = '-' + amountMatchFormatted + ' ' + percentageMatchFormatted;
//                    const newPriceText = document.createElement('div');
//                    newPriceText.innerHTML = '= ' + newSuggestedPriceMatch;
//                    const colorSquare = document.createElement('span');
//                    colorSquare.className = 'color-square-green';

//                    matchPriceLine.appendChild(downArrow);
//                    matchPriceLine.appendChild(reduceText);
//                    matchPriceLine.appendChild(newPriceText);
//                    matchPriceLine.appendChild(colorSquare);
//                    // Listener – przekazujemy lowestPrice (sugerowana cena)
//                    attachPriceChangeListener(matchPriceLine, lowestPrice);
//                    matchPriceBox.appendChild(matchPriceLine);

//                    const strategicPriceBox = document.createElement('div');
//                    strategicPriceBox.className = 'price-box-column';
//                    const strategicPriceLine = document.createElement('div');
//                    strategicPriceLine.className = 'price-action-line';

//                    const downArrow2 = document.createElement('span');
//                    downArrow2.className = 'arrow-down-yellow';
//                    const reduceText2 = document.createElement('span');
//                    reduceText2.innerHTML = '-' + amountBeatFormatted + ' ' + percentageBeatFormatted;
//                    const newPriceText2 = document.createElement('div');
//                    newPriceText2.innerHTML = '= ' + newSuggestedPriceBeat;
//                    const colorSquare2 = document.createElement('span');
//                    colorSquare2.className = 'color-square-turquoise';

//                    strategicPriceLine.appendChild(downArrow2);
//                    strategicPriceLine.appendChild(reduceText2);
//                    strategicPriceLine.appendChild(newPriceText2);
//                    strategicPriceLine.appendChild(colorSquare2);
//                    // Listener – przekazujemy strategicPrice
//                    attachPriceChangeListener(strategicPriceLine, strategicPrice);
//                    strategicPriceBox.appendChild(strategicPriceLine);

//                    priceBoxColumnInfo.appendChild(matchPriceBox);
//                    priceBoxColumnInfo.appendChild(strategicPriceBox);
//                } else {
//                    const diffClass = item.colorClass + ' ' + 'priceBox-diff';
//                    priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Obniż: N/A</div>';
//                }
//            } else if (item.colorClass === "prToHigh") {
//                if (myPrice != null && lowestPrice != null) {
//                    const amountToMatchLowestPrice = myPrice - lowestPrice;
//                    const percentageToMatchLowestPrice = (amountToMatchLowestPrice / myPrice) * 100;

//                    let strategicPrice;
//                    if (usePriceDifference) {
//                        strategicPrice = lowestPrice - setStepPrice;
//                    } else {
//                        strategicPrice = lowestPrice * (1 - setStepPrice / 100);
//                    }
//                    const amountToBeatLowestPrice = myPrice - strategicPrice;
//                    const percentageToBeatLowestPrice = (amountToBeatLowestPrice / myPrice) * 100;

//                    const amountMatchFormatted = amountToMatchLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
//                    const percentageMatchFormatted = '(-' + percentageToMatchLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
//                    const newSuggestedPriceMatch = lowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

//                    const amountBeatFormatted = amountToBeatLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
//                    const percentageBeatFormatted = '(-' + percentageToBeatLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
//                    const newSuggestedPriceBeat = strategicPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

//                    const matchPriceBox = document.createElement('div');
//                    matchPriceBox.className = 'price-box-column';
//                    const matchPriceLine = document.createElement('div');
//                    matchPriceLine.className = 'price-action-line';

//                    const downArrow = document.createElement('span');
//                    downArrow.className = 'arrow-down-red';
//                    const reduceText = document.createElement('span');
//                    reduceText.innerHTML = '-' + amountMatchFormatted + ' ' + percentageMatchFormatted;
//                    const newPriceText = document.createElement('div');
//                    newPriceText.innerHTML = '= ' + newSuggestedPriceMatch;
//                    const colorSquare = document.createElement('span');
//                    colorSquare.className = 'color-square-green';

//                    matchPriceLine.appendChild(downArrow);
//                    matchPriceLine.appendChild(reduceText);
//                    matchPriceLine.appendChild(newPriceText);
//                    matchPriceLine.appendChild(colorSquare);
//                    // Listener – przekazujemy lowestPrice
//                    attachPriceChangeListener(matchPriceLine, lowestPrice);
//                    matchPriceBox.appendChild(matchPriceLine);

//                    const strategicPriceBox = document.createElement('div');
//                    strategicPriceBox.className = 'price-box-column';
//                    const strategicPriceLine = document.createElement('div');
//                    strategicPriceLine.className = 'price-action-line';

//                    const downArrow2 = document.createElement('span');
//                    downArrow2.className = 'arrow-down-red';
//                    const reduceText2 = document.createElement('span');
//                    reduceText2.innerHTML = '-' + amountBeatFormatted + ' ' + percentageBeatFormatted;
//                    const newPriceText2 = document.createElement('div');
//                    newPriceText2.innerHTML = '= ' + newSuggestedPriceBeat;
//                    const colorSquare2 = document.createElement('span');
//                    colorSquare2.className = 'color-square-turquoise';

//                    strategicPriceLine.appendChild(downArrow2);
//                    strategicPriceLine.appendChild(reduceText2);
//                    strategicPriceLine.appendChild(newPriceText2);
//                    strategicPriceLine.appendChild(colorSquare2);
//                    // Listener – przekazujemy strategicPrice
//                    attachPriceChangeListener(strategicPriceLine, strategicPrice);
//                    strategicPriceBox.appendChild(strategicPriceLine);

//                    priceBoxColumnInfo.appendChild(matchPriceBox);
//                    priceBoxColumnInfo.appendChild(strategicPriceBox);
//                } else {
//                    const diffClass = item.colorClass + ' ' + 'priceBox-diff';
//                    priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Obniż: N/A</div>';
//                }
//            } else if (item.colorClass === "prGood") {
//                if (myPrice != null) {
//                    const amountToSuggestedPrice1 = 0;
//                    const percentageToSuggestedPrice1 = 0;
//                    const suggestedPrice1 = myPrice;

//                    const amount1Formatted = '+0,00 PLN';
//                    const percentage1Formatted = '(+0,00%)';
//                    const newSuggestedPrice1Formatted = '= ' + suggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

//                    let amountToSuggestedPrice2, percentageToSuggestedPrice2, suggestedPrice2;
//                    let amount2Formatted, percentage2Formatted, newSuggestedPrice2Formatted;
//                    let downArrowClass, colorSquare2Class;

//                    if (item.storeCount === 1) {
//                        amountToSuggestedPrice2 = 0;
//                        percentageToSuggestedPrice2 = 0;
//                        suggestedPrice2 = myPrice;

//                        amount2Formatted = '+0,00 PLN';
//                        percentage2Formatted = '(+0,00%)';
//                        newSuggestedPrice2Formatted = '= ' + suggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

//                        downArrowClass = 'no-change-icon-turquoise';
//                        colorSquare2Class = 'color-square-turquoise';
//                    } else {
//                        if (usePriceDifference) {
//                            amountToSuggestedPrice2 = -setStepPrice;
//                            suggestedPrice2 = myPrice + amountToSuggestedPrice2;
//                            percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;
//                        } else {
//                            const percentageReduction = setStepPrice / 100;
//                            amountToSuggestedPrice2 = -myPrice * percentageReduction;
//                            suggestedPrice2 = myPrice * (1 - percentageReduction);
//                            percentageToSuggestedPrice2 = -setStepPrice;
//                        }

//                        amount2Formatted = (amountToSuggestedPrice2 >= 0 ? '+' : '') + amountToSuggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
//                        percentage2Formatted = '(' + (percentageToSuggestedPrice2 >= 0 ? '+' : '') + percentageToSuggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
//                        newSuggestedPrice2Formatted = '= ' + suggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

//                        downArrowClass = 'arrow-down-green';
//                        colorSquare2Class = 'color-square-turquoise';
//                    }

//                    const matchPriceBox = document.createElement('div');
//                    matchPriceBox.className = 'price-box-column';
//                    const matchPriceLine = document.createElement('div');
//                    matchPriceLine.className = 'price-action-line';

//                    const noChangeIcon = document.createElement('span');
//                    noChangeIcon.className = 'no-change-icon';
//                    const noChangeText = document.createElement('span');
//                    noChangeText.innerHTML = amount1Formatted + ' ' + percentage1Formatted;
//                    const newPriceText = document.createElement('div');
//                    newPriceText.innerHTML = newSuggestedPrice1Formatted;
//                    const colorSquare = document.createElement('span');
//                    colorSquare.className = 'color-square-green';

//                    matchPriceLine.appendChild(noChangeIcon);
//                    matchPriceLine.appendChild(noChangeText);
//                    matchPriceLine.appendChild(newPriceText);
//                    matchPriceLine.appendChild(colorSquare);
//                    // Listener – przekazujemy suggestedPrice1
//                    attachPriceChangeListener(matchPriceLine, suggestedPrice1);
//                    matchPriceBox.appendChild(matchPriceLine);

//                    const strategicPriceBox = document.createElement('div');
//                    strategicPriceBox.className = 'price-box-column';
//                    const strategicPriceLine = document.createElement('div');
//                    strategicPriceLine.className = 'price-action-line';

//                    const downArrow = document.createElement('span');
//                    downArrow.className = downArrowClass;
//                    const reduceText = document.createElement('span');
//                    reduceText.innerHTML = amount2Formatted + ' ' + percentage2Formatted;
//                    const newPriceText2 = document.createElement('div');
//                    newPriceText2.innerHTML = newSuggestedPrice2Formatted;
//                    const colorSquare2 = document.createElement('span');
//                    colorSquare2.className = colorSquare2Class;

//                    strategicPriceLine.appendChild(downArrow);
//                    strategicPriceLine.appendChild(reduceText);
//                    strategicPriceLine.appendChild(newPriceText2);
//                    strategicPriceLine.appendChild(colorSquare2);
//                    // Listener – przekazujemy suggestedPrice2
//                    attachPriceChangeListener(strategicPriceLine, suggestedPrice2);
//                    strategicPriceBox.appendChild(strategicPriceLine);

//                    priceBoxColumnInfo.appendChild(matchPriceBox);
//                    priceBoxColumnInfo.appendChild(strategicPriceBox);
//                }
//            }
//        } else {
//            priceBoxColumnInfo.innerHTML += '<div class="rejected-product">Produkt odrzucony</div>';
//        }

//        priceBoxData.appendChild(colorBar);

//        if (item.imgUrl) {
//            const productImage = document.createElement('img');
//            productImage.dataset.src = item.imgUrl;
//            productImage.alt = item.productName;
//            productImage.className = 'lazy-load';
//            productImage.style.width = '110px';
//            productImage.style.height = '110px';
//            productImage.style.marginRight = '5px';
//            productImage.style.marginLeft = '5px';
//            productImage.style.backgroundColor = '#ffffff';
//            productImage.style.border = '1px solid #e3e3e3';
//            productImage.style.borderRadius = '4px';
//            productImage.style.padding = '10px';
//            productImage.style.display = 'block';

//            priceBoxData.appendChild(productImage);
//        }

//        priceBoxData.appendChild(priceBoxColumnLowestPrice);
//        priceBoxData.appendChild(priceBoxColumnMyPrice);
//        priceBoxData.appendChild(priceBoxColumnInfo);

//        box.appendChild(priceBoxSpace);
//        box.appendChild(priceBoxColumnCategory);
//        box.appendChild(externalInfoContainer);
//        box.appendChild(priceBoxData);

//        container.appendChild(box);
//    });

//    const visibleProducts = document.querySelectorAll('.price-box:not([style*="display: none"])');
//    document.getElementById('displayedProductCount').textContent = visibleProducts.length;

//    const allIndexes = Array.from(visibleProducts).map(product => parseInt(product.dataset.index));
//    const lazyLoadImages = document.querySelectorAll('.lazy-load');
//    const timers = new Map();

//    const observer = new IntersectionObserver((entries, observer) => {
//        entries.forEach(entry => {
//            const img = entry.target;
//            const index = [...lazyLoadImages].indexOf(img);
//            if (entry.isIntersecting) {
//                const timer = setTimeout(() => {
//                    loadImageWithNeighbors(index);
//                    observer.unobserve(img);
//                    timers.delete(img);
//                }, 100);
//                timers.set(img, timer);
//            } else {
//                if (timers.has(img)) {
//                    clearTimeout(timers.get(img));
//                    timers.delete(img);
//                }
//            }
//        });
//    }, {
//        root: null,
//        rootMargin: '50px',
//        threshold: 0.01
//    });

//    function loadImageWithNeighbors(index) {
//        const range = 6;
//        const start = Math.max(0, index - range);
//        const end = Math.min(lazyLoadImages.length - 1, index + range);
//        for (let i = start; i <= end; i++) {
//            const img = lazyLoadImages[i];
//            if (!img.src) {
//                img.src = img.dataset.src;
//                img.onload = () => { img.classList.add('loaded'); };
//            }
//        }
//    }

//    lazyLoadImages.forEach(img => { observer.observe(img); });
//}
