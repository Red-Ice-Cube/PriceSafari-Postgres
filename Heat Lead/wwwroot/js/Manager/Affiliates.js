function redirectToProfile(codePAR) {
    window.location.href = profileRedirectUrl + '?codePAR=' + codePAR;
}

$(document).ready(function () {
    $('#campaignSearch').on('keyup', function () {
        var searchText = $(this).val().toLowerCase();

        $('.affiliate-wrapper').filter(function () {
            $(this).toggle($(this).find('.campaign-box-head-name').text().toLowerCase().indexOf(searchText) > -1);
        });
    });
});

document.addEventListener("DOMContentLoaded", function () {
    fetch(affiliatesDataUrl)
        .then(response => response.json())
        .then(data => {
            var affiliateWrappers = data.map(affiliate => {
                updateAffiliateRowAndChart(affiliate);
                return document.querySelector(`.affiliate-wrapper[data-codepar='${affiliate.affiliate}']`);
            });

            updateView(affiliateWrappers);

            $('#sortNewest').addClass('selected');
        })
        .catch(error => console.error('Error:', error));
});

function updateAffiliateRowAndChart(affiliate) {
    var wrapper = document.querySelector(`.affiliate-wrapper[data-codepar='${affiliate.affiliate}']`);
    if (wrapper) {
        wrapper.querySelector('.clicks-data').textContent = affiliate.totalClicks;
        wrapper.querySelector('.sales-data').textContent = affiliate.totalSales.toFixed(2) + ' zł';
        wrapper.querySelector('.fullClicks-data').textContent = affiliate.fullClicks;
        wrapper.querySelector('.fullOrders-data').textContent = affiliate.fullOrders;
        wrapper.querySelector('.fullSales-data').textContent = affiliate.fullSales.toFixed(2) + ' zł';
        wrapper.querySelector('.orders-data').textContent = affiliate.totalOrders;
        var ordersToClicksRatio = updateOrdersToClicksRatio(affiliate.totalOrders, affiliate.totalClicks);
        wrapper.querySelector('.ordersToClicksRatio').textContent = ordersToClicksRatio;
        var fullOrdersToClicksRatio = updateFullOrdersToClicksRatio(affiliate.fullOrders, affiliate.fullClicks);
        wrapper.querySelector('.fullOrdersToClicksRatio').textContent = fullOrdersToClicksRatio;
        wrapper.setAttribute('data-creation-date', affiliate.creationDate);
        var creationDate = new Date(affiliate.creationDate);
        var formattedDate = creationDate.toLocaleDateString('pl-PL', { year: 'numeric', month: 'long', day: 'numeric' });
        wrapper.querySelector('.creationDate-data').textContent = formattedDate;

        var labels = affiliate.clicks.map(click => click.clickTime.substring(0, 10));
        var clicks = affiliate.clicks.map(click => click.count);
        var sales = affiliate.sales.map(sale => sale.amount);

        var ctx = wrapper.querySelector('.chart-container canvas').getContext('2d');
        createChart(ctx, labels, clicks, sales);
    }
}

function createChart(ctx, labels, clicks, sales) {
    var gradientFill1 = ctx.createLinearGradient(0, 0, 0, ctx.canvas.clientHeight);
    gradientFill1.addColorStop(0, 'rgba(14, 126, 135, 0.2)');
    gradientFill1.addColorStop(1, 'rgba(14, 126, 135, 0.6)');

    var gradientFill2 = ctx.createLinearGradient(0, 0, 0, ctx.canvas.clientHeight);
    gradientFill2.addColorStop(0, 'rgba(246, 80, 99, 1');
    gradientFill2.addColorStop(1, 'rgba(247, 128, 95, 1)');

    var chart = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,

            datasets: [{
                label: 'Sprzedaż',
                data: sales,
                barThickness: 10,
                backgroundColor: 'rgba(255, 255, 255, 0.6)',
                borderColor: 'rgba(255, 255, 255, 0.6)',
                borderWidth: 1,
                borderRadius: 2,
                yAxisID: 'ySales'
            }, {
                label: 'Ruch na stronę:',
                data: clicks,
                type: 'line',
                backgroundColor: gradientFill1,
                borderColor: 'rgba(14, 126, 135, 1)',
                borderWidth: 2,
                fill: true,
                tension: 0.5,
                yAxisID: 'yClicks'
            }]
        },
        options: {
            maintainAspectRatio: false,
            scales: {
                x: {
                    display: false,
                    offset: false,
                    grid: {
                        color: 'rgba(255, 255, 255, 0.8)',
                    }
                },
                yClicks: {
                    display: true,
                    type: 'linear',
                    position: 'left',
                    beginAtZero: true,
                    suggestedMax: 100,

                    title: {
                        display: false,
                        text: 'Ruch',
                        color: 'rgba(255, 255, 255, 0.8',
                        font: {
                            size: 12,
                            weight: 'regular'
                        }
                    },
                    ticks: {
                        color: 'rgba(255, 255, 255, 0.8',
                    }
                },
                ySales: {
                    display: true,
                    type: 'linear',
                    position: 'right',
                    color: 'rgba(255, 255, 255, 0.8)',
                    beginAtZero: false,
                    suggestedMax: 500,
                    grid: {
                        drawOnChartArea: false,
                    },
                    title: {
                        display: false,
                        text: 'Sprzedaż',
                        color: 'rgba(255, 255, 255, 0.8)',
                        font: {
                            size: 12,
                            weight: 'regular'
                        }
                    },
                    ticks: {
                        color: 'rgba(255, 255, 255, 0.8',
                        callback: function (value, index, values) {
                            return value.toFixed(2) + " zł";
                        }
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

                            if (label === 'Sprzedaż') {
                                return label + ': ' + parseFloat(value).toFixed(2) + ' zł';
                            } else {
                                return label + ': ' + value;
                            }
                        }
                    }
                }
            }
        }
    });
}

function updateOrdersToClicksRatio(totalOrders, totalClicks) {
    var ratio = 0;
    if (totalClicks > 0) {
        ratio = (totalOrders / totalClicks) * 100;
    }
    return ratio.toFixed(2) + ' %';
}
function updateFullOrdersToClicksRatio(fullOrders, fullClicks) {
    var ratio = 0;
    if (fullClicks > 0) {
        ratio = (fullOrders / fullClicks) * 100;
    }
    return ratio.toFixed(2) + ' %';
}

$(document).ready(function () {
    $('.Select-Line button').click(function () {
        $('.Select-Line button').removeClass('selected');

        $(this).addClass('selected');
    });

    $('#sortHighestSales').click(() => sortAffiliatesBySales(true));
    $('#sortLowestSales').click(() => sortAffiliatesBySales(false));
    $('#sortNewest').click(() => sortAffiliatesByJoinDate(true));
    $('#sortOldest').click(() => sortAffiliatesByJoinDate(false));
});

function updateView(sortedAffiliates) {
    var container = document.querySelector('.Vert-affiliate');
    container.innerHTML = '';

    var today = new Date();

    sortedAffiliates.forEach(affiliate => {
        var creationDate = new Date(affiliate.getAttribute('data-creation-date'));
        var diffTime = Math.abs(today - creationDate);
        var diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));

        if (diffDays <= 14) {
            var newAffiliateBadge = affiliate.querySelector('.new-affiliate');
            if (newAffiliateBadge) {
                newAffiliateBadge.style.display = 'block';
            }
        }

        container.appendChild(affiliate);
    });

    sortedAffiliates.sort((a, b) => {
        var salesA = parseFloat(a.querySelector('.fullSales-data').textContent);
        var salesB = parseFloat(b.querySelector('.fullSales-data').textContent);
        return salesB - salesA;
    });

    sortedAffiliates.forEach((affiliate, index) => {
        if (index < 10) {
            var topAffiliateBadge = affiliate.querySelector('.top-affiliate');
            if (topAffiliateBadge) {
                topAffiliateBadge.textContent = 'TOP ' + (index + 1);
                topAffiliateBadge.style.display = 'block';
            }
        }
    });
}

function sortAffiliatesBySales(isHighest) {
    var affiliates = Array.from(document.querySelectorAll('.affiliate-wrapper'));
    affiliates.sort((a, b) => {
        var salesA = parseFloat(a.querySelector('.fullSales-data').textContent);
        var salesB = parseFloat(b.querySelector('.fullSales-data').textContent);
        return isHighest ? salesB - salesA : salesA - salesB;
    });
    updateView(affiliates);
}

function sortAffiliatesByJoinDate(isNewest) {
    var affiliates = Array.from(document.querySelectorAll('.affiliate-wrapper'));
    affiliates.sort((a, b) => {
        var dateA = new Date(a.getAttribute('data-creation-date'));
        var dateB = new Date(b.getAttribute('data-creation-date'));

        return isNewest ? dateB - dateA : dateA - dateB;
    });
    updateView(affiliates);
}