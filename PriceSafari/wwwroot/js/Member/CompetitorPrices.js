// Globalne zmienne (zakładam, że storeId, competitorStoreName, storeName są zdefiniowane globalnie lub przekazywane poprawnie)
const loadedPrices = {};
let allProducts = {}; // Używamy tej nazwy konsekwentnie

document.addEventListener('DOMContentLoaded', () => {
    if (typeof storeId === 'undefined' || typeof competitorStoreName === 'undefined' || typeof storeName === 'undefined') {
        console.error("Kluczowe zmienne globalne (storeId, competitorStoreName, storeName) nie są zdefiniowane.");
        // Możesz tu wyświetlić komunikat użytkownikowi lub zablokować dalsze działanie
        const tableContainer = document.querySelector('.table-container');
        if (tableContainer) tableContainer.innerHTML = '<p style="color:red; text-align:center;">Błąd konfiguracji: Brak niezbędnych danych sklepu.</p>';
        return;
    }
    loadScrapHistoryOptions(storeId, competitorStoreName);


    // Listener dla checkboxów filtrów - bardziej precyzyjny selektor
    document.querySelectorAll('.price-filter-checkbox').forEach(checkbox => { // Załóżmy, że checkboxy filtrów mają tę klasę
        checkbox.addEventListener('change', () => renderPrices());
    });

    const productSearchInput = document.getElementById('productSearch');
    if (productSearchInput) {
        productSearchInput.addEventListener('keyup', function () {
            filterProducts(this.value);
        });
    }
});

function loadScrapHistoryOptions(currentStoreId, currentCompetitorStoreName) {
    fetch(`/Competitors/GetScrapHistoryIds?storeId=${currentStoreId}`)
        .then(response => {
            if (!response.ok) throw new Error(`Błąd HTTP! Status: ${response.status}`);
            return response.json();
        })
        .then(scrapHistoryIds => {
            const container = document.getElementById('scrapHistoryCheckboxes');
            if (!container) {
                console.error('Kontener #scrapHistoryCheckboxes nie znaleziony.');
                return;
            }
            container.innerHTML = '';
            let firstCheckbox = null;

            scrapHistoryIds.sort((a, b) => new Date(b.date) - new Date(a.date)); // Najnowsze pierwsze

            scrapHistoryIds.forEach((scrap, index) => {
                const label = document.createElement('label');
                label.className = 'form-check-label';
                label.style.display = 'block';

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

                const date = new Date(scrap.date);
                const formattedDate = `${date.getDate().toString().padStart(2, '0')}.${(date.getMonth() + 1).toString().padStart(2, '0')}.${date.getFullYear()}`;

                label.appendChild(checkbox);
                label.appendChild(document.createTextNode(` ${formattedDate}`));
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
        loadedPrices[scrapHistoryId] = 'loading';

        const requestData = {
            storeId: currentStoreId,
            competitorStoreName: currentCompetitorStoreName,
            scrapHistoryId: parseInt(scrapHistoryId)
        };

        fetch('/Competitors/GetCompetitorPrices', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(requestData)
        })
            .then(response => {
                if (!response.ok) {
                    delete loadedPrices[scrapHistoryId];
                    return response.text().then(text => { throw new Error(`Błąd HTTP! Status: ${response.status}, Wiadomość: ${text}`) });
                }
                return response.json();
            })
            .then(data => {
                // Zakładamy, że backend zwraca teraz productMainUrl
                loadedPrices[scrapHistoryId] = data;
                renderPrices();
            })
            .catch(error => {
                console.error(`Błąd ładowania cen konkurenta dla scrapId ${scrapHistoryId}:`, error);
                if (loadedPrices[scrapHistoryId] === 'loading') delete loadedPrices[scrapHistoryId];
                renderPrices(); // Odśwież, aby usunąć ew. stare dane lub pokazać błąd
            });
    });
}

function renderPrices() {
    const tableBody = document.getElementById('priceTableBody');
    if (!tableBody) return;
    tableBody.innerHTML = '';

    const newAllProducts = {};
    const successfullyLoadedScrapIds = Object.keys(loadedPrices)
        .filter(id => loadedPrices[id] && loadedPrices[id] !== 'loading');

    successfullyLoadedScrapIds.forEach(scrapHistoryId => {
        const pricesFromThisScrap = loadedPrices[scrapHistoryId];
        pricesFromThisScrap.forEach(priceEntry => {
            if (!newAllProducts[priceEntry.productId]) {
                newAllProducts[priceEntry.productId] = {
                    productName: priceEntry.productName,
                    mainUrl: priceEntry.productMainUrl, // Zapisujemy URL obrazka
                    data: {},
                    ourData: {}
                };
            }
            // Upewnij się, że backend zwraca 'price' i 'ourPrice' (lub dostosuj)
            newAllProducts[priceEntry.productId].data[scrapHistoryId] = priceEntry.price;
            newAllProducts[priceEntry.productId].ourData[scrapHistoryId] = priceEntry.ourPrice;
        });
    });
    allProducts = newAllProducts;

    const table = tableBody.closest('table');
    if (!table) return;
    const thead = table.querySelector('thead');
    if (!thead) { // Jeśli nie ma thead, utwórz go
        thead = table.insertBefore(document.createElement('thead'), table.firstChild);
    }
    let theadRow = thead.querySelector('tr');
    if (!theadRow) {
        theadRow = thead.appendChild(document.createElement('tr'));
    }
    theadRow.innerHTML = `<th class="sticky-col">Produkt</th>`; // Dodajemy klasę dla sticky

    const sortedUniqueScrapHistoryIds = successfullyLoadedScrapIds.sort((a, b) => {
        const checkboxA = document.querySelector(`.deliveryFilterCompetitor[value="${a}"]`);
        const checkboxB = document.querySelector(`.deliveryFilterCompetitor[value="${b}"]`);
        if (!checkboxA?.dataset?.date || !checkboxB?.dataset?.date) return 0;
        return new Date(checkboxA.dataset.date) - new Date(checkboxB.dataset.date); // Rosnąco
    });

    sortedUniqueScrapHistoryIds.forEach(id => {
        const th = document.createElement('th');
        const checkbox = document.querySelector(`.deliveryFilterCompetitor[value="${id}"]`);
        if (checkbox?.dataset?.date) {
            const date = new Date(checkbox.dataset.date);
            th.textContent = `${date.getDate().toString().padStart(2, '0')}.${(date.getMonth() + 1).toString().padStart(2, '0')}.${date.getFullYear()}`;
        } else {
            th.textContent = `Dane (${id})`;
        }
        theadRow.appendChild(th);
    });

    displayProducts(allProducts, sortedUniqueScrapHistoryIds);
}

function filterProducts(searchTerm) {
    const lowerSearchTerm = searchTerm.toLowerCase();
    const filteredProducts = {};
    for (const [productId, product] of Object.entries(allProducts)) {
        if (product.productName && product.productName.toLowerCase().includes(lowerSearchTerm)) {
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
    displayProducts(filteredProducts, sortedUniqueScrapHistoryIds);
}

function displayProducts(productsToDisplay, sortedScrapIds) {
    const tableBody = document.getElementById('priceTableBody');
    tableBody.innerHTML = '';

    const filterIncrease = document.getElementById('filterIncrease')?.checked || false;
    const filterDecrease = document.getElementById('filterDecrease')?.checked || false;
    const filterChange = document.getElementById('filterChange')?.checked || false;

    let productCount = 0;

    for (const productId of Object.keys(productsToDisplay)) {
        const product = productsToDisplay[productId];
        let showRow = true;
        let rowHasChange = false, rowHasIncrease = false, rowHasDecrease = false;

        const row = document.createElement('tr');

        // --- Komórka z informacjami o produkcie (przyklejona) ---
        const productInfoTd = document.createElement('td');
        productInfoTd.classList.add('sticky-col'); // Dodajemy klasę dla sticky

        const productInfoDiv = document.createElement('div');
        productInfoDiv.className = 'product-info-cell';

        const img = document.createElement('img');
        img.src = product.mainUrl ? product.mainUrl : '/images/placeholder.png'; // Użyj placeholdera, jeśli brak URL
        img.alt = product.productName || 'Obrazek produktu';
        img.onerror = function () { this.src = '/images/placeholder.png'; this.onerror = null; }; // Placeholder w razie błędu ładowania
        productInfoDiv.appendChild(img);

        const nameSpan = document.createElement('span');
        nameSpan.className = 'product-name-text';
        nameSpan.textContent = product.productName || 'Brak nazwy';
        productInfoDiv.appendChild(nameSpan);

        productInfoTd.appendChild(productInfoDiv);
        row.appendChild(productInfoTd);
        // --- Koniec komórki produktu ---


        sortedScrapIds.forEach((scrapId, index) => {
            const td = document.createElement('td');
            td.classList.add('nowrap');
            let cellContent = '<div class="flex-row">';

            const ourPriceValue = product.ourData[scrapId];
            const competitorPriceValue = product.data[scrapId];

            // Nasza cena
            let ourPriceDisplay = (typeof ourPriceValue === 'number') ? ourPriceValue.toFixed(2) : "B/D";
            let ourPriceHtml = `<div class="Price-box-content-o flex-column">
                                  <div class="priceBox-style-o ${ourPriceDisplay === "B/D" ? 'no-price' : ''}">`;
            if (index > 0 && typeof ourPriceValue === 'number') {
                const previousScrapId = sortedScrapIds[index - 1];
                const previousOurPrice = product.ourData[previousScrapId];
                if (typeof previousOurPrice === 'number') {
                    const diff = ourPriceValue - previousOurPrice;
                    if (diff !== 0) {
                        const percDiff = previousOurPrice === 0 ? (diff > 0 ? 100 : -100) : ((diff / previousOurPrice) * 100);
                        const isIncrease = diff > 0;
                        const changeClass = isIncrease ? 'priceBox-diff-down' : 'priceBox-diff-up';
                        const arrow = isIncrease ? '&uarr;' : '&darr;';
                        ourPriceHtml += `<div class="${changeClass}">${diff.toFixed(2)} PLN (${percDiff.toFixed(2)}%) ${arrow}</div>`;
                        rowHasChange = true;
                        if (isIncrease) rowHasIncrease = true; else rowHasDecrease = true;
                    }
                }
            }
            ourPriceHtml += `<div class="PriceTagBox-o">${ourPriceDisplay !== "B/D" ? `${ourPriceDisplay} PLN` : ourPriceDisplay}</div></div>`;
            ourPriceHtml += `<div class="Price-box-content-o-t">${typeof storeName !== 'undefined' ? storeName : 'Nasz Sklep'}</div></div>`;
            cellContent += ourPriceHtml;

            // Cena konkurenta
            let competitorPriceDisplay = (typeof competitorPriceValue === 'number') ? competitorPriceValue.toFixed(2) : "B/D";
            let competitorPriceHtml = `<div class="Price-box-content-c flex-column">
                                        <div class="priceBox-style-c ${competitorPriceDisplay === "B/D" ? 'no-price' : ''}">`;
            if (index > 0 && typeof competitorPriceValue === 'number') {
                const previousScrapId = sortedScrapIds[index - 1];
                const previousCompetitorPrice = product.data[previousScrapId];
                if (typeof previousCompetitorPrice === 'number') {
                    const diff = competitorPriceValue - previousCompetitorPrice;
                    if (diff !== 0) {
                        const percDiff = previousCompetitorPrice === 0 ? (diff > 0 ? 100 : -100) : ((diff / previousCompetitorPrice) * 100);
                        const isIncrease = diff > 0;
                        const changeClass = isIncrease ? 'priceBox-diff-down' : 'priceBox-diff-up';
                        const arrow = isIncrease ? '&uarr;' : '&darr;';
                        competitorPriceHtml += `<div class="${changeClass}">${diff.toFixed(2)} PLN (${percDiff.toFixed(2)}%) ${arrow}</div>`;
                        rowHasChange = true;
                        if (isIncrease) rowHasIncrease = true; else rowHasDecrease = true;
                    }
                }
            }
            competitorPriceHtml += `<div class="PriceTagBox-c">${competitorPriceDisplay !== "B/D" ? `${competitorPriceDisplay} PLN` : competitorPriceDisplay}</div></div>`;
            competitorPriceHtml += `<div class="Price-box-content-c-t">${typeof competitorStoreName !== 'undefined' ? competitorStoreName : 'Konkurent'}</div></div>`;
            cellContent += competitorPriceHtml;

            cellContent += '</div>';
            td.innerHTML = cellContent;

            td.addEventListener('click', (event) => {
                if (event.target.tagName === 'A' || event.target.closest('a')) return;
                const url = `/PriceHistory/Details?productId=${productId}&scrapId=${scrapId}`;
                window.open(url, '_blank');
            });
            row.appendChild(td);
        });

        if ((filterIncrease && !rowHasIncrease) || (filterDecrease && !rowHasDecrease) || (filterChange && !rowHasChange)) {
            showRow = false;
        }

        if (showRow) {
            tableBody.appendChild(row);
            productCount++;
        }
    }

    const scrapableCountElement = document.getElementById('scrapable-count');
    if (scrapableCountElement) scrapableCountElement.textContent = productCount;
}