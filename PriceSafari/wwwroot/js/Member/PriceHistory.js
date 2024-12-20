document.addEventListener("DOMContentLoaded", function () {
    let allPrices = [];
    let chartInstance = null;
    let myStoreName = "";
    let setPrice1 = 2.00;
    let setPrice2 = 2.00;
    let setStepPrice = 2.00;
    let usePriceDifference = document.getElementById('usePriceDifference').checked;
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
        showRejected: null
    };

    let positionSlider;
    let offerSlider;

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
        start: [1, 60],
        connect: true,
        range: {
            'min': 1,
            'max': 60
        },
        step: 1,
        format: wNumb({
            decimals: 0
        })
    });

    function enforceLimits(input, min, max) {
        let value = parseFloat(input.value.replace(',', '.'));
        if (isNaN(value)) {
            return;
        }
        if (value < min) {
            input.value = min.toFixed(2);
        } else if (value > max) {
            input.value = max.toFixed(2);
        } else {
            input.value = value.toFixed(2);
        }
    }

    const price1Input = document.getElementById("price1");
    const price2Input = document.getElementById("price2");
    const stepPriceInput = document.getElementById("stepPrice");

    price1Input.addEventListener("blur", () => {
        enforceLimits(price1Input, 0.01, 100);
        if (parseFloat(stepPriceInput.value.replace(',', '.')) > parseFloat(price1Input.value.replace(',', '.'))) {
            stepPriceInput.value = price1Input.value;
        }
        enforceLimits(stepPriceInput, 0.01, parseFloat(price1Input.value.replace(',', '.')));
        updatePricesDebounced();
    });

    price2Input.addEventListener("blur", () => {
        enforceLimits(price2Input, 0.01, 100);
        updatePricesDebounced();
    });

    stepPriceInput.addEventListener("blur", () => {
        enforceLimits(stepPriceInput, 0.01, parseFloat(price1Input.value.replace(',', '.')));
        updatePricesDebounced();
    });

    positionSlider.noUiSlider.on('update', function (values, handle) {
        const displayValues = values.map(value => {
            return parseInt(value) === 60 ? 'Schowany' : 'Pozycja ' + value;
        });
        positionRangeInput.textContent = displayValues.join(' - ');
    });

    positionSlider.noUiSlider.on('change', function () {
        filterPricesAndUpdateUI();
    });

    positionSlider.noUiSlider.on('change', function () {
        filterPricesAndUpdateUI();
    });

    offerSlider = document.getElementById('offerRangeSlider');
    var offerRangeInput = document.getElementById('offerRange');

    noUiSlider.create(offerSlider, {
        start: [1, 1],
        connect: true,
        range: {
            'min': 1,
            'max': 1
        },
        step: 1,
        format: wNumb({
            decimals: 0
        })
    });

    offerSlider.noUiSlider.on('update', function (values, handle) {
        const displayValues = values.map(value => {
            const intValue = parseInt(value);
            let suffix = ' Ofert';
            if (intValue === 1) suffix = ' Oferta';
            else if (intValue >= 2 && intValue <= 4) suffix = ' Oferty';
            return intValue + suffix;
        });
        offerRangeInput.textContent = displayValues.join(' - ');
    });

    offerSlider.noUiSlider.on('change', function () {
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
    const unitLabelStepPrice = document.getElementById('unitLabelStepPrice');

    function updateUnits(usePriceDifference) {
        if (usePriceDifference) {
            unitLabel1.textContent = 'PLN';
            unitLabel2.textContent = 'PLN';
            unitLabelStepPrice.textContent = 'PLN';
        } else {
            unitLabel1.textContent = '%';
            unitLabel2.textContent = '%';
            unitLabelStepPrice.textContent = '%';
        }
    }

    usePriceDifferenceCheckbox.addEventListener('change', function () {
        usePriceDifference = this.checked;
        updateUnits(usePriceDifference);
        updatePricesDebounced();
    });

    function loadPrices() {
        const source = document.getElementById('sourceSelect').value;
        fetch(`/PriceHistory/GetPrices?storeId=${storeId}&competitorStore=${encodeURIComponent(competitorStore)}&source=${encodeURIComponent(source)}`)
            .then(response => response.json())
            .then(response => {
                myStoreName = response.myStoreName;
                setPrice1 = response.setPrice1;
                setPrice2 = response.setPrice2;
                setStepPrice = response.stepPrice;
                missedProductsCount = response.missedProductsCount;

                usePriceDifference = response.usePriceDiff;
                document.getElementById('usePriceDifference').checked = usePriceDifference;
                updateUnits(usePriceDifference);

                //allPrices = response.prices.map(price => {
                //    const isRejected = price.isRejected;

                //    let valueToUse = null;
                //    let colorClass = '';
                //    let marginPrice = null;
                //    let myPrice = null;
                //    let marginAmount = null;
                //    let marginPercentage = null;
                //    let marginSign = '';
                //    let marginClass = '';

                //    if (!isRejected) {
                //        if (usePriceDifference) {
                //            valueToUse = price.savings !== null ? price.savings : price.priceDifference;
                //        } else {
                //            valueToUse = price.percentageDifference;
                //        }

                //        colorClass = getColorClass(valueToUse, price.isUniqueBestPrice, price.isSharedBestPrice);

                //        marginPrice = price.marginPrice != null && !isNaN(price.marginPrice) ? parseFloat(price.marginPrice) : null;
                //        myPrice = price.myPrice != null && !isNaN(price.myPrice) ? parseFloat(price.myPrice) : null;

                //        if (marginPrice != null && myPrice != null) {
                //            marginAmount = myPrice - marginPrice;
                //            if (marginPrice !== 0) {
                //                marginPercentage = (marginAmount / marginPrice) * 100;
                //            } else {
                //                marginPercentage = null;
                //            }

                //            marginSign = marginAmount >= 0 ? '+' : '-';
                //            marginClass = marginAmount >= 0 ? 'priceBox-diff-margin' : 'priceBox-diff-margin-minus';
                //        }
                //    } else {
                //        colorClass = 'prRejected';
                //    }

                //    return {
                //        ...price,
                //        storeCount: price.storeCount,
                //        isRejected: price.isRejected || false,
                //        valueToUse: valueToUse,
                //        colorClass: colorClass,
                //        marginPrice: marginPrice,
                //        myPrice: myPrice,
                //        marginAmount: marginAmount,
                //        marginPercentage: marginPercentage,
                //        marginSign: marginSign,
                //        marginClass: marginClass
                //    };
                //});


                allPrices = response.prices.map(price => {
                    const isRejected = price.isRejected;
                    const onlyMe = price.onlyMe === true; // sprawdzamy onlyMe
                    let valueToUse = null;
                    let colorClass = '';

                    if (onlyMe) {
                        // Jeśli onlyMe = true, ustawiamy nową kategorię prOnlyMe
                        colorClass = 'prOnlyMe';
                        // Nie wykonujemy obliczeń, valueToUse = null
                    } else if (!isRejected) {
                        if (usePriceDifference) {
                            valueToUse = price.savings !== null ? price.savings : price.priceDifference;
                        } else {
                            valueToUse = price.percentageDifference;
                        }
                        colorClass = getColorClass(valueToUse, price.isUniqueBestPrice, price.isSharedBestPrice);
                    } else {
                        // isRejected
                        colorClass = 'prRejected';
                    }

                    // Reszta właściwości pozostaje bez zmian
                    const marginPrice = price.marginPrice != null && !isNaN(price.marginPrice) ? parseFloat(price.marginPrice) : null;
                    const myPrice = price.myPrice != null && !isNaN(price.myPrice) ? parseFloat(price.myPrice) : null;
                    let marginAmount = null;
                    let marginPercentage = null;
                    let marginSign = '';
                    let marginClass = '';

                    if (!isRejected && !onlyMe && marginPrice != null && myPrice != null) {
                        marginAmount = myPrice - marginPrice;
                        if (marginPrice !== 0) {
                            marginPercentage = (marginAmount / marginPrice) * 100;
                        } else {
                            marginPercentage = null;
                        }

                        marginSign = marginAmount >= 0 ? '+' : '-';
                        marginClass = marginAmount >= 0 ? 'priceBox-diff-margin' : 'priceBox-diff-margin-minus';
                    }

                    return {
                        ...price,
                        isRejected: price.isRejected || false,
                        onlyMe: onlyMe,
                        valueToUse: onlyMe ? null : valueToUse,
                        colorClass: colorClass,
                        marginPrice: marginPrice,
                        myPrice: myPrice,
                        marginAmount: marginAmount,
                        marginPercentage: marginPercentage,
                        marginSign: marginSign,
                        marginClass: marginClass
                    };
                });


                const storeCounts = allPrices.map(item => item.storeCount);
                const maxStoreCount = Math.max(...storeCounts);
                const offerSliderMax = Math.max(maxStoreCount, 1);

                offerSlider.noUiSlider.updateOptions({
                    range: {
                        'min': 1,
                        'max': offerSliderMax
                    }
                });

                offerSlider.noUiSlider.set([1, offerSliderMax]);

                document.getElementById('totalPriceCount').textContent = response.priceCount;
                document.getElementById('price1').value = setPrice1;
                document.getElementById('price2').value = setPrice2;
                document.getElementById('stepPrice').value = setStepPrice;
                document.getElementById('missedProductsCount').textContent = missedProductsCount;

                updateFlagCounts(allPrices);
                const currentSearchTerm = document.getElementById('productSearch').value;
                let filteredPrices = allPrices;

                if (currentSearchTerm) {
                    filteredPrices = filteredPrices.filter(price => {
                        const sanitizedInput = currentSearchTerm.replace(/[^a-zA-Z0-9\s.-]/g, '').trim();
                        const sanitizedInputLowerCase = sanitizedInput.toLowerCase().replace(/\s+/g, '');
                        const sanitizedProductName = (price.productName || '').toLowerCase().replace(/[^a-zA-Z0-9\s/.-]/g, '').replace(/\s+/g, '');

                        return sanitizedProductName.includes(sanitizedInputLowerCase);
                    });
                }

                filteredPrices = filterPricesByCategoryAndColorAndFlag(filteredPrices);

                renderPrices(filteredPrices);
                debouncedRenderChart(filteredPrices);
                updateColorCounts(filteredPrices);
                updateMarginSortButtonsVisibility();
            })
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
        const selectedColors = Array.from(document.querySelectorAll('.colorFilter:checked')).map(checkbox => checkbox.value);
        const selectedBid = document.getElementById('bidFilter').checked;

        const selectedDeliveryMyStore = Array.from(document.querySelectorAll('.deliveryFilterMyStore:checked')).map(checkbox => parseInt(checkbox.value));
        const selectedDeliveryCompetitor = Array.from(document.querySelectorAll('.deliveryFilterCompetitor:checked')).map(checkbox => parseInt(checkbox.value));
        const selectedExternalPrice = Array.from(document.querySelectorAll('.externalPriceFilter:checked')).map(checkbox => checkbox.value);

        const positionSliderValues = positionSlider.noUiSlider.get();
        const positionMin = parseInt(positionSliderValues[0]);
        const positionMax = parseInt(positionSliderValues[1]);

        const offerSliderValues = offerSlider.noUiSlider.get();
        const offerMin = parseInt(offerSliderValues[0]);
        const offerMax = parseInt(offerSliderValues[1]);

        let filteredPrices = data;

        filteredPrices = filteredPrices.filter(item => {
            const position = item.myPosition;
            if (position === null || position === undefined) {
                return true;
            }

            const numericPosition = parseInt(position);
            const isInRange = numericPosition >= positionMin && numericPosition <= positionMax;
            if (!isInRange) {
            }
            return isInRange;
        });

        filteredPrices = filteredPrices.filter(item => {
            const storeCount = item.storeCount;
            return storeCount >= offerMin && storeCount <= offerMax;
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

        const sanitizedSearchTerm = searchTerm.replace(/[^a-zA-Z0-9]/g, '').toLowerCase();
        const sanitizedText = text.replace(/[^a-zA-Z0-9]/g, '').toLowerCase();

        const index = sanitizedText.indexOf(sanitizedSearchTerm);

        if (index === -1) {
            return text;
        }

        let sanitizedIndexToOriginalIndex = [];
        let sanitizedIdx = 0;

        for (let i = 0; i < text.length; i++) {
            if (/[a-zA-Z0-9]/.test(text[i])) {
                sanitizedIndexToOriginalIndex[sanitizedIdx] = i;
                sanitizedIdx++;
            }
        }

        const matchStart = sanitizedIndexToOriginalIndex[index];
        const matchEnd = sanitizedIndexToOriginalIndex[index + sanitizedSearchTerm.length - 1];

        const beforeMatch = text.substring(0, matchStart);
        const matchText = text.substring(matchStart, matchEnd + 1);
        const afterMatch = text.substring(matchEnd + 1);

        return beforeMatch + `<span class="highlighted-text">` + matchText + `</span>` + afterMatch;
    }



    function renderPrices(data) {
        const container = document.getElementById('priceContainer');
        const currentProductSearchTerm = document.getElementById('productSearch').value.trim();
        const currentStoreSearchTerm = document.getElementById('storeSearch').value.trim();
        container.innerHTML = '';

        data.forEach(item => {
            const isRejected = item.isRejected;

            const highlightedProductName = highlightMatches(item.productName, currentProductSearchTerm);
            const highlightedStoreName = highlightMatches(item.storeName, currentStoreSearchTerm);
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
            let lowestPrice = null;

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
                myPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;
                myPosition = item.myPosition;
                lowestPrice = item.lowestPrice != null ? parseFloat(item.lowestPrice) : null;
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

            if (item.externalId) {
                const apiBox = document.createElement('span');
                apiBox.className = 'ApiBox';
                apiBox.innerHTML = 'API ID ' + item.externalId;
                priceBoxColumnCategory.appendChild(apiBox);
            }

            const flagsContainer = createFlagsContainer(item);

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
            priceBoxSpace.appendChild(flagsContainer);
            priceBoxSpace.appendChild(assignFlagButton);
            priceBoxSpace.appendChild(priceBoxColumnCategory);

            const externalInfoContainer = document.createElement('div');
            externalInfoContainer.className = 'price-box-externalInfo';

            const priceBoxColumnStoreCount = document.createElement('div');
            priceBoxColumnStoreCount.className = 'price-box-column-offers';
            priceBoxColumnStoreCount.innerHTML =
                (
                    (item.sourceGoogle || item.sourceCeneo) ?
                        '<span class="data-channel">' +
                        (item.sourceGoogle ? '<img src="/images/GoogleShopping.png" alt="Google Icon" style="width:15px; height:15px;" />' : '') +
                        (item.sourceCeneo ? '<img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:15px; height:15px;" />' : '') +
                        '</span>'
                        : ''
                ) +
                '<div class="offer-count-box">' + item.storeCount + ' Ofert</div>';

            externalInfoContainer.appendChild(priceBoxColumnStoreCount);

            if (marginPrice != null) {
                const formattedMarginPrice = marginPrice.toLocaleString('pl-PL', {
                    minimumFractionDigits: 2,
                    maximumFractionDigits: 2
                }) + ' PLN';

                const purchasePriceBox = document.createElement('div');
                purchasePriceBox.className = 'price-box-diff-margin ' + marginClass;
                purchasePriceBox.innerHTML = '<p>Cena zakupu: ' + formattedMarginPrice + '</p>';

                externalInfoContainer.appendChild(purchasePriceBox);

                if (myPrice != null) {
                    const formattedMarginAmount = marginSign + Math.abs(marginAmount).toLocaleString('pl-PL', {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2
                    }) + ' PLN';
                    const formattedMarginPercentage = '(' + marginSign + Math.abs(marginPercentage).toLocaleString('pl-PL', {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2
                    }) + '%)';

                    const marginBox = document.createElement('div');
                    marginBox.className = 'price-box-diff-margin ' + marginClass;
                    marginBox.innerHTML = '<p>Marża: ' + formattedMarginAmount + ' ' + formattedMarginPercentage + '</p>';

                    externalInfoContainer.appendChild(marginBox);
                }
            }

            priceBoxSpace.appendChild(externalInfoContainer);

            const priceBoxData = document.createElement('div');
            priceBoxData.className = 'price-box-data';

            const colorBar = document.createElement('div');
            colorBar.className = 'color-bar ' + item.colorClass;

            const priceBoxColumnLowestPrice = document.createElement('div');
            priceBoxColumnLowestPrice.className = 'price-box-column';

            const priceBoxLowestText = document.createElement('div');
            priceBoxLowestText.className = 'price-box-column-text';
            priceBoxLowestText.innerHTML =
                '<span style="font-weight: 500; font-size:17px;">' + item.lowestPrice.toFixed(2) + ' PLN</span>' + '<br>' + highlightedStoreName;

            const priceBoxLowestDetails = document.createElement('div');
            priceBoxLowestDetails.className = 'price-box-column-text';
            priceBoxLowestDetails.innerHTML =

                (item.isGoogle != null ?
                    '<span class="data-channel"><img src="' +
                    (item.isGoogle ? '/images/GoogleShopping.png' : '/images/Ceneo.png') +
                    '" alt="Channel Icon" style="width:20px; height:20px; margin-right:4px;" /></div>'
                    : '') +

                (item.position !== null ?
                    (item.isGoogle ?
                        '<span class="Position-Google">Poz. Google ' + item.position + '</span>' :
                        '<span class="Position">Poz. Ceneo ' + item.position + '</span>')
                    :
                    '<span class="Position" style="background-color: #414141;">Schowany</span>') +

                (isBidding ? '<span class="Bidding">Bid</span>' : '') +

                (item.delivery != null ? '<span class="' + deliveryClass + '">Wysyłka w ' + (item.delivery == 1 ? '1 dzień' : item.delivery + ' dni') + '</span>' : '');

            priceBoxColumnLowestPrice.appendChild(priceBoxLowestText);
            priceBoxColumnLowestPrice.appendChild(priceBoxLowestDetails);

            const priceBoxColumnMyPrice = document.createElement('div');
            priceBoxColumnMyPrice.className = 'price-box-column';

            if (!isRejected && myPrice != null) {
                const priceBoxMyText = document.createElement('div');
                priceBoxMyText.className = 'price-box-column-text';

                if (item.externalPrice !== null) {
                    const externalPriceDifference = (item.externalPrice - myPrice).toFixed(2);
                    const isPriceDecrease = item.externalPrice < myPrice;

                    const priceChangeContainer = document.createElement('div');
                    priceChangeContainer.style.display = 'flex';
                    priceChangeContainer.style.justifyContent = 'space-between';
                    priceChangeContainer.style.alignItems = 'center';

                    const storeName = document.createElement('span');
                    storeName.style.fontWeight = '500';
                    storeName.style.marginRight = '20px';
                    storeName.textContent = myStoreName;

                    const priceDifference = document.createElement('span');
                    priceDifference.style.fontWeight = '500';
                    const arrow = '<span class="' + (isPriceDecrease ? 'arrow-down' : 'arrow-up') + '"></span>';
                    priceDifference.innerHTML = arrow + (isPriceDecrease ? '-' : '+') + Math.abs(externalPriceDifference) + ' PLN ';

                    priceChangeContainer.appendChild(storeName);
                    priceChangeContainer.appendChild(priceDifference);

                    const priceContainer = document.createElement('div');
                    priceContainer.style.display = 'flex';
                    priceContainer.style.justifyContent = 'space-between';
                    priceContainer.style.alignItems = 'center';

                    const oldPrice = document.createElement('span');
                    oldPrice.style.fontWeight = '500';
                    oldPrice.style.textDecoration = 'line-through';
                    oldPrice.style.marginRight = '10px';
                    oldPrice.textContent = myPrice.toFixed(2) + ' PLN';

                    const newPrice = document.createElement('span');
                    newPrice.style.fontWeight = '500';
                    newPrice.textContent = item.externalPrice.toFixed(2) + ' PLN';

                    priceContainer.appendChild(oldPrice);
                    priceContainer.appendChild(newPrice);

                    priceBoxMyText.appendChild(priceContainer);
                    priceBoxMyText.appendChild(priceChangeContainer);
                } else {
                    priceBoxMyText.innerHTML =
                        '<span style="font-weight: 500; font-size:17px;">' + myPrice.toFixed(2) + ' PLN</span><br>' + myStoreName;
                }

                const priceBoxMyDetails = document.createElement('div');
                priceBoxMyDetails.className = 'price-box-column-text';
                priceBoxMyDetails.innerHTML =
                    (item.myIsGoogle != null ?
                        '<span class="data-channel"><img src="' +
                        (item.myIsGoogle ? '/images/GoogleShopping.png' : '/images/Ceneo.png') +
                        '" alt="Channel Icon" style="width:20px; height:20px; margin-right:4px;" /></div>'
                        : '') +

                    (myPosition !== null ?
                        (item.myIsGoogle ?
                            '<span class="Position-Google">Poz. Google ' + myPosition + '</span>' :
                            '<span class="Position">Poz. Ceneo ' + myPosition + '</span>')
                        :
                        '<span class="Position" style="background-color: #414141;">Schowany</span>') +

                    (myIsBidding ? '<span class="Bidding">Bid</span>' : '') +

                    (item.myDelivery != null ? '<span class="' + myDeliveryClass + '">Wysyłka w ' + (item.myDelivery == 1 ? '1 dzień' : item.myDelivery + ' dni') + '</span>' : '');

                priceBoxColumnMyPrice.appendChild(priceBoxMyText);
                priceBoxColumnMyPrice.appendChild(priceBoxMyDetails);
            } else {
                const priceBoxMyText = document.createElement('div');
                priceBoxMyText.className = 'price-box-column-text';
                priceBoxMyText.innerHTML = '<span style="font-weight: 500;">Brak ceny</span><br>' + myStoreName;

                priceBoxColumnMyPrice.appendChild(priceBoxMyText);
            }

            const priceBoxColumnInfo = document.createElement('div');
            priceBoxColumnInfo.className = 'price-box-column-action';

            priceBoxColumnInfo.innerHTML = '';

            // Dodajemy obsługę "onlyMe" jako osobny przypadek:
            if (!isRejected) {
                if (item.colorClass === "prOnlyMe") {
                    // OnlyMe: Jesteśmy jedynym sklepem, brak zmian cen
                    const diffClass = item.colorClass + ' ' + 'priceBox-diff-top';
                    priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Tylko nasza oferta, brak konkurencji</div>';

                } else if (item.colorClass === "prToLow" || item.colorClass === "prIdeal") {
                    if (myPrice != null && savings != null) {
                        const savingsValue = parseFloat(savings.replace(',', '.'));

                        const upArrowClass = item.colorClass === 'prToLow' ? 'arrow-up-black' : 'arrow-up-turquoise';

                        const suggestedPrice1 = myPrice + savingsValue;
                        const amountToSuggestedPrice1 = savingsValue;
                        const percentageToSuggestedPrice1 = (amountToSuggestedPrice1 / myPrice) * 100;

                        let suggestedPrice2, amountToSuggestedPrice2, percentageToSuggestedPrice2;
                        let arrowClass2 = upArrowClass;

                        if (usePriceDifference) {
                            if (savingsValue < 1) {
                                suggestedPrice2 = suggestedPrice1 - setStepPrice;
                                amountToSuggestedPrice2 = suggestedPrice2 - myPrice;
                                percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;

                                if (amountToSuggestedPrice2 < 0) {
                                    arrowClass2 = 'arrow-down-turquoise';
                                }
                            } else {
                                suggestedPrice2 = suggestedPrice1 - setStepPrice;
                                amountToSuggestedPrice2 = amountToSuggestedPrice1 - setStepPrice;
                                percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;

                                if (amountToSuggestedPrice2 < 0) {
                                    arrowClass2 = 'arrow-down-turquoise';
                                }
                            }
                        } else {
                            const percentageStep = setStepPrice / 100;

                            if (savingsValue < 1) {
                                suggestedPrice2 = suggestedPrice1 * (1 - percentageStep);
                                amountToSuggestedPrice2 = suggestedPrice2 - myPrice;
                                percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;

                                if (amountToSuggestedPrice2 < 0) {
                                    arrowClass2 = 'arrow-down-turquoise';
                                }
                            } else {
                                suggestedPrice2 = suggestedPrice1 * (1 - percentageStep);
                                amountToSuggestedPrice2 = amountToSuggestedPrice1 - (myPrice * percentageStep);
                                percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;

                                if (amountToSuggestedPrice2 < 0) {
                                    arrowClass2 = 'arrow-down-turquoise';
                                }
                            }
                        }

                        const amount1Formatted = (amountToSuggestedPrice1 >= 0 ? '+' : '') + amountToSuggestedPrice1.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + ' PLN';
                        const percentage1Formatted = '(' + (percentageToSuggestedPrice1 >= 0 ? '+' : '') + percentageToSuggestedPrice1.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + '%)';
                        const newSuggestedPrice1Formatted = suggestedPrice1.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + ' PLN';

                        const amount2Formatted = (amountToSuggestedPrice2 >= 0 ? '+' : '') + amountToSuggestedPrice2.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + ' PLN';
                        const percentage2Formatted = '(' + (percentageToSuggestedPrice2 >= 0 ? '+' : '') + percentageToSuggestedPrice2.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + '%)';
                        const newSuggestedPrice2Formatted = suggestedPrice2.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + ' PLN';

                        const matchPriceBox = document.createElement('div');
                        matchPriceBox.className = 'price-box-column';

                        const matchPriceLine = document.createElement('div');
                        matchPriceLine.className = 'price-action-line';

                        const upArrow = document.createElement('span');
                        upArrow.className = upArrowClass;

                        const increaseText = document.createElement('span');
                        increaseText.innerHTML = amount1Formatted + ' ' + percentage1Formatted;

                        const newPriceText = document.createElement('div');
                        newPriceText.innerHTML = '= ' + newSuggestedPrice1Formatted;

                        const colorSquare = document.createElement('span');
                        colorSquare.className = 'color-square-green';

                        matchPriceLine.appendChild(upArrow);
                        matchPriceLine.appendChild(increaseText);
                        matchPriceLine.appendChild(newPriceText);
                        matchPriceLine.appendChild(colorSquare);

                        matchPriceBox.appendChild(matchPriceLine);

                        const strategicPriceBox = document.createElement('div');
                        strategicPriceBox.className = 'price-box-column';

                        const strategicPriceLine = document.createElement('div');
                        strategicPriceLine.className = 'price-action-line';

                        const arrow2 = document.createElement('span');
                        arrow2.className = arrowClass2;

                        const increaseText2 = document.createElement('span');
                        increaseText2.innerHTML = amount2Formatted + ' ' + percentage2Formatted;

                        const newPriceText2 = document.createElement('div');
                        newPriceText2.innerHTML = '= ' + newSuggestedPrice2Formatted;

                        const colorSquare2 = document.createElement('span');
                        colorSquare2.className = 'color-square-turquoise';

                        strategicPriceLine.appendChild(arrow2);
                        strategicPriceLine.appendChild(increaseText2);
                        strategicPriceLine.appendChild(newPriceText2);
                        strategicPriceLine.appendChild(colorSquare2);

                        strategicPriceBox.appendChild(strategicPriceLine);

                        priceBoxColumnInfo.appendChild(matchPriceBox);
                        priceBoxColumnInfo.appendChild(strategicPriceBox);
                    } else {
                        const diffClass = item.colorClass + ' ' + 'priceBox-diff';
                        priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Podnieś: N/A</div>';
                    }
                } else if (item.colorClass === "prMid") {
                    if (myPrice != null && lowestPrice != null) {
                        const amountToMatchLowestPrice = myPrice - lowestPrice;
                        const percentageToMatchLowestPrice = (amountToMatchLowestPrice / myPrice) * 100;

                        let strategicPrice;
                        if (usePriceDifference) {
                            strategicPrice = lowestPrice - setStepPrice;
                        } else {
                            strategicPrice = lowestPrice * (1 - setStepPrice / 100);
                        }
                        const amountToBeatLowestPrice = myPrice - strategicPrice;
                        const percentageToBeatLowestPrice = (amountToBeatLowestPrice / myPrice) * 100;

                        const amountMatchFormatted = amountToMatchLowestPrice.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + ' PLN';
                        const percentageMatchFormatted = '(-' + percentageToMatchLowestPrice.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + '%)';
                        const newSuggestedPriceMatch = lowestPrice.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + ' PLN';

                        const amountBeatFormatted = amountToBeatLowestPrice.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + ' PLN';
                        const percentageBeatFormatted = '(-' + percentageToBeatLowestPrice.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + '%)';
                        const newSuggestedPriceBeat = strategicPrice.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + ' PLN';

                        const matchPriceBox = document.createElement('div');
                        matchPriceBox.className = 'price-box-column';

                        const matchPriceLine = document.createElement('div');
                        matchPriceLine.className = 'price-action-line';

                        const downArrow = document.createElement('span');
                        downArrow.className = 'arrow-down-yellow';

                        const reduceText = document.createElement('span');
                        reduceText.innerHTML = '-' + amountMatchFormatted + ' ' + percentageMatchFormatted;

                        const newPriceText = document.createElement('div');
                        newPriceText.innerHTML = '= ' + newSuggestedPriceMatch;

                        const colorSquare = document.createElement('span');
                        colorSquare.className = 'color-square-green';

                        matchPriceLine.appendChild(downArrow);
                        matchPriceLine.appendChild(reduceText);
                        matchPriceLine.appendChild(newPriceText);
                        matchPriceLine.appendChild(colorSquare);

                        matchPriceBox.appendChild(matchPriceLine);

                        const strategicPriceBox = document.createElement('div');
                        strategicPriceBox.className = 'price-box-column';

                        const strategicPriceLine = document.createElement('div');
                        strategicPriceLine.className = 'price-action-line';

                        const downArrow2 = document.createElement('span');
                        downArrow2.className = 'arrow-down-yellow';

                        const reduceText2 = document.createElement('span');
                        reduceText2.innerHTML = '-' + amountBeatFormatted + ' ' + percentageBeatFormatted;

                        const newPriceText2 = document.createElement('div');
                        newPriceText2.innerHTML = '= ' + newSuggestedPriceBeat;

                        const colorSquare2 = document.createElement('span');
                        colorSquare2.className = 'color-square-turquoise';

                        strategicPriceLine.appendChild(downArrow2);
                        strategicPriceLine.appendChild(reduceText2);
                        strategicPriceLine.appendChild(newPriceText2);
                        strategicPriceLine.appendChild(colorSquare2);

                        strategicPriceBox.appendChild(strategicPriceLine);

                        priceBoxColumnInfo.appendChild(matchPriceBox);
                        priceBoxColumnInfo.appendChild(strategicPriceBox);
                    } else {
                        const diffClass = item.colorClass + ' ' + 'priceBox-diff';
                        priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Obniż: N/A</div>';
                    }
                } else if (item.colorClass === "prToHigh") {
                    if (myPrice != null && lowestPrice != null) {
                        const amountToMatchLowestPrice = myPrice - lowestPrice;
                        const percentageToMatchLowestPrice = (amountToMatchLowestPrice / myPrice) * 100;

                        let strategicPrice;
                        if (usePriceDifference) {
                            strategicPrice = lowestPrice - setStepPrice;
                        } else {
                            strategicPrice = lowestPrice * (1 - setStepPrice / 100);
                        }
                        const amountToBeatLowestPrice = myPrice - strategicPrice;
                        const percentageToBeatLowestPrice = (amountToBeatLowestPrice / myPrice) * 100;

                        const amountMatchFormatted = amountToMatchLowestPrice.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + ' PLN';
                        const percentageMatchFormatted = '(-' + percentageToMatchLowestPrice.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + '%)';
                        const newSuggestedPriceMatch = lowestPrice.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + ' PLN';

                        const amountBeatFormatted = amountToBeatLowestPrice.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + ' PLN';
                        const percentageBeatFormatted = '(-' + percentageToBeatLowestPrice.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + '%)';
                        const newSuggestedPriceBeat = strategicPrice.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + ' PLN';

                        const matchPriceBox = document.createElement('div');
                        matchPriceBox.className = 'price-box-column';

                        const matchPriceLine = document.createElement('div');
                        matchPriceLine.className = 'price-action-line';

                        const downArrow = document.createElement('span');
                        downArrow.className = 'arrow-down-red';

                        const reduceText = document.createElement('span');
                        reduceText.innerHTML = '-' + amountMatchFormatted + ' ' + percentageMatchFormatted;

                        const newPriceText = document.createElement('div');
                        newPriceText.innerHTML = '= ' + newSuggestedPriceMatch;

                        const colorSquare = document.createElement('span');
                        colorSquare.className = 'color-square-green';

                        matchPriceLine.appendChild(downArrow);
                        matchPriceLine.appendChild(reduceText);
                        matchPriceLine.appendChild(newPriceText);
                        matchPriceLine.appendChild(colorSquare);

                        matchPriceBox.appendChild(matchPriceLine);

                        const strategicPriceBox = document.createElement('div');
                        strategicPriceBox.className = 'price-box-column';

                        const strategicPriceLine = document.createElement('div');
                        strategicPriceLine.className = 'price-action-line';

                        const downArrow2 = document.createElement('span');
                        downArrow2.className = 'arrow-down-red';

                        const reduceText2 = document.createElement('span');
                        reduceText2.innerHTML = '-' + amountBeatFormatted + ' ' + percentageBeatFormatted;

                        const newPriceText2 = document.createElement('div');
                        newPriceText2.innerHTML = '= ' + newSuggestedPriceBeat;

                        const colorSquare2 = document.createElement('span');
                        colorSquare2.className = 'color-square-turquoise';

                        strategicPriceLine.appendChild(downArrow2);
                        strategicPriceLine.appendChild(reduceText2);
                        strategicPriceLine.appendChild(newPriceText2);
                        strategicPriceLine.appendChild(colorSquare2);

                        strategicPriceBox.appendChild(strategicPriceLine);

                        priceBoxColumnInfo.appendChild(matchPriceBox);
                        priceBoxColumnInfo.appendChild(strategicPriceBox);
                    } else {
                        const diffClass = item.colorClass + ' ' + 'priceBox-diff';
                        priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Obniż: N/A</div>';
                    }
                } else if (item.colorClass === "prGood") {
                    if (myPrice != null) {
                        const amountToSuggestedPrice1 = 0;
                        const percentageToSuggestedPrice1 = 0;
                        const suggestedPrice1 = myPrice;

                        const amount1Formatted = '+0,00 PLN';
                        const percentage1Formatted = '(+0,00%)';
                        const newSuggestedPrice1Formatted = '= ' + suggestedPrice1.toLocaleString('pl-PL', {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2
                        }) + ' PLN';

                        let amountToSuggestedPrice2, percentageToSuggestedPrice2, suggestedPrice2;
                        let amount2Formatted, percentage2Formatted, newSuggestedPrice2Formatted;
                        let downArrowClass, colorSquare2Class;

                        if (item.storeCount === 1) {
                            amountToSuggestedPrice2 = 0;
                            percentageToSuggestedPrice2 = 0;
                            suggestedPrice2 = myPrice;

                            amount2Formatted = '+0,00 PLN';
                            percentage2Formatted = '(+0,00%)';
                            newSuggestedPrice2Formatted = '= ' + suggestedPrice2.toLocaleString('pl-PL', {
                                minimumFractionDigits: 2,
                                maximumFractionDigits: 2
                            }) + ' PLN';

                            downArrowClass = 'no-change-icon-turquoise';
                            colorSquare2Class = 'color-square-turquoise';
                        } else {
                            if (usePriceDifference) {
                                amountToSuggestedPrice2 = -setStepPrice;
                                suggestedPrice2 = myPrice + amountToSuggestedPrice2;
                                percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;
                            } else {
                                const percentageReduction = setStepPrice / 100;
                                amountToSuggestedPrice2 = -myPrice * percentageReduction;
                                suggestedPrice2 = myPrice * (1 - percentageReduction);
                                percentageToSuggestedPrice2 = -setStepPrice;
                            }

                            amount2Formatted = (amountToSuggestedPrice2 >= 0 ? '+' : '') + amountToSuggestedPrice2.toLocaleString('pl-PL', {
                                minimumFractionDigits: 2,
                                maximumFractionDigits: 2
                            }) + ' PLN';
                            percentage2Formatted = '(' + (percentageToSuggestedPrice2 >= 0 ? '+' : '') + percentageToSuggestedPrice2.toLocaleString('pl-PL', {
                                minimumFractionDigits: 2,
                                maximumFractionDigits: 2
                            }) + '%)';
                            newSuggestedPrice2Formatted = '= ' + suggestedPrice2.toLocaleString('pl-PL', {
                                minimumFractionDigits: 2,
                                maximumFractionDigits: 2
                            }) + ' PLN';

                            downArrowClass = 'arrow-down-green';
                            colorSquare2Class = 'color-square-turquoise';
                        }

                        const matchPriceBox = document.createElement('div');
                        matchPriceBox.className = 'price-box-column';

                        const matchPriceLine = document.createElement('div');
                        matchPriceLine.className = 'price-action-line';

                        const noChangeIcon = document.createElement('span');
                        noChangeIcon.className = 'no-change-icon';

                        const noChangeText = document.createElement('span');
                        noChangeText.innerHTML = amount1Formatted + ' ' + percentage1Formatted;

                        const newPriceText = document.createElement('div');
                        newPriceText.innerHTML = newSuggestedPrice1Formatted;

                        const colorSquare = document.createElement('span');
                        colorSquare.className = 'color-square-green';

                        matchPriceLine.appendChild(noChangeIcon);
                        matchPriceLine.appendChild(noChangeText);
                        matchPriceLine.appendChild(newPriceText);
                        matchPriceLine.appendChild(colorSquare);

                        matchPriceBox.appendChild(matchPriceLine);

                        const strategicPriceBox = document.createElement('div');
                        strategicPriceBox.className = 'price-box-column';

                        const strategicPriceLine = document.createElement('div');
                        strategicPriceLine.className = 'price-action-line';

                        const downArrow = document.createElement('span');
                        downArrow.className = downArrowClass;

                        const reduceText = document.createElement('span');
                        reduceText.innerHTML = amount2Formatted + ' ' + percentage2Formatted;

                        const newPriceText2 = document.createElement('div');
                        newPriceText2.innerHTML = newSuggestedPrice2Formatted;

                        const colorSquare2 = document.createElement('span');
                        colorSquare2.className = colorSquare2Class;

                        strategicPriceLine.appendChild(downArrow);
                        strategicPriceLine.appendChild(reduceText);
                        strategicPriceLine.appendChild(newPriceText2);
                        strategicPriceLine.appendChild(colorSquare2);

                        strategicPriceBox.appendChild(strategicPriceLine);

                        priceBoxColumnInfo.appendChild(matchPriceBox);
                        priceBoxColumnInfo.appendChild(strategicPriceBox);
                    } else {
                        const diffClass = item.colorClass + ' ' + 'priceBox-diff-top';
                        priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Jesteś w najlepszych cenach</div>';
                    }
                }
            } else {
                priceBoxColumnInfo.innerHTML += '<div class="rejected-product">Produkt odrzucony</div>';
            }

            priceBoxData.appendChild(colorBar);

            if (item.imgUrl) {
                const productImage = document.createElement('img');
                productImage.dataset.src = item.imgUrl;
                productImage.alt = item.productName;
                productImage.className = 'lazy-load';
                productImage.style.width = '110px';
                productImage.style.height = '110px';
                productImage.style.marginRight = '5px';
                productImage.style.marginLeft = '5px';
                productImage.style.backgroundColor = '#ffffff';
                productImage.style.border = '1px solid #e3e3e3';
                productImage.style.borderRadius = '4px';
                productImage.style.padding = '10px';
                productImage.style.display = 'block';

                priceBoxData.appendChild(productImage);
            }

            priceBoxData.appendChild(priceBoxColumnLowestPrice);
            priceBoxData.appendChild(priceBoxColumnMyPrice);
            priceBoxData.appendChild(priceBoxColumnInfo);

            box.appendChild(priceBoxSpace);
            box.appendChild(priceBoxColumnCategory);
            box.appendChild(externalInfoContainer);
            box.appendChild(priceBoxData);

            container.appendChild(box);
        });

        const visibleProducts = document.querySelectorAll('.price-box:not([style*="display: none"])');
        document.getElementById('displayedProductCount').textContent = visibleProducts.length;

        const allIndexes = Array.from(visibleProducts).map(product => parseInt(product.dataset.index));

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
    //        priceBoxLowestText.innerHTML =
    //            '<span style="font-weight: 500; font-size:17px;">' + item.lowestPrice.toFixed(2) + ' PLN</span>' + '<br>' + highlightedStoreName;

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
    //        if (!isRejected) {
    //            if (item.colorClass === "prToLow" || item.colorClass === "prIdeal") {
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

    //                            if (amountToSuggestedPrice2 < 0) {
    //                                arrowClass2 = 'arrow-down-turquoise';
    //                            }
    //                        } else {
    //                            suggestedPrice2 = suggestedPrice1 - setStepPrice;
    //                            amountToSuggestedPrice2 = amountToSuggestedPrice1 - setStepPrice;
    //                            percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;

    //                            if (amountToSuggestedPrice2 < 0) {
    //                                arrowClass2 = 'arrow-down-turquoise';
    //                            }
    //                        }
    //                    } else {
    //                        const percentageStep = setStepPrice / 100;

    //                        if (savingsValue < 1) {
    //                            suggestedPrice2 = suggestedPrice1 * (1 - percentageStep);
    //                            amountToSuggestedPrice2 = suggestedPrice2 - myPrice;
    //                            percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;

    //                            if (amountToSuggestedPrice2 < 0) {
    //                                arrowClass2 = 'arrow-down-turquoise';
    //                            }
    //                        } else {
    //                            suggestedPrice2 = suggestedPrice1 * (1 - percentageStep);
    //                            amountToSuggestedPrice2 = amountToSuggestedPrice1 - (myPrice * percentageStep);
    //                            percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;

    //                            if (amountToSuggestedPrice2 < 0) {
    //                                arrowClass2 = 'arrow-down-turquoise';
    //                            }
    //                        }
    //                    }

    //                    const amount1Formatted = (amountToSuggestedPrice1 >= 0 ? '+' : '') + amountToSuggestedPrice1.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + ' PLN';
    //                    const percentage1Formatted = '(' + (percentageToSuggestedPrice1 >= 0 ? '+' : '') + percentageToSuggestedPrice1.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + '%)';
    //                    const newSuggestedPrice1Formatted = suggestedPrice1.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + ' PLN';

    //                    const amount2Formatted = (amountToSuggestedPrice2 >= 0 ? '+' : '') + amountToSuggestedPrice2.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + ' PLN';
    //                    const percentage2Formatted = '(' + (percentageToSuggestedPrice2 >= 0 ? '+' : '') + percentageToSuggestedPrice2.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + '%)';
    //                    const newSuggestedPrice2Formatted = suggestedPrice2.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + ' PLN';

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

    //                    const amountMatchFormatted = amountToMatchLowestPrice.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + ' PLN';
    //                    const percentageMatchFormatted = '(-' + percentageToMatchLowestPrice.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + '%)';
    //                    const newSuggestedPriceMatch = lowestPrice.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + ' PLN';

    //                    const amountBeatFormatted = amountToBeatLowestPrice.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + ' PLN';
    //                    const percentageBeatFormatted = '(-' + percentageToBeatLowestPrice.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + '%)';
    //                    const newSuggestedPriceBeat = strategicPrice.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + ' PLN';

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

    //                    const amountMatchFormatted = amountToMatchLowestPrice.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + ' PLN';
    //                    const percentageMatchFormatted = '(-' + percentageToMatchLowestPrice.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + '%)';
    //                    const newSuggestedPriceMatch = lowestPrice.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + ' PLN';

    //                    const amountBeatFormatted = amountToBeatLowestPrice.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + ' PLN';
    //                    const percentageBeatFormatted = '(-' + percentageToBeatLowestPrice.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + '%)';
    //                    const newSuggestedPriceBeat = strategicPrice.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + ' PLN';

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
    //                    const newSuggestedPrice1Formatted = '= ' + suggestedPrice1.toLocaleString('pl-PL', {
    //                        minimumFractionDigits: 2,
    //                        maximumFractionDigits: 2
    //                    }) + ' PLN';

    //                    let amountToSuggestedPrice2, percentageToSuggestedPrice2, suggestedPrice2;
    //                    let amount2Formatted, percentage2Formatted, newSuggestedPrice2Formatted;
    //                    let downArrowClass, colorSquare2Class;

    //                    if (item.storeCount === 1) {
    //                        amountToSuggestedPrice2 = 0;
    //                        percentageToSuggestedPrice2 = 0;
    //                        suggestedPrice2 = myPrice;

    //                        amount2Formatted = '+0,00 PLN';
    //                        percentage2Formatted = '(+0,00%)';
    //                        newSuggestedPrice2Formatted = '= ' + suggestedPrice2.toLocaleString('pl-PL', {
    //                            minimumFractionDigits: 2,
    //                            maximumFractionDigits: 2
    //                        }) + ' PLN';

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

    //                        amount2Formatted = (amountToSuggestedPrice2 >= 0 ? '+' : '') + amountToSuggestedPrice2.toLocaleString('pl-PL', {
    //                            minimumFractionDigits: 2,
    //                            maximumFractionDigits: 2
    //                        }) + ' PLN';
    //                        percentage2Formatted = '(' + (percentageToSuggestedPrice2 >= 0 ? '+' : '') + percentageToSuggestedPrice2.toLocaleString('pl-PL', {
    //                            minimumFractionDigits: 2,
    //                            maximumFractionDigits: 2
    //                        }) + '%)';
    //                        newSuggestedPrice2Formatted = '= ' + suggestedPrice2.toLocaleString('pl-PL', {
    //                            minimumFractionDigits: 2,
    //                            maximumFractionDigits: 2
    //                        }) + ' PLN';

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

    //                    strategicPriceBox.appendChild(strategicPriceLine);

    //                    priceBoxColumnInfo.appendChild(matchPriceBox);
    //                    priceBoxColumnInfo.appendChild(strategicPriceBox);
    //                } else {
    //                    const diffClass = item.colorClass + ' ' + 'priceBox-diff-top';
    //                    priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Jesteś w najlepszych cenach</div>';
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
    //                img.onload = () => {
    //                    img.classList.add('loaded');
    //                };
    //            }
    //        }
    //    }

    //    lazyLoadImages.forEach(img => {
    //        observer.observe(img);
    //    });
    //}

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
    function renderChart(data) {
        const colorCounts = {
            prOnlyMe: 0,
            prToHigh: 0,
            prMid: 0,
            prGood: 0,
            prIdeal: 0,
            prToLow: 0
        };

        data.forEach(item => {
            if (!item.isRejected) {
                colorCounts[item.colorClass] = (colorCounts[item.colorClass] || 0) + 1;
            }
        });

        const chartData = [
            colorCounts.prOnlyMe,
            colorCounts.prToHigh,
            colorCounts.prMid,
            colorCounts.prGood,
            colorCounts.prIdeal,
            colorCounts.prToLow
        ];

        if (chartInstance) {
            chartInstance.data.datasets[0].data = chartData;
            chartInstance.update();
        } else {
            const ctx = document.getElementById('colorChart').getContext('2d');
            chartInstance = new Chart(ctx, {
                type: 'doughnut',
                data: {
                    labels: ['Tylko My', 'Zawyżona', 'Suboptymalna', 'Konkurencyjna', 'Strategiczna', 'Zaniżona'],
                    datasets: [{
                        data: chartData,
                        backgroundColor: [
                            'rgba(100, 100, 100, 0.8)', // kolor dla prOnlyMe
                            'rgba(171, 37, 32, 0.8)',
                            'rgba(224, 168, 66, 0.8)',
                            'rgba(117, 152, 112, 0.8)',
                            'rgba(0, 145, 123, 0.8)',
                            'rgba(6, 6, 6, 0.8)'
                        ],
                        borderColor: [
                            'rgba(100, 100, 100, 1)',
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
                    cutout: '60%',
                    plugins: {
                        legend: {
                            display: false
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
            prOnlyMe: 0,
            prToHigh: 0,
            prMid: 0,
            prGood: 0,
            prIdeal: 0,
            prToLow: 0
        };

        data.forEach(item => {
            colorCounts[item.colorClass] = (colorCounts[item.colorClass] || 0) + 1;
        });

        document.querySelector('label[for="prOnlyMeCheckbox"]').textContent = `Tylko My (${colorCounts.prOnlyMe})`;
        document.querySelector('label[for="prToHighCheckbox"]').textContent = `Zawyżona (${colorCounts.prToHigh})`;
        document.querySelector('label[for="prMidCheckbox"]').textContent = `Suboptymalna (${colorCounts.prMid})`;
        document.querySelector('label[for="prGoodCheckbox"]').textContent = `Konkurencyjna (${colorCounts.prGood})`;
        document.querySelector('label[for="prIdealCheckbox"]').textContent = `Strategiczna (${colorCounts.prIdeal})`;
        document.querySelector('label[for="prToLowCheckbox"]').textContent = `Zaniżona (${colorCounts.prToLow})`;
    }


    function filterPricesAndUpdateUI() {
        const currentProductSearchTerm = document.getElementById('productSearch').value.toLowerCase().replace(/\s+/g, '').trim();
        const currentStoreSearchTerm = document.getElementById('storeSearch').value.toLowerCase().replace(/\s+/g, '').trim();

        const sanitizedProductSearchTerm = currentProductSearchTerm.replace(/[^a-zA-Z0-9\s/.-]/g, '').toLowerCase().replace(/\s+/g, '');
        const sanitizedStoreSearchTerm = currentStoreSearchTerm.replace(/[^a-zA-Z0-9\s/.-]/g, '').toLowerCase().replace(/\s+/g, '');

        let filteredPrices = allPrices.filter(price => {
            const productName = (price.productName || '').toLowerCase().replace(/[^a-zA-Z0-9\s/.-]/g, '').replace(/\s+/g, '');
            const storeName = (price.storeName || '').toLowerCase().replace(/[^a-zA-Z0-9\s/.-]/g, '').replace(/\s+/g, '');

            const matchesProduct = productName.includes(sanitizedProductSearchTerm);
            const matchesStore = storeName.includes(sanitizedStoreSearchTerm);

            return (sanitizedProductSearchTerm === '' || matchesProduct) && (sanitizedStoreSearchTerm === '' || matchesStore);
        });


        filteredPrices.sort((a, b) => {
            const sanitizedProductNameA = a.productName.toLowerCase().replace(/[^a-zA-Z0-9\s/.-]/g, '').replace(/\s+/g, '');
            const sanitizedProductNameB = b.productName.toLowerCase().replace(/[^a-zA-Z0-9\s/.-]/g, '').replace(/\s+/g, '');

            const exactMatchIndexA = getExactMatchIndex(sanitizedProductNameA, sanitizedProductSearchTerm);
            const exactMatchIndexB = getExactMatchIndex(sanitizedProductNameB, sanitizedProductSearchTerm);

            if (exactMatchIndexA !== exactMatchIndexB) {
                return exactMatchIndexA - exactMatchIndexB;
            }

            const matchLengthA = getLongestMatchLength(sanitizedProductNameA, sanitizedProductSearchTerm);
            const matchLengthB = getLongestMatchLength(sanitizedProductNameB, sanitizedProductSearchTerm);

            if (matchLengthA !== matchLengthB) {
                return matchLengthB - matchLengthA;
            }

            return a.productName.localeCompare(b.productName);
        });

        filteredPrices = filterPricesByCategoryAndColorAndFlag(filteredPrices);

        if (sortingState.showRejected) {
            filteredPrices = filteredPrices.filter(item => item.isRejected);
        } else {
            filteredPrices = filteredPrices.filter(item => !item.isRejected);
        }

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

    document.getElementById('storeSearch').addEventListener('input', debouncedFilterPrices);

    document.getElementById('price1').addEventListener('input', function () {
        setPrice1 = parseFloat(this.value);
        updatePricesDebounced();
    });

    document.getElementById('price2').addEventListener('input', function () {
        setPrice2 = parseFloat(this.value);
        updatePricesDebounced();
    });
    document.getElementById('stepPrice').addEventListener('input', function () {
        setStepPrice = parseFloat(this.value);

        updatePricesDebounced();
    });

    document.getElementById('sourceSelect').addEventListener('change', function () {
        loadStores();
        loadPrices();
    });

    document.getElementById('savePriceValues').addEventListener('click', function () {
        const price1 = parseFloat(document.getElementById('price1').value);
        const price2 = parseFloat(document.getElementById('price2').value);
        const stepPrice = parseFloat(document.getElementById('stepPrice').value);
        const usePriceDiff = document.getElementById('usePriceDifference').checked;
        const data = {
            StoreId: storeId,
            SetPrice1: price1,
            SetPrice2: price2,
            PriceStep: stepPrice,
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
                    setStepPrice = stepPrice;
                    usePriceDifferenceGlobal = usePriceDiff;
                    loadPrices();
                } else {
                    alert('Błąd zapisu wartości: ' + response.message);
                }
            })
            .catch(error => console.error('Błąd zapisu wartości:', error));
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

                    fetch(`/ProductFlags/GetFlagsForProduct?productId=${selectedProductId}`)
                        .then(res => res.json())
                        .then(updatedFlagIds => {
                            const numericProductId = parseInt(selectedProductId);
                            const priceItem = allPrices.find(item => item.productId === numericProductId);
                            if (priceItem) {
                                priceItem.flagIds = updatedFlagIds;
                            } else {
                                console.error('Product not found in allPrices:', selectedProductId);
                            }

                            updateProductFlagsInUI(selectedProductId);

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
        const numericProductId = parseInt(productId);

        const productElement = document.querySelector(`.price-box[data-product-id='${productId}']`);

        if (productElement) {
            const existingFlagsContainer = productElement.querySelector('.flags-container');

            const priceItem = allPrices.find(item => item.productId === numericProductId);

            if (priceItem) {
                const newFlagsContainer = createFlagsContainer(priceItem);

                if (existingFlagsContainer) {
                    existingFlagsContainer.parentNode.replaceChild(newFlagsContainer, existingFlagsContainer);
                } else {
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