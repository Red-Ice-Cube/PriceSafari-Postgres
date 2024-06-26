document.addEventListener("DOMContentLoaded", function () {
    let allPrices = [];

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
                allPrices = response.prices.map(price => ({
                    ...price,
                    colorClass: getColorClass(price.priceDifference)
                }));
                document.getElementById('totalProductCount').textContent = response.productCount;
                document.getElementById('totalPriceCount').textContent = response.priceCount;
                document.getElementById('displayedProductCount').textContent = response.prices.length;
                renderPrices(allPrices, "");
                renderChart(allPrices);
                updateColorTable(allPrices);
            })
            .catch(error => console.error('Error fetching prices:', error));
    }

    function getColorClass(priceDifference) {
        if (priceDifference <= 0) {
            return "blue";
        } else if (priceDifference < 2.00) {
            return "yellow";
        } else {
            return "red";
        }
    }

    function filterPricesByCategoryAndColor(data, searchTerm) {
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

    function renderPrices(data, searchTerm) {
        const tbody = document.getElementById('priceTable').getElementsByTagName('tbody')[0];
        tbody.innerHTML = '';
        data.forEach(item => {
            let rowColor;
            if (item.colorClass === "blue") {
                rowColor = "rgba(14, 126, 135, 0.5)";
            } else if (item.colorClass === "yellow") {
                rowColor = "rgba(240, 240, 105, 0.5)";
            } else {
                rowColor = "rgba(246, 78, 101, 0.5)";
            }

            const percentageDifference = item.percentageDifference != null ? item.percentageDifference.toFixed(2) : "N/A";
            const priceDifference = item.priceDifference != null ? item.priceDifference.toFixed(2) : "N/A";

            const highlightedName = highlightExactMatches(item.productName, searchTerm);

            const row = document.createElement('tr');
            row.style.backgroundColor = rowColor;
            row.className = `priceRow ${item.colorClass}`;
            row.dataset.category = item.category;
            row.innerHTML = `
                <td>${highlightedName}</td>
                <td style="font-weight: 500;">${item.lowestPrice.toFixed(2)} zł</td>
                <td>${item.storeName}</td>
                <td style="font-weight: 500;">${item.myPrice.toFixed(2)} zł</td>
                <td>${percentageDifference}%</td>
                <td>${priceDifference} zł</td>
                <td>
                    <a href="/PriceHistory/Details?scrapId=${item.scrapId}&productId=${item.productId}" class="Button-Add-Small" target="_blank">Szczegóły</a>
                </td>`;
            tbody.appendChild(row);
        });
        document.getElementById('displayedProductCount').textContent = data.length;
    }

    function highlightExactMatches(text, searchTerm) {
        if (!searchTerm) return text;

        const regex = new RegExp(`(${searchTerm})`, 'gi');
        return text.replace(regex, '<span style="background-color: rgba(0, 255, 0, 0.2);">$1</span>');
    }

    function renderChart(data) {
        const colorCounts = {
            blue: 0,
            yellow: 0,
            red: 0
        };

        data.forEach(item => {
            colorCounts[item.colorClass]++;
        });

        const ctx = document.getElementById('colorChart').getContext('2d');
        new Chart(ctx, {
            type: 'doughnut',
            data: {
                labels: ['Top cena', 'Mid cena', 'Zła cena'],
                datasets: [{
                    data: [colorCounts.blue, colorCounts.yellow, colorCounts.red],
                    backgroundColor: [
                        'rgba(14, 126, 135, 0.5)',
                        'rgba(240, 240, 105, 0.5)',
                        'rgba(246, 78, 101, 0.5)'
                    ],
                    borderColor: [
                        'rgba(14, 126, 135, 1)',
                        'rgba(240, 240, 105, 1)',
                        'rgba(246, 78, 101, 1)'
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
            red: 0
        };

        data.forEach(item => {
            colorCounts[item.colorClass]++;
        });

        const colorTableBody = document.getElementById('colorTableBody');
        colorTableBody.innerHTML = '';

        const colors = [
            { name: 'Top cena', count: colorCounts.blue },
            { name: 'Mid cena', count: colorCounts.yellow },
            { name: 'Zła cena', count: colorCounts.red }
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
            filterPricesByProductName(document.getElementById('productSearch').value);
        });
    });

    document.getElementById('productSearch').addEventListener('keyup', function () {
        filterPricesByProductName(this.value);
    });

    loadStores();
    loadPrices();
});
