document.addEventListener("DOMContentLoaded", function () {
    let allPrices = [];
    let chartInstance = null;
    let myStoreName = "";
    let setPrice1 = 2.00;
    let setPrice2 = 2.00;
    let selectedProductId = null;
    let competitorStore = "";
    let selectedFlags = new Set();
    let sortingState = {
        sortName: null,
        sortPrice: null,
        sortRaiseAmount: null,
        sortRaisePercentage: null,
        sortLowerAmount: null,
        sortLowerPercentage: null,
        sortMarginAmount: null,
        sortMarginPercentage: null,
        showRejected: false // Added this line
    };

    let positionSlider;

    function debounce(func, wait) {
        let timeout;
        return function (...args) {
            const context = this;
            clearTimeout(timeout);
            timeout = setTimeout(() => func.apply(context, args), wait);
        };
    }

    positionSlider = document.getElementById('positionRangeSlider');
    var positionRangeInput = document.getElementById('positionRange');

    noUiSlider.create(positionSlider, {
        start: [1, 16], // Początkowy zakres (od 1 do 16)
        connect: true,
        range: {
            'min': 1,
            'max': 16
        },
        step: 1,
        format: wNumb({
            decimals: 0 // Bez miejsc po przecinku
        })  
    });


    // Aktualizacja wyświetlanego zakresu przy zmianie wartości suwaka
    positionSlider.noUiSlider.on('update', function (values, handle) {
        const displayValues = values.map(value => {
            return parseInt(value) === 16 ? 'Schowany' : 'Msc ' + value;
        });
        positionRangeInput.textContent = displayValues.join(' - ');
    });

    // Przefiltrowanie danych przy zmianie wartości suwaka
    positionSlider.noUiSlider.on('change', function () {
        // Wywołaj swoją funkcję filtrowania
        filterPricesAndUpdateUI();
    });

    // Przefiltruj dane, gdy wartość suwaka się zmienia
    positionSlider.noUiSlider.on('change', function () {
        filterPricesAndUpdateUI();
    });

 

    const updatePricesDebounced = debounce(function () {
        const usePriceDifference = document.getElementById('usePriceDifference').checked;

        allPrices.forEach(price => {
            price.valueToUse = usePriceDifference
                ? (price.savings !== null ? price.savings : price.priceDifference)
                : price.percentageDifference;
            price.colorClass = getColorClass(price.valueToUse, price.isUniqueBestPrice, price.isSharedBestPrice);
        });

        filterPricesAndUpdateUI();
    }, 300);

    function loadStores() {
        fetch(`/PriceHistory/GetStores?storeId=${storeId}`)
            .then(response => response.json())
            .then(stores => {
                const storeSelect = document.getElementById('competitorStoreSelect');
                stores.forEach(store => {
                    const option = document.createElement('option');
                    option.value = store;
                    option.text = store;
                    storeSelect.appendChild(option);
                });

                storeSelect.addEventListener('change', function () {
                    competitorStore = this.value;
                    loadPrices();
                });
            })
            .catch(error => console.error('Error fetching stores:', error));
    }

    const usePriceDifferenceCheckbox = document.getElementById('usePriceDifference');
    const unitLabel1 = document.getElementById('unitLabel1');
    const unitLabel2 = document.getElementById('unitLabel2');

    // Funkcja aktualizująca etykiety
    function updateUnits(usePriceDifference) {
        if (usePriceDifference) {
            unitLabel1.textContent = 'PLN';
            unitLabel2.textContent = 'PLN';
        } else {
            unitLabel1.textContent = '%';
            unitLabel2.textContent = '%';
        }
    }

    // Reaguj na kliknięcie checkboxa
    usePriceDifferenceCheckbox.addEventListener('change', function () {
        const usePriceDifference = usePriceDifferenceCheckbox.checked;
        updateUnits(usePriceDifference);
    });

    // Function to load price data from the server
    function loadPrices() {
        fetch(`/PriceHistory/GetPrices?storeId=${storeId}&competitorStore=${competitorStore}`)
            .then(response => response.json())
            .then(response => {
                myStoreName = response.myStoreName;
                setPrice1 = response.setPrice1;
                setPrice2 = response.setPrice2;
                missedProductsCount = response.missedProductsCount;

                // Get usePriceDiff value from server response
                const usePriceDifference = response.usePriceDiff;

                // Set the checkbox to the value from the server
                document.getElementById('usePriceDifference').checked = usePriceDifference;

                // Update labels
                updateUnits(usePriceDifference);

                // Process prices
                allPrices = response.prices.map(price => {
                    const isRejected = price.isRejected;

                    // Initialize variables
                    let valueToUse = null;
                    let colorClass = '';
                    let marginPrice = null;
                    let myPrice = null;
                    let marginAmount = null;
                    let marginPercentage = null;
                    let marginSign = '';
                    let marginClass = '';

                    if (!isRejected) {
                        // Product is not rejected, proceed as usual
                        if (usePriceDifference) {
                            valueToUse = price.savings !== null ? price.savings : price.priceDifference;
                        } else {
                            valueToUse = price.percentageDifference;
                        }

                        colorClass = getColorClass(valueToUse, price.isUniqueBestPrice, price.isSharedBestPrice);

                        // Ensure that marginPrice and myPrice are numbers
                        marginPrice = price.marginPrice != null && !isNaN(price.marginPrice) ? parseFloat(price.marginPrice) : null;
                        myPrice = price.myPrice != null && !isNaN(price.myPrice) ? parseFloat(price.myPrice) : null;

                        // Calculate margin if both prices are available
                        if (marginPrice != null && myPrice != null) {
                            marginAmount = myPrice - marginPrice;
                            if (marginPrice !== 0) {
                                marginPercentage = (marginAmount / marginPrice) * 100;
                            } else {
                                marginPercentage = null;
                            }

                            // Determine sign and class
                            marginSign = marginAmount >= 0 ? '+' : '-';
                            marginClass = marginAmount >= 0 ? 'priceBox-diff-margin' : 'priceBox-diff-margin-minus';
                        }
                    } else {
                        // Product is rejected, assign default values
                        colorClass = 'prRejected'; // Define a new class for rejected products if needed
                    }

                    return {
                        ...price,
                        isRejected: isRejected,
                        valueToUse: valueToUse,
                        colorClass: colorClass,
                        marginPrice: marginPrice,
                        myPrice: myPrice,
                        marginAmount: marginAmount,
                        marginPercentage: marginPercentage,
                        marginSign: marginSign,
                        marginClass: marginClass
                    };
                });

                document.getElementById('totalPriceCount').textContent = response.priceCount;
                document.getElementById('price1').value = setPrice1;
                document.getElementById('price2').value = setPrice2;
                document.getElementById('missedProductsCount').textContent = missedProductsCount;

                updateFlagCounts(allPrices);
                const currentSearchTerm = document.getElementById('productSearch').value;
                let filteredPrices = allPrices;

                if (currentSearchTerm) {
                    filteredPrices = filteredPrices.filter(price => {
                        const sanitizedInput = currentSearchTerm.replace(/[^a-zA-Z0-9\s.-]/g, '').trim();
                        const sanitizedInputLowerCase = sanitizedInput.toLowerCase().replace(/\s+/g, '');
                        const sanitizedProductName = price.productName.toLowerCase().replace(/[^a-zA-Z0-9\s.-]/g, '').replace(/\s+/g, '');
                        return sanitizedProductName.includes(sanitizedInputLowerCase);
                    });
                }

                filteredPrices = filterPricesByCategoryAndColorAndFlag(filteredPrices);

                renderPrices(filteredPrices);
                debouncedRenderChart(filteredPrices);
                updateColorCounts(filteredPrices);
                updateMarginSortButtonsVisibility();
            })
            .catch(error => console.error('Error fetching prices:', error));
    }


    function updateFlagCounts(prices) {
        const flagCounts = {};
        let noFlagCount = 0;

        prices.forEach(price => {
            if (price.flagIds.length === 0) {
                noFlagCount++;
            }
            price.flagIds.forEach(flagId => {
                if (!flagCounts[flagId]) {
                    flagCounts[flagId] = 0;
                }
                flagCounts[flagId]++;
            });
        });

        const flagContainer = document.getElementById('flagContainer');
        flagContainer.innerHTML = '';

        flags.forEach(flag => {
            const count = flagCounts[flag.FlagId] || 0;
            const flagElement = `
            <div class="form-check">
                <input class="form-check-input flagFilter" type="checkbox" id="flagCheckbox_${flag.FlagId}" value="${flag.FlagId}" ${selectedFlags.has(flag.FlagId.toString()) ? 'checked' : ''}>
                <label class="form-check-label" for="flagCheckbox_${flag.FlagId}">${flag.FlagName} (${count})</label>
            </div>
        `;
            flagContainer.innerHTML += flagElement;
        });

        const noFlagElement = `
        <div class="form-check">
            <input class="form-check-input flagFilter" type="checkbox" id="noFlagCheckbox" value="noFlag" ${selectedFlags.has('noFlag') ? 'checked' : ''}>
            <label class="form-check-label" for="noFlagCheckbox">Brak flagi (${noFlagCount})</label>
        </div>
        `;
        flagContainer.innerHTML += noFlagElement;

        document.querySelectorAll('.flagFilter').forEach(checkbox => {
            checkbox.addEventListener('change', function () {
                const flagValue = this.value;
                if (this.checked) {
                    selectedFlags.add(flagValue);
                } else {
                    selectedFlags.delete(flagValue);
                }
                filterPricesAndUpdateUI();
            });
        });
    }

    function filterPricesByCategoryAndColorAndFlag(data) {
        const selectedCategory = document.getElementById('category').value;
        const selectedColors = Array.from(document.querySelectorAll('.colorFilter:checked')).map(checkbox => checkbox.value);
        const selectedBid = document.getElementById('bidFilter').checked;
       /* const selectedPositions = Array.from(document.querySelectorAll('.positionFilter:checked')).map(checkbox => parseInt(checkbox.value));*/
        const selectedDeliveryMyStore = Array.from(document.querySelectorAll('.deliveryFilterMyStore:checked')).map(checkbox => parseInt(checkbox.value));
        const selectedDeliveryCompetitor = Array.from(document.querySelectorAll('.deliveryFilterCompetitor:checked')).map(checkbox => parseInt(checkbox.value));
        const selectedExternalPrice = Array.from(document.querySelectorAll('.externalPriceFilter:checked')).map(checkbox => checkbox.value);


        // Pobierz wartości z suwaka pozycji
        const positionSliderValues = positionSlider.noUiSlider.get();
        const positionMin = parseInt(positionSliderValues[0]);
        const positionMax = parseInt(positionSliderValues[1]);

  


        let filteredPrices = selectedCategory ? data.filter(item => item.category === selectedCategory) : data;

        filteredPrices = filteredPrices.filter(item => {
            const position = item.myPosition === null ? 16 : parseInt(item.myPosition);
            return position >= positionMin && position <= positionMax;
        });

        if (selectedColors.length) {
            filteredPrices = filteredPrices.filter(item => selectedColors.includes(item.colorClass));
        }

        if (selectedFlags.has("noFlag")) {
            filteredPrices = filteredPrices.filter(item => item.flagIds.length === 0);
        } else if (selectedFlags.size > 0) {
            filteredPrices = filteredPrices.filter(item => Array.from(selectedFlags).some(flag => item.flagIds.includes(parseInt(flag))));
        }

        if (selectedBid) {
            filteredPrices = filteredPrices.filter(item => item.myIsBidding === "1");
        }

       

        if (selectedDeliveryMyStore.length) {
            filteredPrices = filteredPrices.filter(item => selectedDeliveryMyStore.includes(item.myDelivery));
        }

        if (selectedDeliveryCompetitor.length) {
            filteredPrices = filteredPrices.filter(item => selectedDeliveryCompetitor.includes(item.delivery));
        }

        if (selectedExternalPrice.includes("yes")) {
            filteredPrices = filteredPrices.filter(item => item.externalPrice !== null);
        } else if (selectedExternalPrice.includes("no")) {
            filteredPrices = filteredPrices.filter(item => item.externalPrice === null);
        }

        return filteredPrices;
    }
    function getColorClass(valueToUse, isUniqueBestPrice = false, isSharedBestPrice = false) {
        if (isUniqueBestPrice && valueToUse <= setPrice1) {
            return "prIdeal";
        }
        if (isUniqueBestPrice) {
            return "prToLow";
        }
        if (isSharedBestPrice) {
            return "prGood";
        }
        if (valueToUse <= 0) {
            return "prGood";
        } else if (valueToUse < setPrice2) {
            return "prMid";
        } else {
            return "prToHigh";
        }
    }

    function highlightMatches(text, searchTerm) {
        if (!searchTerm) return text;

        // Sanityzacja wyszukiwanego terminu
        const sanitizedSearchTerm = searchTerm.replace(/[^a-zA-Z0-9\s/.-]/g, '').replace(/\s+/g, '');

        let searchIndex = 0;
        let result = '';

        for (let i = 0; i < text.length; i++) {
            // Ignorowanie białych znaków w wyszukiwanej frazie
            const currentChar = text[i].toLowerCase();

            if (sanitizedSearchTerm[searchIndex] === currentChar.replace(/\s+/g, '') ||
                sanitizedSearchTerm[searchIndex] === text[i].replace(/\s+/g, '')) {
                result += `<span style="color: #9400D3;font-weight: 600;">${text[i]}</span>`;
                searchIndex++;
            } else {
                result += text[i];
            }

            // Jeśli przeszliśmy przez cały wyszukiwany termin, nie musimy dalej podkreślać
            if (searchIndex >= sanitizedSearchTerm.length) {
                result += text.slice(i + 1);
                break;
            }
        }

        return result;
    }

    function renderPrices(data) {
        const container = document.getElementById('priceContainer');
        const currentSearchTerm = document.getElementById('productSearch').value.trim();
        container.innerHTML = '';

        data.forEach(item => {
            const isRejected = item.isRejected;

            const highlightedProductName = highlightMatches(item.productName, currentSearchTerm);
            const isBidding = item.isBidding === "1";
            const deliveryClass = getDeliveryClass(item.delivery);

            let percentageDifference = null;
            let priceDifference = null;
            let savings = null;
            let myIsBidding = null;
            let myDeliveryClass = null;
            let marginAmount = null;
            let marginPercentage = null;
            let marginSign = '';
            let marginClass = '';
            let marginPrice = null;
            let myPrice = null;
            let myPosition = null;

            if (!isRejected) {
                percentageDifference = item.percentageDifference != null ? item.percentageDifference.toFixed(2) : "N/A";
                priceDifference = item.priceDifference != null ? item.priceDifference.toFixed(2) : "N/A";
                savings = item.savings != null ? item.savings.toFixed(2) : "N/A";

                myIsBidding = item.myIsBidding === "1";
                myDeliveryClass = getDeliveryClass(item.myDelivery);
                marginAmount = item.marginAmount;
                marginPercentage = item.marginPercentage;
                marginSign = item.marginSign;
                marginClass = item.marginClass;
                marginPrice = item.marginPrice;
                myPrice = item.myPrice;
                myPosition = item.myPosition;
            }

            const box = document.createElement('div');
            box.className = 'price-box ' + item.colorClass;
            box.dataset.detailsUrl = '/PriceHistory/Details?scrapId=' + item.scrapId + '&productId=' + item.productId;
            box.dataset.productId = item.productId;

            box.addEventListener('click', function () {
                window.open(this.dataset.detailsUrl, '_blank');
            });

            const priceBoxSpace = document.createElement('div');
            priceBoxSpace.className = 'price-box-space';
            const priceBoxColumnName = document.createElement('div');
            priceBoxColumnName.className = 'price-box-column-name';
            priceBoxColumnName.innerHTML = highlightedProductName;

            const priceBoxColumnCategory = document.createElement('div');
            priceBoxColumnCategory.className = 'price-box-column-category';
            priceBoxColumnCategory.innerHTML = item.category;
            if (item.externalId) {
                const apiBox = document.createElement('span');
                apiBox.className = 'ApiBox';
                apiBox.innerHTML = 'API ID ' + item.externalId;
                priceBoxColumnCategory.appendChild(apiBox);
            }

            const assignFlagButton = document.createElement('button');
            assignFlagButton.className = 'assign-flag-button';
            assignFlagButton.dataset.productId = item.productId;
            assignFlagButton.innerHTML = '+ Przypisz flagi';
            assignFlagButton.style.pointerEvents = 'auto';

            assignFlagButton.addEventListener('click', function (event) {
                event.stopPropagation();
                selectedProductId = this.dataset.productId;
                modal.style.display = 'block';

                fetch(`/ProductFlags/GetFlagsForProduct?productId=${selectedProductId}`)
                    .then(response => response.json())
                    .then(flags => {
                        document.querySelectorAll('.flagCheckbox').forEach(checkbox => {
                            checkbox.checked = flags.includes(parseInt(checkbox.value));
                        });
                    })
                    .catch(error => console.error('Błąd pobierania flag dla produktu:', error));
            });

            priceBoxSpace.appendChild(priceBoxColumnName);
            priceBoxSpace.appendChild(assignFlagButton);

            const priceBoxData = document.createElement('div');
            priceBoxData.className = 'price-box-data';

            const colorBar = document.createElement('div');
            colorBar.className = 'color-bar ' + item.colorClass;

            const priceBoxColumnLowestPrice = document.createElement('div');
            priceBoxColumnLowestPrice.className = 'price-box-column';

            const priceBoxLowestText = document.createElement('div');
            priceBoxLowestText.className = 'price-box-column-text';
            priceBoxLowestText.innerHTML =
                '<span style="font-weight: 500;">' + item.lowestPrice.toFixed(2) + ' PLN</span>' + '<br>' + item.storeName;

            const priceBoxLowestDetails = document.createElement('div');
            priceBoxLowestDetails.className = 'price-box-column-text';
            priceBoxLowestDetails.innerHTML =
                (isBidding ? '<span class="Bidding">Bid</span>' : '') +
                (item.position !== null ? '<span class="Position">Msc ' + item.position + '</span>' :
                    '<span class="Position" style="background-color: #4B0089;">Schowany</span>') +
                (item.delivery != null ? '<span class="' + deliveryClass + '">Wysyłka w ' + (item.delivery == 1 ? '1 dzień' : item.delivery + ' dni') + '</span>' : '');

            priceBoxColumnLowestPrice.appendChild(priceBoxLowestText);
            priceBoxColumnLowestPrice.appendChild(priceBoxLowestDetails);

            const priceBoxColumnMyPrice = document.createElement('div');
            priceBoxColumnMyPrice.className = 'price-box-column';

            if (!isRejected && myPrice != null) {
                const priceBoxMyText = document.createElement('div');
                priceBoxMyText.className = 'price-box-column-text';

                // Sprawdź, czy istnieje externalPrice
                if (item.externalPrice !== null) {
                    // Oblicz różnicę między externalPrice a myPrice
                    const externalPriceDifference = (item.externalPrice - myPrice).toFixed(2);
                    const isPriceDecrease = item.externalPrice < myPrice;

                    // Przekreśl myPrice na górze
                    priceBoxMyText.innerHTML =
                        '<span style="font-weight: 500; text-decoration: line-through;">' + myPrice.toFixed(2) + ' PLN</span><br>';

                    // Dodaj nową cenę externalPrice poniżej
                    priceBoxMyText.innerHTML +=
                        '<span style="font-weight: 500;">' + item.externalPrice.toFixed(2) + ' PLN</span><br>';

                    // Dodaj różnicę między cenami ze strzałką
                    const arrow = '<span class="' + (isPriceDecrease ? 'arrow-down' : 'arrow-up') + '"></span>';
                    const differenceText = '<span style="font-weight: 500;">' + (isPriceDecrease ? '-' : '+') + Math.abs(externalPriceDifference) + ' PLN</span> ' + arrow + '<br>';
                    priceBoxMyText.innerHTML += differenceText;

                    // Dodaj nazwę sklepu na dole
                    priceBoxMyText.innerHTML += myStoreName;
                } else {
                    // Jeśli nie ma externalPrice, wyświetl normalnie myPrice
                    priceBoxMyText.innerHTML =
                        '<span style="font-weight: 500;">' + myPrice.toFixed(2) + ' PLN</span><br>' + myStoreName;
                }

                const priceBoxMyDetails = document.createElement('div');
                priceBoxMyDetails.className = 'price-box-column-text';
                priceBoxMyDetails.innerHTML =
                    (myIsBidding ? '<span class="Bidding">Bid</span>' : '') +
                    (myPosition !== null ? '<span class="Position">Msc ' + myPosition + '</span>' :
                        '<span class="Position" style="background-color: #4B0089;">Schowany</span>') +
                    (item.myDelivery != null ? '<span class="' + myDeliveryClass + '">Wysyłka w ' + (item.myDelivery == 1 ? '1 dzień' : item.myDelivery + ' dni') + '</span>' : '');

                priceBoxColumnMyPrice.appendChild(priceBoxMyText);
                priceBoxColumnMyPrice.appendChild(priceBoxMyDetails);
            } else {
                // Dla produktów odrzuconych lub gdy brak myPrice
                const priceBoxMyText = document.createElement('div');
                priceBoxMyText.className = 'price-box-column-text';
                priceBoxMyText.innerHTML = '<span style="font-weight: 500;">Brak ceny</span><br>' + myStoreName;

                priceBoxColumnMyPrice.appendChild(priceBoxMyText);
            }


            // Create priceBoxColumnInfo once
            const priceBoxColumnInfo = document.createElement('div');
            priceBoxColumnInfo.className = 'price-box-column-action';

            // Initialize innerHTML
            priceBoxColumnInfo.innerHTML = '';

            if (!isRejected) {
                // Add purchase price and margin information
                if (marginPrice != null) {
                    const formattedMarginPrice = marginPrice.toLocaleString('pl-PL', {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2
                    }) + ' PLN';

                    if (myPrice != null) {
                        const formattedMarginAmount = marginSign + Math.abs(marginAmount).toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + ' PLN';
                        const formattedMarginPercentage = '(' + marginSign + Math.abs(marginPercentage).toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + '%)';

                        priceBoxColumnInfo.innerHTML +=
                            '<div class="' + marginClass + '">' +
                            '<p>Cena zakupu: ' + formattedMarginPrice + '</p>' +
                            '<p>Marża: ' + formattedMarginAmount + ' ' + formattedMarginPercentage + '</p>' +
                            '</div>';
                    } else {
                        priceBoxColumnInfo.innerHTML +=
                            '<div class="' + marginClass + '">' +
                            '<p>Cena zakupu: ' + formattedMarginPrice + '</p>' +
                            '</div>';
                    }
                }

                if (item.colorClass === "prToLow" || item.colorClass === "prIdeal") {
                    const diffClass = item.colorClass + ' ' + 'priceBox-diff';
                    if (savings != null && percentageDifference != null) {
                        const savingsFormatted = (savings >= 0 ? '+' : '') + savings.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
                        const percentageFormatted = '(' + (percentageDifference >= 0 ? '+' : '') + percentageDifference.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
                        priceBoxColumnInfo.innerHTML +=
                            '<div class="' + diffClass + '">Podnieś: ' + savingsFormatted + ' ' + percentageFormatted + '</div>';
                    } else {
                        priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Podnieś: N/A</div>';
                    }
                } else if (item.colorClass === "prMid" || item.colorClass === "prToHigh") {
                    const diffClass = item.colorClass + ' ' + 'priceBox-diff';
                    if (priceDifference != null && percentageDifference != null) {
                        const priceDiffFormatted = (priceDifference >= 0 ? '-' : '') + Math.abs(priceDifference).toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
                        const percentageFormatted = '(' + (percentageDifference >= 0 ? '-' : '') + Math.abs(percentageDifference).toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
                        priceBoxColumnInfo.innerHTML +=
                            '<div class="' + diffClass + '">Obniż: ' + priceDiffFormatted + ' ' + percentageFormatted + '</div>';
                    } else {
                        priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Obniż: N/A</div>';
                    }
                } else if (item.colorClass === "prGood") {
                    const diffClass = item.colorClass + ' ' + 'priceBox-diff-top';
                    priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Jesteś w najlepszych cenach</div>';
                }
            } else {
                // For rejected products, display a note or different rendering
                priceBoxColumnInfo.innerHTML += '<div class="rejected-product">Produkt odrzucony</div>';
            }

            const priceBoxColumnExternalPrice = document.createElement('div');
            priceBoxColumnExternalPrice.className = 'price-box-column-api';
            if (!isRejected && item.externalPrice !== null) {
                const externalPriceDifference = (item.externalPrice - item.myPrice).toFixed(2);
                const externalPriceDifferenceText = (item.externalPrice > item.myPrice ? '+' : '') + externalPriceDifference;
                priceBoxColumnExternalPrice.innerHTML =
                    '<div class="price-box-column-text-api">Nowa cena: ' + item.externalPrice.toFixed(2) + ' PLN</div>' +
                    '<div class="price-box-column-text-api">Zmiana: ' + externalPriceDifferenceText + ' PLN</div>';
            }

            const flagsContainer = createFlagsContainer(item);

            priceBoxData.appendChild(colorBar);

            if (item.imgUrl) {
                const productImage = document.createElement('img');
                productImage.dataset.src = item.imgUrl; // Lazy loading
                productImage.alt = item.productName;
                productImage.className = 'lazy-load';

                // Styling placeholder
                productImage.style.width = '84px';
                productImage.style.height = '84px';
                productImage.style.marginRight = '14px';
                productImage.style.marginLeft = '16px';
                productImage.style.backgroundColor = '#ffffff';
                productImage.style.display = 'block';

                priceBoxData.appendChild(productImage);
            }

            priceBoxData.appendChild(priceBoxColumnLowestPrice);
            priceBoxData.appendChild(priceBoxColumnMyPrice);
            priceBoxData.appendChild(priceBoxColumnInfo);
            if (!isRejected && item.externalPrice !== null) {
                priceBoxData.appendChild(priceBoxColumnExternalPrice);
            }
            priceBoxData.appendChild(flagsContainer);

            box.appendChild(priceBoxSpace);
            box.appendChild(priceBoxColumnCategory);
            box.appendChild(priceBoxData);

            container.appendChild(box);
        });

        document.getElementById('displayedProductCount').textContent = data.length;

        // Lazy loading of images
        const lazyLoadImages = document.querySelectorAll('.lazy-load');
        const timers = new Map();

        const observer = new IntersectionObserver((entries, observer) => {
            entries.forEach(entry => {
                const img = entry.target;
                const index = [...lazyLoadImages].indexOf(img);

                if (entry.isIntersecting) {
                    const timer = setTimeout(() => {
                        loadImageWithNeighbors(index);
                        observer.unobserve(img);
                        timers.delete(img);
                    }, 100);
                    timers.set(img, timer);
                } else {
                    if (timers.has(img)) {
                        clearTimeout(timers.get(img));
                        timers.delete(img);
                    }
                }
            });
        }, {
            root: null,
            rootMargin: '50px',
            threshold: 0.01
        });

        function loadImageWithNeighbors(index) {
            const range = 6;
            const start = Math.max(0, index - range);
            const end = Math.min(lazyLoadImages.length - 1, index + range);

            for (let i = start; i <= end; i++) {
                const img = lazyLoadImages[i];
                if (!img.src) {
                    img.src = img.dataset.src;
                    img.onload = () => {
                        img.classList.add('loaded');
                    };
                }
            }
        }

        lazyLoadImages.forEach(img => {
            observer.observe(img);
        });
    }


    function getDeliveryClass(days) {
        if (days <= 1) return 'Availability1Day';
        if (days <= 3) return 'Availability3Days';
        if (days <= 7) return 'Availability7Days';
        return 'Availability14Days';
    }

    function hexToRgba(hex, alpha) {
        let r = 0, g = 0, b = 0;
        if (hex.length == 4) {
            r = parseInt(hex[1] + hex[1], 16);
            g = parseInt(hex[2] + hex[2], 16);
            b = parseInt(hex[3] + hex[3], 16);
        } else if (hex.length == 7) {
            r = parseInt(hex[1] + hex[2], 16);
            g = parseInt(hex[3] + hex[4], 16);
            b = parseInt(hex[5] + hex[6], 16);
        }
        return `rgba(${r}, ${g}, ${b}, ${alpha})`;
    }

    // Funkcja renderująca wykres
    function renderChart(data) {
        const colorCounts = {
            prGood: 0,
            prMid: 0,
            prToHigh: 0,
            prIdeal: 0,
            prToLow: 0
        };

        // Exclude rejected products from the chart data
        data.forEach(item => {
            if (!item.isRejected) {
                colorCounts[item.colorClass]++;
            }
        });

        const chartData = [colorCounts.prToHigh, colorCounts.prMid, colorCounts.prGood, colorCounts.prIdeal, colorCounts.prToLow];

        // Jeśli instancja wykresu już istnieje, aktualizuj ją
        if (chartInstance) {
            chartInstance.data.datasets[0].data = chartData; // Zaktualizuj dane
            chartInstance.update(); // Odśwież wykres
        } else {
            // Tworzymy nowy wykres tylko raz
            const ctx = document.getElementById('colorChart').getContext('2d');
            chartInstance = new Chart(ctx, {
                type: 'doughnut',
                data: {
                    labels: ['Zawyżona', 'Suboptymalna', 'Konkurencyjna', 'Strategiczna', 'Zaniżona'],
                    datasets: [{
                        data: chartData,
                        backgroundColor: [
                            'rgba(171, 37, 32, 0.8)',
                            'rgba(224, 168, 66, 0.8)',
                            'rgba(117, 152, 112, 0.8)',
                            'rgba(0, 145, 123, 0.8)',
                            'rgba(6, 6, 6, 0.8)'
                        ],
                        borderColor: [
                            'rgba(171, 37, 32, 1)',
                            'rgba(224, 168, 66, 1)',
                            'rgba(117, 152, 112, 1)',
                            'rgba(0, 145, 123, 1)',
                            'rgba(6, 6, 6, 1)'
                        ],
                        borderWidth: 1
                    }]
                },
                options: {
                    aspectRatio: 1,
                    plugins: {
                        legend: {
                            display: false,
                            position: 'right',
                            labels: {
                                usePointStyle: true,
                                padding: 16,
                                generateLabels: function (chart) {
                                    const original = Chart.overrides.doughnut.plugins.legend.labels.generateLabels;
                                    const labels = original.call(this, chart);
                                    labels.forEach(label => {
                                        label.text = '   ' + label.text;
                                    });
                                    return labels;
                                }
                            }
                        },
                        tooltip: {
                            callbacks: {
                                label: function (context) {
                                    var value = context.parsed;
                                    return 'Produkty: ' + value;
                                }
                            }
                        }
                    }
                }
            });
        }
    }

    const debouncedRenderChart = debounce(renderChart, 600);

    function updateColorCounts(data) {
        const colorCounts = {
            prToHigh: 0,
            prMid: 0,
            prGood: 0,
            prIdeal: 0,
            prToLow: 0
        };

        data.forEach(item => {
            colorCounts[item.colorClass]++;
        });

        document.querySelector('label[for="prToHighCheckbox"]').textContent = `Zawyżona (${colorCounts.prToHigh})`;
        document.querySelector('label[for="prMidCheckbox"]').textContent = `Suboptymalna (${colorCounts.prMid})`;
        document.querySelector('label[for="prGoodCheckbox"]').textContent = `Konkurencyjna (${colorCounts.prGood})`;
        document.querySelector('label[for="prIdealCheckbox"]').textContent = `Strategiczna (${colorCounts.prIdeal})`;
        document.querySelector('label[for="prToLowCheckbox"]').textContent = `Zaniżona (${colorCounts.prToLow})`;
    }

    // Modify the sorting functions to exclude rejected products from 'raise' and 'lower' sorts
    function filterPricesAndUpdateUI() {
        const currentSearchTerm = document.getElementById('productSearch').value.toLowerCase().replace(/\s+/g, '').trim();

        // Prepare sanitized search term
        const sanitizedSearchTerm = currentSearchTerm.replace(/[^a-zA-Z0-9\s/.-]/g, '').toLowerCase().replace(/\s+/g, '');

        let filteredPrices = allPrices.filter(price => {
            // Sanitize product name
            const sanitizedProductName = price.productName.toLowerCase().replace(/[^a-zA-Z0-9\s/.-]/g, '').replace(/\s+/g, '');
            return sanitizedProductName.includes(sanitizedSearchTerm);
        });

        // Sort results based on string match
        filteredPrices.sort((a, b) => {
            const sanitizedProductNameA = a.productName.toLowerCase().replace(/[^a-zA-Z0-9\s/.-]/g, '').replace(/\s+/g, '');
            const sanitizedProductNameB = b.productName.toLowerCase().replace(/[^a-zA-Z0-9\s/.-]/g, '').replace(/\s+/g, '');

            const exactMatchIndexA = getExactMatchIndex(sanitizedProductNameA, sanitizedSearchTerm);
            const exactMatchIndexB = getExactMatchIndex(sanitizedProductNameB, sanitizedSearchTerm);

            if (exactMatchIndexA !== exactMatchIndexB) {
                return exactMatchIndexA - exactMatchIndexB;
            }

            const matchLengthA = getLongestMatchLength(sanitizedProductNameA, sanitizedSearchTerm);
            const matchLengthB = getLongestMatchLength(sanitizedProductNameB, sanitizedSearchTerm);

            if (matchLengthA !== matchLengthB) {
                return matchLengthB - matchLengthA;
            }

            return a.productName.localeCompare(b.productName);
        });

        filteredPrices = filterPricesByCategoryAndColorAndFlag(filteredPrices);

        // Apply 'Show Rejected' filter
        if (sortingState.showRejected) {
            filteredPrices = filteredPrices.filter(item => item.isRejected);
        } else {
            filteredPrices = filteredPrices.filter(item => !item.isRejected);
        }

        // Apply sorting
        if (sortingState.sortName !== null) {
            if (sortingState.sortName === 'asc') {
                filteredPrices.sort((a, b) => a.productName.localeCompare(b.productName));
            } else {
                filteredPrices.sort((a, b) => b.productName.localeCompare(a.productName));
            }
        } else if (sortingState.sortPrice !== null) {
            if (sortingState.sortPrice === 'asc') {
                filteredPrices.sort((a, b) => a.lowestPrice - b.lowestPrice);
            } else {
                filteredPrices.sort((a, b) => b.lowestPrice - a.lowestPrice);
            }
        } else if (sortingState.sortRaiseAmount !== null) {
            filteredPrices = filteredPrices.filter(item => !item.isRejected && item.savings !== null);
            if (sortingState.sortRaiseAmount === 'asc') {
                filteredPrices.sort((a, b) => a.savings - b.savings);
            } else {
                filteredPrices.sort((a, b) => b.savings - a.savings);
            }
        } else if (sortingState.sortRaisePercentage !== null) {
            filteredPrices = filteredPrices.filter(item => !item.isRejected && item.savings !== null);
            if (sortingState.sortRaisePercentage === 'asc') {
                filteredPrices.sort((a, b) => a.percentageDifference - b.percentageDifference);
            } else {
                filteredPrices.sort((a, b) => b.percentageDifference - a.percentageDifference);
            }
        } else if (sortingState.sortLowerAmount !== null) {
            filteredPrices = filteredPrices.filter(item => !item.isRejected && item.savings === null && item.priceDifference !== 0);
            if (sortingState.sortLowerAmount === 'asc') {
                filteredPrices.sort((a, b) => a.priceDifference - b.priceDifference);
            } else {
                filteredPrices.sort((a, b) => b.priceDifference - a.priceDifference);
            }
        } else if (sortingState.sortLowerPercentage !== null) {
            filteredPrices = filteredPrices.filter(item => !item.isRejected && item.savings === null && item.priceDifference !== 0);
            if (sortingState.sortLowerPercentage === 'asc') {
                filteredPrices.sort((a, b) => a.percentageDifference - b.percentageDifference);
            } else {
                filteredPrices.sort((a, b) => b.percentageDifference - a.percentageDifference);
            }
        } else if (sortingState.sortMarginAmount !== null) {
            filteredPrices = filteredPrices.filter(item => item.marginAmount !== null);
            if (sortingState.sortMarginAmount === 'asc') {
                filteredPrices.sort((a, b) => a.marginAmount - b.marginAmount);
            } else {
                filteredPrices.sort((a, b) => b.marginAmount - a.marginAmount);
            }
        } else if (sortingState.sortMarginPercentage !== null) {
            filteredPrices = filteredPrices.filter(item => item.marginPercentage !== null);
            if (sortingState.sortMarginPercentage === 'asc') {
                filteredPrices.sort((a, b) => a.marginPercentage - b.marginPercentage);
            } else {
                filteredPrices.sort((a, b) => b.marginPercentage - a.marginPercentage);
            }
        }

        renderPrices(filteredPrices);
        debouncedRenderChart(filteredPrices);
        updateColorCounts(filteredPrices);
        updateFlagCounts(filteredPrices);
    }

    // Add event listener for the new 'Show Rejected' button
    document.getElementById('showRejectedButton').addEventListener('click', function () {
        sortingState.showRejected = !sortingState.showRejected;
        if (sortingState.showRejected) {
            this.classList.add('active');
        } else {
            this.classList.remove('active');
        }
        resetSortingStates('showRejected');
        filterPricesAndUpdateUI();
    });

    function resetSortingStates(except) {
        for (let key in sortingState) {
            if (key !== except) {
                if (key === 'showRejected') {
                    sortingState[key] = false;
                    let button = document.getElementById('showRejectedButton');
                    if (button) {
                        button.classList.remove('active');
                    }
                } else {
                    sortingState[key] = null;
                    let button = document.getElementById(key);
                    if (button) {
                        button.innerHTML = getDefaultButtonLabel(key);
                        button.classList.remove('active');
                    }
                }
            }
        }
    }

    function updateMarginSortButtonsVisibility() {
        const hasMarginData = allPrices.some(item => item.marginAmount !== null && item.marginPercentage !== null);
        const sortMarginAmountButton = document.getElementById('sortMarginAmount');
        const sortMarginPercentageButton = document.getElementById('sortMarginPercentage');
        if (hasMarginData) {
            sortMarginAmountButton.style.display = '';
            sortMarginPercentageButton.style.display = '';
        } else {
            sortMarginAmountButton.style.display = 'none';
            sortMarginPercentageButton.style.display = 'none';
        }
    }

    // Update getDefaultButtonLabel function to include 'showRejected'
    function getDefaultButtonLabel(key) {
        switch (key) {
            case 'sortName':
                return 'A-Z';
            case 'sortPrice':
                return 'Cena';
            case 'sortRaiseAmount':
                return 'Podnieś PLN';
            case 'sortRaisePercentage':
                return 'Podnieś %';
            case 'sortLowerAmount':
                return 'Obniż PLN';
            case 'sortLowerPercentage':
                return 'Obniż %';
            case 'sortMarginAmount':
                return 'Marża PLN';
            case 'sortMarginPercentage':
                return 'Marża %';
            case 'showRejected':
                return 'Pokaż Odrzucone';
            default:
                return '';
        }
    }

    function getExactMatchIndex(text, searchTerm) {
        return text.indexOf(searchTerm);
    }

    function getLongestMatchLength(text, searchTerm) {
        let maxLength = 0;

        for (let i = 0; i < text.length; i++) {
            for (let j = i; j <= text.length; j++) {
                const substring = text.slice(i, j);
                if (searchTerm.includes(substring) && substring.length > maxLength) {
                    maxLength = substring.length;
                }
            }
        }

        return maxLength;
    }

    const debouncedFilterPrices = debounce(function () {
        filterPricesAndUpdateUI();
    }, 300);

    document.getElementById('productSearch').addEventListener('input', debouncedFilterPrices);

    document.getElementById('category').addEventListener('change', function () {
        filterPricesAndUpdateUI();
    });

    document.querySelectorAll('.colorFilter, .flagFilter, .positionFilter, .deliveryFilterMyStore, .deliveryFilterCompetitor, .externalPriceFilter').forEach(function (checkbox) {
        checkbox.addEventListener('change', function () {
            filterPricesAndUpdateUI();
        });
    });

    document.getElementById('bidFilter').addEventListener('change', function () {
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortName').addEventListener('click', function () {
        if (sortingState.sortName === null) {
            sortingState.sortName = 'asc';
            this.innerHTML = 'A-Z ↑';
            this.classList.add('active');
        } else if (sortingState.sortName === 'asc') {
            sortingState.sortName = 'desc';
            this.innerHTML = 'A-Z ↓';
            this.classList.add('active');
        } else {
            sortingState.sortName = null;
            this.innerHTML = 'A-Z';
            this.classList.remove('active');
        }
        resetSortingStates('sortName');
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortPrice').addEventListener('click', function () {
        if (sortingState.sortPrice === null) {
            sortingState.sortPrice = 'asc';
            this.innerHTML = 'Cena ↑';
            this.classList.add('active');
        } else if (sortingState.sortPrice === 'asc') {
            sortingState.sortPrice = 'desc';
            this.innerHTML = 'Cena ↓';
            this.classList.add('active');
        } else {
            sortingState.sortPrice = null;
            this.innerHTML = 'Cena';
            this.classList.remove('active');
        }
        resetSortingStates('sortPrice');
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortRaiseAmount').addEventListener('click', function () {
        if (sortingState.sortRaiseAmount === null) {
            sortingState.sortRaiseAmount = 'asc';
            this.innerHTML = 'Podnieś PLN ↑';
            this.classList.add('active');
        } else if (sortingState.sortRaiseAmount === 'asc') {
            sortingState.sortRaiseAmount = 'desc';
            this.innerHTML = 'Podnieś PLN ↓';
            this.classList.add('active');
        } else {
            sortingState.sortRaiseAmount = null;
            this.innerHTML = 'Podnieś PLN';
            this.classList.remove('active');
        }
        resetSortingStates('sortRaiseAmount');
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortRaisePercentage').addEventListener('click', function () {
        if (sortingState.sortRaisePercentage === null) {
            sortingState.sortRaisePercentage = 'asc';
            this.innerHTML = 'Podnieś % ↑';
            this.classList.add('active');
        } else if (sortingState.sortRaisePercentage === 'asc') {
            sortingState.sortRaisePercentage = 'desc';
            this.innerHTML = 'Podnieś % ↓';
            this.classList.add('active');
        } else {
            sortingState.sortRaisePercentage = null;
            this.innerHTML = 'Podnieś %';
            this.classList.remove('active');
        }
        resetSortingStates('sortRaisePercentage');
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortLowerAmount').addEventListener('click', function () {
        if (sortingState.sortLowerAmount === null) {
            sortingState.sortLowerAmount = 'asc';
            this.innerHTML = 'Obniż PLN ↑';
            this.classList.add('active');
        } else if (sortingState.sortLowerAmount === 'asc') {
            sortingState.sortLowerAmount = 'desc';
            this.innerHTML = 'Obniż PLN ↓';
            this.classList.add('active');
        } else {
            sortingState.sortLowerAmount = null;
            this.innerHTML = 'Obniż PLN';
            this.classList.remove('active');
        }
        resetSortingStates('sortLowerAmount');
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortLowerPercentage').addEventListener('click', function () {
        if (sortingState.sortLowerPercentage === null) {
            sortingState.sortLowerPercentage = 'asc';
            this.innerHTML = 'Obniż % ↑';
            this.classList.add('active');
        } else if (sortingState.sortLowerPercentage === 'asc') {
            sortingState.sortLowerPercentage = 'desc';
            this.innerHTML = 'Obniż % ↓';
            this.classList.add('active');
        } else {
            sortingState.sortLowerPercentage = null;
            this.innerHTML = 'Obniż %';
            this.classList.remove('active');
        }
        resetSortingStates('sortLowerPercentage');
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortMarginAmount').addEventListener('click', function () {
        if (sortingState.sortMarginAmount === null) {
            sortingState.sortMarginAmount = 'asc';
            this.innerHTML = 'Marża PLN ↑';
            this.classList.add('active');
        } else if (sortingState.sortMarginAmount === 'asc') {
            sortingState.sortMarginAmount = 'desc';
            this.innerHTML = 'Marża PLN ↓';
            this.classList.add('active');
        } else {
            sortingState.sortMarginAmount = null;
            this.innerHTML = 'Marża PLN';
            this.classList.remove('active');
        }
        resetSortingStates('sortMarginAmount');
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortMarginPercentage').addEventListener('click', function () {
        if (sortingState.sortMarginPercentage === null) {
            sortingState.sortMarginPercentage = 'asc';
            this.innerHTML = 'Marża % ↑';
            this.classList.add('active');
        } else if (sortingState.sortMarginPercentage === 'asc') {
            sortingState.sortMarginPercentage = 'desc';
            this.innerHTML = 'Marża % ↓';
            this.classList.add('active');
        } else {
            sortingState.sortMarginPercentage = null;
            this.innerHTML = 'Marża %';
            this.classList.remove('active');
        }
        resetSortingStates('sortMarginPercentage');
        filterPricesAndUpdateUI();
    });

    document.getElementById('usePriceDifference').addEventListener('change', function () {
        const usePriceDifference = this.checked;

        allPrices.forEach(price => {
            price.valueToUse = usePriceDifference
                ? (price.savings !== null ? price.savings : price.priceDifference)
                : price.percentageDifference;
            price.colorClass = getColorClass(price.valueToUse, price.isUniqueBestPrice, price.isSharedBestPrice);
        });

        filterPricesAndUpdateUI();
    });

    // Aktualizacja przy zmianie price1
    document.getElementById('price1').addEventListener('input', function () {
        setPrice1 = parseFloat(this.value);
        updatePricesDebounced();
    });

    // Aktualizacja przy zmianie price2
    document.getElementById('price2').addEventListener('input', function () {
        setPrice2 = parseFloat(this.value);
        updatePricesDebounced();
    });

    document.getElementById('savePriceValues').addEventListener('click', function () {
        const price1 = parseFloat(document.getElementById('price1').value);
        const price2 = parseFloat(document.getElementById('price2').value);
        const usePriceDiff = document.getElementById('usePriceDifference').checked;

        const data = {
            StoreId: storeId,
            SetPrice1: price1,
            SetPrice2: price2,
            UsePriceDiff: usePriceDiff
        };

        fetch('/PriceHistory/SavePriceValues', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(data)
        })
            .then(response => response.json())
            .then(response => {
                if (response.success) {
                    setPrice1 = price1;
                    setPrice2 = price2;
                    usePriceDifferenceGlobal = usePriceDiff; // Zaktualizuj globalną zmienną, jeśli istnieje

                    loadPrices();
                } else {
                    alert('Error updating price values: ' + response.message);
                }
            })
            .catch(error => console.error('Błąd w aktualizowaniu wartości:', error));
    });

    const modal = document.getElementById('flagModal');
    const span = document.getElementsByClassName('close')[0];

    span.onclick = function () {
        modal.style.display = 'none';
    };

    window.onclick = function (event) {
        if (event.target == modal) {
            modal.style.display = 'none';
        }
    };

    document.getElementById('saveFlagsButton').addEventListener('click', function () {
        const selectedFlags = Array.from(document.querySelectorAll('.flagCheckbox:checked')).map(checkbox => parseInt(checkbox.value));
        const data = {
            productId: selectedProductId,
            flagIds: selectedFlags
        };

        fetch('/ProductFlags/AssignFlagsToProduct', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(data)
        })
            .then(response => response.json())
            .then(response => {
                if (response.success) {
                    modal.style.display = 'none';

                    // Fetch the updated flags for the product from the server
                    fetch(`/ProductFlags/GetFlagsForProduct?productId=${selectedProductId}`)
                        .then(res => res.json())
                        .then(updatedFlagIds => {
                            // Update the flagIds for the product in allPrices
                            const numericProductId = parseInt(selectedProductId);
                            const priceItem = allPrices.find(item => item.productId === numericProductId);
                            if (priceItem) {
                                priceItem.flagIds = updatedFlagIds;
                            } else {
                                console.error('Product not found in allPrices:', selectedProductId);
                            }

                            // Update the flags in the UI
                            updateProductFlagsInUI(selectedProductId);

                            // Optionally, update flag counts and filters
                            updateFlagCounts(allPrices);
                        })
                        .catch(error => console.error('Error fetching updated flags for product:', error));
                } else {
                    alert('Error assigning flags: ' + response.message);
                }
            })
            .catch(error => console.error('Error assigning flags:', error));
    });

    function updateProductFlagsInUI(productId) {
        // Convert productId to number
        const numericProductId = parseInt(productId);
        // Find the product's DOM element
        const productElement = document.querySelector(`.price-box[data-product-id='${productId}']`);

        if (productElement) {
            // Find the existing flags-container element
            const existingFlagsContainer = productElement.querySelector('.flags-container');

            // Create a new flags container
            const priceItem = allPrices.find(item => item.productId === numericProductId);

            if (priceItem) {
                const newFlagsContainer = createFlagsContainer(priceItem);

                // Replace the old flags container with the new one
                if (existingFlagsContainer) {
                    existingFlagsContainer.parentNode.replaceChild(newFlagsContainer, existingFlagsContainer);
                } else {
                    // If there was no existing flags container, append the new one
                    productElement.appendChild(newFlagsContainer);
                }
            } else {
                console.error('Product not found in allPrices:', productId);
            }
        }
    }

    function createFlagsContainer(item) {
        const flagsContainer = document.createElement('div');
        flagsContainer.className = 'flags-container';
        if (item.flagIds && item.flagIds.length > 0) {
            item.flagIds.forEach(function (flagId) {
                const flag = flags.find(function (f) { return f.FlagId === flagId; });
                if (flag) {
                    const flagSpan = document.createElement('span');
                    flagSpan.className = 'flag';
                    flagSpan.style.color = flag.FlagColor;
                    flagSpan.style.border = '2px solid ' + flag.FlagColor;
                    flagSpan.style.backgroundColor = hexToRgba(flag.FlagColor, 0.3);
                    flagSpan.innerHTML = flag.FlagName;
                    flagsContainer.appendChild(flagSpan);
                }
            });
        }
        return flagsContainer;
    }

    loadStores();
    loadPrices();
});