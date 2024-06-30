document.addEventListener("DOMContentLoaded", function () {
    let allPrices = [];
    let chartInstance = null;
    let myStoreName = "";

    function loadStores() {
        fetch(`/PriceHistory/GetStores?storeId=${storeId}`)
            .then(response => response.json())
            .then(stores => {
                const storeSelect = document.getElementById('storeName');
                stores.forEach(store => {
                    const option = document.createElement('option');
                    option.value = store;
                    option.text = store;
                    storeSelect.appendChild(option);
                });
            })
            .catch(error => console.error('Error fetching stores:', error));
    }

    function loadPrices() {
        fetch(`/PriceHistory/GetPrices?storeId=${storeId}`)
            .then(response => response.json())
            .then(response => {
                myStoreName = response.myStoreName;
                allPrices = response.prices.map(price => ({
                    ...price,
                    colorClass: getColorClass(price.priceDifference, price.isUniqueBestPrice, price.isSharedBestPrice, price.savings)
                }));
                document.getElementById('totalProductCount').textContent = response.productCount;
                document.getElementById('totalPriceCount').textContent = response.priceCount;
                renderPrices(allPrices);
                renderChart(allPrices);
                updateColorTable(allPrices);
            })
            .catch(error => console.error('Error fetching prices:', error));
    }

    function getColorClass(priceDifference, isUniqueBestPrice = false, isSharedBestPrice = false, savings = null) {
        if (isUniqueBestPrice && savings >= 0.01 && savings <= 1.00) {
            return "turquoise";
        }
        if (isUniqueBestPrice) {
            return "green";
        }
        if (isSharedBestPrice) {
            return "blue";
        }
        if (priceDifference <= 0) {
            return "blue";
        } else if (priceDifference < 2.00) {
            return "yellow";
        } else {
            return "red";
        }
    }

    function filterPricesByCategoryAndColor(data, searchTerm = "") {
        const selectedCategory = document.getElementById('category').value;
        const selectedColors = Array.from(document.querySelectorAll('.colorFilter:checked')).map(checkbox => checkbox.value);

        let filteredPrices = selectedCategory ? data.filter(item => item.category === selectedCategory) : data;
        filteredPrices = selectedColors.length ? filteredPrices.filter(item => selectedColors.includes(item.colorClass)) : filteredPrices;

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

        filterPricesByCategoryAndColor(filteredPrices, sanitizedInput);
    }

    function highlightMatches(text, searchTerm) {
        if (!searchTerm) return text;
        const regex = new RegExp(`(${searchTerm})`, 'gi');
        return text.replace(regex, '<span style="color: #9400D3;font-weight: 600;">$1</span>');
    }

    function renderPrices(data, searchTerm = "") {
        const selectedColors = Array.from(document.querySelectorAll('.colorFilter:checked')).map(checkbox => checkbox.value);
        const pricesToRender = selectedColors.length ? data.filter(item => selectedColors.includes(item.colorClass)) : data;

        const container = document.getElementById('priceContainer');
        container.innerHTML = '';
        pricesToRender.forEach(item => {
            const highlightedProductName = highlightMatches(item.productName, searchTerm);
            const percentageDifference = item.percentageDifference != null ? item.percentageDifference.toFixed(2) : "N/A";
            const priceDifference = item.priceDifference != null ? item.priceDifference.toFixed(2) : "N/A";
            const savings = item.colorClass === "green" || item.colorClass === "turquoise" ? item.savings != null ? item.savings.toFixed(2) : "N/A" : "N/A";

            const box = document.createElement('div');
            box.className = `price-box ${item.colorClass}`;
            box.dataset.detailsUrl = `/PriceHistory/Details?scrapId=${item.scrapId}&productId=${item.productId}`;
            box.innerHTML = `
                <div class="price-box-column-name">${highlightedProductName} ${item.category}</div>
                <div class="price-box-data">
                    <div class="color-bar ${item.colorClass}"></div>
                    <div class="price-box-column">
                        <div class="price-box-column-text">${item.myPrice.toFixed(2)} zł</div>
                        <div class="price-box-column-text">${myStoreName}</div>
                    </div>
                    <div class="price-box-column-line"></div>
                    <div class="price-box-column">
                        <div class="price-box-column-text">${item.lowestPrice.toFixed(2)} zł</div>
                        <div class="price-box-column-text">${item.storeName}</div>
                    </div>
                    <div class="price-box-column-line"></div>
                    <div class="price-box-column">
                        ${item.colorClass === "green" || item.colorClass === "turquoise" ? `<p>Oszczędność: ${savings} zł</p>` : ""}
                        ${item.colorClass === "red" || item.colorClass === "yellow" ? `<p>Różnica (%): ${percentageDifference}%</p>` : ""}
                        ${item.colorClass === "red" || item.colorClass === "yellow" ? `<p>Różnica (PLN): ${priceDifference} zł</p>` : ""}
                    </div>
                </div>
            `;

            box.addEventListener('click', function () {
                window.open(this.dataset.detailsUrl, '_blank');
            });

            container.appendChild(box);
        });
        document.getElementById('displayedProductCount').textContent = pricesToRender.length;
    }

    function renderChart(data) {
        const colorCounts = {
            blue: 0,
            yellow: 0,
            red: 0,
            green: 0,
            turquoise: 0
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
                labels: ['Top cena', 'Mid cena', 'Zła cena', 'Super cena', 'Idealna cena'],
                datasets: [{
                    data: [colorCounts.blue, colorCounts.yellow, colorCounts.red, colorCounts.green, colorCounts.turquoise],
                    backgroundColor: [
                        'rgba(14, 126, 135, 0.5)',
                        'rgba(240, 240, 105, 0.5)',
                        'rgba(246, 78, 101, 0.5)',
                        'rgba(0, 255, 0, 0.5)',
                        'rgba(64, 224, 208, 0.5)'
                    ],
                    borderColor: [
                        'rgba(14, 126, 135, 1)',
                        'rgba(240, 240, 105, 1)',
                        'rgba(246, 78, 101, 1)',
                        'rgba(0, 255, 0, 1)',
                        'rgba(64, 224, 208, 1)'
                    ],
                    borderWidth: 1
                }]
            },
            options: {
                aspectRatio: 2.5,
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
                                return 'Liczba Produktów: ' + value;
                            }
                        }
                    }
                }
            }
        });
    }

    function updateColorTable(data) {
        const colorCounts = {
            blue: 0,
            yellow: 0,
            red: 0,
            green: 0,
            turquoise: 0
        };

        data.forEach(item => {
            colorCounts[item.colorClass]++;
        });

        const colorTableBody = document.getElementById('colorTableBody');
        colorTableBody.innerHTML = '';

        const colors = [
            { name: 'Top cena', count: colorCounts.blue },
            { name: 'Mid cena', count: colorCounts.yellow },
            { name: 'Zła cena', count: colorCounts.red },
            { name: 'Super cena', count: colorCounts.green },
            { name: 'Idealna cena', count: colorCounts.turquoise }
        ];

        colors.forEach(color => {
            const row = document.createElement('tr');
            row.innerHTML = `<td>${color.name}</td><td>${color.count}</td>`;
            colorTableBody.appendChild(row);
        });
    }

    document.getElementById('category').addEventListener('change', function () {
        filterPricesByProductName(document.getElementById('productSearch').value);
    });

    document.querySelectorAll('.colorFilter').forEach(function (checkbox) {
        checkbox.addEventListener('change', function () {
            filterPricesByCategoryAndColor(allPrices);
        });
    });

    document.getElementById('productSearch').addEventListener('keyup', function () {
        filterPricesByProductName(this.value);
    });

    loadStores();
    loadPrices();
});
