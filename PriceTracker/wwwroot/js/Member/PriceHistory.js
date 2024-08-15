
document.addEventListener("DOMContentLoaded", function () {
    let allPrices = [];
    let chartInstance = null;
    let myStoreName = "";
    let setPrice1 = 2.00;
    let setPrice2 = 2.00;
    let selectedProductId = null;
    let competitorStore = "";
    let selectedFlags = new Set();


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
                missedProductsCount = response.missedProductsCount;

                const usePriceDifference = document.getElementById('usePriceDifference').checked;

                allPrices = response.prices.map(price => {
                    let valueToUse;

                    if (usePriceDifference) {
                        // Jeżeli istnieje wartość savings, używamy jej jako valueToUse, w przeciwnym razie używamy priceDifference
                        valueToUse = price.savings !== null ? price.savings : price.priceDifference;
                    } else {
                        valueToUse = price.percentageDifference;
                    }

                    return {
                        ...price,
                        valueToUse: valueToUse,
                        colorClass: getColorClass(valueToUse, price.isUniqueBestPrice, price.isSharedBestPrice)
                    };
                });

                document.getElementById('totalPriceCount').textContent = response.priceCount;
                document.getElementById('price1').value = setPrice1;
                document.getElementById('price2').value = setPrice2;
                document.getElementById('missedProductsCount').textContent = missedProductsCount;

                updateFlagCounts(allPrices);
                const currentSearchTerm = document.getElementById('productSearch').value;
                let filteredPrices = allPrices;

                if (currentSearchTerm) {
                    filteredPrices = filteredPrices.filter(price => {
                        const sanitizedInput = currentSearchTerm.replace(/[^a-zA-Z0-9\s.-]/g, '').trim();
                        const sanitizedInputLowerCase = sanitizedInput.toLowerCase().replace(/\s+/g, '');
                        const sanitizedProductName = price.productName.toLowerCase().replace(/[^a-zA-Z0-9\s.-]/g, '').replace(/\s+/g, '');
                        return sanitizedProductName.includes(sanitizedInputLowerCase);
                    });
                }

                filteredPrices = filterPricesByCategoryAndColorAndFlag(filteredPrices);

                renderPrices(filteredPrices);
                renderChart(filteredPrices);
                updateColorCounts(filteredPrices);
            })
            .catch(error => console.error('Error fetching prices:', error));
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
        flagContainer.innerHTML = ''; // Wyczyszczenie poprzednich checkboxów

        // Aktualizacja istniejących flag
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

        // Dodanie filtra dla produktów bez flag
        const noFlagElement = `
        <div class="form-check">
            <input class="form-check-input flagFilter" type="checkbox" id="noFlagCheckbox" value="noFlag" ${selectedFlags.has('noFlag') ? 'checked' : ''}>
            <label class="form-check-label" for="noFlagCheckbox">Brak flagi (${noFlagCount})</label>
        </div>
        `;
        flagContainer.innerHTML += noFlagElement;

        // Podpięcie event listenerów do nowych checkboxów
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
        const selectedCategory = document.getElementById('category').value;
        const selectedColors = Array.from(document.querySelectorAll('.colorFilter:checked')).map(checkbox => checkbox.value);
        const selectedBid = document.getElementById('bidFilter').checked;
        const selectedPositions = Array.from(document.querySelectorAll('.positionFilter:checked')).map(checkbox => parseInt(checkbox.value));
        const selectedDeliveryMyStore = Array.from(document.querySelectorAll('.deliveryFilterMyStore:checked')).map(checkbox => parseInt(checkbox.value));
        const selectedDeliveryCompetitor = Array.from(document.querySelectorAll('.deliveryFilterCompetitor:checked')).map(checkbox => parseInt(checkbox.value));
        const selectedExternalPrice = Array.from(document.querySelectorAll('.externalPriceFilter:checked')).map(checkbox => checkbox.value);

        let filteredPrices = selectedCategory ? data.filter(item => item.category === selectedCategory) : data;

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

        if (selectedPositions.length) {
            filteredPrices = filteredPrices.filter(item => selectedPositions.includes(parseInt(item.myPosition)));
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


    function filterPricesByProductName(name) {
        const sanitizedInput = name.replace(/[^a-zA-Z0-9\s.-]/g, '').trim();
        const sanitizedInputLowerCase = sanitizedInput.toLowerCase().replace(/\s+/g, '');

        const filteredPrices = allPrices.filter(item => {
            const sanitizedProductName = item.productName.toLowerCase().replace(/[^a-zA-Z0-9\s.-]/g, '').replace(/\s+/g, '');
            return sanitizedProductName.includes(sanitizedInputLowerCase);
        });

        renderPrices(filteredPrices, sanitizedInput);
    }

    function highlightMatches(text, searchTerm) {
        if (!searchTerm) return text;
        const sanitizedSearchTerm = searchTerm.replace(/[^a-zA-Z0-9\s.-]/g, '');
        const regex = new RegExp(`(${sanitizedSearchTerm})`, 'gi');
        return text.replace(regex, '<span style="color: #9400D3;font-weight: 600;">$1</span>');
    }


    function renderPrices(data, searchTerm = "") {
        const container = document.getElementById('priceContainer');
        container.innerHTML = '';
        data.forEach(item => {
            const highlightedProductName = highlightMatches(item.productName, searchTerm);
            const percentageDifference = item.percentageDifference != null ? item.percentageDifference.toFixed(2) : "N/A";
            const priceDifference = item.priceDifference != null ? item.priceDifference.toFixed(2) : "N/A";
            const savings = item.savings != null ? item.savings.toFixed(2) : "N/A";

            const isBidding = item.isBidding === "1";
            const myIsBidding = item.myIsBidding === "1";

            const deliveryClass = getDeliveryClass(item.delivery);
            const myDeliveryClass = getDeliveryClass(item.myDelivery);

            const box = document.createElement('div');
            box.className = 'price-box ' + item.colorClass;
            box.dataset.detailsUrl = '/PriceHistory/Details?scrapId=' + item.scrapId + '&productId=' + item.productId;

            const priceBoxSpace = document.createElement('div');
            priceBoxSpace.className = 'price-box-space';
            const priceBoxColumnName = document.createElement('div');
            priceBoxColumnName.className = 'price-box-column-name';
            priceBoxColumnName.innerHTML = highlightedProductName;
            const assignFlagButton = document.createElement('button');
            assignFlagButton.className = 'assign-flag-button';
            assignFlagButton.dataset.productId = item.productId;
            assignFlagButton.innerHTML = '+ Przypisz flagi';

            priceBoxSpace.appendChild(priceBoxColumnName);
            priceBoxSpace.appendChild(assignFlagButton);

            const priceBoxColumnCategory = document.createElement('div');
            priceBoxColumnCategory.className = 'price-box-column-category';
            priceBoxColumnCategory.innerHTML = item.category;
            if (item.externalId) {
                const apiBox = document.createElement('span');
                apiBox.className = 'ApiBox';
                apiBox.innerHTML = 'API ID ' + item.externalId;
                priceBoxColumnCategory.appendChild(apiBox);
            }

            const priceBoxData = document.createElement('div');
            priceBoxData.className = 'price-box-data';

            const colorBar = document.createElement('div');
            colorBar.className = 'color-bar ' + item.colorClass;

            const priceBoxColumnLowestPrice = document.createElement('div');
            priceBoxColumnLowestPrice.className = 'price-box-column';
            priceBoxColumnLowestPrice.innerHTML =
                '<div class="price-box-column-text">' + item.lowestPrice.toFixed(2) + ' zł</div>' +
                '<div class="price-box-column-text">' + item.storeName + ' ' +
                (isBidding ? '<span class="Bidding">Bid</span>' : '') +
                '<span class="Position">Msc ' + item.position + '</span>' +
                (item.delivery != null ? '<span class="' + deliveryClass + '">Wysyłka w ' + (item.delivery == 1 ? '1 dzień' : item.delivery + ' dni') + '</span>' : '') +
                '</div>';

            const priceBoxColumnMyPrice = document.createElement('div');
            priceBoxColumnMyPrice.className = 'price-box-column';
            priceBoxColumnMyPrice.innerHTML =
                '<div class="price-box-column-text">' + item.myPrice.toFixed(2) + ' zł</div>' +
                '<div class="price-box-column-text">' + myStoreName + ' ' +
                (myIsBidding ? '<span class="Bidding">Bid</span>' : '') +
                '<span class="Position">Msc ' + item.myPosition + '</span>' +
                (item.myDelivery != null ? '<span class="' + myDeliveryClass + '">Wysyłka w ' + (item.myDelivery == 1 ? '1 dzień' : item.myDelivery + ' dni') + '</span>' : '') +
                '</div>';

            const priceBoxColumnInfo = document.createElement('div');
            priceBoxColumnInfo.className = 'price-box-column-action';

            if (item.colorClass === "prToLow" || item.colorClass === "prIdeal") {
                priceBoxColumnInfo.innerHTML =
                    '<p>Podnieś: ' + savings + ' zł</p>' +
                    '<p>Podnieś: ' + percentageDifference + ' %</p>';
            } else if (item.colorClass === "prMid" || item.colorClass === "prToHigh") {
                priceBoxColumnInfo.innerHTML =
                    '<p>Obniż: ' + priceDifference + ' zł</p>' +
                    '<p>Obniż: ' + percentageDifference + ' %</p>';
            } else if (item.colorClass === "prGood") {
                priceBoxColumnInfo.innerHTML = '<p>Brak działań</p>';
            }

            const priceBoxColumnExternalPrice = document.createElement('div');
            priceBoxColumnExternalPrice.className = 'price-box-column-api';
            if (item.externalPrice !== null) {
                const externalPriceDifference = (item.externalPrice - item.myPrice).toFixed(2);
                const externalPriceDifferenceText = (item.externalPrice > item.myPrice ? '+' : '') + externalPriceDifference;
                priceBoxColumnExternalPrice.innerHTML =
                    '<div class="price-box-column-text-api">Nowa cena: ' + item.externalPrice.toFixed(2) + ' zł</div>' +
                    '<div class="price-box-column-text-api">Zmiana: ' + externalPriceDifferenceText + ' zł</div>';
            }

            const flagsContainer = document.createElement('div');
            flagsContainer.className = 'flags-container';
            if (item.flagIds.length > 0) {
                item.flagIds.forEach(function (flagId) {
                    const flag = flags.find(function (f) { return f.FlagId === flagId; });
                    const flagSpan = document.createElement('span');
                    flagSpan.className = 'flag';
                    flagSpan.style.color = flag.FlagColor;
                    flagSpan.style.border = '2px solid ' + flag.FlagColor;
                    flagSpan.style.backgroundColor = hexToRgba(flag.FlagColor, 0.3);
                    flagSpan.innerHTML = flag.FlagName;
                    flagsContainer.appendChild(flagSpan);
                });
            }

            priceBoxData.appendChild(colorBar);
            priceBoxData.appendChild(priceBoxColumnLowestPrice);
            priceBoxData.appendChild(priceBoxColumnMyPrice);
            priceBoxData.appendChild(priceBoxColumnInfo);
            if (item.externalPrice !== null) {
                priceBoxData.appendChild(priceBoxColumnExternalPrice);
            }
            priceBoxData.appendChild(flagsContainer);

            box.appendChild(priceBoxSpace);
            box.appendChild(priceBoxColumnCategory);
            box.appendChild(priceBoxData);

            box.addEventListener('click', function () {
                window.open(this.dataset.detailsUrl, '_blank');
            });

            container.appendChild(box);

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
                labels: ['Zawyżona', 'Suboptymalna', 'Konkurencyjna', 'Strategiczna', 'Zaniżona'],
                datasets: [{
                    data: [colorCounts.prToHigh, colorCounts.prMid, colorCounts.prGood, colorCounts.prIdeal, colorCounts.prToLow],
                    backgroundColor: [
                        'rgba(171, 37, 32, 0.8)',
                        'rgba(224, 168, 66, 0.8)',
                        'rgba(117, 152, 112, 0.8)',
                        'rgba(0, 145, 123, 0.8)',
                        'rgba(6, 6, 6, 0.8)'
                    ],
                    borderColor: [
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

        document.querySelector('label[for="prToHighCheckbox"]').textContent = `Zawyżona (${colorCounts.prToHigh})`;
        document.querySelector('label[for="prMidCheckbox"]').textContent = `Suboptymalna (${colorCounts.prMid})`;
        document.querySelector('label[for="prGoodCheckbox"]').textContent = `Konkurencyjna (${colorCounts.prGood})`;
        document.querySelector('label[for="prIdealCheckbox"]').textContent = `Strategiczna (${colorCounts.prIdeal})`;
        document.querySelector('label[for="prToLowCheckbox"]').textContent = `Zaniżona (${colorCounts.prToLow})`;
    }

    function filterPricesAndUpdateUI() {
        const filteredPrices = filterPricesByCategoryAndColorAndFlag(allPrices);
        renderPrices(filteredPrices);
        renderChart(filteredPrices);
        updateColorCounts(filteredPrices);
        updateFlagCounts(filteredPrices);
    }

    document.getElementById('category').addEventListener('change', function () {
        filterPricesAndUpdateUI();
    });

    document.querySelectorAll('.colorFilter, .flagFilter, .positionFilter, .deliveryFilterMyStore, .deliveryFilterCompetitor, .externalPriceFilter').forEach(function (checkbox) {
        checkbox.addEventListener('change', function () {
            filterPricesAndUpdateUI();
        });
    });

    document.getElementById('bidFilter').addEventListener('change', function () {
        filterPricesAndUpdateUI();
    });

    document.getElementById('productSearch').addEventListener('keyup', function () {
        filterPricesByProductName(this.value);
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

    document.getElementById('price1').addEventListener('input', function () {
        setPrice1 = parseFloat(this.value);
        const usePriceDifference = document.getElementById('usePriceDifference').checked;

        allPrices.forEach(price => {
            price.valueToUse = usePriceDifference
                ? (price.savings !== null ? price.savings : price.priceDifference)
                : price.percentageDifference;
            price.colorClass = getColorClass(price.valueToUse, price.isUniqueBestPrice, price.isSharedBestPrice);
        });

        filterPricesAndUpdateUI();
    });

    document.getElementById('price2').addEventListener('input', function () {
        setPrice2 = parseFloat(this.value);
        const usePriceDifference = document.getElementById('usePriceDifference').checked;

        allPrices.forEach(price => {
            price.valueToUse = usePriceDifference
                ? (price.savings !== null ? price.savings : price.priceDifference)
                : price.percentageDifference;
            price.colorClass = getColorClass(price.valueToUse, price.isUniqueBestPrice, price.isSharedBestPrice);
        });

        filterPricesAndUpdateUI();
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
