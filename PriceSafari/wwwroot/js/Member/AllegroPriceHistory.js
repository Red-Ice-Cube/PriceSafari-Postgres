document.addEventListener("DOMContentLoaded", function () {
    let allPrices = [];
    let currentlyFilteredPrices = [];
    let chartInstance = null;
    let myStoreName = "";

    let setPrice1 = 2.00;
    let setPrice2 = 2.00;
    let setStepPrice = 2.00;
    let usePriceDifference = document.getElementById('usePriceDifference').checked;

    let selectedProductId = null;
    let selectedFlagsInclude = new Set();
    let selectedFlagsExclude = new Set();

    const selectionStorageKey = `selectedAllegroProducts_${storeId}`;

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
        sortName: null, sortPrice: null, sortRaiseAmount: null,
        sortRaisePercentage: null, sortLowerAmount: null, sortLowerPercentage: null,
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
        if (price.isRejected) return { valueToUse: null, colorClass: 'prNoOffer' };
        if (price.onlyMe) return { valueToUse: null, colorClass: 'prOnlyMe' };

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

        if (valueForColor == null) return { valueToUse: null, colorClass: 'prNoOffer' };

        const colorClass = getColorClass(valueForColor, price.isUniqueBestPrice, price.isSharedBestPrice);
        return { valueToUse: valueForColor, colorClass: colorClass };
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
            case 'sortName': return 'A-Z';
            case 'sortPrice': return 'Cena';
            case 'sortRaiseAmount': return 'Podnieś PLN';
            case 'sortRaisePercentage': return 'Podnieś %';
            case 'sortLowerAmount': return 'Obniż PLN';
            case 'sortLowerPercentage': return 'Obniż %';
            default: return '';
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
        let text = '', className = '';
        switch (deliveryTime) {
            case 1: text = 'Dostawa jutro'; className = 'Delivery1'; break;
            case 2: text = 'Dostawa pojutrze'; className = 'Delivery2'; break;
            case 3: text = 'Długa dostawa'; className = 'Delivery3'; break;
            default: return '';
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
                    <span class="flag-name-count">${flag.flagName} (${count})</span>
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
            <span class="flag-name-count">Brak flagi (${noFlagCount})</span>
        </div>`;
        flagContainer.insertAdjacentHTML('beforeend', noFlagElementHTML);

        setupFlagFilterListeners();
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

    function activateChangeButton(button, actionLine, priceBox) {
        const priceBoxColumn = actionLine.parentElement;
        if (priceBoxColumn) {
            priceBoxColumn.classList.add('active');
        }

        const colorSquare = button.querySelector('span[class^="color-square-"]');
        const colorSquareHTML = colorSquare ? colorSquare.outerHTML : '';

        button.classList.add('active');
        button.innerHTML = colorSquareHTML + " Dodano";

        const removeLink = document.createElement('span');
        removeLink.innerHTML = " <i class='fa fa-trash' style='font-size:12px; display:flex; color:white; margin-left:4px; margin-top:3px;'></i>";
        removeLink.style.cursor = "pointer";
        removeLink.style.pointerEvents = 'auto';

        removeLink.addEventListener('click', function (ev) {
            ev.stopPropagation();

            if (priceBoxColumn) {
                priceBoxColumn.classList.remove('active');
            }

            button.classList.remove('active');
            button.innerHTML = button.dataset.originalText || "Zmień cenę";
            priceBox.classList.remove('price-changed');

        });

        button.appendChild(removeLink);
        priceBox.classList.add('price-changed');
    }

    function attachPriceChangeListener(actionLine, suggestedPrice, priceBox, productId, productName, currentPriceValue, item) {
        const button = actionLine.querySelector('.simulate-change-btn');
        if (!button) return;

        actionLine.addEventListener('click', function (e) {
            e.stopPropagation();

            const currentButton = this.querySelector('.simulate-change-btn');

            if (priceBox.classList.contains('price-changed') || (currentButton && currentButton.classList.contains('active'))) {
                console.log("Zmiana ceny już aktywna dla produktu", productId);
                return;
            }

            activateChangeButton(currentButton, this, priceBox);

            let message = `<p style="margin-bottom:8px; font-size:16px; font-weight:bold;">Zmiana ceny dodana (wizualnie)</p>`;
            message += `<p style="margin:4px 0;"><strong>Produkt:</strong> ${productName}</p>`;
            message += `<p style="margin:4px 0;"><strong>Nowa cena:</strong> ${suggestedPrice.toFixed(2)} PLN</p>`;
            showGlobalUpdate(message);
        });
    }

    function renderPrices(data) {
        const container = document.getElementById('priceContainer');
        container.innerHTML = '';

        const productSearchTerm = document.getElementById('productSearch').value.trim();
        const storeSearchTerm = document.getElementById('storeSearch').value.trim();

        const paginatedData = data.slice((currentPage - 1) * itemsPerPage, currentPage * itemsPerPage);
        currentlyFilteredPrices = paginatedData;

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
            let competitorPromoInfoBadge = '';
            if (item.isPromoted) {
                competitorPromoInfoBadge = `<div class="PromoInfo">Promowane</div>`;
            } else if (item.isSponsored) {
                competitorPromoInfoBadge = `<div class="PromoInfo">Sponsorowane</div>`;
            }
            const mySuperPriceBadge = item.myIsSuperPrice ? `<div class="SuperPrice">SUPERCENA</div>` : '';
            let myPromoInfoBadge = '';
            if (item.myIsPromoted) {
                myPromoInfoBadge = `<div class="PromoInfo">Promowane</div>`;
            } else if (item.isSponsored) {
                myPromoInfoBadge = `<div class="PromoInfo">Sponsorowane</div>`;
            }
            const competitorPriceStyle = item.isBestPriceGuarantee ? 'color: #169A23;' : '';
            const competitorPriceIcon = item.isBestPriceGuarantee ? `<img src="/images/TopPrice.png" alt="Gwarancja Najniższej Ceny" title="Gwarancja Najniższej Ceny" style="width: 18px; height: 18px; vertical-align: middle; margin-top: -5px;">` : '';
            const myPriceStyle = item.myIsBestPriceGuarantee ? 'color: #169A23;' : '';
            const myPriceIcon = item.myIsBestPriceGuarantee ? `<img src="/images/TopPrice.png" alt="Gwarancja Najniższej Ceny" title="Gwarancja Najniższej Ceny" style="width: 18px; height: 18px; vertical-align: middle;  margin-top: -5px;">` : '';

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
                            <div>
                                <div style="display: flex; align-items: center; gap: 6px; flex-wrap: wrap;">
                                    <span style="font-weight: 500; font-size: 17px; ${competitorPriceStyle}">${lowestPrice.toFixed(2)} PLN</span>
                                    ${competitorPriceIcon}
                                    ${competitorSuperPriceBadge}
                                </div>
                                <div>
                                    ${highlightedStoreName || ''}
                                    ${item.isSuperSeller ? `<img src="/images/SuperSeller.png" alt="Super Sprzedawca" title="Super Sprzedawca" style="width: 18px; height: 18px; vertical-align: middle; margin-bottom: 1px;">` : ''}
                                </div>
                                <div style="height: 16.5px; margin-top:-2px; display:flex; align-content:center;">${competitorPromoInfoBadge}</div>
                            </div>
                        </div>
                        <div class="price-box-column-text">
                            <div class="data-channel">
                                ${item.isTopOffer ? `<div class="TopOffer">Top oferta</div>` : ''}
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
                            <div>
                                <div style="display: flex; align-items: center; gap: 6px; flex-wrap: wrap;">
                                    <span style="font-weight: 500; font-size: 17px; ${myPriceStyle}">${myPrice.toFixed(2)} PLN</span>
                                    ${myPriceIcon}
                                    ${mySuperPriceBadge}
                                </div>
                                <div>
                                    ${highlightedMyStoreName}
                                    ${item.myIsSuperSeller ? `<img src="/images/SuperSeller.png" alt="Super Sprzedawca" title="Super Sprzedawca" style="width: 18px; height: 18px; vertical-align: middle; margin-bottom: 1px;">` : ''}
                                </div>
                                <div style="height: 16.5px; margin-top:-2px; display:flex; align-content:center;">${myPromoInfoBadge}</div>
                            </div>
                        </div>
                        <div class="price-box-column-text">
                            <div class="data-channel">
                                ${item.myIsTopOffer ? `<div class="TopOffer">Top oferta</div>` : ''}
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
            if (item.myIdAllegro) {
                apiBox.innerHTML = `ID ${item.myIdAllegro}`;
            } else {
                apiBox.innerHTML = 'Brak ID';
            }

            rightColumn.appendChild(selectProductButton);
            rightColumn.appendChild(apiBox);

            priceBoxSpace.appendChild(leftColumn);
            priceBoxSpace.appendChild(rightColumn);

            box.addEventListener('click', function (event) {
                if (event.target.closest('button, a, img')) {
                    return;
                }
                window.open(this.dataset.detailsUrl, '_blank');
            });

            const infoCol = box.querySelector('.price-box-column-action');

            if (infoCol && !item.onlyMe && !item.isRejected && myPrice !== null && lowestPrice !== null) {
                if (item.colorClass === "prToLow" || item.colorClass === "prIdeal") {
                    const savingsValue = parseFloat(item.savings);
                    const upArrowClass = item.colorClass === 'prToLow' ? 'arrow-up-black' : 'arrow-up-turquoise';
                    const suggestedPrice1 = myPrice + savingsValue;
                    const amountToSuggestedPrice1 = savingsValue;
                    const percentageToSuggestedPrice1 = myPrice > 0 ? (amountToSuggestedPrice1 / myPrice) * 100 : 0;

                    let suggestedPrice2, amountToSuggestedPrice2, percentageToSuggestedPrice2, arrowClass2 = upArrowClass;

                    if (usePriceDifference) {
                        suggestedPrice2 = suggestedPrice1 - setStepPrice;
                    } else {
                        suggestedPrice2 = suggestedPrice1 * (1 - (setStepPrice / 100));
                    }
                    amountToSuggestedPrice2 = suggestedPrice2 - myPrice;
                    percentageToSuggestedPrice2 = myPrice > 0 ? (amountToSuggestedPrice2 / myPrice) * 100 : 0;
                    if (amountToSuggestedPrice2 < 0) {
                        arrowClass2 = 'arrow-down-turquoise';
                    }

                    const matchPriceBox = document.createElement('div');
                    matchPriceBox.className = 'price-box-column';
                    const matchPriceLine = document.createElement('div');
                    matchPriceLine.className = 'price-action-line';
                    matchPriceLine.innerHTML = `
                    <span class="${upArrowClass}"></span>
                    <div class="price-diff-stack">
                        <span class="diff-amount small-font">${amountToSuggestedPrice1 >= 0 ? '+' : ''}${amountToSuggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">PLN</span></span>
                        <span class="diff-percentage small-font">${percentageToSuggestedPrice1 >= 0 ? '+' : ''}${percentageToSuggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">%</span></span>
                    </div>
                    <div class="small-font">= ${suggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">PLN</span></div>
                `;
                    const matchPriceBtn = document.createElement('button');
                    matchPriceBtn.className = 'simulate-change-btn';
                    const matchBtnContent = `<span class="color-square-green"></span> Zmień cenę`;
                    matchPriceBtn.innerHTML = matchBtnContent;
                    matchPriceBtn.dataset.originalText = matchBtnContent;
                    matchPriceLine.appendChild(matchPriceBtn);
                    matchPriceBox.appendChild(matchPriceLine);
                    attachPriceChangeListener(matchPriceLine, suggestedPrice1, box, item.productId, item.productName, myPrice, item);

                    const strategicPriceBox = document.createElement('div');
                    strategicPriceBox.className = 'price-box-column';
                    const strategicPriceLine = document.createElement('div');
                    strategicPriceLine.className = 'price-action-line';
                    strategicPriceLine.innerHTML = `
                    <span class="${arrowClass2}"></span>
                    <div class="price-diff-stack">
                        <span class="diff-amount small-font">${amountToSuggestedPrice2 >= 0 ? '+' : ''}${amountToSuggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">PLN</span></span>
                        <span class="diff-percentage small-font">${percentageToSuggestedPrice2 >= 0 ? '+' : ''}${percentageToSuggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">%</span></span>
                    </div>
                    <div class="small-font">= ${suggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">PLN</span></div>
                `;
                    const strategicPriceBtn = document.createElement('button');
                    strategicPriceBtn.className = 'simulate-change-btn';
                    const strategicBtnContent = `<span class="color-square-turquoise"></span> Zmień cenę`;
                    strategicPriceBtn.innerHTML = strategicBtnContent;
                    strategicPriceBtn.dataset.originalText = strategicBtnContent;
                    strategicPriceLine.appendChild(strategicPriceBtn);
                    strategicPriceBox.appendChild(strategicPriceLine);
                    attachPriceChangeListener(strategicPriceLine, suggestedPrice2, box, item.productId, item.productName, myPrice, item);

                    infoCol.appendChild(matchPriceBox);
                    infoCol.appendChild(strategicPriceBox);

                } else if (item.colorClass === "prMid" || item.colorClass === "prToHigh") {
                    const amountToMatchLowestPrice = myPrice - lowestPrice;
                    const percentageToMatchLowestPrice = myPrice > 0 ? (amountToMatchLowestPrice / myPrice) * 100 : 0;
                    let strategicPrice, amountToBeatLowestPrice, percentageToBeatLowestPrice;
                    if (usePriceDifference) {
                        strategicPrice = lowestPrice - setStepPrice;
                    } else {
                        strategicPrice = lowestPrice * (1 - setStepPrice / 100);
                    }
                    amountToBeatLowestPrice = myPrice - strategicPrice;
                    percentageToBeatLowestPrice = myPrice > 0 ? (amountToBeatLowestPrice / myPrice) * 100 : 0;
                    const arrowClass = item.colorClass === "prMid" ? "arrow-down-yellow" : "arrow-down-red";

                    const matchPriceBox = document.createElement('div');
                    matchPriceBox.className = 'price-box-column';
                    const matchPriceLine = document.createElement('div');
                    matchPriceLine.className = 'price-action-line';
                    matchPriceLine.innerHTML = `
                    <span class="${arrowClass}"></span>
                    <div class="price-diff-stack">
                        <span class="diff-amount small-font">-${amountToMatchLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">PLN</span></span>
                        <span class="diff-percentage small-font">-${percentageToMatchLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">%</span></span>
                    </div>
                    <div class="small-font">= ${lowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">PLN</span></div>
                `;
                    const matchPriceBtn = document.createElement('button');
                    matchPriceBtn.className = 'simulate-change-btn';
                    const matchBtnContent = `<span class="color-square-green"></span> Zmień cenę`;
                    matchPriceBtn.innerHTML = matchBtnContent;
                    matchPriceBtn.dataset.originalText = matchBtnContent;
                    matchPriceLine.appendChild(matchPriceBtn);
                    matchPriceBox.appendChild(matchPriceLine);
                    attachPriceChangeListener(matchPriceLine, lowestPrice, box, item.productId, item.productName, myPrice, item);

                    const strategicPriceBox = document.createElement('div');
                    strategicPriceBox.className = 'price-box-column';
                    const strategicPriceLine = document.createElement('div');
                    strategicPriceLine.className = 'price-action-line';
                    strategicPriceLine.innerHTML = `
                    <span class="${arrowClass}"></span>
                    <div class="price-diff-stack">
                        <span class="diff-amount small-font">-${amountToBeatLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">PLN</span></span>
                        <span class="diff-percentage small-font">-${percentageToBeatLowestPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">%</span></span>
                    </div>
                    <div class="small-font">= ${strategicPrice.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">PLN</span></div>
                `;
                    const strategicPriceBtn = document.createElement('button');
                    strategicPriceBtn.className = 'simulate-change-btn';
                    const strategicBtnContent = `<span class="color-square-turquoise"></span> Zmień cenę`;
                    strategicPriceBtn.innerHTML = strategicBtnContent;
                    strategicPriceBtn.dataset.originalText = strategicBtnContent;
                    strategicPriceLine.appendChild(strategicPriceBtn);
                    strategicPriceBox.appendChild(strategicPriceLine);
                    attachPriceChangeListener(strategicPriceLine, strategicPrice, box, item.productId, item.productName, myPrice, item);

                    infoCol.appendChild(matchPriceBox);
                    infoCol.appendChild(strategicPriceBox);

                } else if (item.colorClass === "prGood") {
                    const amountToSuggestedPrice1 = 0;
                    const percentageToSuggestedPrice1 = 0;
                    const suggestedPrice1 = myPrice;
                    let amountToSuggestedPrice2, percentageToSuggestedPrice2, suggestedPrice2, downArrowClass = 'arrow-down-green';

                    if (item.totalOfferCount > 1) {
                        if (usePriceDifference) {
                            suggestedPrice2 = myPrice - setStepPrice;
                        } else {
                            let reduction = myPrice * (setStepPrice / 100);
                            if (reduction < 0.01) reduction = 0.01;
                            suggestedPrice2 = myPrice - reduction;
                        }
                        amountToSuggestedPrice2 = suggestedPrice2 - myPrice;
                        percentageToSuggestedPrice2 = myPrice > 0 ? (amountToSuggestedPrice2 / myPrice) * 100 : 0;
                    } else {
                        suggestedPrice2 = myPrice;
                        amountToSuggestedPrice2 = 0;
                        percentageToSuggestedPrice2 = 0;
                        downArrowClass = 'no-change-icon-turquoise';
                    }

                    const matchPriceBox = document.createElement('div');
                    matchPriceBox.className = 'price-box-column';
                    const matchPriceLine = document.createElement('div');
                    matchPriceLine.className = 'price-action-line';
                    matchPriceLine.innerHTML = `
                    <span class="no-change-icon"></span>
                    <div class="price-diff-stack">
                        <span class="diff-amount small-font">+${amountToSuggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">PLN</span></span>
                        <span class="diff-percentage small-font">+${percentageToSuggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">%</span></span>
                    </div>
                    <div class="small-font">= ${suggestedPrice1.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">PLN</span></div>
                `;
                    const matchPriceBtn = document.createElement('button');
                    matchPriceBtn.className = 'simulate-change-btn';
                    const matchBtnContent = `<span class="color-square-green"></span> Zmień cenę`;
                    matchPriceBtn.innerHTML = matchBtnContent;
                    matchPriceBtn.dataset.originalText = matchBtnContent;
                    matchPriceLine.appendChild(matchPriceBtn);
                    matchPriceBox.appendChild(matchPriceLine);
                    attachPriceChangeListener(matchPriceLine, suggestedPrice1, box, item.productId, item.productName, myPrice, item);

                    const strategicPriceBox = document.createElement('div');
                    strategicPriceBox.className = 'price-box-column';
                    const strategicPriceLine = document.createElement('div');
                    strategicPriceLine.className = 'price-action-line';
                    strategicPriceLine.innerHTML = `
                    <span class="${downArrowClass}"></span>
                    <div class="price-diff-stack">
                        <span class="diff-amount small-font">${amountToSuggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">PLN</span></span>
                        <span class="diff-percentage small-font">${percentageToSuggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">%</span></span>
                    </div>
                    <div class="small-font">= ${suggestedPrice2.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} <span class="unit">PLN</span></div>
                `;
                    const strategicPriceBtn = document.createElement('button');
                    strategicPriceBtn.className = 'simulate-change-btn';
                    const strategicBtnContent = `<span class="color-square-turquoise"></span> Zmień cenę`;
                    strategicPriceBtn.innerHTML = strategicBtnContent;
                    strategicPriceBtn.dataset.originalText = strategicBtnContent;
                    strategicPriceLine.appendChild(strategicPriceBtn);
                    strategicPriceBox.appendChild(strategicPriceLine);
                    attachPriceChangeListener(strategicPriceLine, suggestedPrice2, box, item.productId, item.productName, myPrice, item);

                    infoCol.appendChild(matchPriceBox);
                    infoCol.appendChild(strategicPriceBox);
                }
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
        table.innerHTML = `<thead><tr><th style="width: 70%;">Nazwa Produktu</th><th>ID Oferty</th><th class="text-center">Akcja</th></tr></thead>`;
        const tbody = document.createElement('tbody');

        selectedProductIds.forEach(productId => {
            const product = allPrices.find(p => p.productId.toString() === productId);
            if (product) {
                const tr = document.createElement('tr');
                tr.innerHTML = `
                    <td>${product.productName}</td>
                    <td>${product.myIdAllegro || 'Brak ID'}</td>
                    <td class="text-center">
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
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ allegroProductIds: Array.from(selectedProductIds, id => parseInt(id)) })
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
            headers: { 'Content-Type': 'application/json' },
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
        const colorCounts = { prNoOffer: 0, prOnlyMe: 0, prToHigh: 0, prMid: 0, prGood: 0, prIdeal: 0, prToLow: 0 };
        data.forEach(item => { if (colorCounts.hasOwnProperty(item.colorClass)) colorCounts[item.colorClass]++; });
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
                            'rgba(6, 6, 6, 0.8)'],
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
                    responsive: true, maintainAspectRatio: false, cutout: '65%', layout: {
                        padding: 4
                    },
                    plugins: { legend: { display: false }, tooltip: { callbacks: { label: (c) => `Produkty: ${c.parsed}` } } }
                }
            });
        }
    }

    const debouncedRenderChart = debounce(renderChart, 300);

    function updateColorCounts(data) {
        const counts = { prNoOffer: 0, prOnlyMe: 0, prToHigh: 0, prMid: 0, prGood: 0, prIdeal: 0, prToLow: 0 };
        data.forEach(item => { if (counts.hasOwnProperty(item.colorClass)) counts[item.colorClass]++; });

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

     
            for (const [key, direction] of Object.entries(sortingState)) {
                if (direction) {
                    filtered.sort((a, b) => {
                        let valA, valB;
                        switch (key) {
                            case 'sortName': valA = a.productName; valB = b.productName; break;
                            case 'sortPrice': valA = a.lowestPrice; valB = b.lowestPrice; break;
                            case 'sortRaiseAmount': valA = a.savings; valB = b.savings; break;
                            case 'sortRaisePercentage': case 'sortLowerPercentage': valA = a.percentageDifference; valB = b.percentageDifference; break;
                            case 'sortLowerAmount': valA = a.priceDifference; valB = b.priceDifference; break;
                            default: return 0;
                        }
                        if (valA == null) return 1;
                        if (valB == null) return -1;
                        if (typeof valA === 'string') {
                            return direction === 'asc' ? valA.localeCompare(valB) : valB.localeCompare(valA);
                        }
                        return direction === 'asc' ? valA - valB : valB - valA;
                    });
                    break;
                }
            }
            renderPrices(filtered);
            debouncedRenderChart(filtered);
            updateColorCounts(filtered);

            updateFlagCounts(filtered);

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

    function setupEventListeners() {
        document.getElementById('productSearch').addEventListener('input', debounce(() => filterAndSortPrices(), 300));
        document.getElementById('storeSearch').addEventListener('input', debounce(() => filterAndSortPrices(), 300));
        document.getElementById('producerFilterDropdown').addEventListener('change', () => filterAndSortPrices());
        document.querySelectorAll('.colorFilter').forEach(el => el.addEventListener('change', () => filterAndSortPrices()));

        document.getElementById('linkOffers').addEventListener('click', function () {
            isCatalogViewActive = !isCatalogViewActive;
            this.classList.toggle('active', isCatalogViewActive);
            filterAndSortPrices();
        });

        document.getElementById('usePriceDifference').addEventListener('change', function () {
            usePriceDifference = this.checked;
            updateUnits(usePriceDifference);
            allPrices = allPrices.map(p => ({ ...p, ...convertPriceValue(p) }));
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

        document.getElementById('savePriceValues').addEventListener('click', function () {
            const price1 = parseFloat(document.getElementById('price1').value);
            const price2 = parseFloat(document.getElementById('price2').value);
            const stepPrice = parseFloat(document.getElementById('stepPrice').value);
            const usePriceDiff = document.getElementById('usePriceDifference').checked;
            const data = { StoreId: storeId, SetPrice1: price1, SetPrice2: price2, PriceStep: stepPrice, UsePriceDifference: usePriceDiff };

            fetch('/AllegroPriceHistory/SavePriceValues', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            })
                .then(res => res.json())
                .then(result => {
                    if (result.success) {
                        loadPrices();
                    } else {
                        alert('Wystąpił błąd podczas zapisywania progów.');
                    }
                }).catch(err => console.error('Błąd zapisu:', err));
        });

    }

    function showLoading() { document.getElementById("loadingOverlay").style.display = "flex"; }
    function hideLoading() { document.getElementById("loadingOverlay").style.display = "none"; }

    function loadPrices() {
        showLoading();
        fetch(`/AllegroPriceHistory/GetAllegroPrices?storeId=${storeId}`)
            .then(response => response.json())
            .then(data => {
                myStoreName = data.myStoreName;
                setPrice1 = data.setPrice1;
                setPrice2 = data.setPrice2;
                setStepPrice = data.stepPrice;
                usePriceDifference = data.usePriceDifference;
                document.getElementById('price1').value = setPrice1.toFixed(2);
                document.getElementById('price2').value = setPrice2.toFixed(2);
                document.getElementById('stepPrice').value = setStepPrice.toFixed(2);
                document.getElementById('usePriceDifference').checked = usePriceDifference;
                updateUnits(usePriceDifference);

                if (data.presetName) {
                    const presetButton = document.getElementById('presetButton');
                    if (presetButton) {
                        if (data.presetName === 'PriceSafari') {
                            presetButton.textContent = 'Presety - Widok standardowy PriceSafari';
                        } else {
                            presetButton.textContent = 'Presety - ' + data.presetName;
                        }
                    }
                }

                allPrices = data.prices.map(p => ({ ...p, ...convertPriceValue(p) }));

                const producerDropdown = document.getElementById('producerFilterDropdown');
                while (producerDropdown.options.length > 1) {
                    producerDropdown.remove(1);
                }
                const producers = [...new Set(allPrices.map(p => p.producer).filter(Boolean))].sort();
                producers.forEach(p => producerDropdown.add(new Option(p, p)));

                document.getElementById('totalProductCount').textContent = allPrices.length;
                document.getElementById('totalPriceCount').textContent = data.priceCount || 0;

                filterAndSortPrices();

                updateFlagCounts(allPrices);
                updateSelectionUI();

            })
            .catch(error => console.error("Błąd ładowania danych:", error))
            .finally(hideLoading);

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
});