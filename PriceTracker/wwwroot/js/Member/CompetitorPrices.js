document.addEventListener('DOMContentLoaded', () => {
    
    loadScrapHistoryOptions(storeId, competitorStoreName);

    document.querySelectorAll('input[type="checkbox"]').forEach(checkbox => {
        checkbox.addEventListener('change', renderPrices);
    });

    document.getElementById('productSearch').addEventListener('keyup', function () {
        filterProducts(this.value);
    });
});

function loadScrapHistoryOptions(storeId, competitorStoreName) {
    fetch(`/Competitors/GetScrapHistoryIds?storeId=${storeId}`)
        .then(response => response.json())
        .then(scrapHistoryIds => {1
            const container = document.getElementById('scrapHistoryCheckboxes');
            let firstCheckbox = null;

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
                        loadCompetitorPrices([scrap.id], storeId, competitorStoreName);
                    } else {
                        delete loadedPrices[scrap.id];
                        renderPrices();
                    }
                });

                const date = new Date(scrap.date);
                const formattedDate = `${date.getDate()}.${date.getMonth() + 1}.${date.getFullYear()}`;

                label.appendChild(checkbox);
                label.appendChild(document.createTextNode(formattedDate));
                container.appendChild(label);

                if (index === 0) {
                    firstCheckbox = checkbox;
                }
            });

            if (firstCheckbox) {
                firstCheckbox.checked = true;
                loadCompetitorPrices([firstCheckbox.value], storeId, competitorStoreName);
            }
        })
        .catch(error => console.error('Error loading scrap history options:', error));
}

const loadedPrices = {};
let allProducts = {};

function loadCompetitorPrices(scrapHistoryIds, storeId, competitorStoreName) {
    scrapHistoryIds.forEach(scrapHistoryId => {
        const requestData = {
            storeId: storeId,
            competitorStoreName: competitorStoreName,
            scrapHistoryId: parseInt(scrapHistoryId)
        };

        fetch('/Competitors/GetCompetitorPrices', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(requestData)
        })
            .then(response => response.json())
            .then(data => {
                loadedPrices[scrapHistoryId] = data;
                renderPrices();
            })
            .catch(error => console.error('Error loading competitor prices:', error));
    });
}

function renderPrices() {
    const tableBody = document.getElementById('priceTableBody');
    tableBody.innerHTML = '';

    const urlProductMap = {};

    for (const [scrapHistoryId, prices] of Object.entries(loadedPrices)) {
        prices.forEach(price => {
            if (!urlProductMap[price.productId]) {
                urlProductMap[price.productId] = {
                    productName: price.productName,
                    scrapHistoryId: scrapHistoryId,
                    data: {},
                    ourData: {},
                    offerUrl: price.offerUrl
                };
            }
            urlProductMap[price.productId].data[scrapHistoryId] = price.price;
            urlProductMap[price.productId].ourData[scrapHistoryId] = price.ourPrice;
        });
    }

    allProducts = urlProductMap;

    const theadRow = document.querySelector('table thead tr');
    theadRow.innerHTML = `<th>Produkt</th>`;

    const uniqueScrapHistoryIds = Object.keys(loadedPrices);
    uniqueScrapHistoryIds.forEach(id => {
        const th = document.createElement('th');

        const checkbox = document.querySelector(`input[type="checkbox"][value="${id}"]`);
        if (checkbox) {
            const date = new Date(checkbox.dataset.date);
            const formattedDate = `${date.getDate()}.${date.getMonth() + 1}.${date.getFullYear()}`;
            th.textContent = `${formattedDate}`;
        } else {
            th.textContent = `Cena (${id})`;
        }

        theadRow.appendChild(th);
    });

    displayProducts(urlProductMap);
}

function filterProducts(searchTerm) {
    const filteredProducts = {};
    for (const [productId, product] of Object.entries(allProducts)) {
        if (product.productName.toLowerCase().includes(searchTerm.toLowerCase())) {
            filteredProducts[productId] = product;
        }
    }
    displayProducts(filteredProducts);
}
function displayProducts(products) {
    const tableBody = document.getElementById('priceTableBody');
    tableBody.innerHTML = '';

    const filterIncrease = document.getElementById('filterIncrease').checked;
    const filterDecrease = document.getElementById('filterDecrease').checked;
    const filterChange = document.getElementById('filterChange').checked;

    let productCount = 0;

    for (const [productId, product] of Object.entries(products)) {
        let showRow = true;
        let hasChange = false;
        let hasIncrease = false;
        let hasDecrease = false;

        const priceChanges = [];

        const row = document.createElement('tr');
        row.innerHTML = `<td><div class="price-box-column-name-com">${product.productName}</div></td>`;

        const uniqueScrapHistoryIds = Object.keys(loadedPrices);

        uniqueScrapHistoryIds.forEach((id, index) => {
            const td = document.createElement('td');
            let tdContent = '<div class="flex-row">';

            const ourPrice = product.ourData[id] ? product.ourData[id].toFixed(2) : "B/D";
            let ourPriceContent = `<div class="priceBox-style-o ${ourPrice === "B/D" ? 'no-price' : ''}">`;

            if (index > 0 && ourPrice !== "B/D") {
                const previousId = uniqueScrapHistoryIds[index - 1];
                const previousOurPrice = product.ourData[previousId] ? product.ourData[previousId] : null;

                if (previousOurPrice !== null) {
                    const ourPriceDifference = product.ourData[id] - previousOurPrice;
                    const ourPercentageDifference = ((ourPriceDifference / previousOurPrice) * 100).toFixed(2);

                    if (ourPriceDifference !== 0) {
                        const isIncrease = ourPriceDifference > 0;
                        const changeType = isIncrease ? 'increase' : 'decrease';
                        const ourDifferenceElement = `<div class="${isIncrease ? 'priceBox-diff-down' : 'priceBox-diff-up'}">
                                ${ourPriceDifference.toFixed(2)} PLN (${ourPercentageDifference}%) ${isIncrease ? '&uarr;' : '&darr;'}
                            </div>`;

                        ourPriceContent += ourDifferenceElement;
                        hasChange = true;
                        if (isIncrease) {
                            hasIncrease = true;
                        } else {
                            hasDecrease = true;
                        }
                        priceChanges.push({ change: changeType });
                    }
                }
            }

            ourPriceContent += `<div class="PriceTagBox-o">${ourPrice !== "B/D" ? `${ourPrice} PLN` : ourPrice}</div></div>`;
            ourPriceContent += `<div class="Price-box-content-o-t">${storeName}</div>`;
            tdContent += `<div class="Price-box-content-o flex-column">${ourPriceContent}</div>`;

            const currentPrice = product.data[id] ? product.data[id].toFixed(2) : "B/D";
            let competitorPriceContent = `<div class="priceBox-style-c ${currentPrice === "B/D" ? 'no-price' : ''}">`;

            competitorPriceContent += `<div class="PriceTagBox-c">${currentPrice !== "B/D" ? `${currentPrice} PLN` : currentPrice}</div>`;

            if (index > 0 && currentPrice !== "B/D") {
                const previousId = uniqueScrapHistoryIds[index - 1];
                const previousPrice = product.data[previousId] ? product.data[previousId] : null;

                if (previousPrice !== null) {
                    const priceDifference = product.data[id] - previousPrice;
                    const percentageDifference = ((priceDifference / previousPrice) * 100).toFixed(2);

                    if (priceDifference !== 0) {
                        const isIncrease = priceDifference > 0;
                        const differenceElement = `<div class="${isIncrease ? 'priceBox-diff-down' : 'priceBox-diff-up'}">
                                ${priceDifference.toFixed(2)} PLN (${percentageDifference}%) ${isIncrease ? '&uarr;' : '&darr;'}
                            </div>`;

                        competitorPriceContent += differenceElement;
                        hasChange = true;
                        if (isIncrease) {
                            hasIncrease = true;
                        } else {
                            hasDecrease = true;
                        }
                        priceChanges.push({ change: isIncrease ? 'increase' : 'decrease' });
                    }
                }
            }

            competitorPriceContent += `</div>`;
            competitorPriceContent += `<div class="Price-box-content-c-t">${competitorStoreName}</div>`;
            tdContent += `<div class="Price-box-content-c flex-column">${competitorPriceContent}</div>`;

            td.innerHTML = tdContent;
            td.classList.add('nowrap');

         
            td.addEventListener('click', () => {
                const url = `/PriceHistory/Details?productId=${productId}&scrapId=${id}`;
                window.open(url, '_blank');
            });

            row.appendChild(td);
        });

        if (filterIncrease && !hasIncrease) {
            showRow = false;
        }
        if (filterDecrease && !hasDecrease) {
            showRow = false;
        }
        if (filterChange && priceChanges.length === 0) {
            showRow = false;
        }

        if (showRow) {
            tableBody.appendChild(row);
            productCount++;
        }
    }

    document.getElementById('scrapable-count').textContent = productCount;
}

