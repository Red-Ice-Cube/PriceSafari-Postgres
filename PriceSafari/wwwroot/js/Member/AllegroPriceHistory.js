document.addEventListener("DOMContentLoaded", function () {
    let allPrices = [];
    let currentlyFilteredPrices = [];
    let chartInstance = null;
    let myStoreName = "";

    let setPrice1 = 2.00;
    let setPrice2 = 2.00;
    let setStepPrice = 2.00;
    let usePriceDifference = document.getElementById('usePriceDifference').checked;

    let allegroMarginSettings = {
        identifierForSimulation: 'ID',
        useMarginForSimulation: true,
        enforceMinimalMargin: true,
        minimalMarginPercent: 0.00,
        includeCommissionInPriceChange: false,

        allegroChangePriceForBagdeSuperPrice: false,
        allegroChangePriceForBagdeTopOffer: false,
        allegroChangePriceForBagdeBestPriceGuarantee: false,
        allegroChangePriceForBagdeInCampaign: false
    };

    let selectedProductId = null;
    let selectedFlagsInclude = new Set();
    let selectedFlagsExclude = new Set();
    let selectedMyBadges = new Set();

    const selectionStorageKey = `selectedAllegroProducts_${storeId}`;

    let currentScrapId = null;
    let selectedPriceChanges = [];
    const priceChangeLocalStorageKey = `selectedAllegroPriceChanges_${storeId}`;

    const stepPriceInput = document.getElementById("stepPrice");
    const stepPriceIndicatorSpan = document.getElementById('stepPriceIndicator');

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

    function saveSelectionToStorage(selectionSet) {
        localStorage.setItem(selectionStorageKey, JSON.stringify(Array.from(selectionSet)));
    }

    function loadSelectionFromStorage() {
        const storedSelection = localStorage.getItem(selectionStorageKey);
        return storedSelection ? new Set(JSON.parse(storedSelection)) : new Set();
    }

    function clearSelectionFromStorage() {
        localStorage.removeItem(selectionStorageKey);
    }

    let selectedProductIds = loadSelectionFromStorage();

    const selectAllVisibleBtn = document.getElementById('selectAllVisibleBtn');
    const deselectAllVisibleBtn = document.getElementById('deselectAllVisibleBtn');

    selectAllVisibleBtn.addEventListener('click', function () {
        if (currentlyFilteredPrices.length === 0) return;
        currentlyFilteredPrices.forEach(product => selectedProductIds.add(product.productId.toString()));
        saveSelectionToStorage(selectedProductIds);
        updateSelectionUI();
        updateVisibleProductSelectionButtons();
    });

    deselectAllVisibleBtn.addEventListener('click', function () {
        if (currentlyFilteredPrices.length === 0) return;
        currentlyFilteredPrices.forEach(product => selectedProductIds.delete(product.productId.toString()));
        saveSelectionToStorage(selectedProductIds);
        updateSelectionUI();
        updateVisibleProductSelectionButtons();
    });

    function updateVisibleProductSelectionButtons() {
        document.querySelectorAll('#priceContainer .select-product-btn').forEach(btn => {
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

    let sortingState = {
        sortName: null,
        sortPrice: null,
        sortRaiseAmount: null,
        sortRaisePercentage: null,
        sortLowerAmount: null,
        sortLowerPercentage: null,
        sortMarginAmount: null,
        sortMarginPercentage: null
    };

    let isCatalogViewActive = false;
    let currentPage = 1;
    const itemsPerPage = 1000;

    function debounce(func, wait) {
        let timeout;
        return function (...args) {
            clearTimeout(timeout);
            timeout = setTimeout(() => func.apply(this, args), wait);
        };
    }

    function updateUnits(isUsingPriceDifference) {
        const unit = isUsingPriceDifference ? 'PLN' : '%';
        document.getElementById('unitLabel1').textContent = unit;
        document.getElementById('unitLabel2').textContent = unit;
        document.getElementById('unitLabelStepPrice').textContent = unit;
    }

    function convertPriceValue(price) {
        if (price.isRejected) return {
            valueToUse: null,
            colorClass: 'prNoOffer'
        };
        if (price.onlyMe) return {
            valueToUse: null,
            colorClass: 'prOnlyMe'
        };

        let valueForColor;
        if (usePriceDifference) {
            valueForColor = (price.savings !== null ? price.savings : price.priceDifference);
        } else {
            if (price.myPrice && price.lowestPrice && parseFloat(price.myPrice) > parseFloat(price.lowestPrice) && !price.isUniqueBestPrice && !price.isSharedBestPrice) {
                valueForColor = ((parseFloat(price.myPrice) - parseFloat(price.lowestPrice)) / parseFloat(price.myPrice)) * 100;
            } else {
                valueForColor = price.percentageDifference;
            }
        }

        if (valueForColor == null) return {
            valueToUse: null,
            colorClass: 'prNoOffer'
        };

        const colorClass = getColorClass(valueForColor, price.isUniqueBestPrice, price.isSharedBestPrice);
        return {
            valueToUse: valueForColor,
            colorClass: colorClass
        };
    }

    function getOfferText(count) {
        if (count === 1) return `${count} Oferta`;
        if (count > 10 && count < 20) return `${count} Ofert`;
        const lastDigit = count % 10;
        if ([2, 3, 4].includes(lastDigit)) return `${count} Oferty`;
        return `${count} Ofert`;
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

    function updateSortButtonVisuals() {
        Object.keys(sortingState).forEach(key => {
            const stateValue = sortingState[key];
            const button = document.getElementById(key);

            if (button && stateValue !== null) {
                button.classList.add('active');
                const directionArrow = stateValue === 'asc' ? '↑' : '↓';
                button.innerHTML = `${getDefaultButtonLabel(key)} ${directionArrow}`;
            } else if (button) {
                button.classList.remove('active');
                button.innerHTML = getDefaultButtonLabel(key);
            }
        });
    }

    function updateMarginSortButtonsVisibility() {

        const hasMarginData = allPrices.some(item => item.marginAmount !== null && item.marginPercentage !== null);
        const sortMarginAmountButton = document.getElementById('sortMarginAmount');
        const sortMarginPercentageButton = document.getElementById('sortMarginPercentage');

        if (sortMarginAmountButton && sortMarginPercentageButton) {
            if (hasMarginData) {
                sortMarginAmountButton.style.display = '';
                sortMarginPercentageButton.style.display = '';
            } else {
                sortMarginAmountButton.style.display = 'none';
                sortMarginPercentageButton.style.display = 'none';
            }
        }
    }

    function highlightMatches(fullText, searchTerm) {
        if (!searchTerm || !fullText) return fullText;
        const escapedTerm = searchTerm.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        const regex = new RegExp(escapedTerm, 'gi');
        return fullText.replace(regex, (match) => `<span class="highlighted-text">${match}</span>`);
    }

    function renderDeliveryInfo(deliveryTime) {
        if (deliveryTime === null || deliveryTime === undefined) {
            return '';
        }
        let text = '',
            className = '';
        switch (deliveryTime) {
            case 1:
                text = 'Dostawa jutro';
                className = 'Delivery1';
                break;
            case 2:
                text = 'Dostawa pojutrze';
                className = 'Delivery2';
                break;
            case 3:
                text = 'Długa dostawa';
                className = 'Delivery3';
                break;
            default:
                return '';
        }
        return `<div class="${className}">${text}</div>`;
    }

    function getColorClass(valueToUse, isUniqueBestPrice = false, isSharedBestPrice = false) {
        if (isSharedBestPrice) return "prGood";
        if (isUniqueBestPrice) {
            if (!usePriceDifference) {
                return Math.abs(valueToUse) > setPrice1 ? "prToLow" : "prIdeal";
            } else {
                return valueToUse <= setPrice1 ? "prIdeal" : "prToLow";
            }
        }
        const numericValue = parseFloat(valueToUse);
        if (isNaN(numericValue)) return "prNoOffer";
        return numericValue < setPrice2 ? "prMid" : "prToHigh";
    }

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
                        <input class="form-check-input flagFilterInclude" type="checkbox" id="flagInclude_${flag.flagId}" value="${flag.flagId}" ${includeChecked}>
                    </div>
                    <div class="form-check form-check-inline check-exclude" style="margin-right:0px; padding-left:16px;">
                        <input class="form-check-input flagFilterExclude" type="checkbox" id="flagExclude_${flag.flagId}" value="${flag.flagId}" ${excludeChecked}>
                    </div>
                        <span class="flag-name-count" style="font-size:14px; font-weight: 400;">${flag.flagName} (${count})</span>
                </div>`;
                flagContainer.insertAdjacentHTML('beforeend', flagElementHTML);
            });
        }

        const noFlagIncludeChecked = selectedFlagsInclude.has('noFlag') ? 'checked' : '';
        const noFlagExcludeChecked = selectedFlagsExclude.has('noFlag') ? 'checked' : '';
        const noFlagElementHTML = `
        <div class="flag-filter-group">
            <div class="form-check form-check-inline check-include" style="margin-right:0px;">
                <input class="form-check-input flagFilterInclude" type="checkbox" id="flagInclude_noFlag" value="noFlag" ${noFlagIncludeChecked}>
            </div>
            <div class="form-check form-check-inline check-exclude" style="margin-right:0px; padding-left:16px;">
                <input class="form-check-input flagFilterExclude" type="checkbox" id="flagExclude_noFlag" value="noFlag" ${noFlagExcludeChecked}>
            </div>
            <span class="flag-name-count" style="font-size:14px; font-weight: 400;">Brak flagi (${noFlagCount})</span>
        </div>`;
        flagContainer.insertAdjacentHTML('beforeend', noFlagElementHTML);

        setupFlagFilterListeners();
    }

    function updateBadgeCounts(prices) {
        let topOfferCount = 0;
        let superPriceCount = 0;
        let bestPriceCount = 0;
        let campaignCount = 0;

        prices.forEach(price => {
            if (price.myIsTopOffer) topOfferCount++;
            if (price.myIsSuperPrice) superPriceCount++;
            if (price.myIsBestPriceGuarantee) bestPriceCount++;
            if (price.anyPromoActive) campaignCount++;
        });

        const topOfferLabel = document.getElementById('labelMyTopOffer');
        const superPriceLabel = document.getElementById('labelMySuperPrice');
        const bestPriceLabel = document.getElementById('labelMyBestPrice');
        const campaignLabel = document.getElementById('labelMyCampaign');

        if (topOfferLabel) topOfferLabel.textContent = `Top oferta (${topOfferCount})`;
        if (superPriceLabel) superPriceLabel.textContent = `Super cena (${superPriceCount})`;
        if (bestPriceLabel) bestPriceLabel.textContent = `Gwarancja najniższej ceny (${bestPriceCount})`;
        if (campaignLabel) campaignLabel.textContent = `W kampanii Allegro (${campaignCount})`;
    }

    function hexToRgba(hex, alpha) {
        let r = 0,
            g = 0,
            b = 0;
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

    function updateStepPriceIndicator() {
        if (!stepPriceInput || !stepPriceIndicatorSpan) return;
        const valueStr = stepPriceInput.value.replace(',', '.');
        const value = parseFloat(valueStr);
        let iconClass = '',
            titleText = '';

        if (isNaN(value)) {
            iconClass = 'fa-solid fa-question';
            titleText = 'Niepoprawna wartość';
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
    function activateChangeButton(button, actionLine, priceBox, stepPriceApplied, stepUnitApplied) {
        if (!button) {
            console.error("Próba aktywacji nieistniejącego przycisku w priceBox:", priceBox);
            return;
        }
        let stepInfoText = "";
        if (typeof stepPriceApplied !== 'undefined' && stepPriceApplied !== null && typeof stepUnitApplied !== 'undefined') {
            const formattedStepPrice = stepPriceApplied.toLocaleString('pl-PL', {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            });
            stepInfoText = ` Krok ${formattedStepPrice} ${stepUnitApplied} |`;
        }

        if (!button.dataset.originalText) {
            button.dataset.originalText = button.innerHTML;
        }

        button.classList.add('active');
        button.innerHTML = stepInfoText + " Dodano";

        const removeLink = document.createElement('span');
        removeLink.innerHTML = " <i class='fa fa-trash' style='font-size:12px; display:flex; color:white; margin-left:4px; margin-top:3px;'></i>";
        removeLink.style.cursor = "pointer";
        removeLink.style.pointerEvents = 'auto';

        removeLink.addEventListener('click', function (ev) {
            ev.stopPropagation();
            const productId = priceBox.dataset.productId;

            selectedPriceChanges = selectedPriceChanges.filter(c => String(c.productId) !== String(productId));

            const removeEvent = new CustomEvent('priceBoxChangeRemove', {
                detail: {
                    productId: String(productId)
                }
            });
            document.dispatchEvent(removeEvent);

            const item = allPrices.find(p => String(p.productId) === String(productId));
            if (!item) {
                console.error("Nie znaleziono danych produktu dla ID:", productId, "podczas usuwania zmiany.");
                priceBox.classList.remove('price-changed');
                const buttonToReset = actionLine.querySelector('.simulate-change-btn');
                if (buttonToReset) {
                    buttonToReset.classList.remove('active');
                    buttonToReset.innerHTML = buttonToReset.dataset.originalText || "Dodaj zmianę ceny";
                }
                return;
            }
            const currentMyPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;
            const priceBoxColumnInfo = priceBox.querySelector('.price-box-column-action');

            if (!priceBoxColumnInfo) {
                console.error("Nie znaleziono kontenera .price-box-column-action dla ID:", productId, "podczas usuwania zmiany.");
                priceBox.classList.remove('price-changed');
                return;
            }

            const newSuggestionData = calculateCurrentSuggestion(item);

            if (newSuggestionData) {
                const suggestionRenderResult = renderSuggestionBlockHTML(item, newSuggestionData);
                priceBoxColumnInfo.innerHTML = suggestionRenderResult.html;
                const newActionLine = priceBoxColumnInfo.querySelector(suggestionRenderResult.actionLineSelector);
                if (newActionLine) {
                    const priceToAddOnClick = newSuggestionData.suggestedPrice;
                    attachPriceChangeListener(newActionLine, priceToAddOnClick, priceBox, item.productId, item.productName, currentMyPrice, item);
                } else {
                    console.error("Nie znaleziono .price-action-line po re-renderowaniu sugestii dla ID:", productId);
                }
            } else {
                priceBoxColumnInfo.innerHTML = '';
                console.warn("Nie udało się obliczyć nowej sugestii po usunięciu dla ID:", productId);
            }

            priceBox.classList.remove('price-changed');

        });

        button.appendChild(removeLink);

        if (!priceBox.classList.contains('price-changed')) {
            priceBox.classList.add('price-changed');
        }
    }

    function attachPriceChangeListener(actionLine, suggestedPrice, priceBox, productId, productName, currentPriceValue, item) {
        const button = actionLine.querySelector('.simulate-change-btn');
        if (!button) return;

        actionLine.addEventListener('click', function (e) {
            e.stopPropagation();

            const currentButton = this.querySelector('.simulate-change-btn');

            if (item.anyPromoActive && !allegroMarginSettings.allegroChangePriceForBagdeInCampaign) {
                showGlobalNotification(
                    `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny zablokowana</p>
                    <p>Oferta jest w Kampanii Allegro. Zmiana jest zablokowana w ustawieniach symulacji.</p>`
                );
                return;
            }
            if (item.myIsSuperPrice && !allegroMarginSettings.allegroChangePriceForBagdeSuperPrice) {
                showGlobalNotification(
                    `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny zablokowana</p>
                    <p>Oferta ma oznaczenie "Super Cena". Zmiana jest zablokowana w ustawieniach symulacji.</p>`
                );
                return;
            }
            if (item.myIsTopOffer && !allegroMarginSettings.allegroChangePriceForBagdeTopOffer) {
                showGlobalNotification(
                    `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny zablokowana</p>
                    <p>Oferta ma oznaczenie "Top Oferta". Zmiana jest zablokowana w ustawieniach symulacji.</p>`
                );
                return;
            }
            if (item.myIsBestPriceGuarantee && !allegroMarginSettings.allegroChangePriceForBagdeBestPriceGuarantee) {
                showGlobalNotification(
                    `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny zablokowana</p>
                    <p>Oferta ma "Gwarancję Najniższej Ceny". Zmiana jest zablokowana w ustawieniach symulacji.</p>`
                );
                return;
            }

            if (priceBox.classList.contains('price-changed') || (currentButton && currentButton.classList.contains('active'))) {
                console.log("Zmiana ceny już aktywna dla produktu", productId);
                return;
            }

            if (allegroMarginSettings.useMarginForSimulation) {
                if (item.marginPrice == null) {
                    showGlobalNotification(
                        `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny nie została dodana</p>
                         <p>Symulacja cenowa z narzutem jest włączona – produkt musi posiadać cenę zakupu.</p>`
                    );
                    return;
                }

                const marginPrice = parseFloat(item.marginPrice);
                const commission = allegroMarginSettings.includeCommissionInPriceChange && item.apiAllegroCommission != null ? parseFloat(item.apiAllegroCommission) : 0;
                const newNetPrice = suggestedPrice - commission;
                const newMarginAmount = newNetPrice - marginPrice;
                const newMarginPercentage = (marginPrice !== 0) ? (newMarginAmount / marginPrice) * 100 : 0;

                const basePriceForOldMargin = item.apiAllegroPriceFromUser != null ?
                    parseFloat(item.apiAllegroPriceFromUser) :
                    currentPriceValue;

                let oldMarginPercentage = -Infinity;
                if (basePriceForOldMargin != null) {
                    const oldNetPrice = basePriceForOldMargin - commission;
                    const oldMarginAmount = oldNetPrice - marginPrice;
                    oldMarginPercentage = (marginPrice !== 0) ? (oldMarginAmount / marginPrice) * 100 : Infinity;
                }

                const minMarginPerc = allegroMarginSettings.minimalMarginPercent;

                if (allegroMarginSettings.enforceMinimalMargin && minMarginPerc >= 0) {
                    if (newMarginPercentage < minMarginPerc) {
                        if (!(oldMarginPercentage < minMarginPerc && newMarginPercentage > oldMarginPercentage)) {
                            showGlobalNotification(
                                `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny nie została dodana</p>
                                 <p>Nowa cena <strong>${formatPricePL(suggestedPrice)}</strong> ustawi narzut (<strong>${newMarginPercentage.toFixed(2)}%</strong>) poniżej wymaganego minimum (<strong>${minMarginPerc}%</strong>).</p>
                                 <p>Obecny narzut: ${oldMarginPercentage !== Infinity ? oldMarginPercentage.toFixed(2) + '%' : 'N/A'}. Cena zakupu: <strong>${formatPricePL(marginPrice)}</strong>.</p>`
                            );
                            return;
                        }
                    }
                } else if (allegroMarginSettings.enforceMinimalMargin && minMarginPerc < 0) {
                    if (newMarginPercentage < minMarginPerc) {
                        if (!(oldMarginPercentage < minMarginPerc && newMarginPercentage > oldMarginPercentage)) {
                            showGlobalNotification(
                                `<p style="margin:8px 0; font-weight:bold;">Zmiana ceny nie została dodana</p>
                                 <p>Nowa cena <strong>${formatPricePL(suggestedPrice)}</strong> spowoduje stratę (<strong>${newMarginPercentage.toFixed(2)}%</strong>) większą niż dozwolony próg (<strong>${minMarginPerc}%</strong>).</p>
                                 <p>Obecny narzut: ${oldMarginPercentage !== Infinity ? oldMarginPercentage.toFixed(2) + '%' : 'N/A'}. Cena zakupu: <strong>${formatPricePL(marginPrice)}</strong>.</p>`
                            );
                            return;
                        }
                    }
                }
            }

            const currentSetStepPrice = setStepPrice;
            const currentUsePriceDifference = usePriceDifference;
            const stepUnit = currentUsePriceDifference ? 'PLN' : '%';

            const priceChangeEvent = new CustomEvent('priceBoxChange', {
                detail: {
                    productId: String(productId),
                    myIdAllegro: item.myIdAllegro, // <-- DODANA TA LINIA
                    productName: productName,
                    currentPrice: currentPriceValue,
                    newPrice: suggestedPrice,
                    storeId: storeId,
                    scrapId: currentScrapId,
                    stepPriceApplied: currentSetStepPrice,
                    stepUnitApplied: stepUnit
                }
            });
            document.dispatchEvent(priceChangeEvent);

            const changeData = priceChangeEvent.detail;
            const existingIndex = selectedPriceChanges.findIndex(c => c.productId === changeData.productId);
            if (existingIndex > -1) {
                selectedPriceChanges[existingIndex] = changeData;
            } else {
                selectedPriceChanges.push(changeData);
            }

            activateChangeButton(currentButton, this, priceBox, currentSetStepPrice, stepUnit);

            let message = `<p style="margin-bottom:8px; font-size:16px; font-weight:bold;">Zmiana ceny dodana</p>`;
            message += `<p style="margin:4px 0;"><strong>Produkt:</strong> ${productName}</p>`;
            message += `<p style="margin:4px 0;"><strong>Nowa cena:</strong> ${formatPricePL(suggestedPrice)}</p>`;
            showGlobalUpdate(message);
        });

        const existingChange = selectedPriceChanges.find(change =>
            String(change.productId) === String(productId)
        );

        if (existingChange) {
            activateChangeButton(button, actionLine, priceBox, existingChange.stepPriceApplied, existingChange.stepUnitApplied);
        }
    }

    function calculateCurrentSuggestion(item) {
        const currentMyPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;
        const currentLowestPrice = item.lowestPrice != null ? parseFloat(item.lowestPrice) : null;
        const currentSavings = item.savings != null ? parseFloat(item.savings) : null;
        const currentSetStepPrice = setStepPrice;
        const currentUsePriceDifference = usePriceDifference;

        const basePriceForChangeCalc = item.apiAllegroPriceFromUser != null ?
            parseFloat(item.apiAllegroPriceFromUser) :
            currentMyPrice;

        let suggestedPrice = null;
        let basePriceForCalc = null;
        let priceType = null;

        if (item.isRejected || currentMyPrice === null) {
            priceType = 'noOffer';
            suggestedPrice = currentMyPrice;
        } else if (item.onlyMe || currentLowestPrice === null) {
            priceType = 'onlyMe';
            basePriceForCalc = currentMyPrice;
            suggestedPrice = currentMyPrice;
        } else if (item.colorClass === "prToLow" || item.colorClass === "prIdeal") {
            if (currentMyPrice != null && currentSavings != null) {
                basePriceForCalc = currentMyPrice + currentSavings;
                priceType = 'raise';
            } else if (currentMyPrice != null) {
                basePriceForCalc = currentMyPrice;
                priceType = 'raise';
            }
        } else if (item.colorClass === "prMid" || item.colorClass === "prToHigh") {
            if (currentLowestPrice != null) {
                basePriceForCalc = currentLowestPrice;
                priceType = 'lower';
            }
        } else if (item.colorClass === "prGood") {
            if (currentMyPrice != null) {
                basePriceForCalc = currentMyPrice;
                if (item.totalOfferCount > 1 || currentSetStepPrice < 0) {
                    priceType = 'good_step';
                } else {
                    priceType = 'good_no_step';
                }
            }
        }

        if (basePriceForCalc !== null && priceType !== null && priceType !== 'noOffer' && priceType !== 'onlyMe') {
            if (priceType === 'good_no_step') {
                suggestedPrice = basePriceForCalc;
            } else {
                if (currentUsePriceDifference) {
                    suggestedPrice = basePriceForCalc + currentSetStepPrice;
                } else {
                    suggestedPrice = basePriceForCalc * (1 + (currentSetStepPrice / 100));
                }
                if (suggestedPrice < 0.01) {
                    suggestedPrice = 0.01;
                }
                suggestedPrice = parseFloat(suggestedPrice.toFixed(2));
            }
        } else if (priceType === 'onlyMe' || priceType === 'noOffer') {
            suggestedPrice = currentMyPrice;
            if (priceType === 'onlyMe') basePriceForCalc = currentMyPrice;
        }

        if (suggestedPrice === null) {
            return null;
        }

        const totalChangeAmount = suggestedPrice - (basePriceForChangeCalc || 0);
        const percentageChange = (basePriceForChangeCalc != null && basePriceForChangeCalc > 0) ? (totalChangeAmount / basePriceForChangeCalc) * 100 : 0;

        let arrowClass = '';
        if (priceType === 'raise') {
            arrowClass = totalChangeAmount > 0 ? (item.colorClass === 'prToLow' ? 'arrow-up-black' : 'arrow-up-turquoise') : (totalChangeAmount < 0 ? 'arrow-down-turquoise' : 'no-change-icon-turquoise');
        } else if (priceType === 'lower') {
            arrowClass = totalChangeAmount > 0 ? (item.colorClass === "prMid" ? "arrow-up-yellow" : "arrow-up-red") : (totalChangeAmount < 0 ? (item.colorClass === "prMid" ? "arrow-down-yellow" : "arrow-down-red") : 'no-change-icon');
        } else if (priceType === 'good_step' || priceType === 'good_no_step') {
            arrowClass = totalChangeAmount > 0 ? 'arrow-up-green' : (totalChangeAmount < 0 ? 'arrow-down-green' : 'no-change-icon-turquoise');
        } else if (priceType === 'onlyMe' || priceType === 'noOffer') {
            arrowClass = 'no-change-icon-gray';
        }

        let squareColorClass = 'color-square-turquoise';

        return {
            suggestedPrice: suggestedPrice,
            totalChangeAmount: totalChangeAmount,
            percentageChange: percentageChange,
            arrowClass: arrowClass,
            priceType: priceType,
            squareColorClass: squareColorClass
        };
    }

    function renderSuggestionBlockHTML(item, suggestionData) {

        if (!suggestionData || suggestionData.suggestedPrice === null || suggestionData.priceType === 'noOffer') {
            return {
                html: '<div class="price-box-column">Brak sugestii</div>',
                actionLineSelector: null,
                suggestedPrice: null,
                myPrice: item.myPrice
            };
        }

        const {
            suggestedPrice,
            totalChangeAmount,
            percentageChange,
            arrowClass,
            priceType,
            squareColorClass
        } = suggestionData;
        const myPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;

        if (priceType === 'onlyMe') {
            return {
                html: '<div class="price-box-column"><div class="price-box-column-text">Jesteś jedynym sprzedawcą</div></div>',
                actionLineSelector: null,
                suggestedPrice: null,
                myPrice: item.myPrice
            };
        }

        if (totalChangeAmount === 0 && (priceType === 'good_no_step' || priceType === 'good_step')) {
            return {
                html: '<div class="price-box-column"><div class="price-box-column-text">Cena optymalna (brak zmian)</div></div>',
                actionLineSelector: null,
                suggestedPrice: null,
                myPrice: item.myPrice
            };
        }

        const suggestionBox = document.createElement('div');
        suggestionBox.className = 'price-box-column';

        const contentWrapper = document.createElement('div');
        contentWrapper.className = 'price-box-column-text';

        const newPriceDisplay = document.createElement('div');
        newPriceDisplay.style.display = 'flex';
        newPriceDisplay.style.alignItems = 'center';
        newPriceDisplay.innerHTML = `<span class="${arrowClass}" style="margin-right: 8px;"></span><div><span style="font-weight: 500; font-size: 17px;">${formatPricePL(suggestedPrice)}</span></div>`;

        const priceChangeLabel = document.createElement('div');
        priceChangeLabel.textContent = 'Zmiana ceny Twojej oferty';
        priceChangeLabel.style.fontSize = '14px';
        priceChangeLabel.style.color = '#212529';

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
            `<span class="diff-amount small-font">${totalChangeAmount >= 0 ? '+' : ''}${formatPricePL(totalChangeAmount, false)} PLN</span>&nbsp;` +
            `<span class="diff-percentage small-font">(${percentageChange >= 0 ? '+' : ''}${percentageChange.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%)</span>` +
            `</div>`;

        contentWrapper.appendChild(newPriceDisplay);
        contentWrapper.appendChild(priceChangeLabel);

        if (totalChangeAmount !== 0) {
            contentWrapper.appendChild(priceDifferenceDisplay);
        }

        if (item.marginPrice != null && suggestedPrice != null) {
            const marginPrice = parseFloat(item.marginPrice);

            const commission = allegroMarginSettings.includeCommissionInPriceChange && item.apiAllegroCommission != null ?
                parseFloat(item.apiAllegroCommission) :
                0;

            const newNetPrice = suggestedPrice - commission;

            const newMarginAmount = newNetPrice - marginPrice;
            const newMarginPercentage = (marginPrice !== 0) ? (newMarginAmount / marginPrice) * 100 : null;

            if (newMarginPercentage !== null) {
                const newMarginSign = newMarginAmount >= 0 ? '+' : '-';
                const newMarginClass = newMarginAmount > 0 ? 'priceBox-diff-margin-ins' : (newMarginAmount < 0 ? 'priceBox-diff-margin-minus-ins' : 'priceBox-diff-margin-neutral-ins');
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

        const actionLine = document.createElement('div');
        actionLine.className = 'price-action-line';
        actionLine.style.marginTop = '8px';

        const actionButton = document.createElement('button');
        actionButton.className = 'simulate-change-btn';

        const buttonContent = `<span class="${squareColorClass}"></span> Dodaj zmianę ceny`;
        actionButton.innerHTML = buttonContent;
        actionButton.dataset.originalText = buttonContent;

        actionLine.appendChild(actionButton);

        suggestionBox.appendChild(contentWrapper);
        suggestionBox.appendChild(actionLine);

        return {
            html: suggestionBox.outerHTML,
            actionLineSelector: '.price-action-line',
            suggestedPrice: suggestedPrice,
            myPrice: myPrice
        };
    }

    function createApiInfoHtml(item) {
        let campaignLine = '';

        if (item.anyPromoActive === true && item.apiAllegroPriceFromUser != null) {
            const formattedPromoPrice = formatPricePL(item.apiAllegroPriceFromUser, false);
            campaignLine = `<div class="Campaign-stack" style="text-align: left; margin-top: 3px;"><span class="campaign-stack-badge">Kampania Allegro | Twoja cena</span><p>${formattedPromoPrice} PLN</p></div>`;
        }

        if (campaignLine === '') {
            return '';
        }

        return `<div class="api-info-container">${campaignLine}</div>`;
    }

    function renderPrices(data) {
        const container = document.getElementById('priceContainer');
        container.innerHTML = '';

        const productSearchTerm = document.getElementById('productSearch').value.trim();
        const storeSearchTerm = document.getElementById('storeSearch').value.trim();

        const paginatedData = data.slice((currentPage - 1) * itemsPerPage, currentPage * itemsPerPage);
        currentlyFilteredPrices = paginatedData;

        const currentActiveChangesMap = new Map();
        selectedPriceChanges.forEach(change => currentActiveChangesMap.set(String(change.productId), change));

        paginatedData.forEach(item => {
            const myPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;
            const lowestPrice = item.lowestPrice != null ? parseFloat(item.lowestPrice) : null;
            const highlightedProductName = highlightMatches(item.productName, productSearchTerm);
            const highlightedStoreName = highlightMatches(item.storeName, storeSearchTerm);
            const highlightedMyStoreName = highlightMatches(myStoreName, storeSearchTerm);
            let myOfferIdHtml = '';
            if (item.myIdAllegro) {
                myOfferIdHtml = `
                <div class="price-box-column-category">
                    <span class="ApiBox">ID ${item.myIdAllegro}</span>
                </div>
                `;
            }
            const competitorSuperPriceBadge = item.isSuperPrice ? `<div class="SuperPrice">SUPERCENA</div>` : '';

            let competitorPromoInfoText = '';
            if (item.isPromoted) {
                competitorPromoInfoText = ` <span class="AddPromoInfoBadge">Promowane</span>`;
            } else if (item.isSponsored) {
                competitorPromoInfoText = ` <span class="AddPromoInfoBadge">Sponsorowane</span>`;
            }

            const mySuperPriceBadge = item.myIsSuperPrice ? `<div class="SuperPrice">SUPERCENA</div>` : '';

            const competitorTopOfferBadge = item.isTopOffer ? `<div class="TopOffer">Top oferta</div>` : '';
            const myTopOfferBadge = item.myIsTopOffer ? `<div class="TopOffer">Top oferta</div>` : '';

            let myPromoInfoText = '';
            if (item.myIsPromoted) {
                myPromoInfoText = ` <span class="AddPromoInfoBadge">Promowane</span>`;
            } else if (item.myIsSponsored) {
                myPromoInfoText = ` <span class="AddPromoInfoBadge">Sponsorowane</span>`;
            }

            let myPricePositionHtml = '';
            if (item.myPricePosition) {
                myPricePositionHtml = `
                <div class="price-box-column-offers-a"><span class="data-channel" title="Pozycja cenowa Twojej oferty"><i class="fas fa-trophy" style="font-size: 15px; color: grey; margin-top:1px;"></i></span><div class="offer-count-box"><p>${item.myPricePosition}</p></div></div>`;
            } else if (item.myPrice != null) {
                myPricePositionHtml = `
                <div class="price-box-column-offers-a"><span class="data-channel" title="Pozycja cenowa Twojej oferty"><i class="fas fa-trophy" style="font-size: 15px; color: grey; margin-top:1px;"></i></span><div class="offer-count-box"><p>N/A</p></div></div>`;
            }

            const competitorPriceStyle = item.isBestPriceGuarantee ? 'color: #169A23;' : '';
            const competitorPriceIcon = item.isBestPriceGuarantee ? `<img src="/images/TopPrice.png" alt="Gwarancja Najniższej Ceny" title="Gwarancja Najniższej Ceny" style="width: 18px; height: 18px; vertical-align: middle;">` : '';
            const myPriceStyle = item.myIsBestPriceGuarantee ? 'color: #169A23;' : '';
            const myPriceIcon = item.myIsBestPriceGuarantee ? `<img src="/images/TopPrice.png" alt="Gwarancja Najniższej Ceny" title="Gwarancja Najniższej Ceny" style="width: 18px; height: 18px; vertical-align: middle;">` : '';

            let marginHtml = '';
            if (item.marginPrice != null && item.marginAmount != null) {
                const formattedMarginPrice = formatPricePL(item.marginPrice);
                const badgeClass = getMarginBadgeClass(item.marginClass);

                let bgMarginClass = 'price-box-diff-margin-ib-neutral';
                if (item.marginClass === 'priceBox-diff-margin-ins') {
                    bgMarginClass = 'price-box-diff-margin-ib-positive';
                } else if (item.marginClass === 'priceBox-diff-margin-minus-ins') {
                    bgMarginClass = 'price-box-diff-margin-ib-negative';
                }

                const formattedMarginAmount = item.marginSign + formatPricePL(Math.abs(item.marginAmount), false) + ' PLN';
                const formattedMarginPercentage = '(' + item.marginSign + Math.abs(item.marginPercentage).toLocaleString('pl-PL', {
                    minimumFractionDigits: 2,
                    maximumFractionDigits: 2
                }) + '%)';

                marginHtml = `
                <div class="price-box-diff-margin-ib ${bgMarginClass}" style="margin-top: 4px;">
                    <span class="price-badge ${badgeClass}">Cena zakupu</span><p>${formattedMarginPrice}</p>
                </div>
                <div class="price-box-diff-margin-ib ${bgMarginClass}" style="margin-top: 3px;">
                    <span class="price-badge ${badgeClass}">Zysk (narzut)</span><p>${formattedMarginAmount} ${formattedMarginPercentage}</p>
                </div>
                `;
            } else if (item.marginPrice != null) {
                marginHtml = `
                <div class="price-box-diff-margin-ib price-box-diff-margin-ib-neutral" style="margin-top: 4px;">
                    <span class="price-badge price-badge-neutral">Cena zakupu</span><p>${formatPricePL(item.marginPrice)}</p>
                </div>
                `;
            }
            let commissionHtml = '';
            if (item.apiAllegroCommission != null) {
                const formattedCommission = formatPricePL(item.apiAllegroCommission, false);
                const commissionStatusText = allegroMarginSettings.includeCommissionInPriceChange ?
                    '<span style="font-weight: 400; color: #555;"> | uwzględniony</span>' :
                    '<span style="font-weight: 400; color: #999;"> | nieuwzględniony</span>';

                commissionHtml = `<div class="Commission-stack" style="text-align: left; margin-top: 3px;"><span class="commission-stack-badge">Koszt Allegro</span><p>${formattedCommission} PLN${commissionStatusText}</p></div>`;
            }

            const box = document.createElement('div');
            box.className = 'price-box ' + item.colorClass;
            box.dataset.productId = item.productId;
            box.dataset.detailsUrl = `/AllegroPriceHistory/Details?storeId=${storeId}&productId=${item.productId}`;
            box.style.cursor = 'pointer';

            box.innerHTML = `
            <div class="price-box-space">
                <div class="price-box-column-name">${highlightedProductName}</div>
                <div class="flags-container"></div>
            </div>
            <div class="price-box-data">
                <div class="color-bar ${item.colorClass}"></div>
                <div class="price-box-stats-container">
                    <div class="price-box-column-offers-a">
                        <span class="data-channel">
                            <img src="/images/AllegroIcon.png" alt="Allegro Icon" style="width:15px; height:15px;" />
                        </span>
                        <div class="offer-count-box">${getOfferText(item.totalOfferCount)}</div>
                    </div>
                    ${myPricePositionHtml}
                    <div class="price-box-column-offers-a">
                        <span class="data-channel">
                            <i class="fas fa-shopping-cart" style="font-size: 15px; color: grey; margin-top:1px;" title="Łączna sprzedaż ostatnich 30 dni"></i>
                        </span>
                        <div class="offer-count-box">
                            <p>${item.totalPopularity} osb. kupiło</p>
                        </div>
                    </div>
                    <div class="price-box-column-offers-a">
                        <span class="data-channel">
                            <i class="fas fa-chart-pie" style="font-size: 15px; color: grey; margin-top:1px;" title="Twój udział w rynku w ostatnich 30 dniach"></i>
                        </span>
                        <div class="offer-count-box">
                            <p>${item.myTotalPopularity} osb. (${item.marketSharePercentage.toFixed(2)}%)</p>
                        </div>
                    </div>
                </div>
                <div class="price-box-column">
                    ${(item.onlyMe || lowestPrice == null) ?
                    `<div class="price-box-column-text">
                            <div><span style="font-weight: 500;">Brak konkurencji</span></div>
                        </div>` :
                    `
                        <div class="price-box-column-text">
                            <div><div style="display: flex; align-items: center; gap: 4px; flex-wrap: wrap;"><span style="font-weight: 500; font-size: 17px; ${competitorPriceStyle}">${formatPricePL(lowestPrice)}</span>${competitorPriceIcon}${competitorSuperPriceBadge}${competitorTopOfferBadge}</div>
                                <div>
                                    ${highlightedStoreName || ''}
                                    ${item.isSuperSeller ? `<img src="/images/SuperSeller.png" alt="Super Sprzedawca" title="Super Sprzedawca" style="width: 16px; height: 16px; vertical-align: middle;">` : ''}
                                    ${competitorPromoInfoText} </div>
                                </div>
                        </div>
                        <div class="price-box-column-text">
                            <div class="data-channel">
                                ${item.isSmart ? `
                                    <div class="Smart-Allegro">
                                        <img src="/images/Smart.png" alt="Smart!" title="Smart!" style="height: 15px; width: auto; margin-left: 2px;">
                                    </div>
                                ` : ''}
                                ${renderDeliveryInfo(item.deliveryTime)}
                            </div>
                        </div>
                        `
                }
                </div>
                <div class="price-box-column">
                    ${myPrice != null ?
                    `
                        <div class="price-box-column-text">
                            <div><div style="display: flex; align-items: center; gap: 4px; flex-wrap: wrap;"><span style="font-weight: 500; font-size: 17px; ${myPriceStyle}">${formatPricePL(myPrice)}</span>${myPriceIcon}${mySuperPriceBadge}${myTopOfferBadge}</div>
                                <div>
                                    ${highlightedMyStoreName}
                                    ${item.myIsSuperSeller ? `<img src="/images/SuperSeller.png" alt="Super Sprzedawca" title="Super Sprzedawca" style="width: 16px; height: 16px; vertical-align: middle;">` : ''}
                                    ${myPromoInfoText} </div>
                                   ${marginHtml}${commissionHtml}${createApiInfoHtml(item)}
                            </div>
                        </div>
                        <div class="price-box-column-text">
                            <div class="data-channel">
                                ${item.myIsSmart ? `
                                    <div class="Smart-Allegro">
                                        <img src="/images/Smart.png" alt="Smart!" title="Smart!" style="height: 15px; width: auto; margin-left: 2px;">
                                    </div>
                                ` : ''}
                                ${renderDeliveryInfo(item.myDeliveryTime)}
                            </div>
                        </div>
                        ` :
                    `<div class="price-box-column-text">
                            <div><span style="font-weight: 500;">Brak Twojej oferty</span></div>
                            ${marginHtml}
                        </div>`
                }
                </div>
                <div class="price-box-column-action"></div>
            </div>
            `;

            container.appendChild(box);

            const priceBoxSpace = box.querySelector('.price-box-space');
            const priceBoxColumnName = box.querySelector('.price-box-column-name');

            priceBoxSpace.innerHTML = '';

            const leftColumn = document.createElement('div');
            leftColumn.className = 'price-box-left-column';

            const rightColumn = document.createElement('div');
            rightColumn.className = 'price-box-right-column';

            const flagsContainer = createFlagsContainer(item);
            leftColumn.appendChild(priceBoxColumnName);
            leftColumn.appendChild(flagsContainer);

            const selectProductButton = document.createElement('button');
            selectProductButton.className = 'select-product-btn';
            selectProductButton.dataset.productId = item.productId;
            selectProductButton.style.pointerEvents = 'auto';
            if (selectedProductIds.has(item.productId.toString())) {
                selectProductButton.textContent = 'Wybrano';
                selectProductButton.classList.add('selected');
            } else {
                selectProductButton.textContent = 'Zaznacz';
                selectProductButton.classList.remove('selected');
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

            if (allegroMarginSettings.identifierForSimulation === 'EAN') {
                if (item.ean) {
                    apiBox.innerHTML = `EAN ${item.ean}`;
                } else {
                    apiBox.innerHTML = 'Brak EAN';
                }
            } else {
                if (item.myIdAllegro) {
                    apiBox.innerHTML = `ID ${item.myIdAllegro}`;
                } else {
                    apiBox.innerHTML = 'Brak ID';
                }
            }

            rightColumn.appendChild(selectProductButton);
            rightColumn.appendChild(apiBox);

            priceBoxSpace.appendChild(leftColumn);
            priceBoxSpace.appendChild(rightColumn);

            box.addEventListener('click', function (event) {
                if (event.target.closest('button, a, img, .select-product-btn')) {
                    return;
                }
                window.open(this.dataset.detailsUrl, '_blank');
            });

            const infoCol = box.querySelector('.price-box-column-action');
            infoCol.innerHTML = '';

            const existingChange = currentActiveChangesMap.get(String(item.productId));

            const defaultSuggestionData = calculateCurrentSuggestion(item);

            if (defaultSuggestionData) {

                let finalSuggestionData;

                if (existingChange) {

                    const basePriceForChangeCalc = item.apiAllegroPriceFromUser != null ?
                        parseFloat(item.apiAllegroPriceFromUser) :
                        (item.myPrice != null ? parseFloat(item.myPrice) : 0);

                    const totalChangeAmount = existingChange.newPrice - basePriceForChangeCalc;
                    const percentageChange = (basePriceForChangeCalc > 0) ? (totalChangeAmount / basePriceForChangeCalc) * 100 : 0;

                    finalSuggestionData = {
                        ...defaultSuggestionData,
                        suggestedPrice: existingChange.newPrice,
                        totalChangeAmount: totalChangeAmount,
                        percentageChange: percentageChange
                    };

                } else {

                    finalSuggestionData = defaultSuggestionData;
                }

                const suggestionRenderResult = renderSuggestionBlockHTML(item, finalSuggestionData);
                infoCol.innerHTML = suggestionRenderResult.html;

                const newActionLine = infoCol.querySelector(suggestionRenderResult.actionLineSelector);
                if (newActionLine) {
                    const myPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;

                    attachPriceChangeListener(newActionLine, defaultSuggestionData.suggestedPrice, box, item.productId, item.productName, myPrice, item);
                }
            } else {
                infoCol.innerHTML = '<div class="price-box-column">Brak danych do sugestii</div>';
            }

        });

        renderPaginationControls(data.length);
        document.getElementById('displayedProductCount').textContent = `${data.length} / ${allPrices.length}`;
    }

    function groupAndFilterByCatalog(data) {
        const catalogGroups = new Map();
        for (const item of data) {
            if (item.myOffersGroupKey) {
                const existingItem = catalogGroups.get(item.myOffersGroupKey);

                if (!existingItem || (item.myPrice !== null && item.myPrice < existingItem.myPrice)) {
                    catalogGroups.set(item.myOffersGroupKey, item);
                }
            } else {

                catalogGroups.set(`no-group-${item.productId}`, item);
            }
        }
        return Array.from(catalogGroups.values());
    }

    function updateSelectionUI() {
        const counter = document.getElementById('selectedProductsCounter');
        const modalCounter = document.getElementById('selectedProductsModalCounter');
        const modalList = document.getElementById('selectedProductsList');
        const count = selectedProductIds.size;

        if (counter) counter.textContent = `Wybrano: ${count}`;
        if (modalCounter) modalCounter.textContent = count;

        modalList.innerHTML = '';
        if (count === 0) {
            modalList.innerHTML = '<div class="alert alert-info text-center">Nie zaznaczono żadnych produktów.</div>';
            return;
        }

        const table = document.createElement('table');
        table.className = 'table-orders';
        table.innerHTML = `<thead><tr><th style="width: 70%;">Nazwa Produktu</th><th>ID/EAN</th><th class="text-center">Akcja</th></tr></thead>`;
        const tbody = document.createElement('tbody');

        selectedProductIds.forEach(productId => {
            const product = allPrices.find(p => p.productId.toString() === productId);
            if (product) {
                const tr = document.createElement('tr');

                let identifierText = '';
                if (allegroMarginSettings.identifierForSimulation === 'EAN') {
                    identifierText = product.ean ? `EAN ${product.ean}` : 'Brak EAN';
                } else {

                    identifierText = product.myIdAllegro ? `ID ${product.myIdAllegro}` : 'Brak ID';
                }

                tr.innerHTML = `
                    <td>${product.productName}</td>
                    <td>${identifierText}</td>
                    <td class="text-center align-middle">
                        <button class="btn btn-danger btn-sm remove-selection-btn" data-product-id="${productId}" title="Usuń z zaznaczonych">
                            <i class="fa-solid fa-trash-can"></i>
                        </button>
                    </td>`;
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

    document.getElementById('openBulkFlagModalBtn').addEventListener('click', function () {
        if (selectedProductIds.size === 0) {
            alert('Nie zaznaczono żadnych produktów.');
            return;
        }
        $('#selectedProductsModal').modal('hide');
        showLoading();

        fetch('/ProductFlags/GetFlagCountsForProducts', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                allegroProductIds: Array.from(selectedProductIds, id => parseInt(id))
            })
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

            flagItem.innerHTML = `
                <div class="flag-label">
                    <span class="flag-name" style="border-color: ${flag.flagColor}; color: ${flag.flagColor}; background-color: ${hexToRgba(flag.flagColor, 0.3)};">${flag.flagName}</span>
                    <span class="flag-count">(${currentCount} / ${totalSelected})</span>
                </div>
                <div class="flag-actions">
                    <div class="action-group">
                        <label><input type="checkbox" class="bulk-flag-action" data-action="add" ${currentCount === totalSelected ? 'disabled' : ''}> Dodaj</label>
                        <span class="change-indicator add-indicator">${currentCount} → ${totalSelected}</span>
                    </div>
                    <div class="action-group">
                        <label><input type="checkbox" class="bulk-flag-action" data-action="remove" ${currentCount === 0 ? 'disabled' : ''}> Odepnij</label>
                        <span class="change-indicator remove-indicator">${currentCount} → 0</span>
                    </div>
                </div>`;
            modalBody.appendChild(flagItem);
        });

        modalBody.querySelectorAll('.bulk-flag-action').forEach(checkbox => {
            checkbox.addEventListener('change', function () {
                const parentItem = this.closest('.bulk-flag-item');
                if (this.checked) {
                    if (this.dataset.action === 'add') {
                        parentItem.querySelector('[data-action="remove"]').checked = false;
                    } else {
                        parentItem.querySelector('[data-action="add"]').checked = false;
                    }
                }
                parentItem.querySelector('.add-indicator').style.display = parentItem.querySelector('[data-action="add"]').checked ? 'inline' : 'none';
                parentItem.querySelector('.remove-indicator').style.display = parentItem.querySelector('[data-action="remove"]').checked ? 'inline' : 'none';
            });
        });
    }

    document.getElementById('saveFlagsButton').addEventListener('click', function () {
        const flagsToAdd = [];
        const flagsToRemove = [];
        document.querySelectorAll('#flagModalBody .bulk-flag-item').forEach(item => {
            const flagId = parseInt(item.dataset.flagId, 10);
            if (item.querySelector('[data-action="add"]').checked) flagsToAdd.push(flagId);
            if (item.querySelector('[data-action="remove"]').checked) flagsToRemove.push(flagId);
        });

        if (flagsToAdd.length === 0 && flagsToRemove.length === 0) {
            return;
        }

        const data = {
            allegroProductIds: Array.from(selectedProductIds).map(id => parseInt(id)),
            flagsToAdd: flagsToAdd,
            flagsToRemove: flagsToRemove
        };

        showLoading();
        fetch('/ProductFlags/UpdateFlagsForMultipleProducts', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(data)
        })
            .then(response => response.json())
            .then(response => {
                if (response.success) {
                    $('#flagModal').modal('hide');
                    showGlobalUpdate(`<p>Pomyślnie zaktualizowano flagi dla ${data.allegroProductIds.length} produktów.</p>`);
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

    function renderPaginationControls(totalItems) {
        const totalPages = Math.ceil(totalItems / itemsPerPage);
        const paginationContainer = document.getElementById('paginationContainer');
        if (!paginationContainer) return;
        paginationContainer.innerHTML = '';
        if (totalPages <= 1) return;

        const createButton = (html, page, isDisabled = false, isActive = false) => {
            const button = document.createElement('button');
            button.innerHTML = html;
            button.disabled = isDisabled;
            if (isActive) button.classList.add('active');
            button.addEventListener('click', () => {
                currentPage = page;
                filterAndSortPrices(false);
            });
            return button;
        };

        paginationContainer.appendChild(createButton('<i class="fa fa-chevron-circle-left" aria-hidden="true"></i>', currentPage - 1, currentPage === 1));
        for (let i = 1; i <= totalPages; i++) {
            paginationContainer.appendChild(createButton(i, i, false, i === currentPage));
        }
        paginationContainer.appendChild(createButton('<i class="fa fa-chevron-circle-right" aria-hidden="true"></i>', currentPage + 1, currentPage === totalPages));
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
            if (colorCounts.hasOwnProperty(item.colorClass)) colorCounts[item.colorClass]++;
        });
        const chartData = Object.values(colorCounts);
        const ctx = document.getElementById('colorChart');
        if (!ctx) return;

        if (chartInstance) {
            chartInstance.data.datasets[0].data = chartData;
            chartInstance.update();
        } else {
            chartInstance = new Chart(ctx.getContext('2d'), {
                type: 'doughnut',
                data: {
                    labels: ['Cena niedostępna', 'Cena solo', 'Cena zawyżona', 'Cena suboptymalna', 'Cena konkurencyjna', 'Cena strategiczna', 'Cena zaniżona'],
                    datasets: [{
                        data: chartData,
                        backgroundColor: ['rgba(230, 230, 230, 1)',
                            'rgba(180, 180, 180, 0.8)',
                            'rgba(171, 37, 32, 0.8)',
                            'rgba(224, 168, 66, 0.8)',
                            'rgba(117, 152, 112, 0.8)',
                            'rgba(13, 110, 253, 0.8)',
                            'rgba(6, 6, 6, 0.8)'
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
                    responsive: true,
                    maintainAspectRatio: false,
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
                                label: (c) => `Produkty: ${c.parsed}`
                            }
                        }
                    }
                }
            });
        }
    }

    const debouncedRenderChart = debounce(renderChart, 300);

    function updateColorCounts(data) {
        const counts = {
            prNoOffer: 0,
            prOnlyMe: 0,
            prToHigh: 0,
            prMid: 0,
            prGood: 0,
            prIdeal: 0,
            prToLow: 0
        };
        data.forEach(item => {
            if (counts.hasOwnProperty(item.colorClass)) counts[item.colorClass]++;
        });

        document.querySelector('label[for="prNoOfferCheckbox"]').textContent = `Cena niedostępna (${counts.prNoOffer})`;
        document.querySelector('label[for="prOnlyMeCheckbox"]').textContent = `Cena solo (${counts.prOnlyMe})`;
        document.querySelector('label[for="prToHighCheckbox"]').textContent = `Cena zawyżona (${counts.prToHigh})`;
        document.querySelector('label[for="prMidCheckbox"]').textContent = `Cena suboptymalna (${counts.prMid})`;
        document.querySelector('label[for="prGoodCheckbox"]').textContent = `Cena konkurencyjna (${counts.prGood})`;
        document.querySelector('label[for="prIdealCheckbox"]').textContent = `Cena strategiczna (${counts.prIdeal})`;
        document.querySelector('label[for="prToLowCheckbox"]').textContent = `Cena zaniżona (${counts.prToLow})`;
    }

    function filterAndSortPrices(resetPageFlag = true) {
        if (resetPageFlag) currentPage = 1;
        showLoading();

        setTimeout(() => {
            let filtered = [...allPrices];

            if (isCatalogViewActive) {
                filtered = groupAndFilterByCatalog(filtered);
            }

            const productSearch = document.getElementById('productSearch').value.toLowerCase();
            if (productSearch) filtered = filtered.filter(p => p.productName && p.productName.toLowerCase().includes(productSearch));

            const storeSearch = document.getElementById('storeSearch').value.toLowerCase();
            if (storeSearch) filtered = filtered.filter(p => (p.storeName && p.storeName.toLowerCase().includes(storeSearch)) || (myStoreName && myStoreName.toLowerCase().includes(storeSearch)));

            const selectedColors = Array.from(document.querySelectorAll('.colorFilter:checked')).map(cb => cb.value);
            if (selectedColors.length > 0) filtered = filtered.filter(p => selectedColors.includes(p.colorClass));

            const selectedProducer = document.getElementById('producerFilterDropdown').value;
            if (selectedProducer) filtered = filtered.filter(p => p.producer === selectedProducer);

            if (selectedFlagsExclude.size > 0) {
                filtered = filtered.filter(item => {
                    if (selectedFlagsExclude.has('noFlag') && (!item.flagIds || item.flagIds.length === 0)) {
                        return false;
                    }
                    return !item.flagIds || !item.flagIds.some(fid => selectedFlagsExclude.has(String(fid)));
                });
            }

            if (selectedFlagsInclude.size > 0) {
                filtered = filtered.filter(item => {
                    if (selectedFlagsInclude.has('noFlag') && (!item.flagIds || item.flagIds.length === 0)) {
                        return true;
                    }
                    return item.flagIds && item.flagIds.some(fid => selectedFlagsInclude.has(String(fid)));
                });
            }

            if (selectedMyBadges.size > 0) {
                filtered = filtered.filter(item => {
                    for (const badge of selectedMyBadges) {
                        if (item[badge] === true) {
                            return true;
                        }
                    }
                    return false;
                });
            }

            for (const [key, direction] of Object.entries(sortingState)) {
                if (direction) {

                    if (key === 'sortName') {
                        filtered.sort((a, b) => {
                            const valA = a.productName || '';
                            const valB = b.productName || '';
                            return direction === 'asc' ? valA.localeCompare(valB) : valB.localeCompare(valA);
                        });
                    } else {

                        filtered.sort((a, b) => {
                            let valA, valB;
                            switch (key) {
                                case 'sortPrice':
                                    valA = a.myPrice;
                                    valB = b.myPrice;
                                    break;
                                case 'sortRaiseAmount':
                                    valA = a.savings;
                                    valB = b.savings;
                                    break;
                                case 'sortRaisePercentage':
                                case 'sortLowerPercentage':
                                    valA = a.percentageDifference;
                                    valB = b.percentageDifference;
                                    break;
                                case 'sortLowerAmount':
                                    valA = a.priceDifference;
                                    valB = b.priceDifference;
                                    break;
                                case 'sortMarginAmount':
                                    valA = a.marginAmount;
                                    valB = b.marginAmount;
                                    break;
                                case 'sortMarginPercentage':
                                    valA = a.marginPercentage;
                                    valB = b.marginPercentage;
                                    break;
                                default:
                                    return 0;
                            }

                            if (direction === 'asc') {
                                return (valB ?? -Infinity) - (valA ?? -Infinity);
                            }

                            else {
                                return (valA ?? Infinity) - (valB ?? Infinity);
                            }
                        });
                    }

                    if (key === 'sortRaiseAmount' || key === 'sortRaisePercentage') {
                        filtered = filtered.filter(item => item.savings != null || item.percentageDifference > 0);
                    }
                    if (key === 'sortLowerAmount' || key === 'sortLowerPercentage') {
                        filtered = filtered.filter(item => item.priceDifference != null || item.percentageDifference < 0);
                    }
                    if (key === 'sortMarginAmount') {
                        filtered = filtered.filter(item => item.marginAmount !== null);
                    }
                    if (key === 'sortMarginPercentage') {
                        filtered = filtered.filter(item => item.marginPercentage !== null);
                    }

                    break;

                }
            }

            renderPrices(filtered);
            debouncedRenderChart(filtered);
            updateColorCounts(filtered);
            updateFlagCounts(filtered);
            updateBadgeCounts(filtered);
            hideLoading();
        }, 10);
    }

    function setupFlagFilterListeners() {
        document.querySelectorAll('.flagFilterInclude').forEach(checkbox => {
            checkbox.addEventListener('change', function () {
                const flagValue = this.value;
                if (this.checked) {
                    const excludeCheckbox = document.getElementById(`flagExclude_${flagValue}`);
                    if (excludeCheckbox) {
                        excludeCheckbox.checked = false;
                        selectedFlagsExclude.delete(flagValue);
                    }
                    selectedFlagsInclude.add(flagValue);
                } else {
                    selectedFlagsInclude.delete(flagValue);
                }
                filterAndSortPrices();
            });
        });

        document.querySelectorAll('.flagFilterExclude').forEach(checkbox => {
            checkbox.addEventListener('change', function () {
                const flagValue = this.value;
                if (this.checked) {
                    const includeCheckbox = document.getElementById(`flagInclude_${flagValue}`);
                    if (includeCheckbox) {
                        includeCheckbox.checked = false;
                        selectedFlagsInclude.delete(flagValue);
                    }
                    selectedFlagsExclude.add(flagValue);
                } else {
                    selectedFlagsExclude.delete(flagValue);
                }
                filterAndSortPrices();
            });
        });
    }

    function validateSimulationData() {
        const storedDataJSON = localStorage.getItem(priceChangeLocalStorageKey);
        if (storedDataJSON) {
            try {
                const storedData = JSON.parse(storedDataJSON);
                if (!storedData.scrapId || storedData.scrapId !== currentScrapId) {
                    console.warn(`Wykryto przestarzałe dane symulacji Allegro. Zapisany scrapId: ${storedData.scrapId}, Obecny scrapId: ${currentScrapId}. Czyszczenie localStorage.`);
                    localStorage.removeItem(priceChangeLocalStorageKey);

                    document.dispatchEvent(new CustomEvent('simulationDataCleared'));
                }
            } catch (e) {
                console.error("Błąd parsowania danych Allegro z localStorage. Czyszczenie...", e);
                localStorage.removeItem(priceChangeLocalStorageKey);
                document.dispatchEvent(new CustomEvent('simulationDataCleared'));
            }
        }
    }

    function refreshPriceBoxStates() {
        const currentStoredChangesJSON = localStorage.getItem(priceChangeLocalStorageKey);
        let currentActiveChangesMap = new Map();

        if (currentStoredChangesJSON) {
            try {
                const parsedData = JSON.parse(currentStoredChangesJSON);
                if (parsedData && parsedData.scrapId === currentScrapId && Array.isArray(parsedData.changes)) {
                    selectedPriceChanges = parsedData.changes;
                    parsedData.changes.forEach(change => currentActiveChangesMap.set(String(change.productId), change));
                } else {
                    selectedPriceChanges = [];
                }
            } catch (err) {
                console.error("Błąd parsowania selectedAllegroPriceChanges:", err);
                selectedPriceChanges = [];
            }
        } else {
            selectedPriceChanges = [];
        }

        const allPriceBoxes = document.querySelectorAll('#priceContainer .price-box');

        allPriceBoxes.forEach(priceBox => {
            const productId = priceBox.dataset.productId;
            if (!productId) return;

            const activeChange = currentActiveChangesMap.get(String(productId));

            if (activeChange) {

                if (!priceBox.classList.contains('price-changed')) {
                    priceBox.classList.add('price-changed');
                }
                const firstButton = priceBox.querySelector('.simulate-change-btn');
                if (firstButton && !firstButton.classList.contains('active')) {
                    const actionLine = firstButton.closest('.price-action-line');
                    if (actionLine) {

                        activateChangeButton(firstButton, actionLine, priceBox, activeChange.stepPriceApplied, activeChange.stepUnitApplied);
                    }
                }
            } else if (priceBox.classList.contains('price-changed')) {

                priceBox.classList.remove('price-changed');
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
            }
        });
    }

    window.refreshPriceBoxStates = refreshPriceBoxStates;

    function applyMassChange(changeType) {
        const productsToChange = currentlyFilteredPrices;
        if (!productsToChange || productsToChange.length === 0) {
            showGlobalUpdate('<p>Brak produktów do zmiany po filtracji.</p>');
            return;
        }

        let countAdded = 0;
        let countRejected = 0;
        let rejectionReasons = {};

        const currentSetStepPrice = setStepPrice;
        const currentUsePriceDifference = usePriceDifference;
        const stepUnit = currentUsePriceDifference ? 'PLN' : '%';

        productsToChange.forEach(item => {

            const isAlreadyChanged = selectedPriceChanges.some(c => String(c.productId) === String(item.productId));
            if (isAlreadyChanged) {
                countAdded++;
                return;
            }

            if (item.anyPromoActive && !allegroMarginSettings.allegroChangePriceForBagdeInCampaign) {
                countRejected++;
                rejectionReasons['Zablokowane (Kampania)'] = (rejectionReasons['Zablokowane (Kampania)'] || 0) + 1;
                return;
            }
            if (item.myIsSuperPrice && !allegroMarginSettings.allegroChangePriceForBagdeSuperPrice) {
                countRejected++;
                rejectionReasons['Zablokowane (Super Cena)'] = (rejectionReasons['Zablokowane (Super Cena)'] || 0) + 1;
                return;
            }
            if (item.myIsTopOffer && !allegroMarginSettings.allegroChangePriceForBagdeTopOffer) {
                countRejected++;
                rejectionReasons['Zablokowane (Top Oferta)'] = (rejectionReasons['Zablokowane (Top Oferta)'] || 0) + 1;
                return;
            }
            if (item.myIsBestPriceGuarantee && !allegroMarginSettings.allegroChangePriceForBagdeBestPriceGuarantee) {
                countRejected++;
                rejectionReasons['Zablokowane (Gwar. Naj. Ceny)'] = (rejectionReasons['Zablokowane (Gwar. Naj. Ceny)'] || 0) + 1;
                return;
            }

            const suggestionData = calculateCurrentSuggestion(item);

            if (!suggestionData || suggestionData.priceType === 'onlyMe' || suggestionData.priceType === 'noOffer' || suggestionData.priceType === 'good_no_step' || (suggestionData.priceType === 'good_step' && suggestionData.totalChangeAmount === 0)) {
                countRejected++;
                rejectionReasons['Brak sugestii (solo/brak oferty/cena optymalna)'] = (rejectionReasons['Brak sugestii (solo/brak oferty/cena optymalna)'] || 0) + 1;
                return;

            }

            const suggestedPrice = suggestionData.suggestedPrice;
            const currentPriceValue = item.myPrice != null ? parseFloat(item.myPrice) : null;
            const basePriceForChangeCalc = item.apiAllegroPriceFromUser != null ? parseFloat(item.apiAllegroPriceFromUser) : currentPriceValue;

            let requiredField = '';
            let requiredLabel = '';
            switch (allegroMarginSettings.identifierForSimulation) {
                case 'ID':
                    requiredField = item.myIdAllegro;
                    requiredLabel = "ID";
                    break;
                case 'EAN':
                default:
                    requiredField = item.ean;
                    requiredLabel = "EAN";
                    break;
            }
            if (!requiredField || requiredField.toString().trim() === "") {
                countRejected++;
                rejectionReasons[`Brak identyfikatora (${requiredLabel})`] = (rejectionReasons[`Brak identyfikatora (${requiredLabel})`] || 0) + 1;
                return;
            }

            if (allegroMarginSettings.useMarginForSimulation) {
                if (item.marginPrice == null) {
                    countRejected++;
                    rejectionReasons['Brak ceny zakupu'] = (rejectionReasons['Brak ceny zakupu'] || 0) + 1;
                    return;
                }

                const marginPrice = parseFloat(item.marginPrice);
                const commission = allegroMarginSettings.includeCommissionInPriceChange && item.apiAllegroCommission != null ? parseFloat(item.apiAllegroCommission) : 0;

                const newNetPrice = suggestedPrice - commission;
                const newMarginAmount = newNetPrice - marginPrice;
                const newMarginPercentage = (marginPrice !== 0) ? (newMarginAmount / marginPrice) * 100 : 0;

                let oldMarginPercentage = -Infinity;
                if (basePriceForChangeCalc != null) {
                    const oldNetPrice = basePriceForChangeCalc - commission;
                    const oldMarginAmount = oldNetPrice - marginPrice;
                    oldMarginPercentage = (marginPrice !== 0) ? (oldMarginAmount / marginPrice) * 100 : Infinity;
                }

                const minMarginPerc = allegroMarginSettings.minimalMarginPercent;

                if (allegroMarginSettings.enforceMinimalMargin && minMarginPerc >= 0) {
                    if (newMarginPercentage < minMarginPerc) {

                        if (!(oldMarginPercentage < minMarginPerc && newMarginPercentage > oldMarginPercentage)) {
                            countRejected++;
                            rejectionReasons['Zbyt niski narzut'] = (rejectionReasons['Zbyt niski narzut'] || 0) + 1;
                            return;
                        }
                    }
                } else if (allegroMarginSettings.enforceMinimalMargin && minMarginPerc < 0) {
                    if (newMarginPercentage < minMarginPerc) {

                        if (!(oldMarginPercentage < minMarginPerc && newMarginPercentage > oldMarginPercentage)) {
                            countRejected++;
                            rejectionReasons['Przekroczona strata'] = (rejectionReasons['Przekroczona strata'] || 0) + 1;
                            return;
                        }
                    }
                }
            }

            const priceChangeEvent = new CustomEvent('priceBoxChange', {
                detail: {
                    productId: String(item.productId),
                    productName: item.productName,
                    currentPrice: currentPriceValue,
                    newPrice: suggestedPrice,
                    storeId: storeId,
                    scrapId: currentScrapId,
                    stepPriceApplied: currentSetStepPrice,
                    stepUnitApplied: stepUnit
                }
            });
            document.dispatchEvent(priceChangeEvent);
            countAdded++;
        });

        refreshPriceBoxStates();

        let summaryHtml = `<p style="margin-bottom:8px; font-size:16px; font-weight:bold;">Masowa zmiana zakończona!</p>
                             <p>Przeanalizowano: <strong>${productsToChange.length}</strong> SKU</p>
                             <p>Dodano/Zaktualizowano: <strong>${countAdded}</strong> SKU</p>
                             <p>Odrzucono: <strong>${countRejected}</strong> SKU</p>`;
        if (countRejected > 0) {
            summaryHtml += `<p style="font-size:12px; margin-top:8px; border-top:1px solid #ccc; padding-top:5px;"><u>Powody odrzucenia:</u><br/>`;
            for (const [reason, count] of Object.entries(rejectionReasons)) {
                summaryHtml += `&bull; ${reason}: ${count}<br/>`;
            }
            summaryHtml += `</p>`;
        }
        showGlobalUpdate(summaryHtml);
    }

    document.addEventListener('priceBoxChangeRemove', function (event) {
        const { productId } = event.detail;
        if (!productId) return;
        selectedPriceChanges = selectedPriceChanges.filter(c => String(c.productId) !== String(productId));
        const priceBox = document.querySelector(`#priceContainer .price-box[data-product-id="${productId}"]`);
        if (priceBox) {
            console.log(`Allegro: Natychmiastowe resetowanie UI dla productId: ${productId} po zdarzeniu priceBoxChangeRemove.`);
            priceBox.classList.remove('price-changed');
            const activeButtons = priceBox.querySelectorAll('.simulate-change-btn.active');
            activeButtons.forEach(button => {
                button.classList.remove('active');
                button.innerHTML = button.dataset.originalText || '<span class="color-square-turquoise"></span> Dodaj zmianę ceny';
                const removeIcon = button.querySelector('i.fa-trash');
                if (removeIcon && removeIcon.parentElement) {
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
            console.warn(`Allegro: Nie znaleziono priceBox dla productId: ${productId} do zresetowania UI.`);
        }
    });

    function setupEventListeners() {
        document.getElementById('productSearch').addEventListener('input', debounce(() => filterAndSortPrices(), 300));
        document.getElementById('storeSearch').addEventListener('input', debounce(() => filterAndSortPrices(), 300));
        document.getElementById('producerFilterDropdown').addEventListener('change', () => filterAndSortPrices());
        document.querySelectorAll('.colorFilter').forEach(el => el.addEventListener('change', () => filterAndSortPrices()));

        document.querySelectorAll('.myBadgeFilter').forEach(el => {
            el.addEventListener('change', function () {
                if (this.checked) {
                    selectedMyBadges.add(this.value);
                } else {
                    selectedMyBadges.delete(this.value);
                }
                filterAndSortPrices();
            });
        });

        document.getElementById('linkOffers').addEventListener('click', function () {
            isCatalogViewActive = !isCatalogViewActive;
            this.classList.toggle('active', isCatalogViewActive);
            filterAndSortPrices();
        });

        document.getElementById('usePriceDifference').addEventListener('change', function () {
            usePriceDifference = this.checked;
            updateUnits(usePriceDifference);

            allPrices = allPrices.map(p => ({
                ...p,
                ...convertPriceValue(p)
            }));
            filterAndSortPrices();
        });

        Object.keys(sortingState).forEach(key => {
            const button = document.getElementById(key);
            if (button) {
                button.addEventListener('click', () => {
                    const currentDirection = sortingState[key];

                    Object.keys(sortingState).forEach(k => sortingState[k] = null);

                    if (currentDirection === 'asc') {
                        sortingState[key] = 'desc';
                    } else if (currentDirection === 'desc') {
                        sortingState[key] = null;
                    } else {
                        sortingState[key] = 'asc';
                    }
                    updateSortButtonVisuals();
                    filterAndSortPrices();
                });
            }
        });

        if (stepPriceInput) {
            stepPriceInput.addEventListener("blur", () => {
                enforceLimits(stepPriceInput, -100.00, 1000.00);
                setStepPrice = parseFloat(stepPriceInput.value.replace(',', '.'));
                updateStepPriceIndicator();
                filterAndSortPrices();
            });
            stepPriceInput.addEventListener('input', updateStepPriceIndicator);
            stepPriceInput.addEventListener('change', updateStepPriceIndicator);
            setTimeout(updateStepPriceIndicator, 0);
        }

        document.getElementById('savePriceValues').addEventListener('click', function () {
            const price1 = parseFloat(document.getElementById('price1').value.replace(',', '.'));
            const price2 = parseFloat(document.getElementById('price2').value.replace(',', '.'));
            const stepPrice = parseFloat(document.getElementById('stepPrice').value.replace(',', '.'));
            const usePriceDiff = document.getElementById('usePriceDifference').checked;
            const data = {
                StoreId: storeId,
                SetPrice1: price1,
                SetPrice2: price2,
                PriceStep: stepPrice,
                UsePriceDifference: usePriceDiff
            };

            fetch('/AllegroPriceHistory/SavePriceValues', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(data)
            })
                .then(res => res.json())
                .then(result => {
                    if (result.success) {

                        setPrice1 = price1;
                        setPrice2 = price2;
                        setStepPrice = stepPrice;
                        usePriceDifference = usePriceDiff;

                        allPrices = allPrices.map(p => ({
                            ...p,
                            ...convertPriceValue(p)
                        }));

                        filterAndSortPrices();

                        showGlobalUpdate('<p style="margin-bottom:8px; font-size:16px; font-weight:bold;">Ustawienia zapisane</p><p>Przeliczam sugestie...</p>');

                    } else {
                        alert('Wystąpił błąd podczas zapisywania progów.');
                    }
                }).catch(err => console.error('Błąd zapisu:', err));
        });

        const openAllegroMarginBtn = document.getElementById('openAllegroMarginSettingsBtn');
        if (openAllegroMarginBtn) {
            openAllegroMarginBtn.addEventListener('click', function () {

                document.getElementById('allegroIdentifierForSimulationInput').value = allegroMarginSettings.identifierForSimulation;
                document.getElementById('allegroUseMarginForSimulationInput').value = allegroMarginSettings.useMarginForSimulation.toString();
                document.getElementById('allegroEnforceMinimalMarginInput').value = allegroMarginSettings.enforceMinimalMargin.toString();
                document.getElementById('allegroMinimalMarginPercentInput').value = allegroMarginSettings.minimalMarginPercent;
                document.getElementById('allegroIncludeCommisionInPriceChangeInput').value = allegroMarginSettings.includeCommissionInPriceChange.toString();

                document.getElementById('allegroChangePriceForBagdeInCampaignInput').value = allegroMarginSettings.allegroChangePriceForBagdeInCampaign.toString();
                document.getElementById('allegroChangePriceForBagdeSuperPriceInput').value = allegroMarginSettings.allegroChangePriceForBagdeSuperPrice.toString();
                document.getElementById('allegroChangePriceForBagdeTopOfferInput').value = allegroMarginSettings.allegroChangePriceForBagdeTopOffer.toString();
                document.getElementById('allegroChangePriceForBagdeBestPriceGuaranteeInput').value = allegroMarginSettings.allegroChangePriceForBagdeBestPriceGuarantee.toString();

                $('#allegroMarginSettingsModal').modal('show');
            });
        }

        const saveAllegroMarginBtn = document.getElementById('saveAllegroMarginSettingsBtn');
        if (saveAllegroMarginBtn) {
            saveAllegroMarginBtn.addEventListener('click', function () {

                const settingsToSave = {
                    StoreId: storeId,
                    AllegroIdentifierForSimulation: document.getElementById('allegroIdentifierForSimulationInput').value,
                    AllegroUseMarginForSimulation: document.getElementById('allegroUseMarginForSimulationInput').value === 'true',
                    AllegroEnforceMinimalMargin: document.getElementById('allegroEnforceMinimalMarginInput').value === 'true',
                    AllegroMinimalMarginPercent: parseFloat(document.getElementById('allegroMinimalMarginPercentInput').value.replace(',', '.')),
                    AllegroIncludeCommisionInPriceChange: document.getElementById('allegroIncludeCommisionInPriceChangeInput').value === 'true',

                    AllegroChangePriceForBagdeSuperPrice: document.getElementById('allegroChangePriceForBagdeSuperPriceInput').value === 'true',
                    AllegroChangePriceForBagdeTopOffer: document.getElementById('allegroChangePriceForBagdeTopOfferInput').value === 'true',
                    AllegroChangePriceForBagdeBestPriceGuarantee: document.getElementById('allegroChangePriceForBagdeBestPriceGuaranteeInput').value === 'true',
                    AllegroChangePriceForBagdeInCampaign: document.getElementById('allegroChangePriceForBagdeInCampaignInput').value === 'true'
                };

                showLoading();

                fetch('/AllegroPriceHistory/SaveAllegroMarginSettings', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify(settingsToSave)
                })
                    .then(response => response.json())
                    .then(data => {
                        if (data.success) {
                            showGlobalUpdate('<p style="margin-bottom:8px; font-size:16px; font-weight:bold;">Ustawienia Allegro zapisane</p>');

                            $('#allegroMarginSettingsModal').modal('hide');

                            loadPrices();
                        } else {
                            showGlobalNotification('<p style="margin-bottom:8px; font-weight:bold;">Błąd zapisu</p><p>Nie udało się zapisać ustawień Allegro.</p>');
                            hideLoading();
                        }
                    })
                    .catch(error => {
                        console.error('Błąd zapisu ustawień marży Allegro:', error);
                        showGlobalNotification('<p style="margin-bottom:8px; font-weight:bold;">Błąd sieci</p><p>Wystąpił błąd podczas komunikacji z serwerem.</p>');
                        hideLoading();
                    });
            });
        }

        const openMassChangeModalBtn = document.getElementById('openMassChangeModalBtn');
        if (openMassChangeModalBtn) {
            openMassChangeModalBtn.addEventListener('click', function () {
                $('#massChangeModal').modal('show');
            });
        }

        const massStrategicBtn = document.getElementById('massStrategicBtn');
        if (massStrategicBtn) {
            massStrategicBtn.addEventListener('click', function () {
                applyMassChange('strategic');
                $('#massChangeModal').modal('hide');
            });
        }

    }

    function showLoading() {
        document.getElementById("loadingOverlay").style.display = "flex";
    }

    function hideLoading() {
        document.getElementById("loadingOverlay").style.display = "none";
    }

    function loadPrices() {
        showLoading();
        fetch(`/AllegroPriceHistory/GetAllegroPrices?storeId=${storeId}`)
            .then(response => response.json())
            .then(data => {

                currentScrapId = data.latestScrapId;
                validateSimulationData();

                const storedChangesJSON = localStorage.getItem(priceChangeLocalStorageKey);
                if (storedChangesJSON) {
                    try {
                        const parsedData = JSON.parse(storedChangesJSON);
                        if (parsedData && parsedData.scrapId === currentScrapId && Array.isArray(parsedData.changes)) {
                            selectedPriceChanges = parsedData.changes;
                        } else {
                            selectedPriceChanges = [];
                        }
                    } catch (e) {
                        console.error("Błąd parsowania LS w loadPrices (Allegro)", e);
                        selectedPriceChanges = [];
                    }
                } else {
                    selectedPriceChanges = [];
                }

                myStoreName = data.myStoreName;
                setPrice1 = data.setPrice1;
                setPrice2 = data.setPrice2;
                setStepPrice = data.stepPrice;
                usePriceDifference = data.usePriceDifference;

                allegroMarginSettings.identifierForSimulation = data.allegroIdentifierForSimulation;
                allegroMarginSettings.useMarginForSimulation = data.allegroUseMarginForSimulation;
                allegroMarginSettings.enforceMinimalMargin = data.allegroEnforceMinimalMargin;
                allegroMarginSettings.minimalMarginPercent = data.allegroMinimalMarginPercent;
                allegroMarginSettings.includeCommissionInPriceChange = data.allegroIncludeCommisionInPriceChange;

                allegroMarginSettings.allegroChangePriceForBagdeSuperPrice = data.allegroChangePriceForBagdeSuperPrice;
                allegroMarginSettings.allegroChangePriceForBagdeTopOffer = data.allegroChangePriceForBagdeTopOffer;
                allegroMarginSettings.allegroChangePriceForBagdeBestPriceGuarantee = data.allegroChangePriceForBagdeBestPriceGuarantee;
                allegroMarginSettings.allegroChangePriceForBagdeInCampaign = data.allegroChangePriceForBagdeInCampaign;

                document.getElementById('price1').value = setPrice1.toFixed(2);
                document.getElementById('price2').value = setPrice2.toFixed(2);
                document.getElementById('stepPrice').value = setStepPrice.toFixed(2);
                updateStepPriceIndicator();
                document.getElementById('usePriceDifference').checked = usePriceDifference;
                updateUnits(usePriceDifference);

                if (data.presetName) {
                    const presetButton = document.getElementById('presetButton');
                    if (presetButton) {
                        presetButton.textContent = data.presetName === 'PriceSafari' ? 'Presety - Widok standardowy PriceSafari' : 'Presety - ' + data.presetName;
                    }
                }

                allPrices = data.prices.map(p => {
                    const priceData = convertPriceValue(p);
                    let marginAmount = null, marginPercentage = null, marginSign = '', marginClass = '';
                    const marginPrice = p.marginPrice != null ? parseFloat(p.marginPrice) : null;
                    const basePriceForCurrentMargin = p.apiAllegroPriceFromUser != null ? parseFloat(p.apiAllegroPriceFromUser) : (p.myPrice != null ? parseFloat(p.myPrice) : null);

                    if (marginPrice != null && basePriceForCurrentMargin != null) {
                        const commission = allegroMarginSettings.includeCommissionInPriceChange && p.apiAllegroCommission != null ? parseFloat(p.apiAllegroCommission) : 0;
                        const netPrice = basePriceForCurrentMargin - commission;
                        marginAmount = netPrice - marginPrice;
                        marginPercentage = (marginPrice !== 0) ? (marginAmount / marginPrice) * 100 : null;
                        marginSign = marginAmount >= 0 ? '+' : '-';
                        marginClass = marginAmount > 0 ? 'priceBox-diff-margin-ins' : (marginAmount < 0 ? 'priceBox-diff-margin-minus-ins' : 'priceBox-diff-margin-neutral-ins');
                    }

                    return {
                        ...p,
                        ...priceData,
                        marginPrice: marginPrice,
                        marginAmount: marginAmount,
                        marginPercentage: marginPercentage,
                        marginSign: marginSign,
                        marginClass: marginClass
                    };
                });

                const producerDropdown = document.getElementById('producerFilterDropdown');
                while (producerDropdown.options.length > 1) producerDropdown.remove(1);
                const producers = [...new Set(allPrices.map(p => p.producer).filter(Boolean))].sort();
                producers.forEach(p => producerDropdown.add(new Option(p, p)));

                document.getElementById('totalProductCount').textContent = allPrices.length;
                document.getElementById('totalPriceCount').textContent = data.priceCount || 0;

                filterAndSortPrices();
                updateFlagCounts(allPrices);
                updateBadgeCounts(allPrices);
                updateSelectionUI();
                updateMarginSortButtonsVisibility();
                refreshPriceBoxStates();

            })
            .catch(error => {
                console.error("Błąd ładowania danych:", error);
                hideLoading();
            });

        window.loadPrices = loadPrices;
    }

    let globalNotificationTimeoutId, globalUpdateTimeoutId;

    function showGlobalNotification(message) {
        const notif = document.getElementById("globalNotification");
        if (!notif) return;
        if (globalNotificationTimeoutId) clearTimeout(globalNotificationTimeoutId);
        notif.innerHTML = message;
        notif.style.display = "block";
        globalNotificationTimeoutId = setTimeout(() => {
            notif.style.display = "none";
        }, 4000);
    }

    function showGlobalUpdate(message) {
        const notif = document.getElementById("globalUpdate");
        if (!notif) return;
        if (globalUpdateTimeoutId) clearTimeout(globalUpdateTimeoutId);
        notif.innerHTML = message;
        notif.style.display = "block";
        globalUpdateTimeoutId = setTimeout(() => {
            notif.style.display = "none";
        }, 4000);
    }

    setupEventListeners();
    loadPrices();
    updateSelectionUI();
});