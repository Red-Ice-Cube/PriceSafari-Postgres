document.addEventListener("DOMContentLoaded", function () {
    let allPrices = [];
    let chartInstance = null;
    let myStoreName = "";

    // Domyślne wartości, nadpisywane przez API
    let setPrice1 = 2.00;
    let setPrice2 = 2.00;
    let setStepPrice = 2.00;
    let usePriceDifference = document.getElementById('usePriceDifference').checked;

    let sortingState = {
        sortName: null, sortPrice: null, sortRaiseAmount: null,
        sortRaisePercentage: null, sortLowerAmount: null, sortLowerPercentage: null,
    };

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
        // Obsługa liczb "nastoletnich" (12-14), które mają końcówkę "Ofert"
        if (count > 10 && count < 20) return `${count} Ofert`;
        const lastDigit = count % 10;
        // Obsługa liczb kończących się na 2, 3, 4
        if ([2, 3, 4].includes(lastDigit)) return `${count} Oferty`;
        // Domyślnie dla 0, 1, 5-9
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


    // Wklej tę funkcję do AllegroPriceHistory.js

    function highlightMatches(fullText, searchTerm) {
        if (!searchTerm || !fullText) return fullText;
        const escapedTerm = searchTerm.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        const regex = new RegExp(escapedTerm, 'gi');
        return fullText.replace(regex, (match) => `<span class="highlighted-text">${match}</span>`);
    }

    function getColorClass(valueToUse, isUniqueBestPrice = false, isSharedBestPrice = false) {
        if (isSharedBestPrice) return "prGood";

        if (isUniqueBestPrice) {
            // ZMIANA START: Dodajemy specjalną logikę dla trybu procentowego
            if (!usePriceDifference) {
                // Jesteśmy w trybie procentowym. Wartość `valueToUse` jest ujemna (np. -5).
                // Musimy porównać jej wartość bezwzględną z progiem.
                // Math.abs(-5) > 2  -->  5 > 2  --> prawda, co oznacza, że cena jest ZANIŻONA.
                return Math.abs(valueToUse) > setPrice1 ? "prToLow" : "prIdeal";
            } else {
                // Jesteśmy w trybie PLN. Wartość `valueToUse` (oszczędność) jest dodatnia.
                // Stara logika jest tutaj poprawna.
                // 3 PLN <= 2 PLN --> fałsz, co oznacza, że cena jest ZANIŻONA.
                return valueToUse <= setPrice1 ? "prIdeal" : "prToLow";
            }
            // ZMIANA KONIEC
        }

        const numericValue = parseFloat(valueToUse);
        if (isNaN(numericValue)) return "prNoOffer";

        // Ta logika dla cen zawyżonych (prMid, prToHigh) jest poprawna,
        // ponieważ `valueToUse` jest wtedy zawsze dodatnie.
        return numericValue < setPrice2 ? "prMid" : "prToHigh";
    }

    function renderPrices(data) {
        const container = document.getElementById('priceContainer');
        container.innerHTML = '';

        const productSearchTerm = document.getElementById('productSearch').value.trim();
        const storeSearchTerm = document.getElementById('storeSearch').value.trim();

        const paginatedData = data.slice((currentPage - 1) * itemsPerPage, currentPage * itemsPerPage);

        paginatedData.forEach(item => {
            const myPrice = item.myPrice != null ? parseFloat(item.myPrice) : null;
            const lowestPrice = item.lowestPrice != null ? parseFloat(item.lowestPrice) : null;

            const highlightedProductName = highlightMatches(item.productName, productSearchTerm);
            const highlightedStoreName = highlightMatches(item.storeName, storeSearchTerm);
            const highlightedMyStoreName = highlightMatches(myStoreName, storeSearchTerm);

            const box = document.createElement('div');
            box.className = 'price-box ' + item.colorClass;
            box.dataset.productId = item.productId;

            // ZMIANA START: Dodajemy atrybut data-details-url
            box.dataset.detailsUrl = `/AllegroPriceHistory/Details?storeId=${storeId}&productId=${item.productId}`;
            box.style.cursor = 'pointer'; // Dodajemy kursor, by wskazać klikalność
            // ZMIANA KONIEC

            box.innerHTML = `
            <div class="price-box-space">
                <div class="price-box-column-name">${highlightedProductName}</div>
            </div>
            <div class="price-box-externalInfo">
                <div class="price-box-column-offers">
                    <span class="data-channel">
                        <img src="/images/AllegroIcon.png" alt="Allegro Icon" style="width:15px; height:15px;" />
                    </span>
                    <div class="offer-count-box">${getOfferText(item.totalOfferCount)}</div>
                </div>
            </div>
            <div class="price-box-data">
                <div class="color-bar ${item.colorClass}"></div>
                <div class="price-box-column">
                    ${(item.onlyMe || lowestPrice == null) ?
                    `<div><span style="font-weight: 500;">Brak konkurencji</span></div>` :
                    `<div><span style="font-weight: 500; font-size: 17px;">${lowestPrice.toFixed(2)} PLN</span><div>${highlightedStoreName || ''}</div></div>`
                }
                </div>
                <div class="price-box-column">
                    ${myPrice != null ?
                    `<div><span style="font-weight: 500; font-size: 17px;">${myPrice.toFixed(2)} PLN</span><div>${highlightedMyStoreName}</div></div>` :
                    `<div><span style="font-weight: 500;">Brak Twojej oferty</span></div>`
                }
                </div>
                <div class="price-box-column-action" id="infoCol-${item.productId}"></div>
            </div>
        `;
            container.appendChild(box);

            // ZMIANA START: Dodajemy event listener do nawigacji
            box.addEventListener('click', function (event) {
                // Upewniamy się, że kliknięcie w przycisk lub inny interaktywny element nie spowoduje nawigacji
                if (event.target.closest('button, a')) {
                    return;
                }
                window.open(this.dataset.detailsUrl, '_blank');
            });
            // ZMIANA KONIEC

            const infoCol = document.getElementById(`infoCol-${item.productId}`);
            if (!infoCol || item.onlyMe || item.isRejected || myPrice === null || lowestPrice === null) {
                return;
            }

            // ... reszta kodu funkcji `renderPrices` (sugestie cen) pozostaje bez zmian ...
            if (item.colorClass === "prToLow" || item.colorClass === "prIdeal") {
                const savingsValue = parseFloat(item.savings);
                const upArrowClass = item.colorClass === 'prToLow' ? 'arrow-up-black' : 'arrow-up-turquoise';
                const suggestedPrice1 = myPrice + savingsValue;
                const amount1 = savingsValue;
                const percentage1 = myPrice > 0 ? (amount1 / myPrice) * 100 : 0;
                let suggestedPrice2, amount2, percentage2;
                let arrowClass2 = upArrowClass;
                if (usePriceDifference) {
                    suggestedPrice2 = suggestedPrice1 - setStepPrice;
                } else {
                    suggestedPrice2 = suggestedPrice1 * (1 - (setStepPrice / 100));
                }
                amount2 = suggestedPrice2 - myPrice;
                percentage2 = myPrice > 0 ? (amount2 / myPrice) * 100 : 0;
                if (amount2 < 0) arrowClass2 = 'arrow-down-turquoise';

                infoCol.innerHTML = `
                <div class="price-box-column">
                    <div class="price-action-line">
                        <span class="${upArrowClass}"></span>
                        <span>${amount1 >= 0 ? '+' : ''}${amount1.toFixed(2)} PLN (${percentage1 >= 0 ? '+' : ''}${percentage1.toFixed(2)}%)</span>
                        <div>= ${suggestedPrice1.toFixed(2)} PLN</div>
                        <span class="color-square-green"></span>
                    </div>
                </div>
                <div class="price-box-column">
                    <div class="price-action-line">
                        <span class="${arrowClass2}"></span>
                        <span>${amount2 >= 0 ? '+' : ''}${amount2.toFixed(2)} PLN (${percentage2 >= 0 ? '+' : ''}${percentage2.toFixed(2)}%)</span>
                        <div>= ${suggestedPrice2.toFixed(2)} PLN</div>
                        <span class="color-square-turquoise"></span>
                    </div>
                </div>
            `;
            } else if (item.colorClass === "prMid" || item.colorClass === "prToHigh") {
                const amountToMatch = myPrice - lowestPrice;
                const percentageToMatch = myPrice > 0 ? (amountToMatch / myPrice) * 100 : 0;
                let strategicPrice, amountToBeat, percentageToBeat;
                if (usePriceDifference) {
                    strategicPrice = lowestPrice - setStepPrice;
                } else {
                    strategicPrice = lowestPrice * (1 - (setStepPrice / 100));
                }
                amountToBeat = myPrice - strategicPrice;
                percentageToBeat = myPrice > 0 ? (amountToBeat / myPrice) * 100 : 0;
                const arrowClass = item.colorClass === "prMid" ? "arrow-down-yellow" : "arrow-down-red";

                infoCol.innerHTML = `
                <div class="price-box-column">
                    <div class="price-action-line">
                        <span class="${arrowClass}"></span>
                        <span>-${amountToMatch.toFixed(2)} PLN (-${percentageToMatch.toFixed(2)}%)</span>
                        <div>= ${lowestPrice.toFixed(2)} PLN</div>
                        <span class="color-square-green"></span>
                    </div>
                </div>
                <div class="price-box-column">
                    <div class="price-action-line">
                        <span class="${arrowClass}"></span>
                        <span>-${amountToBeat.toFixed(2)} PLN (-${percentageToBeat.toFixed(2)}%)</span>
                        <div>= ${strategicPrice.toFixed(2)} PLN</div>
                        <span class="color-square-turquoise"></span>
                    </div>
                </div>
            `;
            } else if (item.colorClass === "prGood") {
                const amount1 = 0;
                const percentage1 = 0;
                const suggestedPrice1 = myPrice;
                let suggestedPrice2, amount2, percentage2;
                let downArrowClass = 'arrow-down-green';

                if (item.storeCount > 1) {
                    if (usePriceDifference) {
                        suggestedPrice2 = myPrice - setStepPrice;
                    } else {
                        let reduction = myPrice * (setStepPrice / 100);
                        if (reduction < 0.01) reduction = 0.01;
                        suggestedPrice2 = myPrice - reduction;
                    }
                    amount2 = suggestedPrice2 - myPrice;
                    percentage2 = myPrice > 0 ? (amount2 / myPrice) * 100 : 0;
                } else {
                    suggestedPrice2 = myPrice;
                    amount2 = 0;
                    percentage2 = 0;
                    downArrowClass = 'no-change-icon-turquoise';
                }

                infoCol.innerHTML = `
                <div class="price-box-column">
                    <div class="price-action-line">
                        <span class="no-change-icon"></span>
                        <span>+${amount1.toFixed(2)} PLN (+${percentage1.toFixed(2)}%)</span>
                        <div>= ${suggestedPrice1.toFixed(2)} PLN</div>
                        <span class="color-square-green"></span>
                    </div>
                </div>
                <div class="price-box-column">
                    <div class="price-action-line">
                        <span class="${downArrowClass}"></span>
                        <span>${amount2.toFixed(2)} PLN (${percentage2.toFixed(2)}%)</span>
                        <div>= ${suggestedPrice2.toFixed(2)} PLN</div>
                        <span class="color-square-turquoise"></span>
                    </div>
                </div>
            `;
            }
        });

        renderPaginationControls(data.length);
        document.getElementById('displayedProductCount').textContent = `${data.length} / ${allPrices.length}`;
    }

   
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
                        backgroundColor: ['#e6e6e6', '#b4b4b4', '#ab2520', '#e0a842', '#759870', '#0d6efd', '#060606'],
                        borderWidth: 1
                    }]
                },
                options: {
                    responsive: true, maintainAspectRatio: false, cutout: '60%',
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
            const productSearch = document.getElementById('productSearch').value.toLowerCase();
            if (productSearch) filtered = filtered.filter(p => p.productName && p.productName.toLowerCase().includes(productSearch));

            const storeSearch = document.getElementById('storeSearch').value.toLowerCase();
            if (storeSearch) filtered = filtered.filter(p => p.storeName && p.storeName.toLowerCase().includes(storeSearch));

            const selectedColors = Array.from(document.querySelectorAll('.colorFilter:checked')).map(cb => cb.value);
            if (selectedColors.length > 0) filtered = filtered.filter(p => selectedColors.includes(p.colorClass));

            const selectedProducer = document.getElementById('producerFilterDropdown').value;
            if (selectedProducer) filtered = filtered.filter(p => p.producer === selectedProducer);

            for (const [key, direction] of Object.entries(sortingState)) {
                if (direction) {
                    // ZMIANA START: Poprawiona i ujednoznaczniona logika sortowania
                    filtered.sort((a, b) => {
                        let valA, valB;

                        switch (key) {
                            case 'sortName':
                                valA = a.productName;
                                valB = b.productName;
                                break;
                            case 'sortPrice':
                                valA = a.lowestPrice;
                                valB = b.lowestPrice;
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
                            default:
                                return 0;
                        }

                        if (valA == null) return 1;
                        if (valB == null) return -1;
                        if (typeof valA === 'string') {
                            return direction === 'asc' ? valA.localeCompare(valB) : valB.localeCompare(valA);
                        }
                        return direction === 'asc' ? valA - valB : valB - valA;
                    });
                    // ZMIANA KONIEC
                    break;
                }
            }

            renderPrices(filtered);
            debouncedRenderChart(filtered);
            updateColorCounts(filtered);
            hideLoading();
        }, 10);
    }

    function setupEventListeners() {
        document.getElementById('productSearch').addEventListener('input', debounce(() => filterAndSortPrices(), 300));
        document.getElementById('storeSearch').addEventListener('input', debounce(() => filterAndSortPrices(), 300));
        document.getElementById('producerFilterDropdown').addEventListener('change', () => filterAndSortPrices());
        document.querySelectorAll('.colorFilter').forEach(el => el.addEventListener('change', () => filterAndSortPrices()));

        document.getElementById('usePriceDifference').addEventListener('change', function () {
            usePriceDifference = this.checked;
            updateUnits(usePriceDifference);
            // Przelicz klasy kolorów dla wszystkich produktów i odśwież widok
            allPrices = allPrices.map(p => ({ ...p, ...convertPriceValue(p) }));
            filterAndSortPrices();
        });

        Object.keys(sortingState).forEach(key => {
            const button = document.getElementById(key);
            if (button) {
                button.addEventListener('click', () => {
                    const currentDirection = sortingState[key];

                    // Resetuj wszystkie stany sortowania
                    Object.keys(sortingState).forEach(k => sortingState[k] = null);

                    // Ustaw nowy stan dla klikniętego przycisku
                    if (currentDirection === 'asc') {
                        sortingState[key] = 'desc';
                    } else if (currentDirection === 'desc') {
                        sortingState[key] = null; // Trzecie kliknięcie resetuje
                    } else {
                        sortingState[key] = 'asc';
                    }

                    // Zaktualizuj wygląd wszystkich przycisków
                    updateSortButtonVisuals();

                    // Przefiltruj i posortuj dane
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
                        loadPrices(); // Przeładuj dane, aby pobrać i zastosować nowe ustawienia
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

                allPrices = data.prices.map(p => ({ ...p, ...convertPriceValue(p) }));

                const producerDropdown = document.getElementById('producerFilterDropdown');
                // Wyczyść stare opcje (oprócz pierwszej)
                while (producerDropdown.options.length > 1) {
                    producerDropdown.remove(1);
                }
                const producers = [...new Set(allPrices.map(p => p.producer).filter(Boolean))].sort();
                producers.forEach(p => producerDropdown.add(new Option(p, p)));

                document.getElementById('totalProductCount').textContent = allPrices.length;
                document.getElementById('totalPriceCount').textContent = data.priceCount || 0;

                filterAndSortPrices();
            })
            .catch(error => console.error("Błąd ładowania danych:", error))
            .finally(hideLoading);
    }

    setupEventListeners();
    loadPrices();
});