document.addEventListener("DOMContentLoaded", function () {
    let allPrices = [];
    let chartInstance = null;
    let myStoreName = "";
    let setPrice1 = 2.00;
    let setPrice2 = 2.00;
    let selectedProductId = null;
    let competitorStore = "";

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

    function loadPrices() {
        fetch(`/PriceHistory/GetPrices?storeId=${storeId}&competitorStore=${competitorStore}`)
            .then(response => response.json())
            .then(response => {
                myStoreName = response.myStoreName;
                setPrice1 = response.setPrice1;
                setPrice2 = response.setPrice2;
                allPrices = response.prices.map(price => ({
                    ...price,
                    colorClass: getColorClass(price.priceDifference, price.isUniqueBestPrice, price.isSharedBestPrice, price.savings)
                }));

                document.getElementById('totalPriceCount').textContent = response.priceCount;
                renderPrices(allPrices);
                renderChart(allPrices);
                updateColorCounts(allPrices);

                document.getElementById('price1').value = setPrice1;
                document.getElementById('price2').value = setPrice2;
            })
            .catch(error => console.error('Error fetching prices:', error));
    }

    function getColorClass(priceDifference, isUniqueBestPrice = false, isSharedBestPrice = false, savings = null) {
        if (isUniqueBestPrice && savings >= 0.01 && savings <= setPrice1) {
            return "prIdeal";
        }
        if (isUniqueBestPrice) {
            return "prToLow";
        }
        if (isSharedBestPrice) {
            return "prGood";
        }
        if (priceDifference <= 0) {
            return "prGood";
        } else if (priceDifference < setPrice2) {
            return "prMid";
        } else {
            return "prToHigh";
        }
    }

    function filterPricesByCategoryAndColorAndFlag(data, searchTerm = "") {
        const selectedCategory = document.getElementById('category').value;
        const selectedColors = Array.from(document.querySelectorAll('.colorFilter:checked')).map(checkbox => checkbox.value);
        const selectedFlags = Array.from(document.querySelectorAll('.flagFilter:checked')).map(checkbox => parseInt(checkbox.value));
        const selectedBid = document.getElementById('bidFilter').checked;
        const selectedPositions = Array.from(document.querySelectorAll('.positionFilter:checked')).map(checkbox => parseInt(checkbox.value));
        const selectedDeliveryMyStore = Array.from(document.querySelectorAll('.deliveryFilterMyStore:checked')).map(checkbox => parseInt(checkbox.value));
        const selectedDeliveryCompetitor = Array.from(document.querySelectorAll('.deliveryFilterCompetitor:checked')).map(checkbox => parseInt(checkbox.value));

        let filteredPrices = selectedCategory ? data.filter(item => item.category === selectedCategory) : data;
        filteredPrices = selectedColors.length ? filteredPrices.filter(item => selectedColors.includes(item.colorClass)) : filteredPrices;
        filteredPrices = selectedFlags.length ? filteredPrices.filter(item => selectedFlags.some(flag => item.flagIds.includes(flag))) : filteredPrices;

        if (selectedBid) {
            filteredPrices = filteredPrices.filter(item => item.myIsBidding === "1");
        }

        if (selectedPositions.length) {
            filteredPrices = filteredPrices.filter(item => selectedPositions.includes(parseInt(item.myPosition)));
        }

        if (selectedDeliveryMyStore.length) {
            filteredPrices = filteredPrices.filter(item => selectedDeliveryMyStore.includes(item.myDelivery));
        }

        if (selectedDeliveryCompetitor.length) {
            filteredPrices = filteredPrices.filter(item => selectedDeliveryCompetitor.includes(item.delivery));
        }

        renderPrices(filteredPrices, searchTerm);
    }

    function filterPricesByProductName(name) {
        const sanitizedInput = name.trim();

        const exactMatches = allPrices.filter(item =>
            item.productName.includes(sanitizedInput)
        );

        const sanitizedInputLowerCase = sanitizedInput.toLowerCase().replace(/\s+/g, '');
        const partialMatches = allPrices.filter(item =>
            !item.productName.includes(sanitizedInput) &&
            item.productName.toLowerCase().replace(/\s+/g, '').includes(sanitizedInputLowerCase)
        );

        const regex = new RegExp(sanitizedInputLowerCase.split('').join('.*'), 'i');
        const regexMatches = allPrices.filter(item =>
            !item.productName.includes(sanitizedInput) &&
            !item.productName.toLowerCase().replace(/\s+/g, '').includes(sanitizedInputLowerCase) &&
            regex.test(item.productName.toLowerCase().replace(/\s+/g, '').replace(/[^a-zA-Z0-9]/g, ''))
        );

        const filteredPrices = [...exactMatches, ...partialMatches, ...regexMatches];

        filterPricesByCategoryAndColorAndFlag(filteredPrices, sanitizedInput);
    }

    function highlightMatches(text, searchTerm) {
        if (!searchTerm) return text;
        const regex = new RegExp(`(${searchTerm})`, 'gi');
        return text.replace(regex, '<span style="color: #9400D3;font-weight: 600;">$1</span>');
    }

    function renderPrices(data, searchTerm = "") {
        const container = document.getElementById('priceContainer');
        container.innerHTML = '';
        data.forEach(item => {
            const highlightedProductName = highlightMatches(item.productName, searchTerm);
            const percentageDifference = item.percentageDifference != null ? item.percentageDifference.toFixed(2) : "N/A";
            const priceDifference = item.priceDifference != null ? item.priceDifference.toFixed(2) : "N/A";
            const savings = item.colorClass === "prToLow" || item.colorClass === "prIdeal" ? item.savings != null ? item.savings.toFixed(2) : "N/A" : "N/A";

            const isBidding = item.isBidding === "1";
            const myIsBidding = item.myIsBidding === "1";

            const deliveryClass = getDeliveryClass(item.delivery);
            const myDeliveryClass = getDeliveryClass(item.myDelivery);

            const box = document.createElement('div');
            box.className = 'price-box ' + item.colorClass;
            box.dataset.detailsUrl = '/PriceHistory/Details?scrapId=' + item.scrapId + '&productId=' + item.productId;
            box.innerHTML =
                '<div class="price-box-space">' +
                '<div class="price-box-column-name">' + highlightedProductName + '</div>' +
                '<button class="assign-flag-button" data-product-id="' + item.productId + '">+ Przypisz flagi</button>' +
                '</div>' +

                '<div class="price-box-column-category">' +
                item.category +
                (item.externalId ? '<span class="ApiBox">API ID: ' + item.externalId + '</span>' : '') +
                '</div>' +

                '<div class="price-box-data">' +
                '<div class="color-bar ' + item.colorClass + '"></div>' +
                '<div class="price-box-column">' +
                '<div class="price-box-column-text">' + item.lowestPrice.toFixed(2) + ' zł</div>' +
                '<div class="price-box-column-text">' + item.storeName + ' ' +
                (isBidding ? '<span class="Bidding">Bid</span>' : '') +
                '<span class="Position">Msc ' + item.position + '</span>' +
                (item.delivery != null ? '<span class="' + deliveryClass + '">Wysyłka w ' + (item.delivery == 1 ? '1 dzień' : item.delivery + ' dni') + '</span>' : '') +
                '</div>' +
                '</div>' +

                '<div class="price-box-column-line"></div>' +
                '<div class="price-box-column">' +
                '<div class="price-box-column-text">' + item.myPrice.toFixed(2) + ' zł</div>' +
                '<div class="price-box-column-text">' + myStoreName + ' ' +
                (myIsBidding ? '<span class="Bidding">Bid</span>' : '') +
                '<span class="Position">Msc ' + item.myPosition + '</span>' +
                (item.myDelivery != null ? '<span class="' + myDeliveryClass + '">Wysyłka w ' + (item.myDelivery == 1 ? '1 dzień' : item.myDelivery + ' dni') + '</span>' : '') +
                '</div>' +
                '</div>';

            // Dodajemy blok dla External Price
            if (item.externalPrice !== null) {
                const externalPriceDifference = (item.externalPrice - item.myPrice).toFixed(2);
                const externalPriceDifferenceText = (item.externalPrice > item.myPrice ? '+' : '') + externalPriceDifference;
                box.innerHTML +=
                    '<div class="price-box-column-line"></div>' +
                    '<div class="price-box-column">' +
                    '<div class="price-box-column-text">' + item.externalPrice.toFixed(2) + ' zł</div>' +
                    '<div class="price-box-column-text">Zaktualizowano cenę o ' + externalPriceDifferenceText + ' zł</div>' +
                    '</div>';
            }

            box.innerHTML +=
                '<div class="flags-container">' +
                (item.flagIds.length > 0 ? item.flagIds.map(function (flagId) {
                    const flag = flags.find(function (f) { return f.FlagId === flagId; });
                    return '<span class="flag" style="color:' + flag.FlagColor + '; border: 2px solid ' + flag.FlagColor + '; background-color:' + hexToRgba(flag.FlagColor, 0.3) + ';">' + flag.FlagName + '</span>';
                }).join('') : '') +
                '</div>' +
                '</div>';

            box.addEventListener('click', function () {
                window.open(this.dataset.detailsUrl, '_blank');
            });

            container.appendChild(box);

            const assignFlagButton = box.querySelector('.assign-flag-button');
            assignFlagButton.addEventListener('click', function (event) {
                event.stopPropagation();
                selectedProductId = this.dataset.productId;
                modal.style.display = 'block';
                fetch('/ProductFlags/GetFlagsForProduct?productId=' + selectedProductId)
                    .then(response => response.json())
                    .then(flags => {
                        document.querySelectorAll('.flagCheckbox').forEach(function (checkbox) {
                            checkbox.checked = flags.includes(parseInt(checkbox.value));
                        });
                    })
                    .catch(error => console.error('Error fetching flags for product:', error));
            });
        });
        document.getElementById('displayedProductCount').textContent = data.length;
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

    function renderChart(data) {
        const colorCounts = {
            prGood: 0,
            prMid: 0,
            prToHigh: 0,
            prIdeal: 0,
            prToLow: 0
        };

        data.forEach(item => {
            colorCounts[item.colorClass]++;
        });

        const ctx = document.getElementById('colorChart').getContext('2d');

        if (chartInstance) {
            chartInstance.destroy();
        }

        chartInstance = new Chart(ctx, {
            type: 'doughnut',
            data: {
                labels: ['Za wysoka cena', 'Mid cena', 'Top cena', 'Idealna cena', 'Za niska cena'],
                datasets: [{
                    data: [colorCounts.prToHigh, colorCounts.prMid, colorCounts.prGood, colorCounts.prIdeal, colorCounts.prToLow],
                    backgroundColor: [
                        'rgba(238, 17, 17, 0.5)',
                        'rgba(240, 240, 105, 0.5)',
                        'rgba(14, 126, 135, 0.5)',
                        'rgba(0, 156, 42, 0.5)',
                        'rgba(86, 0, 178, 0.5)'
                    ],
                    borderColor: [
                        'rgba(238, 17, 17, 1)',
                        'rgba(240, 240, 105, 1)',
                        'rgba(14, 126, 135, 1)',
                        'rgba(0, 156, 42, 1)',
                        'rgba(86, 0, 178, 1)'
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

        document.querySelector('label[for="prToHighCheckbox"]').textContent = `Za wysoka cena (${colorCounts.prToHigh})`;
        document.querySelector('label[for="prMidCheckbox"]').textContent = `Mid cena (${colorCounts.prMid})`;
        document.querySelector('label[for="prGoodCheckbox"]').textContent = `Top cena (${colorCounts.prGood})`;
        document.querySelector('label[for="prIdealCheckbox"]').textContent = `Idealna cena (${colorCounts.prIdeal})`;
        document.querySelector('label[for="prToLowCheckbox"]').textContent = `Za niska cena (${colorCounts.prToLow})`;
    }

    document.getElementById('category').addEventListener('change', function () {
        filterPricesByProductName(document.getElementById('productSearch').value);
    });

    document.querySelectorAll('.colorFilter, .flagFilter, .positionFilter, .deliveryFilterMyStore, .deliveryFilterCompetitor').forEach(function (checkbox) {
        checkbox.addEventListener('change', function () {
            filterPricesByCategoryAndColorAndFlag(allPrices);
        });
    });

    document.getElementById('bidFilter').addEventListener('change', function () {
        filterPricesByCategoryAndColorAndFlag(allPrices);
    });

    document.getElementById('productSearch').addEventListener('keyup', function () {
        filterPricesByProductName(this.value);
    });

    document.getElementById('price1').addEventListener('input', function () {
        setPrice1 = parseFloat(this.value);
        allPrices.forEach(price => {
            price.colorClass = getColorClass(price.priceDifference, price.isUniqueBestPrice, price.isSharedBestPrice, price.savings);
        });
        renderPrices(allPrices);
        renderChart(allPrices);
        updateColorCounts(allPrices);
    });

    document.getElementById('price2').addEventListener('input', function () {
        setPrice2 = parseFloat(this.value);
        allPrices.forEach(price => {
            price.colorClass = getColorClass(price.priceDifference, price.isUniqueBestPrice, price.isSharedBestPrice, price.savings);
        });
        renderPrices(allPrices);
        renderChart(allPrices);
        updateColorCounts(allPrices);
    });

    document.getElementById('savePriceValues').addEventListener('click', function () {
        const price1 = parseFloat(document.getElementById('price1').value);
        const price2 = parseFloat(document.getElementById('price2').value);

        const data = {
            StoreId: storeId,
            SetPrice1: price1,
            SetPrice2: price2
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

    document.querySelectorAll('.assign-flag-button').forEach(button => {
        button.addEventListener('click', function () {
            selectedProductId = this.dataset.productId;
            modal.style.display = 'block';

            fetch(`/ProductFlags/GetFlagsForProduct?productId=${selectedProductId}`)
                .then(response => response.json())
                .then(flags => {
                    document.querySelectorAll('.flagCheckbox').forEach(checkbox => {
                        checkbox.checked = flags.includes(parseInt(checkbox.value));
                    });
                })
                .catch(error => console.error('Błąd dodawania flagi:', error));
        });
    });

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
                    loadPrices();
                } else {
                    alert('Błąd przypisywania flagi: ' + response.message);
                }
            })
            .catch(error => console.error('Błąd przypisywania flagi:', error));
    });

    loadStores();
    loadPrices();
});
