document.addEventListener("DOMContentLoaded", function () {

    const selectionStorageKey = `selectedProducts_${storeId}`;

    function saveSelectionToStorage(selectionSet) {
        localStorage.setItem(selectionStorageKey, JSON.stringify(Array.from(selectionSet)));
    }

    function loadSelectionFromStorage() {
        const storedSelection = localStorage.getItem(selectionStorageKey);
        if (storedSelection) {
            return new Set(JSON.parse(storedSelection));
        }
        return new Set();
    }

    function clearSelectionFromStorage() {
        localStorage.removeItem(selectionStorageKey);
    }
    let allPrices = [];
    let currentScrapId = null;
    let currentlyFilteredPrices = [];
    let chartInstance = null;
    let myStoreName = "";
    let setPrice1 = 2.00;
    let setPrice2 = 2.00;
    let setStepPrice = 2.00;
    let usePriceDifference = document.getElementById('usePriceDifference').checked;
    let marginSettings = {
        identifierForSimulation: 'EAN',
        useMarginForSimulation: true,
        usePriceWithDelivery: true,
        enforceMinimalMargin: true,
        minimalMarginPercent: 0.00
    };

    let selectedFlagsInclude = new Set();
    let selectedFlagsExclude = new Set();
    let selectedProductIds = loadSelectionFromStorage();
    let isBulkFlaggingMode = false;

    const selectAllVisibleBtn = document.getElementById('selectAllVisibleBtn');
    const deselectAllVisibleBtn = document.getElementById('deselectAllVisibleBtn');

    function updateVisibleProductSelectionButtons() {
        const visibleButtons = document.querySelectorAll('#priceContainer .select-product-btn');
        visibleButtons.forEach(btn => {
            const productId = btn.dataset.productId;
            if (selectedProductIds.has(productId)) {
                btn.textContent = 'Wybrano';
                btn.classList.add('selected');
            } else {
                btn.textContent = 'Zaznacz';
                btn.classList.remove('selected');
            }
        });
    }

    selectAllVisibleBtn.addEventListener('click', function () {
        if (currentlyFilteredPrices.length === 0) return;

        currentlyFilteredPrices.forEach(product => {
            selectedProductIds.add(product.productId.toString());
        });

        saveSelectionToStorage(selectedProductIds);
        updateSelectionUI();
        updateVisibleProductSelectionButtons();
    });

    deselectAllVisibleBtn.addEventListener('click', function () {
        if (currentlyFilteredPrices.length === 0) return;

        currentlyFilteredPrices.forEach(product => {
            selectedProductIds.delete(product.productId.toString());
        });

        saveSelectionToStorage(selectedProductIds);
        updateSelectionUI();
        updateVisibleProductSelectionButtons();
    });

    let sortingState = {
        sortName: null,
        sortPrice: null,
        sortRaiseAmount: null,
        sortRaisePercentage: null,
        sortLowerAmount: null,
        sortLowerPercentage: null,
        sortMarginAmount: null,
        sortMarginPercentage: null,

    };

    let positionSlider;
    let offerSlider;

    const openMassChangeModalBtn = document.getElementById('openMassChangeModalBtn');
    const massChangeModal = document.getElementById('massChangeModal');
    const closeMassChangeModalBtn = document.querySelector('#massChangeModal .close');

    openMassChangeModalBtn.addEventListener('click', function () {
        $('#massChangeModal').modal('show');
    });

    closeMassChangeModalBtn.addEventListener('click', function () {
        $('#massChangeModal').modal('hide');
    });

    window.addEventListener('click', function (event) {
        if (event.target === massChangeModal) {
            $('#massChangeModal').modal('hide');
        }
    });

    const massMatchBtn = document.getElementById('massMatchBtn');
    const massStrategicBtn = document.getElementById('massStrategicBtn');

    massMatchBtn.addEventListener('click', function () {
        applyMassChange('match');
        $('#massChangeModal').modal('hide');
    });

    massStrategicBtn.addEventListener('click', function () {
        applyMassChange('strategic');
        $('#massChangeModal').modal('hide');
    });

    if (typeof storeId !== 'undefined') {
        restoreSortingState();
    } else {
        console.warn("storeId nie jest zdefiniowane podczas próby odtworzenia stanu sortowania.");

    }
    function updateSortButtonVisuals() {
        Object.keys(sortingState).forEach(key => {
            const stateValue = sortingState[key];
            const button = document.getElementById(key);

            if (button && stateValue !== null && stateValue !== false) {
                button.classList.add('active');

                switch (key) {
                    case 'sortName':
                        button.innerHTML = stateValue === 'asc' ? 'A-Z ↑' : 'A-Z ↓';
                        break;
                    case 'sortPrice':
                        button.innerHTML = stateValue === 'asc' ? 'Cena ↑' : 'Cena ↓';
                        break;
                    case 'sortRaiseAmount':
                        button.innerHTML = stateValue === 'asc' ? 'Podnieś PLN ↑' : 'Podnieś PLN ↓';
                        break;
                    case 'sortRaisePercentage':
                        button.innerHTML = stateValue === 'asc' ? 'Podnieś % ↑' : 'Podnieś % ↓';
                        break;
                    case 'sortLowerAmount':
                        button.innerHTML = stateValue === 'asc' ? 'Obniż PLN ↑' : 'Obniż PLN ↓';
                        break;
                    case 'sortLowerPercentage':
                        button.innerHTML = stateValue === 'asc' ? 'Obniż % ↑' : 'Obniż % ↓';
                        break;
                    case 'sortMarginAmount':
                        button.innerHTML = stateValue === 'asc' ? 'Narzut PLN ↑' : 'Narzut PLN ↓';
                        break;
                    case 'sortMarginPercentage':
                        button.innerHTML = stateValue === 'asc' ? 'Narzut % ↑' : 'Narzut % ↓';
                        break;

                }
            } else if (button && key !== 'showRejected') {
                button.classList.remove('active');
                button.innerHTML = getDefaultButtonLabel(key);
            } else if (button && key === 'showRejected') {
                button.classList.remove('active');
            }
        });
    }

    function validateSimulationData() {
        const localStorageKey = 'selectedPriceChanges_' + storeId;
        const storedDataJSON = localStorage.getItem(localStorageKey);

        if (storedDataJSON) {
            try {
                const storedData = JSON.parse(storedDataJSON);

                if (!storedData.scrapId || storedData.scrapId !== currentScrapId) {
                    console.warn(`Wykryto przestarzałe dane symulacji. Zapisany scrapId: ${storedData.scrapId}, Obecny scrapId: ${currentScrapId}. Czyszczenie localStorage.`);
                    localStorage.removeItem(localStorageKey);

                    if (typeof updatePriceChangeSummary === 'function') {
                        updatePriceChangeSummary(true);
                    }
                }
            } catch (e) {

                console.error("Błąd parsowania danych z localStorage. Czyszczenie...", e);
                localStorage.removeItem(localStorageKey);
            }
        }
    }

    function restoreSortingState() {

        const storedStateJSON = localStorage.getItem('priceHistorySortingState_' + storeId);
        if (storedStateJSON) {
            try {
                const restoredState = JSON.parse(storedStateJSON);

                sortingState = { ...sortingState, ...restoredState };

                updateSortButtonVisuals();

            } catch (e) {
                console.error("Błąd parsowania stanu sortowania z localStorage:", e);

                localStorage.removeItem('priceHistorySortingState_' + storeId);
            }
        }
    }

    function applyMassChange(changeType) {

        const boxes = Array.from(document.querySelectorAll('#priceContainer .price-box'));

        boxes.forEach(box => {
            const buttons = box.querySelectorAll('.simulate-change-btn');
            if (!buttons || buttons.length === 0) return;

            let targetButton;
            if (changeType === 'match' && buttons[0]) {
                targetButton = buttons[0];
            } else if (changeType === 'strategic' && buttons[1]) {
                targetButton = buttons[1];
            }

            if (!targetButton) return;

            targetButton.click();
        });

        setTimeout(() => {

            const updatedBoxes = Array.from(document.querySelectorAll('#priceContainer .price-box'));
            let countAdded = 0;
            let countRejected = 0;
            updatedBoxes.forEach(box => {

                if (box.classList.contains('price-changed')) {
                    countAdded++;
                } else {
                    countRejected++;
                }
            });

            showGlobalUpdate(
                `<p style="margin-bottom:8px; font-size:16px; font-weight:bold;">Masowa zmiana zakończona!</p>
             <p>Dodano: <strong>${countAdded}</strong> SKU</p>
             <p>Odrzucono: <strong>${countRejected}</strong> SKU</p>`
            );
        }, 500);
    }

    let globalNotificationTimeoutId;
    let globalUpdateTimeoutId;

    function showGlobalNotification(message) {
        const notif = document.getElementById("globalNotification");
        const update = document.getElementById("globalUpdate");
        if (!notif) return;

        if (update && update.style.display === "block") {
            update.style.display = "none";
            if (globalUpdateTimeoutId) {
                clearTimeout(globalUpdateTimeoutId);
                globalUpdateTimeoutId = null;
            }
        }

        if (globalNotificationTimeoutId) {
            clearTimeout(globalNotificationTimeoutId);
        }

        notif.innerHTML = message;
        notif.style.display = "block";
        globalNotificationTimeoutId = setTimeout(() => {
            notif.style.display = "none";
        }, 4000);
    }

    function showGlobalUpdate(message) {
        const notif = document.getElementById("globalUpdate");
        const notification = document.getElementById("globalNotification");
        if (!notif) return;

        if (notification && notification.style.display === "block") {
            notification.style.display = "none";
            if (globalNotificationTimeoutId) {
                clearTimeout(globalNotificationTimeoutId);
                globalNotificationTimeoutId = null;
            }
        }

        if (globalUpdateTimeoutId) {
            clearTimeout(globalUpdateTimeoutId);
        }

        notif.innerHTML = message;
        notif.style.display = "block";
        globalUpdateTimeoutId = setTimeout(() => {
            notif.style.display = "none";
        }, 4000);
    }

    function exportToExcelXLSX(dataToExport) {
        if (!dataToExport || dataToExport.length === 0) {
            alert("Brak danych do wyeksportowania po zastosowaniu filtrów.");
            return;
        }

        const workbook = new ExcelJS.Workbook();
        const worksheet = workbook.addWorksheet("Dane");

        worksheet.addRow(["ID", "Nazwa Produktu", "EAN", "Ilość ofert", "Najniższa Konkurencyjna Cena", "Twoja Cena"]);

        const fontRed = { color: { argb: "FFAA0000" } };
        const fontGreen = { color: { argb: "FF006400" } };
        const fontGray = { color: { argb: "FF7E7E7E" } };

        dataToExport.forEach((item) => {
            const rowData = [
                item.externalId || "",
                item.productName || "",
                item.ean || "",
                item.storeCount || "",
                item.lowestPrice || "",
                item.myPrice || ""
            ];

            const row = worksheet.addRow(rowData);

            const lowestPriceCell = row.getCell(5);
            const myPriceCell = row.getCell(6);

            const lowest = parseFloat(item.lowestPrice) || 0;
            const mine = parseFloat(item.myPrice) || 0;

            if (lowest > 0 && mine > 0) {
                if (mine < lowest) {
                    lowestPriceCell.font = fontRed;
                    myPriceCell.font = fontGreen;
                } else if (mine > lowest) {
                    lowestPriceCell.font = fontGreen;
                    myPriceCell.font = fontRed;
                } else {
                    lowestPriceCell.font = fontGreen;
                    myPriceCell.font = fontGreen;
                }
            } else {
                lowestPriceCell.font = fontGray;
                myPriceCell.font = fontGray;
            }
        });

        workbook.xlsx.writeBuffer().then((buffer) => {
            const blob = new Blob([buffer], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" });
            const fileName = `PriceSafari-${scrapDateJS}-${myStoreNameJS}.xlsx`;

            const link = document.createElement("a");
            link.href = URL.createObjectURL(blob);
            link.download = fileName;
            link.click();

            setTimeout(() => {
                URL.revokeObjectURL(link.href);
            }, 1000);
        });
    }

    let currentPage = 1;
    const itemsPerPage = 1000;

    function resetPage() {
        currentPage = 1;
        window.scrollTo(0, 0);
    }

    function refreshPriceBoxStates() {
        const storedChangesJSON = localStorage.getItem('selectedPriceChanges_' + storeId);
        let activeChangesProductIds = new Set();

        if (storedChangesJSON) {
            try {
                const parsedChanges = JSON.parse(storedChangesJSON);
                parsedChanges.forEach(change => activeChangesProductIds.add(String(change.productId)));
            } catch (err) {
                console.error("Błąd parsowania selectedPriceChanges w refreshPriceBoxStates:", err);

            }
        }

        const allPriceBoxes = document.querySelectorAll('#priceContainer .price-box');

        allPriceBoxes.forEach(priceBox => {
            const productId = priceBox.dataset.productId;
            if (!productId) return;

            const isProductStillTrackedForChange = activeChangesProductIds.has(String(productId));

            if (priceBox.classList.contains('price-changed') && !isProductStillTrackedForChange) {

                priceBox.classList.remove('price-changed');

                const activeButtons = priceBox.querySelectorAll('.simulate-change-btn.active');
                activeButtons.forEach(button => {
                    button.classList.remove('active');
                    button.style.backgroundColor = "";
                    button.style.color = "";

                    while (button.firstChild) {
                        button.removeChild(button.firstChild);
                    }
                    button.textContent = button.dataset.originalText || "Zmień cenę";
                });
            } else if (!priceBox.classList.contains('price-changed') && isProductStillTrackedForChange) {

            }
        });

    }

    window.refreshPriceBoxStates = refreshPriceBoxStates;

    function renderPaginationControls(totalItems) {
        const totalPages = Math.ceil(totalItems / itemsPerPage);
        const paginationContainer = document.getElementById('paginationContainer');
        paginationContainer.innerHTML = '';

        const prevButton = document.createElement('button');
        prevButton.innerHTML = '<i class="fa fa-chevron-circle-left" aria-hidden="true"></i>';
        prevButton.disabled = currentPage === 1;
        prevButton.addEventListener('click', () => {
            if (currentPage > 1) {
                currentPage--;
                filterPricesAndUpdateUI(false);
            }
        });

        paginationContainer.appendChild(prevButton);

        for (let i = 1; i <= totalPages; i++) {
            const pageButton = document.createElement('button');
            pageButton.textContent = i;
            if (i === currentPage) {
                pageButton.classList.add('active');
            }
            pageButton.addEventListener('click', () => {
                currentPage = i;
                filterPricesAndUpdateUI(false);
            });
            paginationContainer.appendChild(pageButton);
        }

        const nextButton = document.createElement('button');
        nextButton.innerHTML = '<i class="fa fa-chevron-circle-right" aria-hidden="true"></i>';
        nextButton.disabled = currentPage === totalPages;
        nextButton.addEventListener('click', () => {
            if (currentPage < totalPages) {
                currentPage++;
                filterPricesAndUpdateUI(false);
            }
        });

        paginationContainer.appendChild(nextButton);
    }

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
        start: [1, 200],
        connect: true,
        range: {
            'min': 1,
            'max': 200
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
        enforceLimits(price1Input, 0.01);
        if (parseFloat(stepPriceInput.value.replace(',', '.')) > parseFloat(price1Input.value.replace(',', '.'))) {
            stepPriceInput.value = price1Input.value;
        }
        enforceLimits(stepPriceInput, 0.01, parseFloat(price1Input.value.replace(',', '.')));
    });

    price2Input.addEventListener("blur", () => {
        enforceLimits(price2Input, 0.01);
    });

    stepPriceInput.addEventListener("blur", () => {
        enforceLimits(stepPriceInput, 0.01, parseFloat(price1Input.value.replace(',', '.')));
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

    function convertPriceValue(price, usePriceDifference) {
        if (price.onlyMe) {

            return { valueToUse: null, colorClass: 'prOnlyMe' };
        } else {
            let valueForColorCalculation;
            let displayValueToUse;

            if (usePriceDifference) {
                valueForColorCalculation = (price.savings !== null ? price.savings : price.priceDifference);
                displayValueToUse = valueForColorCalculation;
            } else {

                displayValueToUse = price.percentageDifference;

                if (price.myPrice && price.lowestPrice && parseFloat(price.myPrice) > parseFloat(price.lowestPrice) && !price.isUniqueBestPrice && !price.isSharedBestPrice) {
                    valueForColorCalculation = ((parseFloat(price.myPrice) - parseFloat(price.lowestPrice)) / parseFloat(price.myPrice)) * 100;
                } else {

                    valueForColorCalculation = price.percentageDifference;
                }
            }

            if (valueForColorCalculation !== null && valueForColorCalculation !== undefined) {
                valueForColorCalculation = parseFloat(valueForColorCalculation.toFixed(2));
            }
            if (displayValueToUse !== null && displayValueToUse !== undefined) {
                displayValueToUse = parseFloat(displayValueToUse.toFixed(2));
            }

            const colorClass = getColorClass(valueForColorCalculation, price.isUniqueBestPrice, price.isSharedBestPrice);

            return { valueToUse: displayValueToUse, colorClass };
        }
    }

    const updatePricesDebounced = debounce(function () {
        const usePriceDifference = document.getElementById('usePriceDifference').checked;

        allPrices.forEach(price => {
            const result = convertPriceValue(price, usePriceDifference);
            price.valueToUse = result.valueToUse;
            price.colorClass = result.colorClass;
        });

        filterPricesAndUpdateUI();
    }, 300);

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
    });

    function populateProducerFilter() {
        const dropdown = document.getElementById('producerFilterDropdown');

        const counts = allPrices.reduce((map, item) => {
            if (item.producer) {
                map[item.producer] = (map[item.producer] || 0) + 1;
            }
            return map;
        }, {});

        const producers = Object.keys(counts).sort((a, b) => a.localeCompare(b));

        dropdown.innerHTML = '<option value="">Wszystkie marki</option>';

        producers.forEach(prod => {
            const opt = document.createElement('option');
            opt.value = prod;
            opt.textContent = `${prod} (${counts[prod]})`;
            dropdown.appendChild(opt);
        });

        dropdown.addEventListener('change', filterPricesAndUpdateUI);
    }

    function loadPrices() {
        showLoading();

        fetch(`/PriceHistory/GetPrices?storeId=${storeId}`)
            .then(response => response.json())
            .then(response => {
                currentScrapId = response.latestScrapId;
                validateSimulationData();
                myStoreName = response.myStoreName;
                setPrice1 = response.setPrice1;
                setPrice2 = response.setPrice2;
                setStepPrice = response.stepPrice;
                missedProductsCount = response.missedProductsCount;

                usePriceDifference = response.usePriceDiff;
                document.getElementById('usePriceDifference').checked = usePriceDifference;
                updateUnits(usePriceDifference);

                marginSettings.useMarginForSimulation = response.useMarginForSimulation;
                marginSettings.enforceMinimalMargin = response.enforceMinimalMargin;
                marginSettings.minimalMarginPercent = response.minimalMarginPercent;
                marginSettings.identifierForSimulation = response.identifierForSimulation;
                marginSettings.usePriceWithDelivery = response.usePriceWithDelivery;

                if (response.presetName) {
                    if (response.presetName === 'PriceSafari') {
                        document.getElementById('presetButton').textContent = 'Presety - Widok standardowy PriceSafari';
                    } else {
                        document.getElementById('presetButton').textContent = 'Presety - ' + response.presetName;
                    }
                }

                allPrices = response.prices.map(price => {

                    let colorClass = '';
                    let displayValueToUse = null;
                    let marginAmount = null;
                    let marginPercentage = null;
                    let marginSign = '';
                    let marginClass = '';

                    const onlyMe = price.onlyMe === true;
                    const myPrice = price.myPrice != null ? parseFloat(price.myPrice) : null;
                    const marginPrice = price.marginPrice != null ? parseFloat(price.marginPrice) : null;

                    if (price.isRejected) {
                        colorClass = 'prNoOffer';
                    } else if (onlyMe) {
                        colorClass = 'prOnlyMe';

                        if (marginPrice != null && myPrice != null) {
                            let priceForMarginCalculation = myPrice;
                            if (marginSettings.usePriceWithDelivery && price.myPriceIncludesDelivery) {
                                const myDeliveryCost = price.myPriceDeliveryCost != null && !isNaN(parseFloat(price.myPriceDeliveryCost)) ? parseFloat(price.myPriceDeliveryCost) : 0;
                                priceForMarginCalculation = myPrice - myDeliveryCost;
                            }
                            marginAmount = priceForMarginCalculation - marginPrice;
                            marginPercentage = (marginPrice !== 0) ? (marginAmount / marginPrice) * 100 : null;
                            marginClass = 'priceBox-diff-margin-neutral';
                        }
                    } else {

                        let valueToUseForColorCalculation;

                        if (usePriceDifference) {
                            valueToUseForColorCalculation = (price.savings !== null ? price.savings : price.priceDifference);
                            displayValueToUse = valueToUseForColorCalculation;
                        } else {
                            displayValueToUse = price.percentageDifference;
                            if (price.myPrice && price.lowestPrice && parseFloat(price.myPrice) > parseFloat(price.lowestPrice) && !price.isUniqueBestPrice && !price.isSharedBestPrice) {
                                valueToUseForColorCalculation = ((parseFloat(price.myPrice) - parseFloat(price.lowestPrice)) / parseFloat(price.myPrice)) * 100;
                            } else {
                                valueToUseForColorCalculation = price.percentageDifference;
                            }
                        }

                        if (valueToUseForColorCalculation != null) {
                            valueToUseForColorCalculation = parseFloat(valueToUseForColorCalculation.toFixed(2));
                        }
                        if (displayValueToUse != null) {
                            displayValueToUse = parseFloat(displayValueToUse.toFixed(2));
                        }

                        colorClass = getColorClass(valueToUseForColorCalculation, price.isUniqueBestPrice, price.isSharedBestPrice);

                        if (marginPrice != null && myPrice != null) {
                            let priceForMarginCalculation = myPrice;

                            if (marginSettings.usePriceWithDelivery && price.myPriceIncludesDelivery) {
                                const myDeliveryCost = price.myPriceDeliveryCost != null && !isNaN(parseFloat(price.myPriceDeliveryCost)) ? parseFloat(price.myPriceDeliveryCost) : 0;
                                priceForMarginCalculation = myPrice - myDeliveryCost;
                            }

                            marginAmount = priceForMarginCalculation - marginPrice;
                            marginPercentage = (marginPrice !== 0) ? (marginAmount / marginPrice) * 100 : null;
                            marginSign = marginAmount >= 0 ? '+' : '-';
                            marginClass = marginAmount >= 0 ? 'priceBox-diff-margin' : 'priceBox-diff-margin-minus';
                        }
                    }

                    return {
                        ...price,
                        isRejected: price.isRejected || false,
                        onlyMe: onlyMe,
                        valueToUse: displayValueToUse,
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

                const positions = allPrices
                    .map(item => item.myPosition)
                    .filter(p => p !== null && !isNaN(p));

                const maxPosition = positions.length > 0 ? Math.max(...positions) : 60;

                positionSlider.noUiSlider.updateOptions({
                    range: {
                        'min': 1,
                        'max': maxPosition
                    }
                });

                positionSlider.noUiSlider.set([1, maxPosition]);

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
                        const sanitizedInput = currentSearchTerm
                            .replace(/[^a-zA-Z0-9\s.-]/g, '')
                            .trim()
                            .toLowerCase()
                            .replace(/\s+/g, '');

                        let identifierValue = '';
                        switch (marginSettings.identifierForSimulation) {
                            case 'ID':
                                identifierValue = price.externalId ? price.externalId.toString() : '';
                                break;
                            case 'ProducerCode':
                                identifierValue = price.producerCode || '';
                                break;
                            case 'EAN':
                            default:
                                identifierValue = price.ean || '';
                                break;
                        }

                        const productNamePlusCode = (price.productName || '') + ' ' + identifierValue;

                        const combinedLower = productNamePlusCode
                            .toLowerCase()
                            .replace(/[^a-zA-Z0-9\s/.-]/g, '')
                            .replace(/\s+/g, '');

                        return combinedLower.includes(sanitizedInput);
                    });
                }

                filteredPrices = filterPricesByCategoryAndColorAndFlag(filteredPrices);

                debouncedRenderChart(filteredPrices);
                updateColorCounts(filteredPrices);
                updateMarginSortButtonsVisibility();
                populateProducerFilter();
                filterPricesAndUpdateUI();
            })

            .finally(() => {
                hideLoading();
            });

        window.loadPrices = loadPrices;

    }

    function updateFlagCounts(prices) {
        const flagCounts = {};
        let noFlagCount = 0;

        prices.forEach(price => {
            if (!price.flagIds || price.flagIds.length === 0) {
                noFlagCount++;
            } else {
                price.flagIds.forEach(flagId => {
                    flagCounts[flagId] = (flagCounts[flagId] || 0) + 1;
                });
            }
        });

        const flagContainer = document.getElementById('flagContainer');
        if (!flagContainer) return;
        flagContainer.innerHTML = '';

        if (typeof flags !== 'undefined' && flags) {
            flags.forEach(flag => {
                const count = flagCounts[flag.flagId] || 0;
                const includeChecked = selectedFlagsInclude.has(String(flag.flagId)) ? 'checked' : '';
                const excludeChecked = selectedFlagsExclude.has(String(flag.flagId)) ? 'checked' : '';

                const flagElementHTML = `
            <div class="flag-filter-group">
                <div class="form-check form-check-inline check-include" style="margin-right:0px;">
                    <input class="form-check-input flagFilterInclude" type="checkbox" id="flagCheckboxInclude_${flag.flagId}" value="${flag.flagId}" ${includeChecked} title="Pokaż wszystkie produkty z tą flagą">
                </div>
                <div class="form-check form-check-inline check-exclude" style="margin-right:0px; padding-left:16px;">
                    <input class="form-check-input flagFilterExclude" type="checkbox" id="flagCheckboxExclude_${flag.flagId}" value="${flag.flagId}" ${excludeChecked} title="Ukryj wszystkie produkty z tą flagą">
                </div>
                <span class="flag-name-count" style="font-size:14px; font-weight: 400;">
                    ${flag.flagName} (${count})
                </span>
            </div>`;
                flagContainer.insertAdjacentHTML('beforeend', flagElementHTML);
            });
        }

        const noFlagIncludeChecked = selectedFlagsInclude.has('noFlag') ? 'checked' : '';
        const noFlagExcludeChecked = selectedFlagsExclude.has('noFlag') ? 'checked' : '';

        const noFlagElementHTML = `
        <div class="flag-filter-group">
            <div class="form-check form-check-inline check-include" style="margin-right:0px;">
                <input class="form-check-input flagFilterInclude" type="checkbox" id="flagCheckboxInclude_noFlag" value="noFlag" ${noFlagIncludeChecked} title="Pokaż wszystkie produkty bez flagi">
            </div>
            <div class="form-check form-check-inline check-exclude" style="margin-right:0px; padding-left:16px;">
                <input class="form-check-input flagFilterExclude" type="checkbox" id="flagCheckboxExclude_noFlag" value="noFlag" ${noFlagExcludeChecked} title="Ukryj wszystkie produkty bez flagi">
            </div>
            <span class="flag-name-count" style="font-size:14px; font-weight: 400;">
                Brak flagi (${noFlagCount})
            </span>
        </div>`;
        flagContainer.insertAdjacentHTML('beforeend', noFlagElementHTML);

        document.querySelectorAll('.flagFilterInclude, .flagFilterExclude').forEach(checkbox => {
            const newCheckbox = checkbox.cloneNode(true);
            checkbox.parentNode.replaceChild(newCheckbox, checkbox);

            newCheckbox.addEventListener('change', function () {
                const flagValue = this.value;
                const isInclude = this.classList.contains('flagFilterInclude');

                if (isInclude) {
                    if (this.checked) {
                        const excludeCheckbox = document.getElementById(`flagCheckboxExclude_${flagValue}`);
                        if (excludeCheckbox) {
                            excludeCheckbox.checked = false;
                            selectedFlagsExclude.delete(flagValue);
                        }
                        selectedFlagsInclude.add(flagValue);
                    } else {
                        selectedFlagsInclude.delete(flagValue);
                    }
                } else {
                    if (this.checked) {
                        const includeCheckbox = document.getElementById(`flagCheckboxInclude_${flagValue}`);
                        if (includeCheckbox) {
                            includeCheckbox.checked = false;
                            selectedFlagsInclude.delete(flagValue);
                        }
                        selectedFlagsExclude.add(flagValue);
                    } else {
                        selectedFlagsExclude.delete(flagValue);
                    }
                }
                filterPricesAndUpdateUI();
            });
        });
    }

    function filterPricesByCategoryAndColorAndFlag(data) {
        const selectedColors = Array.from(document.querySelectorAll('.colorFilter:checked')).map(checkbox => checkbox.value);
        const selectedBid = document.getElementById('bidFilter').checked;
        const suspiciouslyLowFilter = document.getElementById('suspiciouslyLowFilter').checked;
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

        if (suspiciouslyLowFilter) {

            filteredPrices = filteredPrices.filter(item =>
                item.singleBestCheaperDiffPerc !== null &&
                item.singleBestCheaperDiffPerc > 25
            );

            filteredPrices.sort((a, b) => b.singleBestCheaperDiffPerc - a.singleBestCheaperDiffPerc);
        }

        if (selectedColors.length) {
            filteredPrices = filteredPrices.filter(item => selectedColors.includes(item.colorClass));
        }

        if (selectedFlagsExclude.size > 0) {
            filteredPrices = filteredPrices.filter(item => {

                if (selectedFlagsExclude.has('noFlag') && item.flagIds.length === 0) {
                    return false;
                }
                for (const excl of selectedFlagsExclude) {

                    if (excl !== 'noFlag' && item.flagIds.includes(parseInt(excl))) {
                        return false;
                    }
                }
                return true;
            });
        }

        if (selectedFlagsInclude.size > 0) {
            filteredPrices = filteredPrices.filter(item => {

                if (selectedFlagsInclude.has('noFlag') && item.flagIds.length === 0) {
                    return true;
                }

                return item.flagIds.some(fid => selectedFlagsInclude.has(fid.toString()));
            });
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
        if (isSharedBestPrice) {
            return "prGood";
        }
        if (isUniqueBestPrice) {
            return valueToUse <= setPrice1 ? "prIdeal" : "prToLow";
        }

        return valueToUse < setPrice2 ? "prMid" : "prToHigh";
    }

    function renderPrices(data) {

        const storedChanges = localStorage.getItem('selectedPriceChanges_' + storeId);
        if (storedChanges) {
            try {
                selectedPriceChanges = JSON.parse(storedChanges);
            } catch (err) {
                console.error("Błąd parsowania danych z localStorage:", err);
                selectedPriceChanges = [];
            }
        } else {
            selectedPriceChanges = [];
        }

        const container = document.getElementById('priceContainer');
        const currentProductSearchTerm = document.getElementById('productSearch').value.trim();
        const currentStoreSearchTerm = document.getElementById('storeSearch').value.trim();
        container.innerHTML = '';

        const startIndex = (currentPage - 1) * itemsPerPage;
        const endIndex = currentPage * itemsPerPage;
        const paginatedData = data.slice(startIndex, endIndex);

        function activateChangeButton(button, priceBox, newPrice) {
            button.classList.add('active');
            button.style.backgroundColor = "#333333";
            button.style.color = "#f5f5f5";
            button.textContent = "Dodano |";

            const removeLink = document.createElement('span');
            removeLink.innerHTML = " <i class='fa fa-trash' style='font-size:14px; display:flex; color:white; margin-left:4px; margin-top:3px;'></i>";

            removeLink.style.textDecoration = "none";
            removeLink.style.cursor = "pointer";
            removeLink.addEventListener('click', function (ev) {
                ev.stopPropagation();
                button.classList.remove('active');
                button.style.backgroundColor = "";
                button.style.color = "";
                button.textContent = "Zmień cenę";
                priceBox.classList.remove('price-changed');

                const productId = priceBox ? priceBox.dataset.productId : null;

                const removeEvent = new CustomEvent('priceBoxChangeRemove', {
                    detail: { productId }
                });
                document.dispatchEvent(removeEvent);
            });
            button.appendChild(removeLink);
            priceBox.classList.add('price-changed');
        }

        function attachPriceChangeListener(button, suggestedPrice, priceBox, productId, productName, currentPriceValue, item) {

            let requiredField = '';
            let requiredLabel = '';
            switch (marginSettings.identifierForSimulation) {
                case 'ID':
                    requiredField = item.externalId;
                    requiredLabel = "ID";
                    break;
                case 'ProducerCode':
                    requiredField = item.producerCode;
                    requiredLabel = "Kod producenta";
                    break;
                case 'EAN':
                default:
                    requiredField = item.ean;
                    requiredLabel = "EAN";
                    break;
            }

            if (!requiredField || requiredField.toString().trim() === "") {

                button.disabled = true;
                button.title = `Produkt musi mieć zmapowany ${requiredLabel}`;
                return;
            }

            button.addEventListener('click', function (e) {
                e.stopPropagation();

                if (marginSettings.useMarginForSimulation) {
                    if (item.marginPrice == null) {
                        showGlobalNotification(
                            `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny nie została dodana</p>
                         <p>Symulacja cenowa z narzutem jest włączona – produkt musi posiadać cenę zakupu.</p>`
                        );
                        return;
                    }

                    let oldPriceForMarginCalculation = currentPriceValue;
                    if (marginSettings.usePriceWithDelivery && item.myPriceIncludesDelivery) {
                        const oldDeliveryCost = item.myPriceDeliveryCost != null && !isNaN(parseFloat(item.myPriceDeliveryCost)) ? parseFloat(item.myPriceDeliveryCost) : 0;
                        oldPriceForMarginCalculation = currentPriceValue - oldDeliveryCost;
                    }

                    let newPriceForMarginCalculation = suggestedPrice;

                    if (marginSettings.usePriceWithDelivery && item.myPriceIncludesDelivery) {

                        const myDeliveryCost = item.myPriceDeliveryCost != null && !isNaN(parseFloat(item.myPriceDeliveryCost)) ? parseFloat(item.myPriceDeliveryCost) : 0;
                        newPriceForMarginCalculation = suggestedPrice - myDeliveryCost;
                    }

                    let oldMargin = ((oldPriceForMarginCalculation - item.marginPrice) / item.marginPrice) * 100;
                    let newMargin = ((newPriceForMarginCalculation - item.marginPrice) / item.marginPrice) * 100;
                    oldMargin = parseFloat(oldMargin.toFixed(2));
                    newMargin = parseFloat(newMargin.toFixed(2));

                    if (marginSettings.useMarginForSimulation) {
                        if (item.marginPrice == null) {
                            showGlobalNotification(
                                `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny nie została dodana</p>
                         <p>Symulacja cenowa z narzutem jest włączona – produkt musi posiadać cenę zakupu.</p>`
                            );
                            return;
                        }

                        let oldPriceForMarginCalculation = currentPriceValue;
                        if (marginSettings.usePriceWithDelivery && item.myPriceIncludesDelivery) {
                            const oldDeliveryCost = item.myPriceDeliveryCost != null && !isNaN(parseFloat(item.myPriceDeliveryCost)) ? parseFloat(item.myPriceDeliveryCost) : 0;
                            oldPriceForMarginCalculation = currentPriceValue - oldDeliveryCost;
                        }

                        let newPriceForMarginCalculation = suggestedPrice;

                        if (marginSettings.usePriceWithDelivery && item.myPriceIncludesDelivery) {
                            const myDeliveryCost = item.myPriceDeliveryCost != null && !isNaN(parseFloat(item.myPriceDeliveryCost)) ? parseFloat(item.myPriceDeliveryCost) : 0;
                            newPriceForMarginCalculation = suggestedPrice - myDeliveryCost;
                        }

                        let oldMargin = ((oldPriceForMarginCalculation - item.marginPrice) / item.marginPrice) * 100;
                        let newMargin = ((newPriceForMarginCalculation - item.marginPrice) / item.marginPrice) * 100;
                        oldMargin = parseFloat(oldMargin.toFixed(2));
                        newMargin = parseFloat(newMargin.toFixed(2));

                        let suggestedPriceDisplay = suggestedPrice.toFixed(2) + ' PLN';
                        if (marginSettings.usePriceWithDelivery && item.myPriceIncludesDelivery) {
                            const myDeliveryCost = item.myPriceDeliveryCost != null && !isNaN(parseFloat(item.myPriceDeliveryCost)) ? parseFloat(item.myPriceDeliveryCost) : 0;
                            const actualProductPrice = suggestedPrice - myDeliveryCost;
                            suggestedPriceDisplay = `${suggestedPrice.toFixed(2)} PLN (z dostawą) | ${actualProductPrice.toFixed(2)} PLN (bez dostawy)`;
                        }

                        if (marginSettings.minimalMarginPercent > 0) {
                            if (newMargin < marginSettings.minimalMarginPercent) {

                                if (newMargin > oldMargin && oldMargin < marginSettings.minimalMarginPercent) {

                                } else {

                                    let reason = "";
                                    if (oldMargin >= marginSettings.minimalMarginPercent) {

                                        reason = `Zmiana obniża narzut z <strong>${oldMargin}%</strong> (który spełniał minimum) do <strong>${newMargin}%</strong>, czyli poniżej wymaganego progu <strong>${marginSettings.minimalMarginPercent}%</strong>.`;
                                    } else if (newMargin <= oldMargin) {

                                        reason = `Nowy narzut (<strong>${newMargin}%</strong>) jest poniżej wymaganego minimum (<strong>${marginSettings.minimalMarginPercent}%</strong>) i nie stanowi poprawy (lub jest pogorszeniem) poprzedniego, już niskiego narzutu (<strong>${oldMargin}%</strong>).`;
                                    } else {

                                        reason = `Nowy narzut (<strong>${newMargin}%</strong>) jest poniżej wymaganego minimum (<strong>${marginSettings.minimalMarginPercent}%</strong>), a warunki poprawy nie zostały spełnione (poprzedni narzut: <strong>${oldMargin}%</strong>).`;
                                    }

                                    showGlobalNotification(
                                        `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny nie została dodana</p>
                                 <p>${reason}</p>
                                 <p>Cena zakupu wynosi <strong>${item.marginPrice.toFixed(2)} PLN</strong>.</p>
                                   <p>Sugerowana cena: <strong>${suggestedPriceDisplay}</strong>.</p>`
                                    );
                                    return;
                                }
                            }
                        }

                        if (marginSettings.enforceMinimalMargin) {

                            if (newMargin < 0) {

                                if (!(oldMargin < 0 && newMargin > oldMargin)) {
                                    showGlobalNotification(
                                        `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny nie została dodana</p>
                                 <p>Nowa cena <strong>${suggestedPriceDisplay}</strong> spowoduje ujemny narzut (nowy narzut: <strong>${newMargin}%</strong>).</p>
                                 <p>Cena zakupu wynosi <strong>${item.marginPrice.toFixed(2)} PLN</strong>. Zmiana nie może zostać zastosowana.</p>`
                                    );
                                    return;
                                }
                            }

                            if (marginSettings.minimalMarginPercent < 0 && newMargin > marginSettings.minimalMarginPercent) {
                                showGlobalNotification(
                                    `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny nie została dodana</p>
                             <p>Nowa cena <strong>${suggestedPriceDisplay}</strong> ustawi narzut (<strong>${newMargin}%</strong>), który jest powyżej dopuszczalnego progu straty (<strong>${marginSettings.minimalMarginPercent}%</strong>).</p>
                             <p>Nowy narzut wynosi <strong>${newMargin}%</strong>.</p>`
                                );
                                return;
                            }
                        }
                    }

                    if (marginSettings.enforceMinimalMargin) {

                        if (newMargin < 0) {

                            if (!(oldMargin < 0 && newMargin > oldMargin)) {
                                showGlobalNotification(
                                    `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny nie została dodana</p>
                                 <p>Nowa cena <strong>${suggestedPrice.toFixed(2)} PLN</strong> spowoduje ujemny narzut (nowy narzut: <strong>${newMargin}%</strong>).</p>
                                 <p>Cena zakupu wynosi <strong>${item.marginPrice.toFixed(2)} PLN</strong>. Zmiana nie może zostać zastosowana.</p>`
                                );
                                return;
                            }
                        }

                        if (marginSettings.minimalMarginPercent < 0 && newMargin > marginSettings.minimalMarginPercent) {
                            showGlobalNotification(
                                `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny nie została dodana</p>
                             <p>Nowa cena <strong>${suggestedPrice.toFixed(2)} PLN</strong> ustawi narzut (<strong>${newMargin}%</strong>), który jest powyżej dopuszczalnego progu straty (<strong>${marginSettings.minimalMarginPercent}%</strong>).</p>
                             <p>Nowy narzut wynosi <strong>${newMargin}%</strong>.</p>`
                            );
                            return;
                        }
                    }
                }

                if (priceBox.classList.contains('price-changed') || button.classList.contains('active')) {
                    console.log("Zmiana ceny już aktywna dla produktu", productId);
                    return;
                }

                const priceChangeEvent = new CustomEvent('priceBoxChange', {
                    detail: {
                        productId,
                        productName,
                        currentPrice: currentPriceValue,
                        newPrice: suggestedPrice,
                        storeId: storeId,
                        scrapId: item.scrapId
                    }
                });
                document.dispatchEvent(priceChangeEvent);

                activateChangeButton(button, priceBox, suggestedPrice);
                let message = `<p style="margin-bottom:8px; font-size:16px; font-weight:bold;">Zmiana ceny dodana</p>`;
                message += `<p style="margin:4px 0;"><strong>Produkt:</strong> ${productName}</p>`;

                let displayPriceLabel = "Nowa cena";
                let newPriceWithoutDelivery = suggestedPrice;

                if (marginSettings.usePriceWithDelivery && item.myPriceIncludesDelivery) {
                    displayPriceLabel = "Nowa cena (z dostawą)";
                    const myDeliveryCost = item.myPriceDeliveryCost != null && !isNaN(parseFloat(item.myPriceDeliveryCost)) ? parseFloat(item.myPriceDeliveryCost) : 0;
                    newPriceWithoutDelivery = suggestedPrice - myDeliveryCost;
                }

                message += `<p style="margin:4px 0;"><strong>${displayPriceLabel}:</strong> ${suggestedPrice.toFixed(2)} PLN</p>`;

                if (marginSettings.usePriceWithDelivery && item.myPriceIncludesDelivery) {
                    message += `<p style="margin:4px 0;"><strong>Nowa cena (bez dostawy):</strong> ${newPriceWithoutDelivery.toFixed(2)} PLN</p>`;
                }

                if (marginSettings.useMarginForSimulation) {
                    let finalPriceForMarginCalculation = suggestedPrice;

                    if (marginSettings.usePriceWithDelivery && item.myPriceIncludesDelivery) {
                        const myDeliveryCost = item.myPriceDeliveryCost != null && !isNaN(parseFloat(item.myPriceDeliveryCost)) ? parseFloat(item.myPriceDeliveryCost) : 0;
                        finalPriceForMarginCalculation = suggestedPrice - myDeliveryCost;
                    }

                    let finalMargin = ((finalPriceForMarginCalculation - item.marginPrice) / item.marginPrice) * 100;
                    finalMargin = parseFloat(finalMargin.toFixed(2));
                    message += `<p style="margin:4px 0;"><strong>Nowy narzut:</strong> ${finalMargin}%</p>`;
                    message += `<p style="margin:4px 0;"><strong>Cena zakupu:</strong> ${item.marginPrice.toFixed(2)} PLN</p>`;
                    if (marginSettings.enforceMinimalMargin) {
                        if (marginSettings.minimalMarginPercent > 0) {
                            message += `<p style="margin:4px 0;"><strong>Minimalny wymagany narzut:</strong> ${marginSettings.minimalMarginPercent}%</p>`;
                        } else if (marginSettings.minimalMarginPercent < 0) {
                            message += `<p style="margin:4px 0;"><strong>Maksymalne obniżenie narzutu:</strong> ${marginSettings.minimalMarginPercent}%</p>`;
                        }
                    }
                }

                showGlobalUpdate(message);
            });

            const existingChange = selectedPriceChanges.find(change =>
                parseInt(change.productId) === parseInt(productId) &&
                parseFloat(change.newPrice) === parseFloat(suggestedPrice)
            );
            if (existingChange) {
                activateChangeButton(button, priceBox, suggestedPrice);
            }
        }

        paginatedData.forEach(item => {

            const highlightedProductName = highlightMatches(item.productName, currentProductSearchTerm);
            const highlightedStoreName = highlightMatches(item.storeName, currentStoreSearchTerm);
            const deliveryClass = getDeliveryClass(item.delivery);

            const myPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;
            const lowestPrice = item.lowestPrice != null ? parseFloat(item.lowestPrice) : null;
            const savings = item.savings != null ? item.savings.toFixed(2) : "N/A";
            const myPosition = item.myPosition;
            const myDeliveryClass = getDeliveryClass(item.myDelivery);
            const marginAmount = item.marginAmount;
            const marginPercentage = item.marginPercentage;
            const marginSign = item.marginSign;
            const marginClass = item.marginClass;
            const marginPrice = item.marginPrice;

            const box = document.createElement('div');
            box.className = 'price-box ' + item.colorClass;
            box.dataset.detailsUrl = '/PriceHistory/Details?scrapId=' + item.scrapId + '&productId=' + item.productId;
            box.dataset.productId = item.productId;
            box.dataset.productName = item.productName;

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

            const apiBox = document.createElement('span');
            apiBox.className = 'ApiBox';

            let identifierValue, identifierLabel, displayedIdentifier;
            switch (marginSettings.identifierForSimulation) {
                case 'ID':
                    identifierValue = item.externalId ? item.externalId.toString() : null;
                    identifierLabel = 'ID';
                    break;
                case 'ProducerCode':
                    identifierValue = item.producerCode || null;
                    identifierLabel = 'KOD';
                    break;
                case 'EAN':
                default:
                    identifierValue = item.ean || null;
                    identifierLabel = 'EAN';
                    break;
            }

            if (identifierValue) {
                displayedIdentifier = highlightMatches(identifierValue, currentProductSearchTerm, 'highlighted-text-yellow');
                apiBox.innerHTML = `${identifierLabel} ${displayedIdentifier}`;
            } else {
                apiBox.innerHTML = `Brak ${identifierLabel}`;
            }

            priceBoxColumnCategory.appendChild(apiBox);

            const flagsContainer = createFlagsContainer(item);

            priceBoxSpace.appendChild(priceBoxColumnName);
            priceBoxSpace.appendChild(flagsContainer);

            const selectProductButton = document.createElement('button');
            selectProductButton.className = 'select-product-btn';
            selectProductButton.dataset.productId = item.productId;
            selectProductButton.style.pointerEvents = 'auto';
            selectProductButton.style.marginLeft = '5px';

            if (selectedProductIds.has(item.productId.toString())) {
                selectProductButton.textContent = 'Wybrano';
                selectProductButton.classList.add('selected');
            } else {
                selectProductButton.textContent = 'Zaznacz';
            }

            selectProductButton.addEventListener('click', function (event) {
                event.stopPropagation();
                const productId = this.dataset.productId;

                if (selectedProductIds.has(productId)) {
                    selectedProductIds.delete(productId);
                    this.textContent = 'Zaznacz';
                    this.classList.remove('selected');
                } else {
                    selectedProductIds.add(productId);
                    this.textContent = 'Wybrano';
                    this.classList.add('selected');
                }

                saveSelectionToStorage(selectedProductIds);

                updateSelectionUI();
            });

            priceBoxSpace.appendChild(selectProductButton);

            priceBoxSpace.appendChild(priceBoxColumnCategory);

            const externalInfoContainer = document.createElement('div');
            externalInfoContainer.className = 'price-box-externalInfo';

            const priceBoxColumnStoreCount = document.createElement('div');
            priceBoxColumnStoreCount.className = 'price-box-column-offers';
            priceBoxColumnStoreCount.innerHTML =
                ((item.sourceGoogle || item.sourceCeneo) ?
                    '<span class="data-channel">' +
                    (item.sourceGoogle ? `<img src="/images/GoogleShopping.png" alt="Google Icon" style="width:15px; height:15px;" />` : '') +
                    (item.sourceCeneo ? `<img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:15px; height:15px;" />` : '') +
                    '</span>' : ''
                ) +
                '<div class="offer-count-box">' + item.storeCount + ' Ofert</div>';

            externalInfoContainer.appendChild(priceBoxColumnStoreCount);

            if (marginPrice != null) {
                const formattedMarginPrice = marginPrice.toLocaleString('pl-PL', {
                    minimumFractionDigits: 2,
                    maximumFractionDigits: 2
                }) + ' PLN';

                const purchasePriceBox = document.createElement('div');

                purchasePriceBox.className = myPrice != null
                    ? 'price-box-diff-margin ' + marginClass
                    : 'priceBox-diff-margin-neutral';

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
                    marginBox.innerHTML = '<p>Narzut: ' + formattedMarginAmount + ' ' + formattedMarginPercentage + '</p>';

                    externalInfoContainer.appendChild(marginBox);
                } else {

                    const marginBox = document.createElement('div');

                    marginBox.className = 'priceBox-diff-margin-neutral';
                    marginBox.innerHTML = '<p>Narzut: Brak informacji</p>';

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

            if (item.colorClass === 'prOnlyMe') {

                const noCompetitionText = document.createElement('div');
                noCompetitionText.className = 'price-box-column-text';

                const mainText = document.createElement('span');
                mainText.style.fontWeight = '500';
                mainText.textContent = 'Brak ofert konkurencji';
                noCompetitionText.appendChild(mainText);

                const storeNameDiv = document.createElement('div');
                storeNameDiv.textContent = myStoreName;
                noCompetitionText.appendChild(storeNameDiv);

                const soloDetails = document.createElement('div');
                soloDetails.className = 'price-box-column-text';

                const soloBadge = document.createElement('span');
                soloBadge.className = 'Position';
                soloBadge.style.backgroundColor = '#9C9C9C';
                soloBadge.style.color = 'white';
                soloBadge.textContent = 'Brak ofert konkurencji - Produkt solo';
                soloDetails.appendChild(soloBadge);

                priceBoxColumnLowestPrice.appendChild(noCompetitionText);
                priceBoxColumnLowestPrice.appendChild(soloDetails);
            }

            else if (item.lowestPrice != null) {
                const priceBoxLowestText = document.createElement('div');
                priceBoxLowestText.className = 'price-box-column-text';

                const priceLine = document.createElement('div');
                priceLine.style.display = 'flex';
                priceLine.style.alignItems = 'center';

                const priceSpan = document.createElement('span');
                priceSpan.style.fontWeight = '500';
                priceSpan.style.fontSize = '17px';
                priceSpan.textContent = item.lowestPrice.toFixed(2) + ' PLN';
                priceLine.appendChild(priceSpan);

                if (typeof item.externalBestPriceCount !== 'undefined' && item.externalBestPriceCount !== null) {
                    if (item.externalBestPriceCount === 1) {
                        const uniqueBox = document.createElement('div');
                        uniqueBox.className = 'uniqueBestPriceBox';
                        uniqueBox.textContent = '★ TOP';
                        uniqueBox.style.marginLeft = '4px';
                        priceLine.appendChild(uniqueBox);
                    } else if (item.externalBestPriceCount > 1) {
                        const shareBox = document.createElement('div');
                        shareBox.className = 'shareBestPriceBox';
                        shareBox.textContent = item.externalBestPriceCount + ' TOP';
                        shareBox.style.marginLeft = '4px';
                        priceLine.appendChild(shareBox);
                    }
                }

                if (item.singleBestCheaperDiffPerc !== null && item.singleBestCheaperDiffPerc !== undefined) {
                    const diffBox = document.createElement('div');
                    diffBox.style.marginLeft = '4px';
                    diffBox.className = item.singleBestCheaperDiffPerc > 25 ? 'singlePriceDiffBoxHigh' : 'singlePriceDiffBox';
                    const spanDiff = document.createElement('span');
                    spanDiff.textContent = item.singleBestCheaperDiffPerc.toFixed(2) + '%';
                    diffBox.appendChild(spanDiff);
                    priceLine.appendChild(diffBox);
                }

                priceBoxLowestText.appendChild(priceLine);

                if (marginSettings.usePriceWithDelivery) {
                    const lowestPriceDeliveryInfo = createDeliveryInfoDisplay(
                        item.lowestPrice,
                        item.bestPriceDeliveryCost,
                        item.bestPriceIncludesDelivery
                    );
                    if (lowestPriceDeliveryInfo) {
                        priceBoxLowestText.insertBefore(lowestPriceDeliveryInfo, priceBoxLowestText.children[1]);
                    }
                }

                const storeNameDiv = document.createElement('div');
                storeNameDiv.innerHTML = highlightedStoreName;
                priceBoxLowestText.appendChild(storeNameDiv);

                const priceBoxLowestDetails = document.createElement('div');
                priceBoxLowestDetails.className = 'price-box-column-text';
                priceBoxLowestDetails.innerHTML =
                    (item.isGoogle != null ?
                        '<span class="data-channel"><img src="' +
                        (item.isGoogle ? '/images/GoogleShopping.png' : '/images/Ceneo.png') +
                        '" alt="Channel Icon" style="width:20px; height:20px; margin-right:4px;" /></div>' : ''
                    ) +
                    (item.position !== null ?
                        (item.isGoogle ?
                            '<span class="Position-Google">Poz. Google ' + item.position + '</span>' :
                            '<span class="Position">Poz. Ceneo ' + item.position + '</span>'
                        ) :
                        '<span class="Position" style="background-color: #414141;">Schowany</span>'
                    ) +
                    (item.isBidding === "1" ? '<span class="Bidding">Bid</span>' : '') +
                    (item.delivery != null ? '<span class="' + deliveryClass + '">Wysyłka w ' + (item.delivery == 1 ? '1 dzień' : item.delivery + ' dni') + '</span>' : '');

                priceBoxColumnLowestPrice.appendChild(priceBoxLowestText);
                priceBoxColumnLowestPrice.appendChild(priceBoxLowestDetails);
            }

            const priceBoxColumnMyPrice = document.createElement('div');
            priceBoxColumnMyPrice.className = 'price-box-column';

            if (item.colorClass !== 'prNoOffer' && myPrice != null) {
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

                    const priceDifferenceElem = document.createElement('span');
                    priceDifferenceElem.style.fontWeight = '500';
                    const arrow = '<span class="' + (isPriceDecrease ? 'arrow-down' : 'arrow-up') + '"></span>';
                    priceDifferenceElem.innerHTML = arrow + (isPriceDecrease ? '-' : '+') + Math.abs(externalPriceDifference) + ' PLN ';

                    priceChangeContainer.appendChild(storeName);
                    priceChangeContainer.appendChild(priceDifferenceElem);

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

                    if (marginSettings.usePriceWithDelivery) {
                        const myPriceDeliveryInfo = createDeliveryInfoDisplay(
                            myPrice,
                            item.myPriceDeliveryCost,
                            item.myPriceIncludesDelivery
                        );
                        if (myPriceDeliveryInfo) {
                            priceBoxMyText.insertBefore(myPriceDeliveryInfo, priceBoxMyText.children[1]);
                        }
                    }
                } else {
                    const myPriceLine = document.createElement('div');
                    myPriceLine.style.display = 'flex';
                    myPriceLine.style.alignItems = 'center';

                    const myPriceSpan = document.createElement('span');
                    myPriceSpan.style.fontWeight = '500';
                    myPriceSpan.style.fontSize = '17px';
                    myPriceSpan.textContent = myPrice.toFixed(2) + ' PLN';
                    myPriceLine.appendChild(myPriceSpan);

                    const storeNameDiv = document.createElement('div');
                    storeNameDiv.textContent = myStoreName;

                    priceBoxMyText.appendChild(myPriceLine);
                    if (marginSettings.usePriceWithDelivery) {
                        const myPriceDeliveryInfo = createDeliveryInfoDisplay(
                            myPrice,
                            item.myPriceDeliveryCost,
                            item.myPriceIncludesDelivery
                        );
                        if (myPriceDeliveryInfo) {
                            priceBoxMyText.insertBefore(myPriceDeliveryInfo, priceBoxMyText.children[1]);
                        }
                    }
                    priceBoxMyText.appendChild(storeNameDiv);
                }

                const priceBoxMyDetails = document.createElement('div');
                priceBoxMyDetails.className = 'price-box-column-text';
                priceBoxMyDetails.innerHTML =
                    (item.myIsGoogle != null ?
                        '<span class="data-channel"><img src="' +
                        (item.myIsGoogle ? '/images/GoogleShopping.png' : '/images/Ceneo.png') +
                        '" alt="Channel Icon" style="width:20px; height:20px; margin-right:4px;" /></div>' : ''
                    ) +
                    (myPosition !== null ?
                        (item.myIsGoogle ?
                            '<span class="Position-Google">Poz. Google ' + myPosition + '</span>' :
                            '<span class="Position">Poz. Ceneo ' + myPosition + '</span>'
                        ) :
                        '<span class="Position" style="background-color: #414141;">Schowany</span>'
                    ) +
                    (item.myIsBidding === "1" ? '<span class="Bidding">Bid</span>' : '') +
                    (item.myDelivery != null ? '<span class="' + myDeliveryClass + '">Wysyłka w ' + (item.myDelivery == 1 ? '1 dzień' : item.myDelivery + ' dni') + '</span>' : '');

                priceBoxColumnMyPrice.appendChild(priceBoxMyText);
                priceBoxColumnMyPrice.appendChild(priceBoxMyDetails);
            } else {

                const priceBoxMyText = document.createElement('div');
                priceBoxMyText.className = 'price-box-column-text';

                const mainText = document.createElement('span');
                mainText.style.fontWeight = '500';
                mainText.textContent = 'Brak Twojej oferty';
                priceBoxMyText.appendChild(mainText);

                const storeNameDiv = document.createElement('div');
                storeNameDiv.textContent = myStoreName;
                priceBoxMyText.appendChild(storeNameDiv);

                const detailsContainer = document.createElement('div');
                detailsContainer.className = 'price-box-column-text';

                const noOfferBadge = document.createElement('span');
                noOfferBadge.className = 'Position';
                noOfferBadge.style.backgroundColor = '#9C9C9C';

                noOfferBadge.textContent = 'Brak Twojej oferty - Cena niedostępna';
                detailsContainer.appendChild(noOfferBadge);

                priceBoxColumnMyPrice.appendChild(priceBoxMyText);
                priceBoxColumnMyPrice.appendChild(detailsContainer);

            }

            const priceBoxColumnInfo = document.createElement('div');
            priceBoxColumnInfo.className = 'price-box-column-action';
            priceBoxColumnInfo.innerHTML = '';

            if (item.colorClass === 'prNoOffer') {

            } else {

                if (item.colorClass === "prToLow" || item.colorClass === "prIdeal") {
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
                                if (amountToSuggestedPrice2 < 0) { arrowClass2 = 'arrow-down-turquoise'; }
                            } else {
                                suggestedPrice2 = suggestedPrice1 - setStepPrice;
                                amountToSuggestedPrice2 = amountToSuggestedPrice1 - setStepPrice;
                                percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;
                                if (amountToSuggestedPrice2 < 0) { arrowClass2 = 'arrow-down-turquoise'; }
                            }
                        } else {
                            const percentageStep = setStepPrice / 100;
                            if (savingsValue < 1) {
                                suggestedPrice2 = suggestedPrice1 * (1 - percentageStep);
                                amountToSuggestedPrice2 = suggestedPrice2 - myPrice;
                                percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;
                                if (amountToSuggestedPrice2 < 0) { arrowClass2 = 'arrow-down-turquoise'; }
                            } else {
                                suggestedPrice2 = suggestedPrice1 * (1 - percentageStep);
                                amountToSuggestedPrice2 = amountToSuggestedPrice1 - (myPrice * percentageStep);
                                percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;
                                if (amountToSuggestedPrice2 < 0) { arrowClass2 = 'arrow-down-turquoise'; }
                            }
                        }

                        const amount1Formatted = (amountToSuggestedPrice1 >= 0 ? '+' : '') + amountToSuggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
                        const percentage1Formatted = '(' + (percentageToSuggestedPrice1 >= 0 ? '+' : '') + percentageToSuggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
                        const newSuggestedPrice1Formatted = suggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

                        const amount2Formatted = (amountToSuggestedPrice2 >= 0 ? '+' : '') + amountToSuggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
                        const percentage2Formatted = '(' + (percentageToSuggestedPrice2 >= 0 ? '+' : '') + percentageToSuggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
                        const newSuggestedPrice2Formatted = suggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

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

                        const matchPriceBtn = document.createElement('button');
                        matchPriceBtn.className = 'simulate-change-btn';
                        matchPriceBtn.textContent = 'Zmień cenę';
                        matchPriceBtn.dataset.originalText = 'Zmień cenę';

                        attachPriceChangeListener(matchPriceBtn, suggestedPrice1, box, item.productId, item.productName, myPrice, item);

                        matchPriceLine.appendChild(matchPriceBtn);

                        matchPriceBox.appendChild(matchPriceLine);

                        const strategicPriceBox = document.createElement('div');
                        strategicPriceBox.className = 'price-box-column';

                        const strategicPriceLine = document.createElement('div');
                        strategicPriceLine.className = 'price-action-line';

                        const arrow2Elem = document.createElement('span');
                        arrow2Elem.className = arrowClass2;
                        const increaseText2 = document.createElement('span');
                        increaseText2.innerHTML = amount2Formatted + ' ' + percentage2Formatted;
                        const newPriceText2 = document.createElement('div');
                        newPriceText2.innerHTML = '= ' + newSuggestedPrice2Formatted;
                        const colorSquare2 = document.createElement('span');
                        colorSquare2.className = 'color-square-turquoise';

                        strategicPriceLine.appendChild(arrow2Elem);
                        strategicPriceLine.appendChild(increaseText2);
                        strategicPriceLine.appendChild(newPriceText2);
                        strategicPriceLine.appendChild(colorSquare2);

                        const strategicPriceBtn = document.createElement('button');
                        strategicPriceBtn.className = 'simulate-change-btn';
                        strategicPriceBtn.textContent = 'Zmień cenę';
                        strategicPriceBtn.dataset.originalText = 'Zmień cenę';

                        attachPriceChangeListener(strategicPriceBtn, suggestedPrice2, box, item.productId, item.productName, myPrice, item);

                        strategicPriceLine.appendChild(strategicPriceBtn);

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

                        const amountMatchFormatted = amountToMatchLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
                        const percentageMatchFormatted = '(-' + percentageToMatchLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
                        const newSuggestedPriceMatch = lowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

                        const amountBeatFormatted = amountToBeatLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
                        const percentageBeatFormatted = '(-' + percentageToBeatLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
                        const newSuggestedPriceBeat = strategicPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

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

                        const matchPriceBtn = document.createElement('button');
                        matchPriceBtn.className = 'simulate-change-btn';
                        matchPriceBtn.textContent = 'Zmień cenę';
                        matchPriceBtn.dataset.originalText = 'Zmień cenę';

                        attachPriceChangeListener(matchPriceBtn, lowestPrice, box, item.productId, item.productName, myPrice, item);
                        matchPriceLine.appendChild(matchPriceBtn);

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

                        const strategicPriceBtn = document.createElement('button');
                        strategicPriceBtn.className = 'simulate-change-btn';
                        strategicPriceBtn.textContent = 'Zmień cenę';
                        strategicPriceBtn.dataset.originalText = 'Zmień cenę';

                        attachPriceChangeListener(strategicPriceBtn, strategicPrice, box, item.productId, item.productName, myPrice, item);

                        strategicPriceLine.appendChild(strategicPriceBtn);

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

                        const amountMatchFormatted = amountToMatchLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
                        const percentageMatchFormatted = '(-' + percentageToMatchLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
                        const newSuggestedPriceMatch = lowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

                        const amountBeatFormatted = amountToBeatLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
                        const percentageBeatFormatted = '(-' + percentageToBeatLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
                        const newSuggestedPriceBeat = strategicPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

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

                        const matchPriceBtn = document.createElement('button');
                        matchPriceBtn.className = 'simulate-change-btn';
                        matchPriceBtn.textContent = 'Zmień cenę';
                        matchPriceBtn.dataset.originalText = 'Zmień cenę';

                        attachPriceChangeListener(matchPriceBtn, lowestPrice, box, item.productId, item.productName, myPrice, item);

                        matchPriceLine.appendChild(matchPriceBtn);

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

                        const strategicPriceBtn = document.createElement('button');
                        strategicPriceBtn.className = 'simulate-change-btn';
                        strategicPriceBtn.textContent = 'Zmień cenę';
                        strategicPriceBtn.dataset.originalText = 'Zmień cenę';

                        attachPriceChangeListener(strategicPriceBtn, strategicPrice, box, item.productId, item.productName, myPrice, item);

                        strategicPriceLine.appendChild(strategicPriceBtn);

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
                        const newSuggestedPrice1Formatted = '= ' + suggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

                        let amountToSuggestedPrice2, percentageToSuggestedPrice2, suggestedPrice2;
                        let amount2Formatted, percentage2Formatted, newSuggestedPrice2Formatted;
                        let downArrowClass, colorSquare2Class;

                        if (item.storeCount === 1) {
                            amountToSuggestedPrice2 = 0;
                            percentageToSuggestedPrice2 = 0;
                            suggestedPrice2 = myPrice;

                            amount2Formatted = '+0,00 PLN';
                            percentage2Formatted = '(+0,00%)';
                            newSuggestedPrice2Formatted = '= ' + suggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

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
                                if (Math.abs(amountToSuggestedPrice2) < 0.01) {
                                    amountToSuggestedPrice2 = -0.01;
                                }
                                suggestedPrice2 = myPrice + amountToSuggestedPrice2;
                                percentageToSuggestedPrice2 = (amountToSuggestedPrice2 / myPrice) * 100;
                            }

                            amount2Formatted = (amountToSuggestedPrice2 >= 0 ? '+' : '') + amountToSuggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';
                            percentage2Formatted = '(' + (percentageToSuggestedPrice2 >= 0 ? '+' : '') + percentageToSuggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '%)';
                            newSuggestedPrice2Formatted = '= ' + suggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' PLN';

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

                        const matchPriceBtn = document.createElement('button');
                        matchPriceBtn.className = 'simulate-change-btn';
                        matchPriceBtn.textContent = 'Zmień cenę';
                        matchPriceBtn.dataset.originalText = 'Zmień cenę';

                        attachPriceChangeListener(matchPriceBtn, suggestedPrice1, box, item.productId, item.productName, myPrice, item);

                        matchPriceLine.appendChild(matchPriceBtn);

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

                        const strategicPriceBtn = document.createElement('button');
                        strategicPriceBtn.className = 'simulate-change-btn';
                        strategicPriceBtn.textContent = 'Zmień cenę';
                        strategicPriceBtn.dataset.originalText = 'Zmień cenę';

                        attachPriceChangeListener(strategicPriceBtn, suggestedPrice2, box, item.productId, item.productName, myPrice, item);
                        strategicPriceLine.appendChild(strategicPriceBtn);

                        strategicPriceBox.appendChild(strategicPriceLine);

                        priceBoxColumnInfo.appendChild(matchPriceBox);
                        priceBoxColumnInfo.appendChild(strategicPriceBox);

                    } else {
                        const diffClass = item.colorClass + ' ' + 'priceBox-diff-top';
                        priceBoxColumnInfo.innerHTML += '<div class="' + diffClass + '">Jesteś w najlepszych cenach</div>';
                    }

                } else {

                }

            }

            priceBoxData.appendChild(colorBar);

            if (item.imgUrl) {
                const productImage = document.createElement('img');
                productImage.dataset.src = item.imgUrl;
                productImage.alt = item.productName;
                productImage.className = 'lazy-load';
                productImage.style.width = '114px';
                productImage.style.height = '114px';
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

        renderPaginationControls(data.length);

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
                    img.onload = () => { img.classList.add('loaded'); };
                }
            }
        }

        lazyLoadImages.forEach(img => { observer.observe(img); });

        document.getElementById('displayedProductCount').textContent = data.length;
    }

    function getDeliveryClass(days) {
        if (days <= 1) return 'Availability1Day';
        if (days <= 3) return 'Availability3Days';
        if (days <= 7) return 'Availability7Days';
        return 'Availability14Days';
    }

    function createDeliveryInfoDisplay(totalPrice, deliveryCost, includesDelivery) {

        if (totalPrice === null || totalPrice === undefined) {
            return null;
        }

        const numericTotalPrice = parseFloat(totalPrice);

        let deliveryKnown = deliveryCost !== null && deliveryCost !== undefined && !isNaN(parseFloat(deliveryCost));
        let numericDeliveryCost = deliveryKnown ? parseFloat(deliveryCost) : 0;

        const truckColor = (includesDelivery === true && deliveryKnown)
            ? '#21a73e'
            : '#f44336';

        let truckTooltip = (includesDelivery === true && deliveryKnown)
            ? "Cena zawiera dostawę"
            : "Cena NIE zawiera dostawy";

        const infoContainer = document.createElement('div');
        infoContainer.className = 'delivery-info-line';

        const truckIcon = document.createElement('i');
        truckIcon.className = 'fa fa-truck';
        truckIcon.style.marginRight = '4px';
        truckIcon.style.marginLeft = '1px';
        truckIcon.style.fontSize = '12px';
        truckIcon.title = truckTooltip;
        truckIcon.style.color = truckColor;

        const priceText = document.createElement('span');
        priceText.style.fontSize = '12px';
        priceText.style.color = '#555';

        if (deliveryKnown) {

            const basePrice = numericTotalPrice - numericDeliveryCost;
            priceText.textContent = `${basePrice.toFixed(2)} PLN | ${numericDeliveryCost.toFixed(2)} PLN`;
        } else {

            priceText.textContent = `${numericTotalPrice.toFixed(2)} PLN | Brak danych o wysyłce`;
        }

        infoContainer.appendChild(truckIcon);
        infoContainer.appendChild(priceText);

        return infoContainer;
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
            prNoOffer: 0,
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

        const chartData = [
            colorCounts.prNoOffer,
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
                    labels: ['Cena niedostępna', 'Cena solo', 'Cena zawyżona', 'Cena suboptymalna', 'Cena konkurencyjna', 'Cena strategiczna', 'Cena zaniżona'],
                    datasets: [{
                        data: chartData,
                        backgroundColor: [
                            'rgba(230, 230, 230, 1)',
                            'rgba(180, 180, 180, 0.8)',
                            'rgba(171, 37, 32, 0.8)',
                            'rgba(224, 168, 66, 0.8)',
                            'rgba(117, 152, 112, 0.8)',
                            'rgba(13, 110, 253, 0.8)',
                            'rgba(6, 6, 6, 0.8)',

                        ],
                        borderColor: [
                            'rgba(230, 230, 230, 1)',
                            'rgba(180, 180, 180, 1)',
                            'rgba(171, 37, 32, 1)',
                            'rgba(224, 168, 66, 1)',
                            'rgba(117, 152, 112, 1)',
                            'rgba(13, 110, 253, 1)',
                            'rgba(6, 6, 6, 1)'

                        ],
                        borderWidth: 1
                    }]
                },
                options: {
                    aspectRatio: 1,
                    cutout: '65%',
                    layout: {
                        padding: 4
                    },
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

    document.getElementById('openMarginSettingsBtn').addEventListener('click', function () {

        document.getElementById('identifierForSimulationInput').value = marginSettings.identifierForSimulation;
        document.getElementById('useMarginForSimulationInput').value = marginSettings.useMarginForSimulation.toString();
        document.getElementById('usePriceWithDeliveryInput').value = marginSettings.usePriceWithDelivery.toString();
        document.getElementById('enforceMinimalMarginInput').value = marginSettings.enforceMinimalMargin.toString();
        document.getElementById('minimalMarginPercentInput').value = marginSettings.minimalMarginPercent;

        $('#marginSettingsModal').modal('show');
    });

    document.getElementById('saveMarginSettingsBtn').addEventListener('click', function () {
        const oldUsePriceWithDeliverySetting = marginSettings.usePriceWithDelivery;

        const updatedMarginSettings = {
            StoreId: storeId,

            IdentifierForSimulation: document.getElementById('identifierForSimulationInput').value,
            UseMarginForSimulation: document.getElementById('useMarginForSimulationInput').value === 'true',
            UsePriceWithDelivery: document.getElementById('usePriceWithDeliveryInput').value === 'true',
            EnforceMinimalMargin: document.getElementById('enforceMinimalMarginInput').value === 'true',
            MinimalMarginPercent: parseFloat(document.getElementById('minimalMarginPercentInput').value)
        };

        fetch('/PriceHistory/SaveMarginSettings', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(updatedMarginSettings)
        })
            .then(response => response.json())
            .then(data => {
                if (data.success) {

                    if (oldUsePriceWithDeliverySetting !== updatedMarginSettings.UsePriceWithDelivery) {

                        localStorage.removeItem('selectedPriceChanges_' + storeId);

                        if (typeof selectedPriceChanges !== 'undefined') {
                            selectedPriceChanges = [];
                            console.log("Zmiany symulacji cenowej usunięte z Local Storage i pamięci, ponieważ zmieniono ustawienie 'Uwzględniaj koszt wysyłki'. Przeładowuję stronę.");
                        }

                        showGlobalUpdate('<p style="margin-bottom:8px; font-size:16px; font-weight:bold;">Ustawienia zapisane</p><p>Zmiana opcji "Uwzględniaj koszt wysyłki" spowodowała usunięcie wprowadzonych zmian symulacji cenowej. Przeładowuję stronę...</p>');

                        setTimeout(function () {
                            window.location.reload();
                        }, 2000);

                    } else {

                        showGlobalUpdate('<p style="margin-bottom:8px; font-size:16px; font-weight:bold;">Ustawienia zapisane</p>');

                        marginSettings = updatedMarginSettings;

                        $('#marginSettingsModal').modal('hide');

                        loadPrices();
                    }
                } else {
                    alert('Błąd zapisu ustawień: ' + data.message);
                }
            })
            .catch(error => {
                console.error('Błąd zapisu ustawień narzutu:', error);
                showGlobalNotification('<p style="margin-bottom:8px; font-weight:bold;">Błąd zapisu ustawień</p><p>Wystąpił błąd podczas zapisywania ustawień narzutu.</p>');
            });
    });
    function updateColorCounts(data) {
        const colorCounts = {
            prNoOffer: 0,
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
        document.querySelector('label[for="prNoOfferCheckbox"]').textContent = `Cena niedostępna (${colorCounts.prNoOffer})`;
        document.querySelector('label[for="prOnlyMeCheckbox"]').textContent = `Cena solo (${colorCounts.prOnlyMe})`;
        document.querySelector('label[for="prToHighCheckbox"]').textContent = `Cena zawyżona (${colorCounts.prToHigh})`;
        document.querySelector('label[for="prMidCheckbox"]').textContent = `Cena suboptymalna (${colorCounts.prMid})`;
        document.querySelector('label[for="prGoodCheckbox"]').textContent = `Cena konkurencyjna (${colorCounts.prGood})`;
        document.querySelector('label[for="prIdealCheckbox"]').textContent = `Cena strategiczna (${colorCounts.prIdeal})`;
        document.querySelector('label[for="prToLowCheckbox"]').textContent = `Cena zaniżona (${colorCounts.prToLow})`;
    }

    function filterPricesAndUpdateUI(resetPageFlag = true) {
        if (resetPageFlag) {
            resetPage();
        }
        showLoading();

        setTimeout(() => {

            let filteredPrices = [...allPrices];

            const productSearchRaw = document.getElementById('productSearch').value.trim();
            const storeSearchRaw = document.getElementById('storeSearch').value.trim();
            if (productSearchRaw) {
                const sanitizedProductSearch = productSearchRaw
                    .replace(/[^a-zA-Z0-9\s.-]/g, '')
                    .toLowerCase()
                    .replace(/\s+/g, '');

                filteredPrices = filteredPrices.filter(price => {

                    let identifierValue = '';
                    switch (marginSettings.identifierForSimulation) {
                        case 'ID':
                            identifierValue = price.externalId ? price.externalId.toString() : '';
                            break;
                        case 'ProducerCode':
                            identifierValue = price.producerCode || '';
                            break;
                        case 'EAN':
                        default:
                            identifierValue = price.ean || '';
                            break;
                    }
                    const combined = (price.productName || '') + ' ' + identifierValue;

                    const combinedSanitized = combined
                        .toLowerCase()
                        .replace(/[^a-zA-Z0-9\s.-]/g, '')
                        .replace(/\s+/g, '');
                    return combinedSanitized.includes(sanitizedProductSearch);
                });
            }

            if (storeSearchRaw) {
                const sanitizedStoreSearch = storeSearchRaw
                    .replace(/[^a-zA-Z0-9\s.-]/g, '')
                    .toLowerCase()
                    .replace(/\s+/g, '');
                filteredPrices = filteredPrices.filter(price => {
                    const storeNameSanitized = (price.storeName || '')
                        .toLowerCase()
                        .replace(/[^a-zA-Z0-9\s.-]/g, '')
                        .replace(/\s+/g, '');
                    return storeNameSanitized.includes(sanitizedStoreSearch);
                });
            }

            const sanitizedProductSearchTerm = productSearchRaw
                ? productSearchRaw.replace(/[^a-zA-Z0-9\s.-]/g, '').toLowerCase().replace(/\s+/g, '')
                : '';
            if (sanitizedProductSearchTerm !== '') {
                filteredPrices.sort((a, b) => {
                    const aCombined = ((a.productName || '') + ' ' + (a.ean || ''))
                        .toLowerCase().replace(/[^a-zA-Z0-9\s.-]/g, '').replace(/\s+/g, '');
                    const bCombined = ((b.productName || '') + ' ' + (b.ean || ''))
                        .toLowerCase().replace(/[^a-zA-Z0-9\s.-]/g, '').replace(/\s+/g, '');
                    const exactMatchIndexA = getExactMatchIndex(aCombined, sanitizedProductSearchTerm);
                    const exactMatchIndexB = getExactMatchIndex(bCombined, sanitizedProductSearchTerm);
                    if (exactMatchIndexA !== exactMatchIndexB) {
                        return exactMatchIndexA - exactMatchIndexB;
                    }
                    const matchLengthA = getLongestMatchLength(aCombined, sanitizedProductSearchTerm);
                    const matchLengthB = getLongestMatchLength(bCombined, sanitizedProductSearchTerm);
                    if (matchLengthA !== matchLengthB) {
                        return matchLengthB - matchLengthA;
                    }
                    return a.productName.localeCompare(b.productName);
                });
            }

            filteredPrices = filterPricesByCategoryAndColorAndFlag(filteredPrices);

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

            const selectedProducer = document.getElementById('producerFilterDropdown').value;
            if (selectedProducer) {
                filteredPrices = filteredPrices.filter(item =>

                    item.producer === selectedProducer
                );
            }

            currentlyFilteredPrices = [...filteredPrices];

            renderPrices(filteredPrices);
            debouncedRenderChart(filteredPrices);
            updateColorCounts(filteredPrices);
            updateFlagCounts(filteredPrices);

            hideLoading();
        }, 0);
    }

    function highlightMatches(fullText, searchTerm, customClass) {
        if (!searchTerm) return fullText;
        const escapedTerm = searchTerm.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        const regex = new RegExp(escapedTerm, 'gi');
        const cssClass = customClass || "highlighted-text";
        return fullText.replace(regex, (match) => `<span class="${cssClass}">${match}</span>`);
    }

    function resetSortingStates(except) {
        Object.keys(sortingState).forEach(key => {
            if (key !== except) {
                sortingState[key] = null;
            }
        });

        updateSortButtonVisuals();
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
                return 'Narzut PLN';
            case 'sortMarginPercentage':
                return 'Narzut %';

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

    document.querySelectorAll('.colorFilter, .flagFilter, .positionFilter, .deliveryFilterMyStore, .deliveryFilterCompetitor, .externalPriceFilter')
        .forEach(function (checkbox) {
            checkbox.addEventListener('change', function () {
                showLoading();
                filterPricesAndUpdateUI();
            });
        });

    document.getElementById('bidFilter').addEventListener('change', function () {
        filterPricesAndUpdateUI();
    });

    document.getElementById('suspiciouslyLowFilter').addEventListener('change', function () {
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

        localStorage.setItem('priceHistorySortingState_' + storeId, JSON.stringify(sortingState));
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
        localStorage.setItem('priceHistorySortingState_' + storeId, JSON.stringify(sortingState));
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
        localStorage.setItem('priceHistorySortingState_' + storeId, JSON.stringify(sortingState));
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
        localStorage.setItem('priceHistorySortingState_' + storeId, JSON.stringify(sortingState));
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
        localStorage.setItem('priceHistorySortingState_' + storeId, JSON.stringify(sortingState));
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
        localStorage.setItem('priceHistorySortingState_' + storeId, JSON.stringify(sortingState));
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortMarginAmount').addEventListener('click', function () {
        if (sortingState.sortMarginAmount === null) {
            sortingState.sortMarginAmount = 'asc';
            this.innerHTML = 'Narzut PLN ↑';
            this.classList.add('active');
        } else if (sortingState.sortMarginAmount === 'asc') {
            sortingState.sortMarginAmount = 'desc';
            this.innerHTML = 'Narzut PLN ↓';
            this.classList.add('active');
        } else {
            sortingState.sortMarginAmount = null;
            this.innerHTML = 'Narzut PLN';
            this.classList.remove('active');
        }
        resetSortingStates('sortMarginAmount');
        localStorage.setItem('priceHistorySortingState_' + storeId, JSON.stringify(sortingState));
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortMarginPercentage').addEventListener('click', function () {
        if (sortingState.sortMarginPercentage === null) {
            sortingState.sortMarginPercentage = 'asc';
            this.innerHTML = 'Narzut % ↑';
            this.classList.add('active');
        } else if (sortingState.sortMarginPercentage === 'asc') {
            sortingState.sortMarginPercentage = 'desc';
            this.innerHTML = 'Narzut % ↓';
            this.classList.add('active');
        } else {
            sortingState.sortMarginPercentage = null;
            this.innerHTML = 'Narzut %';
            this.classList.remove('active');
        }
        resetSortingStates('sortMarginPercentage');
        localStorage.setItem('priceHistorySortingState_' + storeId, JSON.stringify(sortingState));
        filterPricesAndUpdateUI();
    });

    document.getElementById('usePriceDifference').addEventListener('change', function () {
        usePriceDifference = this.checked;
        updateUnits(usePriceDifference);

    });

    document.getElementById('storeSearch').addEventListener('input', debouncedFilterPrices);

    document.getElementById('price1').addEventListener('input', function () {
        setPrice1 = parseFloat(this.value);

    });

    document.getElementById('price2').addEventListener('input', function () {
        setPrice2 = parseFloat(this.value);

    });
    document.getElementById('stepPrice').addEventListener('input', function () {
        setStepPrice = parseFloat(this.value);

    });

    document.getElementById('productSearch').addEventListener('input', function () {
        currentPage = 1;
        window.scrollTo(0, 0);
        debouncedFilterPrices();
    });

    const exportButton = document.getElementById("exportToExcelButton");
    if (exportButton) {
        exportButton.addEventListener("click", function () {
            exportToExcelXLSX(currentlyFilteredPrices);
        });
    }

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

    document.getElementById('openBulkFlagModalBtn').addEventListener('click', function () {
        if (selectedProductIds.size === 0) {
            alert('Nie zaznaczono żadnych produktów.');
            return;
        }

        isBulkFlaggingMode = true;
        $('#selectedProductsModal').modal('hide');
        showLoading();

        const productIds = Array.from(selectedProductIds);

        fetch('/ProductFlags/GetFlagCountsForProducts', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ productIds: productIds })
        })
            .then(response => response.json())
            .then(counts => {
                populateBulkFlagModal(counts);
                hideLoading();
                $('#flagModal').modal('show');
            })
            .catch(error => {
                console.error('Błąd pobierania liczników flag:', error);
                hideLoading();
                alert('Nie udało się pobrać danych o flagach.');
            });
    });

    function populateBulkFlagModal(flagCounts) {
        const modalBody = document.getElementById('flagModalBody');
        const flagModalTitle = document.querySelector('#flagModal .price-box-column-name');
        const totalSelected = selectedProductIds.size;

        flagModalTitle.textContent = `Zarządzaj flagami dla ${totalSelected} produktów`;
        modalBody.innerHTML = '';

        flags.forEach(flag => {
            const currentCount = flagCounts[flag.flagId] || 0;

            const flagItem = document.createElement('div');
            flagItem.className = 'bulk-flag-item';
            flagItem.dataset.flagId = flag.flagId;

            const label = document.createElement('div');
            label.className = 'flag-label';
            label.innerHTML = `
            <span class="flag-name" style="border-color: ${flag.flagColor}; color: ${flag.flagColor};">${flag.flagName}</span>
            <span class="flag-count">(${currentCount} / ${totalSelected})</span>
        `;

            const actions = document.createElement('div');
            actions.className = 'flag-actions';

            actions.innerHTML = `
            <div class="action-group">
                <label>
                    <input type="checkbox" class="bulk-flag-action" data-action="add" ${currentCount === totalSelected ? 'disabled' : ''}> Dodaj
                </label>
                <span class="change-indicator add-indicator">${currentCount} → ${totalSelected}</span>
            </div>
            <div class="action-group">
                <label>
                    <input type="checkbox" class="bulk-flag-action" data-action="remove" ${currentCount === 0 ? 'disabled' : ''}> Odepnij
                </label>
                <span class="change-indicator remove-indicator">${currentCount} → 0</span>
            </div>
        `;

            flagItem.appendChild(label);
            flagItem.appendChild(actions);
            modalBody.appendChild(flagItem);
        });

        modalBody.querySelectorAll('.bulk-flag-action').forEach(checkbox => {
            checkbox.addEventListener('change', function () {
                const parentItem = this.closest('.bulk-flag-item');
                const action = this.dataset.action;

                if (this.checked) {
                    if (action === 'add') {
                        parentItem.querySelector('.bulk-flag-action[data-action="remove"]').checked = false;
                    } else {
                        parentItem.querySelector('.bulk-flag-action[data-action="add"]').checked = false;
                    }
                }

                parentItem.querySelector('.add-indicator').style.display = parentItem.querySelector('.bulk-flag-action[data-action="add"]').checked ? 'inline' : 'none';
                parentItem.querySelector('.remove-indicator').style.display = parentItem.querySelector('.bulk-flag-action[data-action="remove"]').checked ? 'inline' : 'none';
            });
        });
    }

    document.getElementById('saveFlagsButton').addEventListener('click', function () {

        const flagsToAdd = [];
        const flagsToRemove = [];

        document.querySelectorAll('#flagModalBody .bulk-flag-item').forEach(item => {
            const flagId = parseInt(item.dataset.flagId, 10);
            const addCheckbox = item.querySelector('.bulk-flag-action[data-action="add"]');
            const removeCheckbox = item.querySelector('.bulk-flag-action[data-action="remove"]');

            if (addCheckbox && addCheckbox.checked) {
                flagsToAdd.push(flagId);
            }
            if (removeCheckbox && removeCheckbox.checked) {
                flagsToRemove.push(flagId);
            }
        });

        if (flagsToAdd.length === 0 && flagsToRemove.length === 0) {
            alert("Nie wybrano żadnych akcji do wykonania.");
            return;
        }

        const data = {
            productIds: Array.from(selectedProductIds).map(id => parseInt(id)),
            flagsToAdd: flagsToAdd,
            flagsToRemove: flagsToRemove
        };

        showLoading();
        fetch('/ProductFlags/UpdateFlagsForMultipleProducts', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        })
            .then(response => response.json())
            .then(response => {
                if (response.success) {
                    $('#flagModal').modal('hide');
                    showGlobalUpdate(`<p>Pomyślnie zaktualizowano flagi dla ${data.productIds.length} produktów.</p>`);

                    selectedProductIds.clear();
                    clearSelectionFromStorage();
                    updateSelectionUI();

                    loadPrices();
                } else {
                    alert('Błąd: ' + response.message);
                }
            })
            .catch(error => console.error('Błąd masowej aktualizacji flag:', error))
            .finally(() => hideLoading());
    });

    function updateSelectionUI() {
        const selectionContainer = document.getElementById('selectionContainer');
        const counter = document.getElementById('selectedProductsCounter');
        const modalList = document.getElementById('selectedProductsList');
        const count = selectedProductIds.size;

        selectionContainer.style.display = 'flex';

        counter.textContent = `Wybrano: ${count}`;

        modalList.innerHTML = '';
        if (count === 0) {
            modalList.innerHTML = '<p>Brak zaznaczonych produktów.</p>';
            return;
        }

        const table = document.createElement('table');
        table.className = 'table table-striped';
        table.innerHTML = `<thead><tr><th>Nazwa Produktu</th><th>EAN/ID</th><th>Akcja</th></tr></thead>`;
        const tbody = document.createElement('tbody');

        selectedProductIds.forEach(productId => {
            const product = allPrices.find(p => p.productId.toString() === productId);
            if (product) {
                const tr = document.createElement('tr');
                let identifier = product.ean || product.externalId || 'Brak ID';
                tr.innerHTML = `
                <td>${product.productName}</td>
                <td>${identifier}</td>
                <td><button class="btn btn-danger btn-sm remove-selection-btn" data-product-id="${productId}">Usuń</button></td>
            `;
                tbody.appendChild(tr);
            }
        });

        table.appendChild(tbody);
        modalList.appendChild(table);
    }

    document.getElementById('selectedProductsList').addEventListener('click', function (event) {
        if (event.target.classList.contains('remove-selection-btn')) {
            const productId = event.target.dataset.productId;

            selectedProductIds.delete(productId);

            saveSelectionToStorage(selectedProductIds);

            const mainButton = document.querySelector(`.select-product-btn[data-product-id='${productId}']`);
            if (mainButton) {
                mainButton.textContent = 'Zaznacz';
                mainButton.classList.remove('selected');
            }

            updateSelectionUI();
        }
    });

    document.getElementById('showSelectedProductsBtn').addEventListener('click', function () {
        updateSelectionUI();
        $('#selectedProductsModal').modal('show');
    });

    function createFlagsContainer(item) {
        const flagsContainer = document.createElement('div');
        flagsContainer.className = 'flags-container';
        if (item.flagIds && item.flagIds.length > 0) {
            item.flagIds.forEach(function (flagId) {

                const flag = flags.find(f => f.flagId === flagId);
                if (flag) {
                    const flagSpan = document.createElement('span');
                    flagSpan.className = 'flag';
                    flagSpan.style.color = flag.flagColor;
                    flagSpan.style.border = '2px solid ' + flag.flagColor;
                    flagSpan.style.backgroundColor = hexToRgba(flag.flagColor, 0.3);
                    flagSpan.innerHTML = flag.flagName;
                    flagsContainer.appendChild(flagSpan);
                }
            });
        }
        return flagsContainer;
    }

    loadPrices();
    updateSelectionUI();

    function showLoading() {
        document.getElementById("loadingOverlay").style.display = "flex";
    }

    function hideLoading() {
        document.getElementById("loadingOverlay").style.display = "none";
    }
});