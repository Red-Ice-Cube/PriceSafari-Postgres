const loadedPrices = {};
let allProducts = {};

// Pomocnicza funkcja do formatowania waluty po polsku
// Zamienia 1649.00 na "1 649,00"
function formatMoneyPL(amount) {
    if (typeof amount !== 'number') return 'B/D';
    return amount.toLocaleString('pl-PL', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
        useGrouping: true // To dodaje spację jako separator tysięcy
    });
}

document.addEventListener('DOMContentLoaded', () => {

    if (typeof storeId === 'undefined' || typeof competitorStoreName === 'undefined' || typeof storeName === 'undefined' || typeof apiConfig === 'undefined') {
        console.error("Kluczowe zmienne globalne (storeId, competitorStoreName, storeName, apiConfig) nie są zdefiniowane.");
        const container = document.querySelector('.Vert-table') || document.querySelector('.table-container');
        if (container) container.innerHTML = '<p style="color:red; text-align:center; padding: 20px;">Błąd konfiguracji: Brak niezbędnych danych API lub sklepu.</p>';
        return;
    }

    loadScrapHistoryOptions(storeId, competitorStoreName);

    const increaseCheckbox = document.getElementById('filterIncrease');
    const decreaseCheckbox = document.getElementById('filterDecrease');
    const changeCheckbox = document.getElementById('filterChange');

    if (increaseCheckbox) increaseCheckbox.addEventListener('change', () => renderPrices());
    if (decreaseCheckbox) decreaseCheckbox.addEventListener('change', () => renderPrices());
    if (changeCheckbox) changeCheckbox.addEventListener('change', () => renderPrices());

    const productSearchInput = document.getElementById('productSearch');
    if (productSearchInput) {
        productSearchInput.addEventListener('keyup', function () {
            filterProducts(this.value);
        });
    }
});

function loadScrapHistoryOptions(currentStoreId, currentCompetitorStoreName) {
    const baseUrl = apiConfig.getHistoryUrl;

    const url = baseUrl.includes('storeId') ? baseUrl : `${baseUrl}${baseUrl.includes('?') ? '&' : '?'}storeId=${currentStoreId}`;

    fetch(url)
        .then(response => {
            if (!response.ok) throw new Error(`Błąd HTTP! Status: ${response.status}`);
            return response.json();
        })
        .then(scrapHistoryIds => {
            const container = document.getElementById('scrapHistoryCheckboxes');
            if (!container) return;

            container.innerHTML = '';
            let firstCheckbox = null;

            // Sortowanie: najnowsze na górze
            scrapHistoryIds.sort((a, b) => new Date(b.date) - new Date(a.date));

            scrapHistoryIds.forEach((scrap, index) => {

                const label = document.createElement('label');
                label.className = 'scraphistory-item';

                const checkbox = document.createElement('input');
                checkbox.type = 'checkbox';
                checkbox.value = scrap.id;
                checkbox.dataset.date = scrap.date;
                checkbox.className = 'form-check-input deliveryFilterCompetitor';

                checkbox.addEventListener('change', () => {
                    if (checkbox.checked) {
                        loadCompetitorPrices([scrap.id], currentStoreId, currentCompetitorStoreName);
                    } else {
                        delete loadedPrices[scrap.id];
                        renderPrices();
                    }
                });

                const dateObj = new Date(scrap.date);
                const dateStr = dateObj.toLocaleDateString('pl-PL', {
                    day: '2-digit', month: '2-digit', year: 'numeric'
                });
                const timeStr = dateObj.toLocaleTimeString('pl-PL', {
                    hour: '2-digit', minute: '2-digit'
                });

                const infoDiv = document.createElement('div');
                infoDiv.className = 'scraphistory-info';

                const dateSpan = document.createElement('span');
                dateSpan.className = 'scraphistory-date';
                dateSpan.textContent = dateStr;

                const timeSpan = document.createElement('span');
                timeSpan.className = 'scraphistory-time';
                timeSpan.textContent = timeStr;

                infoDiv.appendChild(dateSpan);
                infoDiv.appendChild(timeSpan);

                label.appendChild(checkbox);
                label.appendChild(infoDiv);
                container.appendChild(label);

                if (index === 0) firstCheckbox = checkbox;
            });

            if (firstCheckbox) {
                firstCheckbox.checked = true;
                loadCompetitorPrices([firstCheckbox.value], currentStoreId, currentCompetitorStoreName);
            }
        })
        .catch(error => console.error('Błąd ładowania opcji historii analiz:', error));
}

function loadCompetitorPrices(scrapHistoryIds, currentStoreId, currentCompetitorStoreName) {
    scrapHistoryIds.forEach(scrapHistoryId => {
        if (loadedPrices[scrapHistoryId] === 'loading') return;

        if (loadedPrices[scrapHistoryId] && loadedPrices[scrapHistoryId] !== 'loading') {
            renderPrices();
            return;
        }

        loadedPrices[scrapHistoryId] = 'loading';

        const requestData = {
            storeId: parseInt(currentStoreId),
            competitorStoreName: currentCompetitorStoreName,
            scrapHistoryId: parseInt(scrapHistoryId)
        };

        fetch(apiConfig.getPricesUrl, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(requestData)
        })
            .then(response => {
                if (!response.ok) {
                    delete loadedPrices[scrapHistoryId];
                    return response.text().then(text => { throw new Error(`Status: ${response.status}`) });
                }
                return response.json();
            })
            .then(data => {
                loadedPrices[scrapHistoryId] = data;
                renderPrices();
            })
            .catch(error => {
                console.error(`Błąd (ID: ${scrapHistoryId}):`, error);
                if (loadedPrices[scrapHistoryId] === 'loading') delete loadedPrices[scrapHistoryId];
                renderPrices();
            });
    });
}

function renderPrices() {

    const newAllProducts = {};
    const successfullyLoadedScrapIds = Object.keys(loadedPrices)
        .filter(id => loadedPrices[id] && loadedPrices[id] !== 'loading');

    successfullyLoadedScrapIds.forEach(scrapHistoryId => {
        const pricesFromThisScrap = loadedPrices[scrapHistoryId];
        pricesFromThisScrap.forEach(priceEntry => {
            if (!newAllProducts[priceEntry.productId]) {
                newAllProducts[priceEntry.productId] = {
                    productName: priceEntry.productName,
                    mainUrl: priceEntry.productMainUrl,
                    data: {},
                    ourData: {}
                };
            }
            newAllProducts[priceEntry.productId].data[scrapHistoryId] = priceEntry.price;
            newAllProducts[priceEntry.productId].ourData[scrapHistoryId] = priceEntry.ourPrice;
        });
    });

    allProducts = newAllProducts;

    const searchInput = document.getElementById('productSearch');
    const searchTerm = searchInput ? searchInput.value : "";
    filterProducts(searchTerm);
}

function filterProducts(searchTerm) {
    const lowerSearchTerm = searchTerm.toLowerCase();
    const filteredProducts = {};

    for (const [productId, product] of Object.entries(allProducts)) {
        const pName = product.productName ? product.productName.toLowerCase() : "";
        if (pName.includes(lowerSearchTerm)) {
            filteredProducts[productId] = product;
        }
    }

    const successfullyLoadedScrapIds = Object.keys(loadedPrices)
        .filter(id => loadedPrices[id] && loadedPrices[id] !== 'loading');

    const sortedUniqueScrapHistoryIds = successfullyLoadedScrapIds.sort((a, b) => {
        const checkboxA = document.querySelector(`.deliveryFilterCompetitor[value="${a}"]`);
        const checkboxB = document.querySelector(`.deliveryFilterCompetitor[value="${b}"]`);
        if (!checkboxA?.dataset?.date || !checkboxB?.dataset?.date) return 0;
        return new Date(checkboxA.dataset.date) - new Date(checkboxB.dataset.date);
    });

    const theadRow = document.getElementById('table-header-row');
    if (theadRow) {

        while (theadRow.children.length > 1) {
            theadRow.removeChild(theadRow.lastChild);
        }

        sortedUniqueScrapHistoryIds.forEach(id => {
            const th = document.createElement('th');
            const checkbox = document.querySelector(`.deliveryFilterCompetitor[value="${id}"]`);
            if (checkbox?.dataset?.date) {
                const date = new Date(checkbox.dataset.date);
                const dateStr = date.toLocaleDateString('pl-PL', { day: '2-digit', month: '2-digit', year: 'numeric' });
                const timeStr = date.toLocaleTimeString('pl-PL', { hour: '2-digit', minute: '2-digit' });
                th.innerHTML = `${dateStr}<br><span style="font-size:11px; font-weight:normal;">${timeStr}</span>`;
            } else {
                th.textContent = `Dane (${id})`;
            }
            theadRow.appendChild(th);
        });
    }

    displayProducts(filteredProducts, sortedUniqueScrapHistoryIds);
}

function displayProducts(productsToDisplay, sortedScrapIds) {
    const tableBody = document.getElementById('priceTableBody');
    if (!tableBody) return;
    tableBody.innerHTML = '';

    const filterIncrease = document.getElementById('filterIncrease')?.checked || false;
    const filterDecrease = document.getElementById('filterDecrease')?.checked || false;
    const filterChange = document.getElementById('filterChange')?.checked || false;

    // --- ZMIENNE DO STATYSTYK (DASHBOARD) ---
    let stats = {
        our: { up: 0, down: 0 },
        comp: { up: 0, down: 0 }
    };
    // ----------------------------------------

    let productCount = 0;

    for (const productId of Object.keys(productsToDisplay)) {
        const product = productsToDisplay[productId];

        let rowHasAnyPriceChange = false;
        let rowHasAnyPriceIncrease = false;
        let rowHasAnyPriceDecrease = false;

        // --- Tymczasowe liczniki dla bieżącego wiersza ---
        let rowStats = {
            our: { up: 0, down: 0 },
            comp: { up: 0, down: 0 }
        };

        sortedScrapIds.forEach((scrapId, index) => {
            if (index > 0) {
                const prevScrapId = sortedScrapIds[index - 1];

                // NASZ SKLEP - analiza
                const ourCurr = product.ourData[scrapId];
                const ourPrev = product.ourData[prevScrapId];
                if (typeof ourCurr === 'number' && typeof ourPrev === 'number') {
                    if (ourCurr > ourPrev) {
                        rowHasAnyPriceIncrease = true;
                        rowStats.our.up++; // +1 podwyżka
                    }
                    if (ourCurr < ourPrev) {
                        rowHasAnyPriceDecrease = true;
                        rowStats.our.down++; // +1 obniżka
                    }
                    if (ourCurr !== ourPrev) rowHasAnyPriceChange = true;
                }

                // KONKURENT - analiza
                const compCurr = product.data[scrapId];
                const compPrev = product.data[prevScrapId];
                if (typeof compCurr === 'number' && typeof compPrev === 'number') {
                    if (compCurr > compPrev) {
                        rowHasAnyPriceIncrease = true;
                        rowStats.comp.up++; // +1 podwyżka
                    }
                    if (compCurr < compPrev) {
                        rowHasAnyPriceDecrease = true;
                        rowStats.comp.down++; // +1 obniżka
                    }
                    if (compCurr !== compPrev) rowHasAnyPriceChange = true;
                }
            }
        });

        // Filtracja wierszy
        let showThisRow = true;
        const anyChangeFilterActive = filterIncrease || filterDecrease || filterChange;

        if (anyChangeFilterActive) {
            showThisRow = false;
            if (filterIncrease && rowHasAnyPriceIncrease) showThisRow = true;
            if (filterDecrease && rowHasAnyPriceDecrease) showThisRow = true;
            if (filterChange && rowHasAnyPriceChange) showThisRow = true;
        }

        if (!showThisRow) continue;

        // --- Jeśli wiersz jest widoczny, dodajemy jego statystyki do sumy globalnej ---
        stats.our.up += rowStats.our.up;
        stats.our.down += rowStats.our.down;
        stats.comp.up += rowStats.comp.up;
        stats.comp.down += rowStats.comp.down;
        // -----------------------------------------------------------------------------

        const row = document.createElement('tr');

        // --- KOLUMNA PRODUKTU (LOGIKA ZDJĘĆ + FIX ALLEGRO) ---
        const productInfoTd = document.createElement('td');
        const productInfoDiv = document.createElement('div');
        productInfoDiv.className = 'product-info-cell';

        // Sprawdzamy, czy sklep to Allegro (bez względu na wielkość liter)
        const isAllegro = competitorStoreName && competitorStoreName.toLowerCase().includes('allegro');

        // Wyświetlamy zdjęcie TYLKO jeśli to nie Allegro i mamy poprawny URL
        if (!isAllegro && product.mainUrl && product.mainUrl.length > 5) {
            const img = document.createElement('img');
            img.src = product.mainUrl;
            img.alt = product.productName;

            img.onerror = function () {
                this.style.display = 'none';
                productInfoDiv.classList.add('no-image');
            };

            productInfoDiv.appendChild(img);
        } else {
            // Jeśli Allegro lub brak URL -> od razu klasa no-image
            productInfoDiv.classList.add('no-image');
        }

        const nameSpan = document.createElement('span');
        nameSpan.className = 'product-name-text';
        nameSpan.textContent = product.productName || 'Brak nazwy';
        productInfoDiv.appendChild(nameSpan);

        productInfoTd.appendChild(productInfoDiv);
        row.appendChild(productInfoTd);

        // --- KOLUMNY Z CENAMI ---
        sortedScrapIds.forEach((scrapId, index) => {
            const td = document.createElement('td');

            let cellContent = '<div class="flex-row">';

            // --- NASZA CENA ---
            const ourPriceValue = product.ourData[scrapId];
            let ourPriceDisplay = (typeof ourPriceValue === 'number') ? formatMoneyPL(ourPriceValue) : "B/D";

            let ourPriceHtml = `<div class="Price-box-content-o flex-column">
                                    <div class="priceBox-style-o ${ourPriceDisplay === "B/D" ? 'no-price' : ''}">`;

            let ourDiffHtml = '';
            if (index > 0 && typeof ourPriceValue === 'number') {
                const previousScrapId = sortedScrapIds[index - 1];
                const previousOurPrice = product.ourData[previousScrapId];
                if (typeof previousOurPrice === 'number') {
                    const diff = ourPriceValue - previousOurPrice;
                    if (diff !== 0) {
                        const percDiff = previousOurPrice === 0 ? 100 : ((diff / previousOurPrice) * 100);
                        const isIncrease = diff > 0;
                        const changeClass = isIncrease ? 'priceBox-diff-up' : 'priceBox-diff-down';
                        const arrow = isIncrease ? '&uarr;' : '&darr;';

                        const formattedDiff = formatMoneyPL(Math.abs(diff));
                        const formattedPerc = Math.abs(percDiff).toFixed(1).replace('.', ',');

                        ourDiffHtml = `<div class="${changeClass}" style="margin-right:8px;">${formattedDiff} PLN (${formattedPerc}%) ${arrow}</div>`;
                    }
                }
            }

            ourPriceHtml += ourDiffHtml;
            ourPriceHtml += `<div class="PriceTagBox-o">${ourPriceDisplay !== "B/D" ? `${ourPriceDisplay} PLN` : "Brak ceny"}</div></div>`;
            ourPriceHtml += `<div class="Price-box-content-o-t">${storeName}</div></div>`;

            cellContent += ourPriceHtml;

            // --- KONKURENCJA CENA ---
            const competitorPriceValue = product.data[scrapId];
            let competitorPriceDisplay = (typeof competitorPriceValue === 'number') ? formatMoneyPL(competitorPriceValue) : "B/D";

            let competitorPriceHtml = `<div class="Price-box-content-c flex-column">
                                            <div class="priceBox-style-c ${competitorPriceDisplay === "B/D" ? 'no-price' : ''}">`;

            competitorPriceHtml += `<div class="PriceTagBox-c">${competitorPriceDisplay !== "B/D" ? `${competitorPriceDisplay} PLN` : "Brak ceny"}</div>`;

            if (index > 0 && typeof competitorPriceValue === 'number') {
                const previousScrapId = sortedScrapIds[index - 1];
                const previousCompetitorPrice = product.data[previousScrapId];
                if (typeof previousCompetitorPrice === 'number') {
                    const diff = competitorPriceValue - previousCompetitorPrice;
                    if (diff !== 0) {
                        const percDiff = previousCompetitorPrice === 0 ? 100 : ((diff / previousCompetitorPrice) * 100);
                        const isIncrease = diff > 0;
                        const changeClass = isIncrease ? 'priceBox-diff-up' : 'priceBox-diff-down';
                        const arrow = isIncrease ? '&uarr;' : '&darr;';

                        const formattedDiff = formatMoneyPL(Math.abs(diff));
                        const formattedPerc = Math.abs(percDiff).toFixed(1).replace('.', ',');

                        competitorPriceHtml += `<div class="${changeClass}" style="margin-left:8px;">${formattedDiff} PLN (${formattedPerc}%) ${arrow}</div>`;
                    }
                }
            }

            competitorPriceHtml += `</div>`;
            competitorPriceHtml += `<div class="Price-box-content-c-t">${competitorStoreName}</div></div>`;

            cellContent += competitorPriceHtml;
            cellContent += '</div>';

            td.innerHTML = cellContent;

            td.style.cursor = 'pointer';

            // --- POPRAWIONA OBSŁUGA KLIKNIĘCIA (LINKI - Routing) ---
            td.addEventListener('click', (event) => {
                if (event.target.tagName === 'A') return;

                let url;
                // Jeśli sklep to Allegro, przekieruj do kontrolera Allegro
                if (isAllegro) {
                    // Kontroler: AllegroPriceHistory, Akcja: Details(int storeId, int productId)
                    url = `/AllegroPriceHistory/Details?storeId=${storeId}&productId=${productId}`;
                } else {
                    // Standardowy sklep - stary link
                    // Kontroler: PriceHistory, Akcja: Details(int productId, int scrapId)
                    url = `/PriceHistory/Details?productId=${productId}&scrapId=${scrapId}`;
                }

                window.open(url, '_blank');
            });

            row.appendChild(td);
        });

        tableBody.appendChild(row);
        productCount++;
    }

    const scrapableCountElement = document.getElementById('scrapable-count');
    if (scrapableCountElement) scrapableCountElement.textContent = productCount;

    // --- AKTUALIZACJA LICZNIKÓW W UI (DASHBOARD) ---
    updateStatElement('count-our-up', stats.our.up);
    updateStatElement('count-our-down', stats.our.down);
    updateStatElement('count-comp-up', stats.comp.up);
    updateStatElement('count-comp-down', stats.comp.down);
}

// Funkcja pomocnicza do aktualizacji liczb w dashboardzie
function updateStatElement(id, value) {
    const el = document.getElementById(id);
    if (el) {
        el.textContent = value;
        // Opcjonalnie: zmiana koloru jeśli wartość > 0 (dla lepszej widoczności)
        if (value > 0) el.style.color = "#000";
        else el.style.color = "#aaa";
    }
}