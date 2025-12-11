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
    let currentViewMode = 'competitiveness';
    let currentlyFilteredPrices = [];
    let chartInstance = null;
    let myStoreName = "";
    let setPrice1 = 2.00;
    let setPrice2 = 2.00;
    let setStepPrice = 2.00;
    let setPriceIndexTarget = 100.00;
    let usePriceDifference = document.getElementById('usePriceDifference').checked;
    let marginSettings = {
        identifierForSimulation: 'EAN',
        useMarginForSimulation: true,
        usePriceWithDelivery: true,
        enforceMinimalMargin: true,
        minimalMarginPercent: 0.00
    };

    function formatPricePL(value, includeUnit = true) {

        if (value === null || value === undefined || isNaN(parseFloat(value))) {
            return "N/A";
        }
        const numberValue = parseFloat(value);

        const formatted = numberValue.toLocaleString('pl-PL', {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });

        return includeUnit ? formatted + ' PLN' : formatted;
    }

    function switchMode(mode) {
        currentViewMode = mode;

        // 1. Zmiana guzików
        document.getElementById('btn-mode-comp').classList.toggle('active', mode === 'competitiveness');
        document.getElementById('btn-mode-profit').classList.toggle('active', mode === 'profit');

        // 2. Przełączanie widoczności filtrów
        if (mode === 'competitiveness') {
            $('#filters-competitiveness').fadeIn();
            $('#filters-profit').hide();
        } else {
            $('#filters-competitiveness').hide();
            $('#filters-profit').fadeIn();
        }

        // 3. Reset paginacji i odświeżenie listy oraz wykresu
        resetPage();
        filterPricesAndUpdateUI(); // Ta funkcja wywoła renderPrices i renderChart
    }

    const btnComp = document.getElementById('btn-mode-comp');
    const btnProfit = document.getElementById('btn-mode-profit');

    if (btnComp) {
        btnComp.addEventListener('click', function () { switchMode('competitiveness'); });
    }
    if (btnProfit) {
        btnProfit.addEventListener('click', function () { switchMode('profit'); });
    }
   

    function getStockStatusBadge(inStock) {
        if (inStock === true) {
            return '<span class="stock-available">Dostępny</span>';
        } else if (inStock === false) {
            return '<span class="stock-unavailable">Niedostępny</span>';
        } else {

            return '<span class="BD">Brak danych</span>';
        }
    }

    let selectedFlagsInclude = new Set();
    let selectedFlagsExclude = new Set();
    let selectedProductIds = loadSelectionFromStorage();
    let selectedPriceChanges = [];
    let isBulkFlaggingMode = false;

    const selectAllVisibleBtn = document.getElementById('selectAllVisibleBtn');
    const deselectAllVisibleBtn = document.getElementById('deselectAllVisibleBtn');

    function getOfferText(count) {
        if (count === 1) return `${count} Oferta`;
        const lastDigit = count % 10;
        if (count > 10 && count < 20) return `${count} Ofert`;
        if ([2, 3, 4].includes(lastDigit)) return `${count} Oferty`;
        return `${count} Ofert`;
    }

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

        sortCeneoSales: null,
        sortSalesTrendAmount: null,
        sortSalesTrendPercent: null
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

    const massStrategicBtn = document.getElementById('massStrategicBtn');

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

                    document.dispatchEvent(new CustomEvent('simulationDataCleared'));
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
        const productsToChange = currentlyFilteredPrices;

        if (!productsToChange || productsToChange.length === 0) {
            showGlobalUpdate('<p>Brak produktów do zmiany po filtracji.</p>');
            return;
        }

        let countAdded = 0;
        let countSkipped = 0;
        let countRejected = 0;
        let rejectionReasons = {};

        // 1. USTALENIE KONTEKSTU (TRYBU) DLA CAŁEJ MASOWEJ ZMIANY
        const modeApplied = currentViewMode; // 'competitiveness' lub 'profit'

        let stepPriceToSave = null;
        let stepUnitToSave = null;
        let indexTargetToSave = null;

        // Ustalenie wartości w zależności od trybu
        if (modeApplied === 'profit') {
            // Tryb Zysk
            indexTargetToSave = setPriceIndexTarget;
            stepPriceToSave = null;
            stepUnitToSave = null;
        } else {
            // Tryb Konkurencja
            stepPriceToSave = setStepPrice;
            const currentUsePriceDifference = document.getElementById('usePriceDifference').checked;
            stepUnitToSave = currentUsePriceDifference ? 'PLN' : '%';
            indexTargetToSave = null;
        }

        productsToChange.forEach(item => {
            // A. Odrzucamy produkty już zatwierdzone w bazie (Committed)
            if (item.committed) {
                countRejected++;
                rejectionReasons['Oferta już zaktualizowana (zatwierdzona)'] = (rejectionReasons['Oferta już zaktualizowana (zatwierdzona)'] || 0) + 1;
                return;
            }
            // --- POPRAWKA KRYTYCZNA: Sprawdzenie ceny 0.00 PLN ---
            // Musimy to sprawdzić ZANIM pójdziemy dalej.
            const currentPriceCheck = item.myPrice != null ? parseFloat(item.myPrice) : 0;
            if (currentPriceCheck <= 0.01) {
                countRejected++;
                rejectionReasons['Brak ceny oferty (0 PLN)'] = (rejectionReasons['Brak ceny oferty (0 PLN)'] || 0) + 1;
                return;
            }
            // B. OCHRONA PRZED NADPISANIEM
            const isAlreadyChanged = selectedPriceChanges.some(c => String(c.productId) === String(item.productId));

            if (isAlreadyChanged) {
                countSkipped++;
                return;
            }

            // C. Wyliczamy sugestię dla bieżącego trybu
            const suggestionData = calculateCurrentSuggestion(item);

            if (!suggestionData) {
                countRejected++;
                rejectionReasons['Brak sugestii'] = (rejectionReasons['Brak sugestii'] || 0) + 1;
                return;
            }

            const suggestedPrice = suggestionData.suggestedPrice;
            // POPRAWKA: Pewność co do ceny bieżącej (float)
            const currentPriceValue = item.myPrice != null ? parseFloat(item.myPrice) : 0;

            // D. Walidacja Identyfikatora
            let requiredField = '';
            let requiredLabel = '';
            switch (marginSettings.identifierForSimulation) {
                case 'ID': requiredField = item.externalId; requiredLabel = "ID"; break;
                case 'ProducerCode': requiredField = item.producerCode; requiredLabel = "Kod producenta"; break;
                case 'EAN': default: requiredField = item.ean; requiredLabel = "EAN"; break;
            }
            if (!requiredField || requiredField.toString().trim() === "") {
                countRejected++;
                rejectionReasons[`Brak identyfikatora (${requiredLabel})`] = (rejectionReasons[`Brak identyfikatora (${requiredLabel})`] || 0) + 1;
                return;
            }

            // E. Walidacja Marży
            if (marginSettings.useMarginForSimulation) {
                if (item.marginPrice == null) {
                    countRejected++;
                    rejectionReasons['Brak ceny zakupu'] = (rejectionReasons['Brak ceny zakupu'] || 0) + 1;
                    return;
                }

                const getNetPrice = (grossPrice) => {
                    if (marginSettings.usePriceWithDelivery && item.myPriceIncludesDelivery) {
                        const delivery = item.myPriceDeliveryCost != null ? parseFloat(item.myPriceDeliveryCost) : 0;
                        return grossPrice - delivery;
                    }
                    return grossPrice;
                };

                const oldNetPrice = getNetPrice(currentPriceValue);
                const newNetPrice = getNetPrice(suggestedPrice);

                const calculateMargin = (price, cost) => (cost !== 0) ? ((price - cost) / cost) * 100 : Infinity;
                let oldMargin = calculateMargin(oldNetPrice, item.marginPrice);
                let newMargin = calculateMargin(newNetPrice, item.marginPrice);

                if (marginSettings.minimalMarginPercent > 0) {
                    if (newMargin < marginSettings.minimalMarginPercent) {
                        if (!(oldMargin < marginSettings.minimalMarginPercent && newMargin > oldMargin)) {
                            countRejected++;
                            rejectionReasons['Zbyt niski narzut'] = (rejectionReasons['Zbyt niski narzut'] || 0) + 1;
                            return;
                        }
                    }
                } else if (marginSettings.enforceMinimalMargin) {
                    if (newMargin < 0) {
                        if (!(oldMargin < 0 && newMargin > oldMargin)) {
                            countRejected++;
                            rejectionReasons['Ujemny narzut'] = (rejectionReasons['Ujemny narzut'] || 0) + 1;
                            return;
                        }
                    }
                    if (marginSettings.minimalMarginPercent < 0 && newMargin < marginSettings.minimalMarginPercent) {
                        countRejected++;
                        rejectionReasons['Przekroczona strata'] = (rejectionReasons['Przekroczona strata'] || 0) + 1;
                        return;
                    }
                }
            }

            // F. Dodanie nowej zmiany - WYSYŁKA ZDARZENIA
            // POPRAWKA: Używamy currentScrapId jako fallback, jeśli item.scrapId jest puste
            const safeScrapId = item.scrapId || currentScrapId;

            const priceChangeEvent = new CustomEvent('priceBoxChange', {
                detail: {
                    productId: String(item.productId), // POPRAWKA: Zawsze String
                    productName: item.productName,
                    currentPrice: currentPriceValue,
                    newPrice: suggestedPrice,
                    storeId: storeId,
                    scrapId: safeScrapId, // POPRAWKA: Pewny ID scrapu
                    marginPrice: item.marginPrice,

                    mode: modeApplied,
                    indexTarget: indexTargetToSave,
                    priceIndexTarget: indexTargetToSave, // POPRAWKA: Dublowanie pola dla pewności obsługi przez listener
                    stepPriceApplied: stepPriceToSave,
                    stepUnitApplied: stepUnitToSave
                }
            });
            document.dispatchEvent(priceChangeEvent);

            countAdded++;
        });

        // Odświeżenie widoku
        refreshPriceBoxStates();

        // Podsumowanie
        let summaryHtml = `<p style="margin-bottom:8px; font-size:16px; font-weight:bold;">Masowa zmiana zakończona!</p>
                          <p>Tryb: <strong>${modeApplied === 'profit' ? 'Maksymalny Zysk' : 'Konkurencja'}</strong></p>
                          <p>Przeanalizowano: <strong>${productsToChange.length}</strong> SKU</p>
                          <p>Dodano nowych zmian: <strong>${countAdded}</strong> SKU</p>`;

        if (countSkipped > 0) {
            summaryHtml += `<p"><strong>Pominięto (już dodane): ${countSkipped} SKU</strong></p>`;
        }

        summaryHtml += `<p>Odrzucono (błędy/marża): <strong>${countRejected}</strong> SKU</p>`;

        if (countRejected > 0) {
            summaryHtml += `<p style="font-size:12px; margin-top:8px; border-top:1px solid #ccc; padding-top:5px;"><u>Powody odrzucenia:</u><br/>`;
            for (const [reason, count] of Object.entries(rejectionReasons)) {
                summaryHtml += `&bull; ${reason}: ${count}<br/>`;
            }
            summaryHtml += `</p>`;
        }

        showGlobalUpdate(summaryHtml);
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

    function getMarginBadgeClass(marginClass) {
        switch (marginClass) {
            case 'priceBox-diff-margin-ins':
                return 'price-badge-positive';
            case 'priceBox-diff-margin-minus-ins':
                return 'price-badge-negative';
            case 'priceBox-diff-margin-neutral-ins':
            default:
                return 'price-badge-neutral';
        }
    }


    let currentPage = 1;
    const itemsPerPage = 1000;

    function resetPage() {
        currentPage = 1;
        window.scrollTo(0, 0);
    }
    function refreshPriceBoxStates() {
        const currentStoredChangesJSON = localStorage.getItem('selectedPriceChanges_' + storeId);
        let currentActiveChangesMap = new Map();

        if (currentStoredChangesJSON) {
            try {
                const parsedData = JSON.parse(currentStoredChangesJSON);
                if (parsedData && parsedData.scrapId && Array.isArray(parsedData.changes)) {
                    selectedPriceChanges = parsedData.changes;
                    parsedData.changes.forEach(change => currentActiveChangesMap.set(String(change.productId), change));
                } else {
                    selectedPriceChanges = [];
                }
            } catch (err) {
                console.error("Błąd parsowania selectedPriceChanges:", err);
                selectedPriceChanges = [];
            }
        } else {
            selectedPriceChanges = [];
        }

        const allPriceBoxes = document.querySelectorAll('#priceContainer .price-box');

        allPriceBoxes.forEach(priceBox => {
            const productId = priceBox.dataset.productId;
            if (!productId) return;

            const item = allPrices.find(p => String(p.productId) === String(productId));
            if (!item) return;

            const activeChange = currentActiveChangesMap.get(String(productId));

 
            const priceBoxMyColumn = priceBox.querySelector('.my-price-column');

            const priceBoxActionColumn = priceBox.querySelector('.price-box-column-action');

            if (priceBoxMyColumn) {
                const myPriceTextDiv = priceBoxMyColumn.querySelector('.price-box-column-text');
                if (myPriceTextDiv) {
                    const priceLineDiv = myPriceTextDiv.querySelector('div[style*="display: flex"]');
                    if (priceLineDiv) {
                        const myPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;
                        const myPriceFormatted = formatPricePL(myPrice);

                        if (item.committed && item.committed.newPrice) {
                      
                            const newCommittedPrice = parseFloat(item.committed.newPrice);
                            priceLineDiv.innerHTML = `
                                <s style="color: #999; font-size: 17px;">${formatPricePL(myPrice)}</s>
                   
                            `;
                        } else {
                        
                            priceLineDiv.innerHTML = `
                                <span style="font-weight: 500; font-size: 17px;">${myPriceFormatted}</span>
                            `;
                        }
                    }
                }
            }

            if (priceBoxActionColumn) {

                if (item.committed && !activeChange) {
                    priceBox.classList.remove('price-changed');
                    return;
                }

                const newSuggestionData = calculateCurrentSuggestion(item);

                if (newSuggestionData) {
                    // Tutaj przekazujemy activeChange do renderera HTML (jeśli masz taką logikę w renderSuggestionBlockHTML)
                    // Ale kluczowe jest to, co dzieje się PO renderowaniu, czyli aktywacja przycisku:

                    const suggestionRenderResult = renderSuggestionBlockHTML(item, newSuggestionData, activeChange); // activeChange przekazany opcjonalnie
                    priceBoxActionColumn.innerHTML = suggestionRenderResult.html;

                    const newActionLine = priceBoxActionColumn.querySelector(suggestionRenderResult.actionLineSelector);

                    if (newActionLine) {
                        const currentMyPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;

                        // Podpinamy listener kliknięcia (dla nowej zmiany)
                        attachPriceChangeListener(newActionLine, newSuggestionData.suggestedPrice, priceBox, item.productId, item.productName, currentMyPrice, item);

                        if (activeChange) {
                            const btn = newActionLine.querySelector('.simulate-change-btn');
                            if (btn) {
                                // TU JEST ZMIANA: Przekazujemy zapisane parametry (mode, indexTarget)
                                activateChangeButton(
                                    btn,
                                    newActionLine,
                                    priceBox,
                                    activeChange.stepPriceApplied,
                                    activeChange.stepUnitApplied,
                                    activeChange.mode,        // <--- Odczyt z zapisanego obiektu
                                    activeChange.indexTarget  // <--- Odczyt z zapisanego obiektu
                                );
                            }
                            priceBox.classList.add('price-changed');
                        } else {
                            priceBox.classList.remove('price-changed');
                        }
                    }
                } else {

                    priceBoxActionColumn.innerHTML = '';
                    priceBox.classList.remove('price-changed');
                }
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

            input.value = (min !== undefined) ? min.toFixed(2) : "0.00";
            return;
        }

        if (min !== undefined && value < min) {
            input.value = min.toFixed(2);

        } else if (max !== undefined && value > max) {
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
    });

    price2Input.addEventListener("blur", () => {

        enforceLimits(price2Input, 0.01);
    });

    stepPriceInput.addEventListener("blur", () => {

        enforceLimits(stepPriceInput, -100.00, 1000.00);
    });
    const stepPriceInputIndicator = document.getElementById('stepPrice');
    const stepPriceIndicatorSpan = document.getElementById('stepPriceIndicator');

    function updateStepPriceIndicator() {
        if (!stepPriceInputIndicator || !stepPriceIndicatorSpan) return;

        const valueStr = stepPriceInputIndicator.value.replace(',', '.');
        const value = parseFloat(valueStr);

        let iconClass = '';
        let titleText = '';

        if (isNaN(value)) {

        } else if (value < 0) {
            iconClass = 'fa-solid fa-minus';
            titleText = 'Obniżka (cena < konkurenta)';
        } else if (value > 0) {
            iconClass = 'fa-solid fa-plus';
            titleText = 'Podwyżka (cena > konkurenta)';
        } else {
            iconClass = 'fa-solid fa-equals';
            titleText = 'Wyrównanie ceny (cena = konkurenta)';
        }

        if (iconClass) {
            stepPriceIndicatorSpan.innerHTML = `<i class="${iconClass}"></i>`;
        } else {
            stepPriceIndicatorSpan.innerHTML = '';
        }

        stepPriceIndicatorSpan.title = titleText;
    }

    stepPriceInputIndicator.addEventListener('input', updateStepPriceIndicator);
    stepPriceInputIndicator.addEventListener('change', updateStepPriceIndicator);
    setTimeout(updateStepPriceIndicator, 0);

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

    function extractRankNumber(rankStr) {
        if (!rankStr) return null;

        const part = rankStr.toString().split('/')[0].trim();

        const num = parseInt(part, 10);
        return isNaN(num) ? null : num;
    }

    function extractTotalOffersCount(posStr) {
        if (!posStr) return "";
        const parts = posStr.toString().split('/');
        if (parts.length > 1) {
            return parts[1].trim();
        }
        return "";

    }

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
                setPriceIndexTarget = response.priceIndexTarget || 100.00;
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
                            marginClass = 'priceBox-diff-margin-neutral-ins';
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
                            marginClass = marginAmount >= 0 ? 'priceBox-diff-margin-ins' : 'priceBox-diff-margin-minus-ins';
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

                const targetInput = document.getElementById('priceIndexTargetInput');
                if (targetInput) targetInput.value = setPriceIndexTarget;

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
                updateStepPriceIndicator();
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
        // --- 1. Filtrowanie Główne (Zależne od Trybu) ---
        let filteredPrices = data;

        if (currentViewMode === 'competitiveness') {
            // Stary tryb: Filtrowanie po klasach kolorystycznych (prGood, prToHigh itp.)
            const selectedColors = Array.from(document.querySelectorAll('.colorFilter:checked')).map(checkbox => checkbox.value);
            if (selectedColors.length) {
                filteredPrices = filteredPrices.filter(item => selectedColors.includes(item.colorClass));
            }
        } else {
            // Nowy tryb: Filtrowanie po bucketach indeksu (market-average, market-overpriced itp.)
            const selectedBuckets = Array.from(document.querySelectorAll('.bucketFilter:checked')).map(checkbox => checkbox.value);
            if (selectedBuckets.length) {
                filteredPrices = filteredPrices.filter(item => selectedBuckets.includes(item.marketBucket));
            }
        }

        // --- 2. Filtrowanie Wspólne (Niezależne od trybu) ---
        const selectedBid = document.getElementById('bidFilter').checked;
        const suspiciouslyLowFilter = document.getElementById('suspiciouslyLowFilter').checked;

        const selectedStockMyStore = Array.from(document.querySelectorAll('.stockFilterMyStore:checked')).map(checkbox => {
            if (checkbox.value === 'null') return null;
            return checkbox.value === 'true';
        });
        const selectedStockCompetitor = Array.from(document.querySelectorAll('.stockFilterCompetitor:checked')).map(checkbox => {
            if (checkbox.value === 'null') return null;
            return checkbox.value === 'true';
        });

        // Pozycja suwaka
        const positionSliderValues = positionSlider.noUiSlider.get();
        const positionMin = parseInt(positionSliderValues[0]);
        const positionMax = parseInt(positionSliderValues[1]);

        // Oferty suwak
        const offerSliderValues = offerSlider.noUiSlider.get();
        const offerMin = parseInt(offerSliderValues[0]);
        const offerMax = parseInt(offerSliderValues[1]);

        // Aplikacja filtrów wspólnych
        filteredPrices = filteredPrices.filter(item => {
            const position = item.myPosition;
            if (position === null || position === undefined) return true;
            const numericPosition = parseInt(position);
            return numericPosition >= positionMin && numericPosition <= positionMax;
        });

        filteredPrices = filteredPrices.filter(item => {
            const storeCount = item.storeCount;
            return storeCount >= offerMin && storeCount <= offerMax;
        });

        if (suspiciouslyLowFilter) {
            filteredPrices = filteredPrices.filter(item =>
                item.singleBestCheaperDiffPerc !== null && item.singleBestCheaperDiffPerc > 25
            );
            filteredPrices.sort((a, b) => b.singleBestCheaperDiffPerc - a.singleBestCheaperDiffPerc);
        }

        if (selectedFlagsExclude.size > 0) {
            filteredPrices = filteredPrices.filter(item => {
                if (selectedFlagsExclude.has('noFlag') && item.flagIds.length === 0) return false;
                for (const excl of selectedFlagsExclude) {
                    if (excl !== 'noFlag' && item.flagIds.includes(parseInt(excl))) return false;
                }
                return true;
            });
        }

        if (selectedFlagsInclude.size > 0) {
            filteredPrices = filteredPrices.filter(item => {
                if (selectedFlagsInclude.has('noFlag') && item.flagIds.length === 0) return true;
                return item.flagIds.some(fid => selectedFlagsInclude.has(fid.toString()));
            });
        }

        if (selectedBid) {
            filteredPrices = filteredPrices.filter(item => item.myIsBidding === "1");
        }

        if (selectedStockMyStore.length) {
            filteredPrices = filteredPrices.filter(item => selectedStockMyStore.includes(item.myEntryInStock));
        }

        if (selectedStockCompetitor.length) {
            filteredPrices = filteredPrices.filter(item => selectedStockCompetitor.includes(item.bestEntryInStock));
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

    function createSalesTrendHtml(item) {
        const trendStatus = item.salesTrendStatus;

        if (trendStatus === 'NoData' || trendStatus === null) {
            return `
        <div class="price-box-column-offers-a">
            <span class="data-channel">
                <i class="fas fa-chart-line" style="font-size: 15px; color: grey; margin-top:1px;" title="Trend sprzedaży"></i>
            </span>
            <div class="offer-count-box">
                <p>Brak danych</p>
            </div>
        </div>`;
        }

        const imagePath = '/images/';
        const imageName = `Flag-${trendStatus}.svg`;

        let trendText = '';

        if (item.salesDifference !== 0 && item.salesDifference !== null) {
            const sign = item.salesDifference > 0 ? '+' : '';
            const percentageText = item.salesPercentageChange !== null ? ` (${sign}${item.salesPercentageChange.toFixed(2)}%)` : '';
            trendText = `<p>${sign}${item.salesDifference}${percentageText}</p>`;
        } else {

            trendText = `<p>Bez zmian</p>`;
        }

        return `
    <div class="price-box-column-offers-a">
        <span class="data-channel" title="Trend sprzedaży">
             <img src="${imagePath}${imageName}" alt="${trendStatus}" style="width:18px; height:18px;" />
        </span>
        <div class="offer-count-box">
            ${trendText}
        </div>
    </div>`;
    }

   
    function activateChangeButton(button, actionLine, priceBox, stepPriceApplied, stepUnitApplied, mode, indexTarget) {
        if (!button) return;

        let btnInfoText = "";
        let badgeHtml = "";

        // Domyślny tryb jeśli brak danych (np. stare wpisy w cache)
        const currentMode = mode || 'competitiveness';

        if (currentMode === 'profit') {
            // --- TRYB ZYSK (PROFIT) ---
            const targetVal = indexTarget != null ? parseFloat(indexTarget) : 100.00;

            btnInfoText = ` Indeks ${targetVal}%`;

            // Fioletowy badge dla strategii zysku
            badgeHtml = `<div class="strategy-badge-dynamic mode-profit">
                <i class="fa-solid fa-dollar-sign" style="margin-right: 5px;"></i>
                Strategia optymalizacji zysku
             </div>`;

        } else {
            // --- TRYB KONKURENCJA (COMPETITIVENESS) ---
            let formattedStep = "0.00";
            let unit = "%";

            // Ustalanie jednostki (z argumentu lub z checkboxa globalnego)
            if (stepUnitApplied) {
                unit = stepUnitApplied;
            } else {
                const useDiffCheckbox = document.getElementById('usePriceDifference');
                unit = (useDiffCheckbox && useDiffCheckbox.checked) ? 'PLN' : '%';
            }

            if (stepPriceApplied !== null && stepPriceApplied !== undefined) {
                formattedStep = parseFloat(stepPriceApplied).toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            }

            btnInfoText = ` Krok ${formattedStep} ${unit}`;

            // Niebieski badge dla strategii konkurencji
            badgeHtml = `<div class="strategy-badge-dynamic mode-competitiveness">
                <i class="fa-solid fa-bolt" style="margin-right: 5px;"></i>
                Strategia maksymalnej konkurencyjności
             </div>`;
        }

        // 1. Wstawienie Badge'a (usuwamy stary, jeśli istnieje, żeby nie dublować)
        const existingBadge = actionLine.querySelector('.strategy-badge-dynamic');
        if (existingBadge) existingBadge.remove();

        actionLine.insertAdjacentHTML('afterbegin', badgeHtml);

        // 2. Stylizacja i Tekst Przycisku
        button.classList.add('active');
        button.innerHTML = `${btnInfoText} | Dodano`;

        // 3. Dodanie ikony kosza (usuwania)
        const removeLink = document.createElement('span');
        removeLink.innerHTML = " <i class='fa fa-trash' style='font-size:12px; display:flex; color:white; margin-top:3px; margin-left:5px;'></i>";
        removeLink.style.textDecoration = "none";
        removeLink.style.cursor = "pointer";
        removeLink.style.pointerEvents = 'auto';
        removeLink.title = "Usuń tę zmianę";

        // Obsługa kliknięcia w kosz
        removeLink.addEventListener('click', function (ev) {
            ev.stopPropagation(); // Ważne: żeby nie triggerować kliknięcia w sam przycisk
            const productId = priceBox.dataset.productId;

            // A. Aktualizacja tablicy w pamięci
            if (typeof selectedPriceChanges !== 'undefined') {
                selectedPriceChanges = selectedPriceChanges.filter(c => String(c.productId) !== String(productId));
            }

            // B. Wysłanie zdarzenia, że usunięto zmianę
            // UWAGA: To zdarzenie jest odbierane przez globalny listener 'priceBoxChangeRemove' (który masz w głównym kodzie),
            // i to on odpowiada za zresetowanie wyglądu przycisku i przeliczenie sugestii na nowo.
            const removeEvent = new CustomEvent('priceBoxChangeRemove', {
                detail: { productId }
            });
            document.dispatchEvent(removeEvent);
        });

        button.appendChild(removeLink);

        // 4. Oznaczenie kontenera jako zmienionego
        if (!priceBox.classList.contains('price-changed')) {
            priceBox.classList.add('price-changed');
        }
    }




    function attachPriceChangeListener(actionLine, suggestedPrice, priceBox, productId, productName, currentPriceValue, item) {

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

        const button = actionLine.querySelector('.simulate-change-btn');

        if (!requiredField || requiredField.toString().trim() === "") {
            if (button) button.disabled = true;
            actionLine.title = `Produkt musi mieć zmapowany ${requiredLabel}`;
            actionLine.style.cursor = 'not-allowed';
            actionLine.style.opacity = '0.6';
            return;
        }

        actionLine.addEventListener('click', function (e) {
            e.stopPropagation();

            const currentButton = this.querySelector('.simulate-change-btn');

            if (priceBox.classList.contains('price-changed') || (currentButton && currentButton.classList.contains('active'))) {
                console.log("Zmiana ceny już aktywna dla produktu", productId);
                return;
            }

            let finalMargin = null;
            let formattedMarginInfo = '';

            if (marginSettings.useMarginForSimulation) {
                if (item.marginPrice == null) {
                    showGlobalNotification(
                        `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny nie została dodana</p>
                     <p>Symulacja cenowa z narzutem jest włączona – produkt musi posiadać cenę zakupu.</p>`
                    );
                    return;
                }

                const getNetPrice = (grossPrice) => {
                    if (marginSettings.usePriceWithDelivery && item.myPriceIncludesDelivery) {
                        const delivery = item.myPriceDeliveryCost != null && !isNaN(parseFloat(item.myPriceDeliveryCost)) ? parseFloat(item.myPriceDeliveryCost) : 0;
                        return grossPrice - delivery;
                    }
                    return grossPrice;
                };

                const oldNetPrice = getNetPrice(currentPriceValue);
                const newNetPrice = getNetPrice(suggestedPrice);

                const calculateMargin = (price, cost) => (cost !== 0) ? ((price - cost) / cost) * 100 : Infinity;

                let oldMargin = parseFloat(calculateMargin(oldNetPrice, item.marginPrice).toFixed(2));
                let newMargin = parseFloat(calculateMargin(newNetPrice, item.marginPrice).toFixed(2));

                finalMargin = newMargin;

                let suggestedPriceMsg = formatPricePL(suggestedPrice);
                if (marginSettings.usePriceWithDelivery && item.myPriceIncludesDelivery) {
                    suggestedPriceMsg += ` (z dostawą) | ${formatPricePL(newNetPrice)} (bez dostawy)`;
                }

                if (marginSettings.minimalMarginPercent > 0) {
                    if (newMargin < marginSettings.minimalMarginPercent) {

                        if (!(oldMargin < marginSettings.minimalMarginPercent && newMargin > oldMargin)) {
                            let reason = (oldMargin >= marginSettings.minimalMarginPercent)
                                ? `Zmiana obniża narzut z <strong>${oldMargin}%</strong> do <strong>${newMargin}%</strong> (poniżej progu ${marginSettings.minimalMarginPercent}%).`
                                : `Nowy narzut (<strong>${newMargin}%</strong>) nadal jest poniżej minimum i nie poprawia sytuacji.`;

                            showGlobalNotification(
                                `<p style="margin:8px 0; font-weight:bold;">Zmiana odrzucona (Narzut)</p>
                             <p>${reason}</p>
                             <p>Cena zakupu: <strong>${formatPricePL(item.marginPrice)}</strong></p>`
                            );
                            return;
                        }
                    }
                }

                else if (marginSettings.enforceMinimalMargin) {
                    if (newMargin < 0) {
                        if (!(oldMargin < 0 && newMargin > oldMargin)) {
                            showGlobalNotification(
                                `<p style="margin:8px 0; font-weight:bold;">Zmiana odrzucona (Strata)</p>
                             <p>Nowa cena generuje ujemny narzut: <strong>${newMargin}%</strong>.</p>
                             <p>Cena zakupu: <strong>${formatPricePL(item.marginPrice)}</strong></p>`
                            );
                            return;
                        }
                    }

                    if (marginSettings.minimalMarginPercent < 0 && newMargin < marginSettings.minimalMarginPercent) {
                        showGlobalNotification(
                            `<p style="margin:8px 0; font-weight:bold;">Zmiana odrzucona (Zbyt duża strata)</p>
                         <p>Narzut <strong>${newMargin}%</strong> przekracza dopuszczalną stratę (<strong>${marginSettings.minimalMarginPercent}%</strong>).</p>`
                        );
                        return;
                    }
                }
            }

            const modeApplied = currentViewMode;

            let stepPriceToSave = null;
            let stepUnitToSave = null;
            let indexTargetToSave = null;

            if (modeApplied === 'profit') {

                indexTargetToSave = setPriceIndexTarget;

                stepPriceToSave = null;
                stepUnitToSave = null;
            } else {

                stepPriceToSave = setStepPrice;
                const useDiff = document.getElementById('usePriceDifference');
                stepUnitToSave = (useDiff && useDiff.checked) ? 'PLN' : '%';

                indexTargetToSave = null;
            }

            const priceChangeEvent = new CustomEvent('priceBoxChange', {
                detail: {
                    productId: item.productId,
                    productName: item.productName,
                    currentPrice: currentPriceValue,
                    newPrice: suggestedPrice,
                    storeId: storeId,
                    scrapId: item.scrapId,
                    marginPrice: item.marginPrice,

                    mode: modeApplied,
                    stepPriceApplied: stepPriceToSave,
                    stepUnitApplied: stepUnitToSave,
                    indexTarget: indexTargetToSave
                }
            });
            document.dispatchEvent(priceChangeEvent);

            activateChangeButton(
                currentButton,
                this,
                priceBox,
                stepPriceToSave,
                stepUnitToSave,
                modeApplied,
                indexTargetToSave
            );

            let message = `<p style="margin-bottom:8px; font-size:16px; font-weight:bold;">Zmiana ceny dodana</p>`;
            message += `<p style="margin:4px 0;"><strong>Produkt:</strong> ${productName}</p>`;

            let displayPriceLabel = "Nowa cena";
            let newPriceFormatted = formatPricePL(suggestedPrice);

            if (marginSettings.usePriceWithDelivery && item.myPriceIncludesDelivery) {
                const delivery = item.myPriceDeliveryCost != null && !isNaN(parseFloat(item.myPriceDeliveryCost)) ? parseFloat(item.myPriceDeliveryCost) : 0;
                const netPrice = suggestedPrice - delivery;
                displayPriceLabel = "Nowa cena (z dostawą)";
                newPriceFormatted += ` <span style="font-size:0.9em; color:#666;">(bez dostawy: ${formatPricePL(netPrice)})</span>`;
            }

            message += `<p style="margin:4px 0;"><strong>${displayPriceLabel}:</strong> ${newPriceFormatted}</p>`;

            if (marginSettings.useMarginForSimulation && finalMargin !== null) {
                message += `<p style="margin:4px 0;"><strong>Nowy narzut:</strong> ${finalMargin.toFixed(2)}%</p>`;
                message += `<p style="margin:4px 0; font-size: 0.9em; color:#555;">(Cena zakupu: ${formatPricePL(item.marginPrice)})</p>`;
            }

            showGlobalUpdate(message);
        });

        const existingChange = selectedPriceChanges.find(change =>
            String(change.productId) === String(productId)
        );

        if (existingChange) {

            activateChangeButton(
                button,
                actionLine,
                priceBox,
                existingChange.stepPriceApplied,
                existingChange.stepUnitApplied,
                existingChange.mode,
                existingChange.priceIndexTarget || existingChange.indexTarget

            );
        }
    }




    document.addEventListener('priceBoxChangeRemove', function (event) {
        const { productId } = event.detail;
        if (!productId) return;

        const priceBox = document.querySelector(`#priceContainer .price-box[data-product-id="${productId}"]`);

        if (priceBox) {
            priceBox.classList.remove('price-changed');

            // Usuwamy badge, jeśli istnieje
            const actionLine = priceBox.querySelector('.price-action-line');
            if (actionLine) {
                const badge = actionLine.querySelector('.strategy-badge-dynamic');
                if (badge) badge.remove();
            }

            const activeButtons = priceBox.querySelectorAll('.simulate-change-btn.active');

            activeButtons.forEach(button => {

                button.classList.remove('active');

                button.innerHTML = button.dataset.originalText || '<span class="color-square-turquoise"></span> Dodaj zmianę ceny';

                const removeIcon = button.querySelector('span > i.fa-trash');
                if (removeIcon) {
                    removeIcon.parentElement.remove();
                }
            });

            const item = allPrices.find(p => String(p.productId) === String(productId));
            const priceBoxColumnInfo = priceBox.querySelector('.price-box-column-action');
            if (item && priceBoxColumnInfo) {
                const newSuggestionData = calculateCurrentSuggestion(item);
                if (newSuggestionData) {
                    const suggestionRenderResult = renderSuggestionBlockHTML(item, newSuggestionData);
                    priceBoxColumnInfo.innerHTML = suggestionRenderResult.html;
                    const newActionLine = priceBoxColumnInfo.querySelector(suggestionRenderResult.actionLineSelector);
                    if (newActionLine) {
                        const currentMyPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;
                        attachPriceChangeListener(newActionLine, newSuggestionData.suggestedPrice, priceBox, item.productId, item.productName, currentMyPrice, item);
                    }
                } else {
                    priceBoxColumnInfo.innerHTML = '';
                }
            }

        } else {

            console.warn(`Nie znaleziono priceBox dla productId: ${productId} do zresetowania UI po priceBoxChangeRemove.`);
        }
    });

    function getMarginBadgeClass(marginClass) {
        switch (marginClass) {
            case 'priceBox-diff-margin-ins':
                return 'price-badge-positive';
            case 'priceBox-diff-margin-minus-ins':
                return 'price-badge-negative';
            case 'priceBox-diff-margin-neutral-ins':
            default:
                return 'price-badge-neutral';
        }
    }

    //function calculateCurrentSuggestion(item) {
    //    const currentMyPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;
    //    const currentLowestPrice = item.lowestPrice != null ? parseFloat(item.lowestPrice) : null;
    //    const currentSavings = item.savings != null ? item.savings.toFixed(2) : "N/A";
    //    const currentSetStepPrice = setStepPrice;
    //    const currentUsePriceDifference = document.getElementById('usePriceDifference').checked;

    //    let suggestedPrice = null;
    //    let basePriceForCalc = null;
    //    let priceType = null;

    //    if (item.colorClass === "prToLow" || item.colorClass === "prIdeal") {
    //        if (currentMyPrice != null && currentSavings !== "N/A") {
    //            const savingsValue = parseFloat(currentSavings.replace(',', '.'));
    //            basePriceForCalc = currentMyPrice + savingsValue;
    //            priceType = 'raise';
    //        }
    //    } else if (item.colorClass === "prMid" || item.colorClass === "prToHigh") {
    //        if (currentMyPrice != null && currentLowestPrice != null) {
    //            basePriceForCalc = currentLowestPrice;
    //            priceType = 'lower';
    //        }
    //    } else if (item.colorClass === "prGood") {
    //        if (currentMyPrice != null) {

    //            basePriceForCalc = currentMyPrice;
    //            if (item.storeCount > 1 || currentSetStepPrice > 0) {
    //                priceType = 'good_step';
    //            } else {
    //                priceType = 'good_no_step';
    //            }
    //        }
    //    }

    //    if (basePriceForCalc !== null && priceType !== null) {
    //        if (priceType === 'good_no_step') {
    //            suggestedPrice = basePriceForCalc;
    //        } else {
    //            if (currentUsePriceDifference) {
    //                suggestedPrice = basePriceForCalc + currentSetStepPrice;
    //            } else {
    //                suggestedPrice = basePriceForCalc * (1 + (currentSetStepPrice / 100));
    //            }
    //            if (suggestedPrice < 0.01) { suggestedPrice = 0.01; }
    //        }
    //    }

    //    if (suggestedPrice === null) {
    //        return null;
    //    }

    //    const totalChangeAmount = suggestedPrice - (currentMyPrice || 0);
    //    const percentageChange = (currentMyPrice != null && currentMyPrice > 0) ? (totalChangeAmount / currentMyPrice) * 100 : 0;
    //    let arrowClass = '';
    //    if (priceType === 'raise') {
    //        arrowClass = totalChangeAmount > 0 ? (item.colorClass === 'prToLow' ? 'arrow-up-black' : 'arrow-up-turquoise') : (totalChangeAmount < 0 ? 'arrow-down-turquoise' : 'no-change-icon-turquoise');
    //    } else if (priceType === 'lower') {
    //        arrowClass = totalChangeAmount > 0 ? (item.colorClass === "prMid" ? "arrow-up-yellow" : "arrow-up-red") : (totalChangeAmount < 0 ? (item.colorClass === "prMid" ? "arrow-down-yellow" : "arrow-down-red") : 'no-change-icon');
    //    } else if (priceType === 'good_step' || priceType === 'good_no_step') {
    //        arrowClass = totalChangeAmount > 0 ? 'arrow-up-green' : (totalChangeAmount < 0 ? 'arrow-down-green' : 'no-change-icon-turquoise');
    //    }

    //    return {
    //        suggestedPrice: suggestedPrice,
    //        totalChangeAmount: totalChangeAmount,
    //        percentageChange: percentageChange,
    //        arrowClass: arrowClass,
    //        priceType: priceType
    //    };
    //}

    function calculateCurrentSuggestion(item) {
        const currentMyPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;

        // --- POPRAWKA KRYTYCZNA: Blokada, jeśli brak naszej ceny ---
        // Jeśli cena jest null, 0 lub bliska zeru -> nie proponuj żadnej zmiany.
        if (currentMyPrice === null || currentMyPrice <= 0.01) {
            return null;
        }

        // Wspólne zmienne
        let suggestedPrice = null;
        let totalChangeAmount = 0;
        let percentageChange = 0;
        let arrowClass = '';
        let priceType = null;

        // =========================================================
        // TRYB: PROFIT (INDEKS CENOWY)
        // =========================================================
        if (currentViewMode === 'profit') {
            const medianPrice = item.marketAveragePrice; // Mediana z backendu

            // Jeśli nie ma mediany (np. brak konkurencji), nie możemy wyliczyć celu
            if (medianPrice === null || medianPrice === undefined || medianPrice === 0) {
                return null;
            }

            // WZÓR: Cena = Mediana * (Cel% / 100)
            const targetRatio = setPriceIndexTarget / 100.0;
            suggestedPrice = medianPrice * targetRatio;

            // Zaokrąglenie do 2 miejsc
            suggestedPrice = Math.round(suggestedPrice * 100) / 100;

            // Obsługa dostawy w kalkulacji
            // Mediana na backendzie jest wyliczana na podstawie cen "takich jak w tabeli".
            // Jeśli marginSettings.usePriceWithDelivery == true, to Mediana zawiera dostawę.
            // My w bazie chcemy zapisać cenę produktu (netto od dostawy).
            if (marginSettings.usePriceWithDelivery && item.myPriceIncludesDelivery) {
                const myDelivery = item.myPriceDeliveryCost != null ? parseFloat(item.myPriceDeliveryCost) : 0;
                // Odejmujemy naszą dostawę, żeby uzyskać cenę "na półkę"
                suggestedPrice = suggestedPrice - myDelivery;
                if (suggestedPrice < 0.01) suggestedPrice = 0.01;
            }

            priceType = 'index_target'; // Typ dla kolorowania strzałek
        }
        // =========================================================
        // TRYB: KONKURENCYJNOŚĆ (STARA LOGIKA)
        // =========================================================
        else {
            const currentLowestPrice = item.lowestPrice != null ? parseFloat(item.lowestPrice) : null;
            const currentSavings = item.savings != null ? item.savings.toFixed(2) : "N/A";
            const currentSetStepPrice = setStepPrice;
            const currentUsePriceDifference = document.getElementById('usePriceDifference').checked;

            let basePriceForCalc = null;

            if (item.colorClass === "prToLow" || item.colorClass === "prIdeal") {
                if (currentMyPrice != null && currentSavings !== "N/A") {
                    const savingsValue = parseFloat(currentSavings.replace(',', '.'));
                    basePriceForCalc = currentMyPrice + savingsValue;
                    priceType = 'raise';
                }
            } else if (item.colorClass === "prMid" || item.colorClass === "prToHigh") {
                if (currentMyPrice != null && currentLowestPrice != null) {
                    basePriceForCalc = currentLowestPrice;
                    priceType = 'lower';
                }
            } else if (item.colorClass === "prGood") {
                if (currentMyPrice != null) {
                    basePriceForCalc = currentMyPrice;
                    if (item.storeCount > 1 || currentSetStepPrice > 0) {
                        priceType = 'good_step';
                    } else {
                        priceType = 'good_no_step';
                    }
                }
            }

            if (basePriceForCalc !== null && priceType !== null) {
                if (priceType === 'good_no_step') {
                    suggestedPrice = basePriceForCalc;
                } else {
                    if (currentUsePriceDifference) {
                        suggestedPrice = basePriceForCalc + currentSetStepPrice;
                    } else {
                        suggestedPrice = basePriceForCalc * (1 + (currentSetStepPrice / 100));
                    }
                    if (suggestedPrice < 0.01) { suggestedPrice = 0.01; }
                }
            }
        }

        // --- WSPÓLNE OBLICZENIA KOŃCOWE ---
        if (suggestedPrice === null) {
            return null;
        }

        totalChangeAmount = suggestedPrice - (currentMyPrice || 0);
        percentageChange = (currentMyPrice != null && currentMyPrice > 0) ? (totalChangeAmount / currentMyPrice) * 100 : 0;

        // Dobór kolorów strzałek
        if (priceType === 'index_target') {
            // W trybie Profit po prostu pokazujemy czy rośnie czy maleje
            if (totalChangeAmount > 0) arrowClass = 'arrow-up-turquoise';
            else if (totalChangeAmount < 0) arrowClass = 'arrow-down-turquoise';
            else arrowClass = 'no-change-icon-turquoise';
        } else {
            // Stara logika kolorów dla konkurencji
            if (priceType === 'raise') {
                arrowClass = totalChangeAmount > 0 ? (item.colorClass === 'prToLow' ? 'arrow-up-black' : 'arrow-up-turquoise') : (totalChangeAmount < 0 ? 'arrow-down-turquoise' : 'no-change-icon-turquoise');
            } else if (priceType === 'lower') {
                arrowClass = totalChangeAmount > 0 ? (item.colorClass === "prMid" ? "arrow-up-yellow" : "arrow-up-red") : (totalChangeAmount < 0 ? (item.colorClass === "prMid" ? "arrow-down-yellow" : "arrow-down-red") : 'no-change-icon');
            } else if (priceType === 'good_step' || priceType === 'good_no_step') {
                arrowClass = totalChangeAmount > 0 ? 'arrow-up-green' : (totalChangeAmount < 0 ? 'arrow-down-green' : 'no-change-icon-turquoise');
            }
        }

        return {
            suggestedPrice: suggestedPrice,
            totalChangeAmount: totalChangeAmount,
            percentageChange: percentageChange,
            arrowClass: arrowClass,
            priceType: priceType
        };
    }

    function renderSuggestionBlockHTML(item, suggestionData, activeChange = null) {
        // USUNIĘTO BLOK 'if (activeChange)' - teraz zawsze renderujemy strukturę z przyciskiem,
        // a stan "Dodano" nakładamy dynamicznie przez activateChangeButton.

        // Jeśli z jakiegoś powodu brak danych sugestii, nic nie renderujemy
        if (!suggestionData) {
            return { html: '', actionLineSelector: null, suggestedPrice: null, myPrice: null };
        }

        const { suggestedPrice, totalChangeAmount, percentageChange, arrowClass, priceType } = suggestionData;

        // Zmienne pomocnicze
        const myPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;
        const marginPrice = item.marginPrice != null ? parseFloat(item.marginPrice) : null;
        let squareColorClass = 'color-square-turquoise';

        // --- BUDOWANIE STRUKTURY HTML ---
        const strategicPriceBox = document.createElement('div');
        strategicPriceBox.className = 'price-box-column';

        const contentWrapper = document.createElement('div');
        contentWrapper.className = 'price-box-column-text';

        // 1. Nowa Cena (duża)
        const newPriceDisplay = document.createElement('div');
        newPriceDisplay.style.display = 'flex';
        newPriceDisplay.style.alignItems = 'center';
        newPriceDisplay.innerHTML = `<span class="${arrowClass}" style="margin-right: 8px;"></span><div><span style="font-weight: 500; font-size: 17px;">${formatPricePL(suggestedPrice)}</span></div>`;

        // 2. Label "Zmiana ceny..."
        const priceChangeLabel = document.createElement('div');
        priceChangeLabel.textContent = 'Zmiana ceny Twojej oferty';
        priceChangeLabel.style.fontSize = '14px';
        priceChangeLabel.style.color = '#212529';

        // 3. Różnica cenowa (badge + kwota)
        const priceDifferenceDisplay = document.createElement('div');
        priceDifferenceDisplay.style.marginTop = '3px';
        let badgeText = '';
        if (totalChangeAmount > 0) badgeText = 'Podwyżka ceny';
        else if (totalChangeAmount < 0) badgeText = 'Obniżka ceny';
        else badgeText = 'Brak zmiany';

        const badgeHtml = `<div class="price-diff-stack-badge">${badgeText}</div>`;
        priceDifferenceDisplay.innerHTML =
            `<div class="price-diff-stack" style="text-align: left;">` +
            `${badgeHtml}` +
            `<span class="diff-amount small-font">${totalChangeAmount > 0 ? '+' : ''}${formatPricePL(totalChangeAmount, false)} PLN</span>&nbsp;` +
            `<span class="diff-percentage small-font">(${percentageChange > 0 ? '+' : ''}${percentageChange.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%)</span>` +
            `</div>`;

        contentWrapper.appendChild(newPriceDisplay);
        contentWrapper.appendChild(priceChangeLabel);
        contentWrapper.appendChild(priceDifferenceDisplay);

        // 4. Kalkulacja Narzutu (jeśli dostępna)
        if (marginPrice !== null && suggestedPrice !== null) {
            let newPriceForMarginCalculation = suggestedPrice;
            if (marginSettings.usePriceWithDelivery && item.myPriceIncludesDelivery) {
                const myDeliveryCost = item.myPriceDeliveryCost != null && !isNaN(parseFloat(item.myPriceDeliveryCost)) ? parseFloat(item.myPriceDeliveryCost) : 0;
                newPriceForMarginCalculation = suggestedPrice - myDeliveryCost;
            }

            const newMarginAmount = newPriceForMarginCalculation - marginPrice;
            const newMarginPercentage = (marginPrice !== 0) ? (newMarginAmount / marginPrice) * 100 : null;

            if (newMarginPercentage !== null) {
                const newMarginSign = newMarginAmount >= 0 ? '+' : '-';
                const newMarginClass = newMarginAmount >= 0 ? 'priceBox-diff-margin-ins' : 'priceBox-diff-margin-minus-ins';
                const newBadgeClass = getMarginBadgeClass(newMarginClass);

                let newBgMarginClass = 'price-box-diff-margin-ib-neutral';
                if (newMarginClass === 'priceBox-diff-margin-ins') {
                    newBgMarginClass = 'price-box-diff-margin-ib-positive';
                } else if (newMarginClass === 'priceBox-diff-margin-minus-ins') {
                    newBgMarginClass = 'price-box-diff-margin-ib-negative';
                }

                const formattedNewMarginAmount = newMarginSign + formatPricePL(Math.abs(newMarginAmount), false) + ' PLN';
                const formattedNewMarginPercentage = '(' + newMarginSign + Math.abs(newMarginPercentage).toLocaleString('pl-PL', {
                    minimumFractionDigits: 2,
                    maximumFractionDigits: 2
                }) + '%)';

                const newMarginBox = document.createElement('div');
                newMarginBox.className = 'price-box-diff-margin-ib ' + newBgMarginClass;
                newMarginBox.style.marginTop = '3px';
                newMarginBox.innerHTML = `<span class="price-badge ${newBadgeClass}">Nowy narzut</span><p>${formattedNewMarginAmount} ${formattedNewMarginPercentage}</p>`;

                contentWrapper.appendChild(newMarginBox);
            }
        }

        // 5. Przycisk (Zawsze go generujemy)
        const strategicPriceLine = document.createElement('div');
        strategicPriceLine.className = 'price-action-line';
        strategicPriceLine.style.marginTop = '8px';

        const strategicPriceBtn = document.createElement('button');
        strategicPriceBtn.className = 'simulate-change-btn';
        // Domyślny tekst przed aktywacją
        const strategicBtnContent = `<span class="${squareColorClass}"></span> Dodaj zmianę ceny`;
        strategicPriceBtn.innerHTML = strategicBtnContent;
        strategicPriceBtn.dataset.originalText = strategicBtnContent;

        strategicPriceLine.appendChild(strategicPriceBtn);
        strategicPriceBox.appendChild(contentWrapper);
        strategicPriceBox.appendChild(strategicPriceLine);

        return {
            html: strategicPriceBox.outerHTML,
            actionLineSelector: '.price-action-line',
            suggestedPrice: suggestedPrice,
            myPrice: myPrice
        };
    }
        
    function renderPrices(data) {
        const storedChangesJSON = localStorage.getItem('selectedPriceChanges_' + storeId);
        if (storedChangesJSON) {
            try {
                const parsedData = JSON.parse(storedChangesJSON);

                if (parsedData && parsedData.scrapId && Array.isArray(parsedData.changes)) {

                    selectedPriceChanges = parsedData.changes;
                } else {

                    console.warn("Nieznany lub stary format danych 'selectedPriceChanges' w localStorage. Czyszczenie.");
                    selectedPriceChanges = [];
                    localStorage.removeItem('selectedPriceChanges_' + storeId);
                }
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

        paginatedData.forEach(item => {
            const highlightedProductName = highlightMatches(item.productName, currentProductSearchTerm);
            const highlightedStoreName = highlightMatches(item.storeName, currentStoreSearchTerm);

            const myPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;
            const lowestPrice = item.lowestPrice != null ? parseFloat(item.lowestPrice) : null;
            const savings = item.savings != null ? item.savings.toFixed(2) : "N/A";
            const myPosition = item.myPosition;
            const marginAmount = item.marginAmount;
            const marginPercentage = item.marginPercentage;
            const marginSign = item.marginSign;
            const marginClass = item.marginClass;
            const marginPrice = item.marginPrice;

            const box = document.createElement('div');
            // Zmiana: Jeśli tryb profit, używamy marketBucket jako klasy koloru paska
            const cssClass = (currentViewMode === 'profit') ? item.marketBucket : item.colorClass;
            box.className = 'price-box ' + cssClass;

            box.dataset.detailsUrl = '/PriceHistory/Details?scrapId=' + item.scrapId + '&productId=' + item.productId;
            box.dataset.productId = item.productId;
            box.dataset.productName = item.productName;

            box.addEventListener('click', function () {
                window.open(this.dataset.detailsUrl, '_blank');
            });

            const priceBoxSpace = document.createElement('div');
            priceBoxSpace.className = 'price-box-space';

            const leftColumn = document.createElement('div');
            leftColumn.className = 'price-box-left-column';

            const priceBoxColumnName = document.createElement('div');
            priceBoxColumnName.className = 'price-box-column-name';
            priceBoxColumnName.innerHTML = highlightedProductName;
            const flagsContainer = createFlagsContainer(item);

            leftColumn.appendChild(priceBoxColumnName);
            leftColumn.appendChild(flagsContainer);

            const rightColumn = document.createElement('div');
            rightColumn.className = 'price-box-right-column';

            const selectProductButton = document.createElement('button');
            selectProductButton.className = 'select-product-btn';
            selectProductButton.dataset.productId = item.productId;
            selectProductButton.style.pointerEvents = 'auto';

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

            rightColumn.appendChild(selectProductButton);
            rightColumn.appendChild(apiBox);

            priceBoxSpace.appendChild(leftColumn);
            priceBoxSpace.appendChild(rightColumn);

            const priceBoxData = document.createElement('div');
            priceBoxData.className = 'price-box-data';

            const colorBar = document.createElement('div');
            colorBar.className = 'color-bar ' + item.colorClass;

            const priceBoxColumnLowestPrice = document.createElement('div');
            priceBoxColumnLowestPrice.className = 'price-box-column';

            if (currentViewMode === 'profit') {
                // --- WIDOK PROFIT: POKAZUJEMY MEDIANĘ I INDEKS ---

                // 1. Nagłówek: Cena Mediana
                const marketHeader = document.createElement('div');
                marketHeader.className = 'price-box-column-text';

                const avgPriceDisplay = item.marketAveragePrice
                    ? formatPricePL(item.marketAveragePrice)
                    : 'Brak danych';

                marketHeader.innerHTML = `
                    <div style="display:flex; align-items:center;">
                        <span style="font-weight: 500; font-size: 17px; color:#444;">${avgPriceDisplay}</span>
                        <span class="uniqueBestPriceBox" style="background:#eee; color:#333; margin-left:6px; font-weight:normal;" title="Mediana rynkowa (środek rynku)">Mediana</span>
                    </div>
                `;

                // 2. Sub-nagłówek: Etykieta zamiast nazwy sklepu
                const marketLabel = document.createElement('div');
                marketLabel.innerHTML = `
                    <div style="display:flex; align-items:center; color: #666; font-size: 13px; margin-top: 2px;">
                        <i class="fas fa-chart-pie" style="margin-right: 5px;"></i> Indeks Rynku
                    </div>
                `;

                // 3. Szczegóły: Indeks w %
                const marketDetails = document.createElement('div');
                marketDetails.className = 'price-box-column-text';

                if (item.marketPriceIndex !== null) {
                    const idx = item.marketPriceIndex;
                    const sign = idx > 0 ? '+' : '';
                    // Prosta logika kolorów dla badge'a indeksu
                    let badgeColorClass = 'index-gray';
                    if (idx > 2) badgeColorClass = 'index-red'; // Drożej niż rynek
                    else if (idx < -2) badgeColorClass = 'index-green'; // Taniej niż rynek

                    marketDetails.innerHTML = `
                        <div style="margin-top:4px;">
                            <span class="market-index-badge ${badgeColorClass}" style="padding:2px 6px; border-radius:4px; font-size:12px; font-weight:600; color:white;">
                                Twoja cena: ${sign}${idx}%
                            </span>
                        </div>
                    `;
                } else {
                    marketDetails.innerHTML = `<span style="font-size:12px; color:#999;">Brak danych do porównania</span>`;
                }

                priceBoxColumnLowestPrice.appendChild(marketHeader);
                priceBoxColumnLowestPrice.appendChild(marketLabel);
                priceBoxColumnLowestPrice.appendChild(marketDetails);

            } else {

                if (item.colorClass === 'prOnlyMe') {
                    const noCompetitionText = document.createElement('div');
                    noCompetitionText.className = 'price-box-column-text';

                    const mainText = document.createElement('span');
                    mainText.style.fontWeight = '500';
                    mainText.style.fontSize = '17px';
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

                } else if (item.lowestPrice != null) {
                    const priceBoxLowestText = document.createElement('div');
                    priceBoxLowestText.className = 'price-box-column-text';

                    const priceLine = document.createElement('div');
                    priceLine.style.display = 'flex';
                    priceLine.style.alignItems = 'center';

                    const priceSpan = document.createElement('span');
                    priceSpan.style.fontWeight = '500';
                    priceSpan.style.fontSize = '17px';
                    priceSpan.textContent = formatPricePL(lowestPrice);
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

                    const competitorIconHtml = (item.isGoogle != null ?
                        '<span class="data-channel" style="display: inline-block; vertical-align: middle;"><img src="' +
                        (item.isGoogle ? '/images/GoogleShopping.png' : '/images/Ceneo.png') +
                        '" alt="Channel Icon" style="width:14px; height:14px; margin-right:4px;" /></span>' : ''
                    );

                    const storeNameDiv = document.createElement('div');
                    storeNameDiv.style.display = 'flex';
                    storeNameDiv.style.alignItems = 'center';
                    storeNameDiv.innerHTML = competitorIconHtml + `<span style="display: inline-block; vertical-align: middle;">${highlightedStoreName}</span>`;
                    priceBoxLowestText.appendChild(storeNameDiv);

                    if (marginSettings.usePriceWithDelivery) {
                        const lowestPriceDeliveryInfo = createDeliveryInfoDisplay(
                            item.lowestPrice,
                            item.bestPriceDeliveryCost,
                            item.bestPriceIncludesDelivery
                        );
                        if (lowestPriceDeliveryInfo) {
                            priceBoxLowestText.appendChild(lowestPriceDeliveryInfo);
                        }
                    }

                    const priceBoxLowestDetails = document.createElement('div');
                    priceBoxLowestDetails.className = 'price-box-column-text';

                    let badgeHtml = '';
                    if (item.isBidding === "1") {

                        badgeHtml = ' <span class="Bidding">Bid</span>';
                    } else if (item.isBidding === "bpg") {

                        badgeHtml = ' <span class="Bidding" style="background-color: #0F9D58; border-color: #0F9D58;" title="Google: Najlepsza cena">Odz. ceny Google</span>';
                    } else if (item.isBidding === "hpg") {

                        badgeHtml = ' <span class="Bidding" style="background-color: #4285F4; border-color: #4285F4;" title="Google: Najpopularniejsze">Popularne</span>';
                    }

                    priceBoxLowestDetails.innerHTML =
                        (item.position !== null ?
                            (item.isGoogle ?
                                '<span class="Position-Google">Poz. Google ' + item.position + '</span>' :
                                '<span class="Position">Poz. Ceneo ' + item.position + '</span>'
                            ) :
                            '<span class="Position" style="background-color: #414141;">Schowany</span>'
                        ) +
                        badgeHtml +

                        ' ' + getStockStatusBadge(item.bestEntryInStock);

                    priceBoxColumnLowestPrice.appendChild(priceBoxLowestText);
                    priceBoxColumnLowestPrice.appendChild(priceBoxLowestDetails);
                }
            }

            const priceBoxColumnMyPrice = document.createElement('div');
            priceBoxColumnMyPrice.className = 'price-box-column my-price-column'; 

            if (item.colorClass !== 'prNoOffer' && myPrice != null) {

                const myIconHtml = (item.myIsGoogle != null ?
                    '<span class="data-channel" style="display: inline-block; vertical-align: middle; margin-right: 4px;"><img src="' +
                    (item.myIsGoogle ? '/images/GoogleShopping.png' : '/images/Ceneo.png') +
                    '" alt="Channel Icon" style="width:14px; height:14px; margin-right:4px;" /></span>' : ''
                );

                const priceBoxMyText = document.createElement('div');
                priceBoxMyText.className = 'price-box-column-text';

                const myPriceLine = document.createElement('div');
                myPriceLine.style.display = 'flex';
                myPriceLine.style.alignItems = 'center';

                if (item.committed && item.committed.newPrice) {
                    const newCommittedPrice = parseFloat(item.committed.newPrice);
                    myPriceLine.innerHTML = `
        <s style="color: #999; font-size: 17px; font-weight: 500;">${formatPricePL(myPrice)}</s>
     
    `;
                } else {

                    const myPriceSpan = document.createElement('span');
                    myPriceSpan.style.fontWeight = '500';
                    myPriceSpan.style.fontSize = '17px';
                    myPriceSpan.textContent = formatPricePL(myPrice);
                    myPriceLine.appendChild(myPriceSpan);
                }

                const storeNameDiv = document.createElement('div');
                storeNameDiv.style.display = 'flex';
                storeNameDiv.style.alignItems = 'center';
                storeNameDiv.innerHTML = myIconHtml + `<span style="display: inline-block; vertical-align: middle;">${myStoreName}</span>`;

                priceBoxMyText.appendChild(myPriceLine);
                priceBoxMyText.appendChild(storeNameDiv);

                if (marginSettings.usePriceWithDelivery) {
                    const myPriceDeliveryInfo = createDeliveryInfoDisplay(
                        myPrice,
                        item.myPriceDeliveryCost,
                        item.myPriceIncludesDelivery
                    );
                    if (myPriceDeliveryInfo) {
                        priceBoxMyText.appendChild(myPriceDeliveryInfo);
                    }
                }

                if (marginPrice != null) {
                    const formattedMarginPrice = formatPricePL(marginPrice);
                    const badgeClass = getMarginBadgeClass(marginClass);
                    let bgMarginClass = 'price-box-diff-margin-ib-neutral';
                    if (marginClass === 'priceBox-diff-margin-ins') {
                        bgMarginClass = 'price-box-diff-margin-ib-positive';
                    } else if (marginClass === 'priceBox-diff-margin-minus-ins') {
                        bgMarginClass = 'price-box-diff-margin-ib-negative';
                    }

                    const purchasePriceBox = document.createElement('div');
                    purchasePriceBox.className = 'price-box-diff-margin-ib ' + bgMarginClass;
                    purchasePriceBox.style.marginTop = '4px';
                    purchasePriceBox.innerHTML = `<span class="price-badge ${badgeClass}">Cena zakupu</span><p>${formattedMarginPrice}</p>`;

                    const formattedMarginAmount = marginSign + formatPricePL(Math.abs(marginAmount), false) + ' PLN';
                    const formattedMarginPercentage = '(' + marginSign + Math.abs(marginPercentage).toLocaleString('pl-PL', {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2
                    }) + '%)';
                    const marginBox = document.createElement('div');
                    marginBox.className = 'price-box-diff-margin-ib ' + bgMarginClass;
                    marginBox.style.marginTop = '3px';
                    marginBox.innerHTML = `<span class="price-badge ${badgeClass}">Zysk (narzut)</span><p>${formattedMarginAmount} ${formattedMarginPercentage}</p>`;

                    priceBoxMyText.appendChild(purchasePriceBox);
                    priceBoxMyText.appendChild(marginBox);
                }

                const priceBoxMyDetails = document.createElement('div');
                priceBoxMyDetails.className = 'price-box-column-text';

                let detailsHtmlParts = [];

                if (myPosition !== null) {
                    if (item.myIsGoogle) {
                        detailsHtmlParts.push(`<span class="Position-Google">Poz. Google ${myPosition}</span>`);
                    } else {
                        detailsHtmlParts.push(`<span class="Position">Poz. Ceneo ${myPosition}</span>`);
                    }
                } else {
                    detailsHtmlParts.push('<span class="Position" style="background-color: #414141;">Schowany</span>');
                }

                if (item.myIsBidding === "1") {
                    detailsHtmlParts.push('<span class="Bidding">Bid</span>');
                } else if (item.myIsBidding === "bpg") {
                    detailsHtmlParts.push('<span class="Bidding" style="background-color: #0F9D58; border-color: #0F9D58;" title="Twoja oferta ma oznaczenie Najlepsza cena">Odz. ceny Google</span>');
                } else if (item.myIsBidding === "hpg") {
                    detailsHtmlParts.push('<span class="Bidding" style="background-color: #4285F4; border-color: #4285F4;" title="Twoja oferta ma oznaczenie Najpopularniejsze">Popularne</span>');
                }

                detailsHtmlParts.push(getStockStatusBadge(item.myEntryInStock));
                priceBoxMyDetails.innerHTML = detailsHtmlParts.join(' ');

                priceBoxColumnMyPrice.appendChild(priceBoxMyText);
                priceBoxColumnMyPrice.appendChild(priceBoxMyDetails);

            } else {
                const priceBoxMyText = document.createElement('div');
                priceBoxMyText.className = 'price-box-column-text';
                const mainText = document.createElement('span');
                mainText.style.fontWeight = '500';

                mainText.textContent = 'Brak Twojej oferty';
                mainText.style.fontSize = '17px';
                priceBoxMyText.appendChild(mainText);

                const storeNameDiv = document.createElement('div');
                storeNameDiv.textContent = myStoreName;
                priceBoxMyText.appendChild(storeNameDiv);

                if (marginPrice != null) {
                    const formattedMarginPrice = formatPricePL(marginPrice);
                    const badgeClass = getMarginBadgeClass('priceBox-diff-margin-neutral-ins');
                    const bgMarginClass = 'price-box-diff-margin-ib-neutral';

                    const purchasePriceBox = document.createElement('div');
                    purchasePriceBox.className = 'price-box-diff-margin-ib ' + bgMarginClass;
                    purchasePriceBox.style.marginTop = '4px';
                    purchasePriceBox.innerHTML = `<span class="price-badge ${badgeClass}">Cena zakupu</span><p>${formattedMarginPrice}</p>`;

                    const marginBox = document.createElement('div');
                    marginBox.className = 'price-box-diff-margin-ib ' + bgMarginClass;
                    marginBox.style.marginTop = '3px';
                    marginBox.innerHTML = `<span class="price-badge ${badgeClass}">Zysk (narzut)</span><p>Brak informacji</p>`;

                    priceBoxMyText.appendChild(purchasePriceBox);
                    priceBoxMyText.appendChild(marginBox);
                }

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

            if (item.committed) {

                const newPrice = parseFloat(item.committed.newPrice);
                const oldPrice = myPrice != null ? parseFloat(myPrice) : 0;

                const diffAmount = newPrice - oldPrice;
                const diffPercent = (oldPrice > 0) ? (diffAmount / oldPrice) * 100 : 0;
                let badgeText = diffAmount > 0 ? 'Podwyżka ceny' : (diffAmount < 0 ? 'Obniżka ceny' : 'Brak zmiany ceny');

                const priceDiffHtml = `
        <div class="price-diff-stack" style="text-align: left; margin-top: 3px;">
            <div class="price-diff-stack-badge">${badgeText}</div>
            <span class="diff-amount small-font">${diffAmount >= 0 ? '+' : ''}${formatPricePL(diffAmount, false)} PLN</span>
            <span class="diff-percentage small-font" style="margin-left: 6px;">(${diffPercent >= 0 ? '+' : ''}${diffPercent.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%)</span>
        </div>`;

                let newMarginHtml = '';
                if (marginPrice != null) {
                    let netPrice = newPrice;
                    if (marginSettings.usePriceWithDelivery && item.myPriceIncludesDelivery) {
                        const delCost = item.myPriceDeliveryCost != null ? parseFloat(item.myPriceDeliveryCost) : 0;
                        netPrice = newPrice - delCost;
                    }
                    const newMarginAmount = netPrice - marginPrice;
                    const newMarginPercent = (marginPrice !== 0) ? (newMarginAmount / marginPrice) * 100 : 0;
                    const mSign = newMarginAmount >= 0 ? '+' : '';
                    const mClass = newMarginAmount > 0 ? 'priceBox-diff-margin-ins' : (newMarginAmount < 0 ? 'priceBox-diff-margin-minus-ins' : 'priceBox-diff-margin-neutral-ins');
                    const bClass = getMarginBadgeClass(mClass);
                    let bgClass = 'price-box-diff-margin-ib-neutral';
                    if (mClass === 'priceBox-diff-margin-ins') bgClass = 'price-box-diff-margin-ib-positive';
                    else if (mClass === 'priceBox-diff-margin-minus-ins') bgClass = 'price-box-diff-margin-ib-negative';

                    newMarginHtml = `
        <div class="price-box-diff-margin-ib ${bgClass}" style="margin-top: 3px;">
            <span class="price-badge ${bClass}">Nowy narzut</span>
            <p>${mSign}${formatPricePL(Math.abs(newMarginAmount), false)} PLN (${mSign}${Math.abs(newMarginPercent).toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%)</p>
        </div>`;
                }

                priceBoxColumnInfo.innerHTML = `
        <div class="price-box-column">
            <div class="price-box-column-text" style="padding: 0;">
                <div style="display: flex; align-items: center; gap: 6px; font-size: 17px; font-weight: 600; color: #212529;">
                    <i class="fas fa-check-circle" style="color: #198754;"></i> ${formatPricePL(newPrice)}
                </div>
                <div style="font-size: 14px; color: #212529;">Zaktualizowane dane Twojej oferty</div>
                ${priceDiffHtml}
                ${newMarginHtml}
            </div>
            <div style="color: #198754; font-weight: 600; display: flex; align-items: center; gap: 5px; margin-top: 8px; font-size: 14px;">
                <i class="fas fa-check-circle"></i> Aktualizacja ceny wprowadzona
            </div>
        </div>`;

            } else {

                const existingChange = selectedPriceChanges.find(change =>
                    String(change.productId) === String(item.productId)
                );

                if (item.colorClass !== 'prNoOffer') {
                    let displaySuggestedPrice = null;

                    if (existingChange) {
                        displaySuggestedPrice = existingChange.newPrice;
                    }

                    if (item.colorClass === "prToLow" || item.colorClass === "prIdeal") {
                        if (myPrice != null && savings != null) {
                            let strategicPrice;

                            if (existingChange) {
                                strategicPrice = displaySuggestedPrice;
                            } else {
                                const suggestionData = calculateCurrentSuggestion(item);
                                if (suggestionData) {
                                    strategicPrice = suggestionData.suggestedPrice;
                                } else {
                                    strategicPrice = null;
                                }
                            }

                            if (strategicPrice !== null) {
                                const totalChangeAmount = strategicPrice - myPrice;
                                const percentageChange = myPrice > 0 ? (totalChangeAmount / myPrice) * 100 : 0;

                                let arrowClassStrategic;

                                if (totalChangeAmount > 0) {
                                    arrowClassStrategic = item.colorClass === 'prToLow' ? 'arrow-up-black' : 'arrow-up-turquoise';
                                } else if (totalChangeAmount < 0) {
                                    arrowClassStrategic = 'arrow-down-turquoise';
                                } else {
                                    arrowClassStrategic = 'no-change-icon-turquoise';
                                }

                                const suggestionDataForRender = {
                                    suggestedPrice: strategicPrice,
                                    totalChangeAmount: totalChangeAmount,
                                    percentageChange: percentageChange,
                                    arrowClass: arrowClassStrategic,
                                    priceType: 'raise'
                                };
                                const suggestionRenderResult = renderSuggestionBlockHTML(item, suggestionDataForRender);
                                priceBoxColumnInfo.innerHTML = suggestionRenderResult.html;

                                const newActionLine = priceBoxColumnInfo.querySelector(suggestionRenderResult.actionLineSelector);
                                if (newActionLine) {
                                    const currentSuggestionOnClick = calculateCurrentSuggestion(item);
                                    const priceToAddOnClick = currentSuggestionOnClick ? currentSuggestionOnClick.suggestedPrice : strategicPrice;

                                    attachPriceChangeListener(newActionLine, priceToAddOnClick, box, item.productId, item.productName, myPrice, item);
                                }
                            }
                        }
                    } else if (item.colorClass === "prMid" || item.colorClass === "prToHigh") {
                        if (myPrice != null && lowestPrice != null) {
                            let strategicPrice;

                            if (existingChange) {
                                strategicPrice = displaySuggestedPrice;
                            } else {
                                const suggestionData = calculateCurrentSuggestion(item);
                                if (suggestionData) {
                                    strategicPrice = suggestionData.suggestedPrice;
                                } else {
                                    strategicPrice = null;
                                }
                            }

                            if (strategicPrice !== null) {
                                const totalChangeAmount = strategicPrice - myPrice;
                                const percentageChange = myPrice > 0 ? (totalChangeAmount / myPrice) * 100 : 0;

                                let arrowClass;
                                if (totalChangeAmount > 0) {
                                    arrowClass = item.colorClass === "prMid" ? "arrow-up-yellow" : "arrow-up-red";
                                } else if (totalChangeAmount < 0) {
                                    arrowClass = item.colorClass === "prMid" ? "arrow-down-yellow" : "arrow-down-red";
                                } else {
                                    arrowClass = 'no-change-icon';
                                }

                                const suggestionDataForRender = {
                                    suggestedPrice: strategicPrice,
                                    totalChangeAmount: totalChangeAmount,
                                    percentageChange: percentageChange,
                                    arrowClass: arrowClass,
                                    priceType: 'lower'
                                };
                                const suggestionRenderResult = renderSuggestionBlockHTML(item, suggestionDataForRender);
                                priceBoxColumnInfo.innerHTML = suggestionRenderResult.html;

                                const newActionLine = priceBoxColumnInfo.querySelector(suggestionRenderResult.actionLineSelector);
                                if (newActionLine) {
                                    const currentSuggestionOnClick = calculateCurrentSuggestion(item);
                                    const priceToAddOnClick = currentSuggestionOnClick ? currentSuggestionOnClick.suggestedPrice : strategicPrice;
                                    attachPriceChangeListener(newActionLine, priceToAddOnClick, box, item.productId, item.productName, myPrice, item);
                                }
                            }
                        }
                    } else if (item.colorClass === "prGood") {
                        if (myPrice != null) {
                            let suggestedPrice2;

                            if (existingChange) {
                                suggestedPrice2 = displaySuggestedPrice;
                            } else {
                                const suggestionData = calculateCurrentSuggestion(item);
                                if (suggestionData) {
                                    suggestedPrice2 = suggestionData.suggestedPrice;
                                } else {
                                    suggestedPrice2 = null;
                                }
                            }

                            if (suggestedPrice2 !== null) {
                                const amountToSuggestedPrice2 = suggestedPrice2 - myPrice;
                                const percentageToSuggestedPrice2 = myPrice > 0 ? (amountToSuggestedPrice2 / myPrice) * 100 : 0;

                                let arrowClassStrategic;
                                if (amountToSuggestedPrice2 > 0) {
                                    arrowClassStrategic = 'arrow-up-green';
                                } else if (amountToSuggestedPrice2 < 0) {
                                    arrowClassStrategic = 'arrow-down-green';
                                } else {
                                    arrowClassStrategic = 'no-change-icon-turquoise';
                                }

                                const suggestionDataForRender = {
                                    suggestedPrice: suggestedPrice2,
                                    totalChangeAmount: amountToSuggestedPrice2,
                                    percentageChange: percentageToSuggestedPrice2,
                                    arrowClass: arrowClassStrategic,
                                    priceType: 'good'
                                };
                                const suggestionRenderResult = renderSuggestionBlockHTML(item, suggestionDataForRender);
                                priceBoxColumnInfo.innerHTML = suggestionRenderResult.html;

                                const newActionLine = priceBoxColumnInfo.querySelector(suggestionRenderResult.actionLineSelector);
                                if (newActionLine) {
                                    const currentSuggestionOnClick = calculateCurrentSuggestion(item);
                                    const priceToAddOnClick = currentSuggestionOnClick ? currentSuggestionOnClick.suggestedPrice : suggestedPrice2;
                                    attachPriceChangeListener(newActionLine, priceToAddOnClick, box, item.productId, item.productName, myPrice, item);
                                }
                            }
                        }
                    }
                }
            }

            priceBoxData.appendChild(colorBar);

            if (item.imgUrl) {
                const productImage = document.createElement('img');
                productImage.dataset.src = item.imgUrl;
                productImage.alt = item.productName;
                productImage.className = 'lazy-load';
                productImage.style.width = '142px';
                productImage.style.height = '182px';
                productImage.style.objectFit = 'contain';
                productImage.style.objectPosition = 'center';
                productImage.style.marginRight = '3px';
                productImage.style.marginLeft = '3px';
                productImage.style.backgroundColor = '#ffffff';
                productImage.style.border = '1px solid #e3e3e3';
                productImage.style.borderRadius = '4px';
                productImage.style.padding = '8px';
                productImage.style.display = 'block';

                priceBoxData.appendChild(productImage);
            }

            const statsContainer = document.createElement('div');
            statsContainer.className = 'price-box-stats-container';

            let offerCountHtml = `
            <div class="price-box-column-offers-a">
                <span class="data-channel">
                    ${(item.sourceGoogle ? `<img src="/images/GoogleShopping.png" alt="Google Icon" style="width:15px; height:15px;" />` : '')}
                    ${(item.sourceCeneo ? `<img src="/images/Ceneo.png" alt="Ceneo Icon" style="width:15px; height:15px;" />` : '')}
                </span>
                <div class="offer-count-box">${getOfferText(item.storeCount)}</div>
            </div>`;

          
            let myPricePositionHtml = '';

            if (item.committed && (item.committed.newGoogleRanking || item.committed.newCeneoRanking)) {

            
                const oldTotalCount = extractTotalOffersCount(item.myPricePosition);

           
                const newGoogleRank = extractRankNumber(item.committed.newGoogleRanking);
                const newCeneoRank = extractRankNumber(item.committed.newCeneoRanking);

            
                const validRanks = [];
                if (newGoogleRank !== null) validRanks.push(newGoogleRank);
                if (newCeneoRank !== null) validRanks.push(newCeneoRank);

                let newCalculatedRank = null;
                if (validRanks.length > 0) {
                    newCalculatedRank = Math.max(...validRanks);
                }

              
                if (newCalculatedRank !== null) {
                
                    const newPosStr = oldTotalCount ? `${newCalculatedRank}/${oldTotalCount}` : `${newCalculatedRank}`;

                    myPricePositionHtml = `
                    <div class="price-box-column-offers-a">
                        <span class="data-channel" title="Pozycja cenowa: Stara -> Nowa">
                            <i class="fas fa-trophy" style="font-size: 15px; color: grey; margin-top:1px;"></i>
                        </span>
                        <div class="offer-count-box">
                            <p>
                                <s style="color:#999; font-size: 0.9em;">${item.myPricePosition}</s>
                                <span style="color: #4e4e4e;">${newPosStr}</span>
                            </p>
                        </div>
                    </div>`;
                } else {
                 
                    myPricePositionHtml = `
                    <div class="price-box-column-offers-a">
                        <span class="data-channel" title="Pozycja cenowa twojej oferty">
                            <i class="fas fa-trophy" style="font-size: 15px; color: grey; margin-top:1px;"></i>
                        </span>
                        <div class="offer-count-box">
                            <p>${item.myPricePosition || 'N/A'}</p>
                        </div>
                    </div>`;
                }

            } else {
          
                myPricePositionHtml = `
                <div class="price-box-column-offers-a">
                    <span class="data-channel" title="Pozycja cenowa twojej oferty">
                        <i class="fas fa-trophy" style="font-size: 15px; color: grey; margin-top:1px;"></i>
                    </span>
                    <div class="offer-count-box">
                        <p>${item.myPricePosition || 'N/A'}</p>
                    </div>
                </div>`;
            }

            const ceneoTooltip = 'Ilość zakupionych produktów przez ostatnie 90 dni na Ceneo';
            let ceneoSalesHtml = '';
            if (item.ceneoSalesCount > 0) {
                ceneoSalesHtml = `
            <div class="price-box-column-offers-a">
                <span class="data-channel">
                    <i class="fas fa-shopping-cart" style="font-size: 15px; color: grey; margin-top:1px;" title="${ceneoTooltip}"></i>
                </span>
                <div class="offer-count-box">
                    <p>${item.ceneoSalesCount} osb. kupiło</p>
                </div>
            </div>`;
            } else {
                ceneoSalesHtml = `
            <div class="price-box-column-offers-a">
                <span class="data-channel">
                    <i class="fas fa-shopping-cart" style="font-size: 15px; color: grey; margin-top:1px;" title="${ceneoTooltip}"></i>
                </span>
                <div class="offer-count-box">
                    <p>Brak danych</p>
                </div>
            </div>`;
            }

            let salesTrendHtml = createSalesTrendHtml(item);

            statsContainer.innerHTML = offerCountHtml + myPricePositionHtml + ceneoSalesHtml + salesTrendHtml;

            priceBoxData.appendChild(statsContainer);
            priceBoxData.appendChild(priceBoxColumnLowestPrice);
            priceBoxData.appendChild(priceBoxColumnMyPrice);
            priceBoxData.appendChild(priceBoxColumnInfo);

            box.appendChild(priceBoxSpace);

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

            priceText.textContent = `${formatPricePL(basePrice, false)} PLN | ${formatPricePL(numericDeliveryCost, false)} PLN`;
        } else {

            priceText.textContent = `${formatPricePL(numericTotalPrice, false)} PLN | Brak danych o wysyłce`;
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
    renderChart = function (data) {
        const ctx = document.getElementById('colorChart').getContext('2d');

        if (chartInstance) {
            chartInstance.destroy();
            chartInstance = null;
        }

        let chartLabels = [];
        let chartValues = [];
        let chartColors = [];
        let chartBorderColors = [];

        if (currentViewMode === 'competitiveness') {
            // --- TRYB KONKURENCJA (Stary) ---
            const colorCounts = {
                prNoOffer: 0, prOnlyMe: 0, prToHigh: 0, prMid: 0, prGood: 0, prIdeal: 0, prToLow: 0
            };
            data.forEach(item => { colorCounts[item.colorClass] = (colorCounts[item.colorClass] || 0) + 1; });

            chartLabels = ['Cena niedostępna', 'Cena solo', 'Cena zawyżona', 'Cena suboptymalna', 'Cena konkurencyjna', 'Cena strategiczna', 'Cena zaniżona'];
            chartValues = [colorCounts.prNoOffer, colorCounts.prOnlyMe, colorCounts.prToHigh, colorCounts.prMid, colorCounts.prGood, colorCounts.prIdeal, colorCounts.prToLow];
            chartColors = ['rgba(230, 230, 230, 1)', 'rgba(180, 180, 180, 0.8)', 'rgba(171, 37, 32, 0.8)', 'rgba(224, 168, 66, 0.8)', 'rgba(117, 152, 112, 0.8)', 'rgba(13, 110, 253, 0.8)', 'rgba(6, 6, 6, 0.8)'];
            chartBorderColors = chartColors.map(c => c.replace('0.8', '1'));

            updateColorCounts(data);

        } else {
            // --- TRYB ZYSK (Nowy) ---
            const bucketCounts = {
                'market-deep-discount': 0,
                'market-below-average': 0,
                'market-average': 0,
                'market-above-average': 0,
                'market-overpriced': 0,
                'market-solo': 0
            };

            data.forEach(item => {
                const bucket = item.marketBucket || 'market-average';
                bucketCounts[bucket] = (bucketCounts[bucket] || 0) + 1;
            });

            chartLabels = ['Super Okazja', 'Poniżej Średniej', 'Poziom Rynku', 'Powyżej Średniej', 'Przeszacowane', 'Solo'];
            chartValues = [
                bucketCounts['market-deep-discount'],
                bucketCounts['market-below-average'],
                bucketCounts['market-average'],
                bucketCounts['market-above-average'],
                bucketCounts['market-overpriced'],
                bucketCounts['market-solo']
            ];
            // Kolory zdefiniowane w CSS
            chartColors = ['#157347', '#56cc9d', '#adb5bd', '#fd7e14', '#dc3545', '#6f42c1'];
            chartBorderColors = chartColors;

            updateBucketCountsUI(bucketCounts);
        }

        chartInstance = new Chart(ctx, {
            type: 'doughnut',
            data: {
                labels: chartLabels,
                datasets: [{
                    data: chartValues,
                    backgroundColor: chartColors,
                    borderColor: chartBorderColors,
                    borderWidth: 1
                }]
            },
            options: {
                aspectRatio: 1,
                cutout: '65%',
                layout: { padding: 4 },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            label: function (context) {
                                return context.label + ': ' + context.parsed;
                            }
                        }
                    }
                }
            }
        });
    };

    function updateBucketCountsUI(counts) {
        // Mapowanie nazw bucketów na ID checkboxów w widoku HTML
        const labels = {
            'market-deep-discount': 'bucketDeepDiscount',
            'market-below-average': 'bucketBelowAverage',
            'market-average': 'bucketAverage',
            'market-above-average': 'bucketAboveAverage',
            'market-overpriced': 'bucketOverpriced',
            'market-solo': 'bucketSolo'
        };

        for (const [bucket, elementId] of Object.entries(labels)) {
            const checkbox = document.getElementById(elementId);
            if (checkbox) {
                // Znajdź label powiązany z tym checkboxem
                const label = document.querySelector(`label[for="${elementId}"]`);
                if (label) {
                    // Pobieramy oryginalny tekst (bez starego licznika) i dodajemy nowy
                    const text = label.textContent.split('(')[0].trim();
                    label.textContent = `${text} (${counts[bucket] || 0})`;
                }
            }
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

    function updateBucketCountsUI(counts) {
        // Aktualizacja tekstów przy checkboxach w trybie Profit
        const labels = {
            'market-deep-discount': 'bucketDeepDiscount',
            'market-below-average': 'bucketBelowAverage',
            'market-average': 'bucketAverage',
            'market-above-average': 'bucketAboveAverage',
            'market-overpriced': 'bucketOverpriced',
            'market-solo': 'bucketSolo'
        };

        for (const [bucket, elementId] of Object.entries(labels)) {
            const label = document.querySelector(`label[for="${elementId}"]`);
            if (label) {
                // Pobieramy oryginalny tekst (bez licznika) i dodajemy nowy
                const text = label.textContent.split('(')[0].trim();
                label.textContent = `${text} (${counts[bucket] || 0})`;
            }
        }
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

            filteredPrices.forEach(item => {
                const suggestionData = calculateCurrentSuggestion(item);
                if (suggestionData) {
                    item.calculatedPercentageChange = suggestionData.percentageChange;
                    item.calculatedTotalChangeAmount = suggestionData.totalChangeAmount;
                } else {
                    item.calculatedPercentageChange = null;
                    item.calculatedTotalChangeAmount = null;
                }
            });

            if (sortingState.sortName !== null) {

                if (sortingState.sortName === 'asc') {
                    filteredPrices.sort((a, b) => a.productName.localeCompare(b.productName));
                } else {
                    filteredPrices.sort((a, b) => b.productName.localeCompare(a.productName));
                }
            } else if (sortingState.sortPrice !== null) {

                if (sortingState.sortPrice === 'asc') {

                    filteredPrices.sort((a, b) => (b.myPrice ?? -Infinity) - (a.myPrice ?? -Infinity));
                } else {

                    filteredPrices.sort((a, b) => (a.myPrice ?? Infinity) - (b.myPrice ?? Infinity));
                }
            } else if (sortingState.sortRaiseAmount !== null) {
                filteredPrices = filteredPrices.filter(item => item.calculatedTotalChangeAmount !== null && item.calculatedTotalChangeAmount > 0);
                if (sortingState.sortRaiseAmount === 'asc') {

                    filteredPrices.sort((a, b) => (b.calculatedTotalChangeAmount ?? -Infinity) - (a.calculatedTotalChangeAmount ?? -Infinity));
                } else {

                    filteredPrices.sort((a, b) => (a.calculatedTotalChangeAmount ?? Infinity) - (b.calculatedTotalChangeAmount ?? Infinity));
                }
            } else if (sortingState.sortRaisePercentage !== null) {
                filteredPrices = filteredPrices.filter(item => item.calculatedPercentageChange !== null && item.calculatedPercentageChange > 0);
                if (sortingState.sortRaisePercentage === 'asc') {

                    filteredPrices.sort((a, b) => (b.calculatedPercentageChange ?? -Infinity) - (a.calculatedPercentageChange ?? -Infinity));
                } else {

                    filteredPrices.sort((a, b) => (a.calculatedPercentageChange ?? Infinity) - (b.calculatedPercentageChange ?? Infinity));
                }
            } else if (sortingState.sortLowerAmount !== null) {
                filteredPrices = filteredPrices.filter(item => item.calculatedTotalChangeAmount !== null && item.calculatedTotalChangeAmount < 0);
                if (sortingState.sortLowerAmount === 'asc') {

                    filteredPrices.sort((a, b) => (b.calculatedTotalChangeAmount ?? -Infinity) - (a.calculatedTotalChangeAmount ?? -Infinity));
                } else {

                    filteredPrices.sort((a, b) => (a.calculatedTotalChangeAmount ?? Infinity) - (b.calculatedTotalChangeAmount ?? Infinity));
                }
            } else if (sortingState.sortLowerPercentage !== null) {
                filteredPrices = filteredPrices.filter(item => item.calculatedPercentageChange !== null && item.calculatedPercentageChange < 0);
                if (sortingState.sortLowerPercentage === 'asc') {

                    filteredPrices.sort((a, b) => (b.calculatedPercentageChange ?? -Infinity) - (a.calculatedPercentageChange ?? -Infinity));
                } else {

                    filteredPrices.sort((a, b) => (a.calculatedPercentageChange ?? Infinity) - (b.calculatedPercentageChange ?? Infinity));
                }

            } else if (sortingState.sortMarginAmount !== null) {
                filteredPrices = filteredPrices.filter(item => item.marginAmount !== null);
                if (sortingState.sortMarginAmount === 'asc') {

                    filteredPrices.sort((a, b) => (b.marginAmount ?? -Infinity) - (a.marginAmount ?? -Infinity));
                } else {

                    filteredPrices.sort((a, b) => (a.marginAmount ?? Infinity) - (b.marginAmount ?? Infinity));

                }
            } else if (sortingState.sortMarginPercentage !== null) {
                filteredPrices = filteredPrices.filter(item => item.marginPercentage !== null);
                if (sortingState.sortMarginPercentage === 'asc') {

                    filteredPrices.sort((a, b) => (b.marginPercentage ?? -Infinity) - (a.marginPercentage ?? -Infinity));
                } else {

                    filteredPrices.sort((a, b) => (a.marginPercentage ?? Infinity) - (b.marginPercentage ?? Infinity));
                }
            } else if (sortingState.sortCeneoSales !== null) {
                filteredPrices = filteredPrices.filter(item => item.ceneoSalesCount !== null);
                if (sortingState.sortCeneoSales === 'asc') {

                    filteredPrices.sort((a, b) => (b.ceneoSalesCount ?? -Infinity) - (a.ceneoSalesCount ?? -Infinity));
                } else {

                    filteredPrices.sort((a, b) => (a.ceneoSalesCount ?? Infinity) - (b.ceneoSalesCount ?? Infinity));
                }
            } else if (sortingState.sortSalesTrendAmount !== null) {
                filteredPrices = filteredPrices.filter(item => item.salesDifference !== null);
                if (sortingState.sortSalesTrendAmount === 'asc') {

                    filteredPrices.sort((a, b) => (b.salesDifference ?? -Infinity) - (a.salesDifference ?? -Infinity));
                } else {

                    filteredPrices.sort((a, b) => (a.salesDifference ?? Infinity) - (b.salesDifference ?? Infinity));
                }
            } else if (sortingState.sortSalesTrendPercent !== null) {
                filteredPrices = filteredPrices.filter(item => item.salesPercentageChange !== null);
                if (sortingState.sortSalesTrendPercent === 'asc') {

                    filteredPrices.sort((a, b) => (b.salesPercentageChange ?? -Infinity) - (a.salesPercentageChange ?? -Infinity));
                } else {

                    filteredPrices.sort((a, b) => (a.salesPercentageChange ?? Infinity) - (b.salesPercentageChange ?? Infinity));
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

            case 'sortCeneoSales':
                return 'Sprzedaż - ilość';
            case 'sortSalesTrendAmount':
                return 'Trend - ilość';
            case 'sortSalesTrendPercent':
                return 'Trend - %';
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

    document.querySelectorAll('.colorFilter, .flagFilter, .positionFilter, .stockFilterMyStore, .stockFilterCompetitor, .externalPriceFilter')
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

    document.getElementById('sortCeneoSales').addEventListener('click', function () {
        if (sortingState.sortCeneoSales === null) {
            sortingState.sortCeneoSales = 'asc';
            this.innerHTML = 'Sprzedaż - ilość ↑';
            this.classList.add('active');
        } else if (sortingState.sortCeneoSales === 'asc') {
            sortingState.sortCeneoSales = 'desc';
            this.innerHTML = 'Sprzedaż - ilość ↓';
            this.classList.add('active');
        } else {
            sortingState.sortCeneoSales = null;
            this.innerHTML = 'Sprzedaż - ilość';
            this.classList.remove('active');
        }
        resetSortingStates('sortCeneoSales');
        localStorage.setItem('priceHistorySortingState_' + storeId, JSON.stringify(sortingState));
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortSalesTrendAmount').addEventListener('click', function () {
        if (sortingState.sortSalesTrendAmount === null) {
            sortingState.sortSalesTrendAmount = 'asc';
            this.innerHTML = 'Trend - ilość ↑';
            this.classList.add('active');
        } else if (sortingState.sortSalesTrendAmount === 'asc') {
            sortingState.sortSalesTrendAmount = 'desc';
            this.innerHTML = 'Trend - ilość ↓';
            this.classList.add('active');
        } else {
            sortingState.sortSalesTrendAmount = null;
            this.innerHTML = 'Trend - ilość';
            this.classList.remove('active');
        }
        resetSortingStates('sortSalesTrendAmount');
        localStorage.setItem('priceHistorySortingState_' + storeId, JSON.stringify(sortingState));
        filterPricesAndUpdateUI();
    });

    document.getElementById('sortSalesTrendPercent').addEventListener('click', function () {
        if (sortingState.sortSalesTrendPercent === null) {
            sortingState.sortSalesTrendPercent = 'asc';
            this.innerHTML = 'Trend - % ↑';
            this.classList.add('active');
        } else if (sortingState.sortSalesTrendPercent === 'asc') {
            sortingState.sortSalesTrendPercent = 'desc';
            this.innerHTML = 'Trend - % ↓';
            this.classList.add('active');
        } else {
            sortingState.sortSalesTrendPercent = null;
            this.innerHTML = 'Trend - %';
            this.classList.remove('active');
        }
        resetSortingStates('sortSalesTrendPercent');
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

    document.getElementById('productSearch').addEventListener('input', function () {
        currentPage = 1;
        window.scrollTo(0, 0);
        debouncedFilterPrices();
    });

    // Podpięcie eventów dla nowych filtrów kubełkowych (Profit Mode)
    document.querySelectorAll('.bucketFilter').forEach(cb => {
        cb.addEventListener('change', function () {
            showLoading();
            filterPricesAndUpdateUI();
        });
    });

   

    // Synchronizacja inputów stepPrice
    const stepPriceMain = document.getElementById('stepPrice');
    const stepPriceProfit = document.getElementById('stepPrice_profit');

    if (stepPriceMain && stepPriceProfit) {
        stepPriceMain.addEventListener('input', function () {
            stepPriceProfit.value = this.value;
        });
        stepPriceProfit.addEventListener('input', function () {
            stepPriceMain.value = this.value;
        });
    }


    const exportButton = document.getElementById("exportToExcelButton");
    if (exportButton) {
        exportButton.addEventListener("click", function () {
            const url = `/PriceHistory/ExportToExcel?storeId=${storeId}`;
            window.location.href = url;
        });
    }

    const profitCalcBtn = document.getElementById('savePriceValues_profit');
    if (profitCalcBtn) {
        profitCalcBtn.addEventListener('click', function () {
            // Wywołujemy główny save, ale najpierw aktualizujemy zmienną globalną z inputu
            const inputVal = document.getElementById('priceIndexTargetInput').value;
            setPriceIndexTarget = parseFloat(inputVal);

            // Wywołujemy kliknięcie głównego przycisku zapisu (który musimy zaktualizować poniżej)
            document.getElementById('savePriceValues').click();
        });
    }

    document.getElementById('savePriceValues').addEventListener('click', function () {
        const price1 = parseFloat(document.getElementById('price1').value);
        const price2 = parseFloat(document.getElementById('price2').value);
        const stepPrice = parseFloat(document.getElementById('stepPrice').value);
        const usePriceDiff = document.getElementById('usePriceDifference').checked;

        // Pobieramy wartość z nowego inputu (jeśli jesteśmy w trybie profit, to stamtąd, jak nie to ze zmiennej)
        const profitInput = document.getElementById('priceIndexTargetInput');
        const priceIndexTarget = profitInput ? parseFloat(profitInput.value) : 100.00;

        const data = {
            StoreId: storeId,
            SetPrice1: price1,
            SetPrice2: price2,
            PriceStep: stepPrice,
            UsePriceDiff: usePriceDiff,
            PriceIndexTargetPercent: priceIndexTarget // <--- NOWE POLE DO API
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
                    usePriceDifference = usePriceDiff;
                    setPriceIndexTarget = priceIndexTarget;
                    updatePricesDebounced();

                    showGlobalUpdate('<p style="margin-bottom:8px; font-size:16px; font-weight:bold;">Ustawienia zapisane</p><p>Przeliczam sugestie...</p>');

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
                <span class="flag-name" style="border-color: ${flag.flagColor}; color: ${flag.flagColor}; background-color: ${hexToRgba(flag.flagColor, 0.3)};">${flag.flagName}</span>
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
        const modalCounter = document.getElementById('selectedProductsModalCounter');
        const modalList = document.getElementById('selectedProductsList');
        const count = selectedProductIds.size;

        selectionContainer.style.display = 'flex';
        counter.textContent = `Wybrano: ${count}`;

        if (modalCounter) {
            modalCounter.textContent = count;
        }

        modalList.innerHTML = '';
        if (count === 0) {

            modalList.innerHTML = '<div class="alert alert-info text-center">Nie zaznaczono żadnych produktów.</div>';
            return;
        }

        const table = document.createElement('table');

        table.className = 'table-orders';
        table.innerHTML = `
        <thead class="thead-light">
            <tr>
                <th style="width: 65%;">Nazwa Produktu</th>
                <th>EAN/ID</th>
                <th class="text-center">Akcja</th>
            </tr>
        </thead>`;
        const tbody = document.createElement('tbody');

        selectedProductIds.forEach(productId => {
            const product = allPrices.find(p => p.productId.toString() === productId);
            if (product) {
                const tr = document.createElement('tr');
                let identifier = product.ean || product.externalId || 'Brak ID';
                tr.innerHTML = `
                <td class="align-middle">${product.productName}</td>
                <td class="align-middle">${identifier}</td>
                <td class="text-center align-middle">
                    <button class="btn btn-danger btn-sm remove-selection-btn" data-product-id="${productId}" title="Usuń z zaznaczonych">
                        <i class="fa-solid fa-trash-can"></i>
                    </button>
                </td>
            `;
                tbody.appendChild(tr);
            }
        });

        table.appendChild(tbody);
        modalList.appendChild(table);
    }

    document.getElementById('selectedProductsList').addEventListener('click', function (event) {

        const removeButton = event.target.closest('.remove-selection-btn');

        if (removeButton) {
            const productId = removeButton.dataset.productId;

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