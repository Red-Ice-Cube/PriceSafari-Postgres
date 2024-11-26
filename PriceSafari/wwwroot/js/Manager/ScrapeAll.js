document.addEventListener("DOMContentLoaded", function () {
    var ctx = document.getElementById('speedChart').getContext('2d');

    var gradientFill1 = ctx.createLinearGradient(0, 0, 0, ctx.canvas.clientHeight);
    gradientFill1.addColorStop(0, 'rgba(14, 126, 135, 0.2)');
    gradientFill1.addColorStop(1, 'rgba(14, 126, 135, 0.6)');

    var speedChart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: [],
            datasets: [{
                label: 'Zebrane produkty na sekunde:',
                data: [],
                backgroundColor: gradientFill1,
                borderColor: 'rgba(14, 126, 135, 1)',
                borderWidth: 2,
                fill: true,
                tension: 0.5,
                pointRadius: 0,
                pointHoverRadius: 6,
                pointHoverBorderWidth: 2
            }]
        },
        options: {
            maintainAspectRatio: false,
            scales: {
                x: {
                    type: 'linear',
                    position: 'bottom',
                    title: {
                        display: false,
                        text: 'Sekundy',
                        color: 'rgba(255, 255, 255, 0.8)'
                    },
                    grid: {
                        color: 'rgba(255, 255, 255, 0.2)'
                    },
                    ticks: {
                        color: 'rgba(255, 255, 255, 0.8)'
                    }
                },
                y: {
                    beginAtZero: true,
                    title: {
                        display: false,
                        text: 'Zebrane produkty',
                        color: 'rgba(255, 255, 255, 0.8)'
                    },
                    grid: {
                        color: 'rgba(255, 255, 255, 0.2)'
                    },
                    ticks: {
                        color: 'rgba(255, 255, 255, 0.8'
                    }
                }
            },
            elements: {
                point: {
                    radius: 0,
                    hoverRadius: 6,
                    hoverBorderWidth: 2
                }
            },
            plugins: {
                legend: {
                    display: false,
                    labels: {
                        color: 'rgba(0, 69, 82, 1)',
                        font: {
                            size: 15
                        }
                    }
                },
                tooltip: {
                    mode: 'index',
                    intersect: false,
                    callbacks: {
                        label: function (context) {
                            var label = context.dataset.label || '';
                            var value = context.parsed.y;
                            return label + ': ' + value;
                        }
                    }
                }
            }
        }
    });

    var connection = new signalR.HubConnectionBuilder().withUrl("/scrapingHub").build();
    var scrapingComplete = false;

    connection.on("ReceiveScrapingUpdate", function (offerUrl, isScraped, isRejected,  pricesCount) {
        var rowId = "row-" + offerUrl;
        var row = document.getElementById(rowId);
        var rowClass = isRejected && isScraped ? "rejected-row" : isScraped ? "scraped-row" : "unscraped-row";

        if (row) {
            row.className = "product-row " + rowClass;
            row.cells[2].innerText = isScraped ? "Tak" : "Nie";
            row.cells[3].innerText = isRejected ? "Tak" : "Nie";
       
            row.cells[4].innerText = pricesCount;
        } else {
            var newRow = `<tr id="${rowId}" class="product-row ${rowClass}">
                        <td>${offerUrl}</td>
                        <td></td>
                        <td>${isScraped ? "Tak" : "Nie"}</td>
                        <td>${isRejected ? "Tak" : "Nie"}</td>
                       
                        <td>${pricesCount}</td>
                    </tr>`;
            document.getElementById("scrapingTableBody").insertAdjacentHTML('beforeend', newRow);
        }

        updateCounters();
    });

    connection.on("ReceiveProgressUpdate", function (totalScraped, uniqueProducts, elapsedSeconds, rejectedCount) {
        if (scrapingComplete) return;

        var percentage = Math.round((totalScraped / uniqueProducts) * 100);
        var progressBarInner = document.getElementById("progressBarInner");
        var progressText = document.getElementById("progressText");
        var completionMessage = document.getElementById("completionMessage");

        progressBarInner.style.width = percentage + "%";
        progressBarInner.innerHTML = percentage + "%";
        progressText.innerHTML = `Zbieranie cen ${totalScraped}/${uniqueProducts} produktów (${rejectedCount} odrzuconych)`;

        var currentTime = Math.floor(elapsedSeconds);

        var speed = (totalScraped / elapsedSeconds).toFixed(2);

        if (speedChart.data.labels.length > 0 && currentTime === speedChart.data.labels[speedChart.data.labels.length - 1]) {
            var lastIndex = speedChart.data.labels.length - 1;
            speedChart.data.datasets[0].data[lastIndex] = (parseFloat(speedChart.data.datasets[0].data[lastIndex]) + parseFloat(speed)) / 2;
        } else {
            speedChart.data.labels.push(currentTime);
            speedChart.data.datasets[0].data.push(parseFloat(speed));
        }

        speedChart.update();

        if (totalScraped === uniqueProducts) {
            completionMessage.style.display = "block";
            completionMessage.innerHTML = `Zebrano ceny dla ${totalScraped}/${uniqueProducts} produktów (${rejectedCount} odrzuconych).`;
            completionMessage.style.color = "#0E7E87";

            progressBarInner.style.backgroundColor = "#0E7E87";

            progressText.style.display = "none";

            scrapingComplete = true;
        }
    });

    connection.start().catch(function (err) {
        return console.error(err.toString());
    });

    $('#scrapingForm').on('submit', function (e) {
        e.preventDefault();
        var form = $(this);
        var url = form.attr('action');

        $.ajax({
            url: url,
            method: 'POST',
            success: function () {
                console.log('Scraping started successfully');
            },
            error: function () {
                console.error('Error starting scraping');
            }
        });
    });

    $('#stopScrapingButton').on('click', function () {
        $.ajax({
            url: '/PriceScraping/StopScraping',
            method: 'POST',
            success: function () {
                console.log('Scraping stopped successfully');
            },
            error: function () {
                console.error('Error stopping scraping');
            }
        });
    });

    function updateCounters() {
        var rows = document.querySelectorAll("#scrapingTableBody tr");
        var totalUrls = rows.length;
        var totalProductIds = 0;
        var totalPricesCount = 0;
        var totalRejectedCount = 0;

        rows.forEach(function (row) {
            totalProductIds += row.querySelectorAll('td')[1].textContent.split(', ').length;
            totalPricesCount += parseInt(row.querySelectorAll('td')[5].textContent) || 0;
            if (row.querySelectorAll('td')[3].innerText === "Tak") {
                totalRejectedCount++;
            }
        });

        document.getElementById("totalUrlCount").textContent = totalUrls;
        document.getElementById("totalProductCount").textContent = totalProductIds;
        document.getElementById("totalPricesCount").textContent = totalPricesCount;
        document.getElementById("totalRejectedCount").textContent = totalRejectedCount;
    }
});



