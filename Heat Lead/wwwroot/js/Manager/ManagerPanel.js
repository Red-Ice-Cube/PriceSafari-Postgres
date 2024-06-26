function formatNumber(num) {
    return num.toString().replace(/\B(?=(\d{3})+(?!\d))/g, " ");
}

function showClicksChart() {
    $('#earningsChart').hide();
    $('#ordersChart').hide();
    $('#salesChart').hide();
    $('#clicksChart').show();
}

function showEarningsChart() {
    $('#clicksChart').hide();
    $('#ordersChart').hide();
    $('#salesChart').hide();
    $('#earningsChart').show();
}

function showOrdersChart() {
    $('#clicksChart').hide();
    $('#earningsChart').hide();
    $('#salesChart').hide();
    $('#ordersChart').show();
}

function showSalesChart() {
    $('#clicksChart').hide();
    $('#ordersChart').hide();
    $('#earningsChart').hide();
    $('#salesChart').show();
}

function showcategoryClicksChart() {
    $('#categoryEarningsChart').hide();
    $('#tableCategoryEarningsChart').hide();
    $('#categoryOrdersChart').hide();
    $('#tableCategoryOrdersChart').hide();
    $('#categoryClicksChart').show();
    $('#tableCategoryClicksChart').show();
}

function showcategoryEarningsChart() {
    $('#categoryClicksChart').hide();
    $('#tableCategoryClicksChart').hide();
    $('#categoryOrdersChart').hide();
    $('#tableCategoryOrdersChart').hide();
    $('#categoryEarningsChart').show();
    $('#tableCategoryEarningsChart').show();
}

function showcategoryOrdersChart() {
    $('#categoryClicksChart').hide();
    $('#tableCategoryClicksChart').hide();
    $('#categoryEarningsChart').hide();
    $('#tableCategoryEarningsChart').hide();
    $('#categoryOrdersChart').show();
    $('#tableCategoryOrdersChart').show();
}

$(document).ready(function () {
    var clicksCtx = document.getElementById('clicksChart').getContext('2d');
    var clicksChart = new Chart(clicksCtx, {
        type: 'bar',
        data: {
            labels: 'modelTimeLabels',
            datasets: [{
                label: 'Kliknięcia',
                data: 'managerClicks',
                backgroundColor: 'rgba(0, 69, 82, 0.51)',
                tension: 0.0,
                fill: 'origin',
                minBarLength: 3,
                borderWidth: 2,
                borderRadius: 4,
                fill: true,
                borderColor: 'rgba(0, 69, 82, 1)',
            }]
        },
        options: {
            maintainAspectRatio: true,
            scales: {
                x: {
                    grid: {
                        color: 'rgba(40, 40, 40, 0)',
                    },

                    title: {
                        display: false,
                        text: 'Data',
                        color: 'rgba(0, 0, 0, 0.2)',
                        font: {
                            size: 18,
                            weight: 'regular'
                        }
                    },
                },
                y: {
                    beginAtZero: true,
                    suggestedMax: 50,
                    grid: {
                        color: 'rgba(40, 40, 40, 0.2)',
                    },
                    title: {
                        display: false,
                        text: 'Kliknięcia',
                        color: 'rgba(0, 69, 82, 1)',
                        font: {
                            size: 18,
                            weight: 'regular'
                        }
                    },
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
                    intersect: false
                }
            }
        }
    });

    var earningsCtx = document.getElementById('earningsChart').getContext('2d');
    var earningsChart = new Chart(earningsCtx, {
        type: 'bar',
        data: {
            labels: 'modelTimeLabels',
            datasets: [{
                label: 'Zarobiona prowizja',
                data: 'managerEarnings',
                backgroundColor: 'rgba(40, 160, 151, 0.51)',
                tension: 0.1,
                fill: 'origin',
                minBarLength: 3,
                borderWidth: 2,
                borderRadius: 4,
                fill: true,
                borderColor: 'rgba(40, 160, 151, 1)',
            }]
        },
        options: {
            maintainAspectRatio: true,
            scales: {
                x: {
                    grid: {
                        color: 'rgba(40, 40, 40, 0)',
                    },

                    title: {
                        display: false,
                        text: 'Data',
                        color: 'rgba(0, 0, 0, 0.2)',
                        font: {
                            size: 18,
                            weight: 'regular'
                        }
                    },
                },
                y: {
                    beginAtZero: true,
                    suggestedMax: 100,
                    ticks: {
                        callback: function (value, index, values) {
                            return value.toFixed(2) + " zł";
                        }
                    },
                    grid: {
                        color: 'rgba(40, 40, 40, 0.2)',
                    },
                    title: {
                        display: false,
                        text: 'Zarobki',
                        color: 'rgba(0, 69, 82, 1)',
                        font: {
                            size: 18,
                            weight: 'regular'
                        }
                    },
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
                            var value = context.parsed.y;
                            return 'Zarobki: ' + parseFloat(value).toFixed(2) + ' zł';
                        }
                    }
                }
            }
        }
    });

    var ordersCtx = document.getElementById('ordersChart').getContext('2d');
    var ordersChart = new Chart(ordersCtx, {
        type: 'bar',
        data: {
            labels: 'modelTimeLabels',
            datasets: [{
                label: 'Zamówienia',
                data: 'managerOrders',
                backgroundColor: 'rgba(6, 99, 108, 0.51)',
                tension: 0.1,
                fill: 'origin',
                minBarLength: 3,
                borderWidth: 2,
                borderRadius: 4,
                fill: true,
                borderColor: 'rgba(6, 99, 108, 1)',
            }]
        },
        options: {
            maintainAspectRatio: true,
            scales: {
                x: {
                    grid: {
                        color: 'rgba(40, 40, 40, 0)',
                    },

                    title: {
                        display: false,
                        text: 'Data',
                        color: 'rgba(0, 0, 0, 0.2)',
                        font: {
                            size: 18,
                            weight: 'regular'
                        }
                    },
                },
                y: {
                    beginAtZero: true,
                    suggestedMax: 10,
                    grid: {
                        color: 'rgba(40, 40, 40, 0.2)',
                    },
                    title: {
                        display: false,
                        text: 'Zamówienia',
                        color: 'rgba(0, 69, 82, 1)',
                        font: {
                            size: 18,
                            weight: 'regular'
                        }
                    },
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
                    intersect: false
                }
            }
        }
    });

    var salesCtx = document.getElementById('salesChart').getContext('2d');
    var salesChart = new Chart(salesCtx, {
        type: 'bar',
        data: {
            labels: 'modelTimeLabels',
            datasets: [{
                label: 'Wartość zamówień',
                data: 'managerSales',
                backgroundColor: 'rgba(14, 126, 135, 0.51)',
                tension: 0.1,
                fill: 'origin',
                minBarLength: 3,
                borderWidth: 2,
                borderRadius: 4,
                fill: true,
                borderColor: 'rgba(14, 126, 135, 1)',
            }]
        },
        options: {
            maintainAspectRatio: true,
            scales: {
                x: {
                    grid: {
                        color: 'rgba(40, 40, 40, 0)',
                    },

                    title: {
                        display: false,
                        text: 'Data',
                        color: 'rgba(0, 0, 0, 0.2)',
                        font: {
                            size: 18,
                            weight: 'regular'
                        }
                    },
                },
                y: {
                    beginAtZero: true,
                    suggestedMax: 1000,
                    ticks: {
                        callback: function (value, index, values) {
                            return value.toFixed(2) + " zł";
                        }
                    },
                    grid: {
                        color: 'rgba(40, 40, 40, 0.2)',
                    },
                    title: {
                        display: false,
                        text: 'Zamówienia',
                        color: 'rgba(0, 69, 82, 1)',
                        font: {
                            size: 18,
                            weight: 'regular'
                        }
                    },
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
                            var value = context.parsed.y;
                            return 'Wartość zamówień: ' + parseFloat(value).toFixed(2) + ' zł';
                        }
                    }
                }
            }
        }
    });

    $('#showClicksChart').click(showClicksChart);
    $('#showEarningsChart').click(showEarningsChart);
    $('#showOrdersChart').click(showOrdersChart);
    $('#showSalesChart').click(showSalesChart);
    $('#showcategoryClicksChart').click(showcategoryClicksChart);
    $('#showcategoryEarningsChart').click(showcategoryEarningsChart);
    $('#showcategoryOrdersChart').click(showcategoryOrdersChart);

    var categoryClicksCtx = document.getElementById('categoryClicksChart').getContext('2d');
    var categoryClicksChart = new Chart(categoryClicksCtx, {
        type: 'doughnut',
        data: {
            labels: [],
            datasets: [{
                data: [],
                backgroundColor: [
                    'rgba(68, 240, 240, 1)',
                    'rgba(65, 199, 199, 1)',
                    'rgba(72, 151, 151, 1)',
                    'rgba(68, 182, 154, 1)',
                    'rgba(76, 131, 117, 1)',
                    'rgba(71, 117, 117, 1)',
                    'rgba(80, 110, 128, 1)',
                    'rgba(72, 87, 125, 1)'
                ],
                borderColor: [
                    'rgba(68, 240, 240, 1)',
                    'rgba(65, 199, 199, 1)',
                    'rgba(72, 151, 151, 1)',
                    'rgba(68, 182, 154, 1)',
                    'rgba(76, 131, 117, 1)',
                    'rgba(71, 117, 117, 1)',
                    'rgba(80, 110, 128, 1)',
                    'rgba(72, 87, 125, 1)'
                ],
                borderWidth: 1
            }]
        },
        options: {
            aspectRatio: 1.3,

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
                            return 'Kliki: ' + (value);
                        }
                    }
                }
            }
        }
    });
    var categoryEarningsCtx = document.getElementById('categoryEarningsChart').getContext('2d');
    var categoryEarningsChart = new Chart(categoryEarningsCtx, {
        type: 'doughnut',
        data: {
            labels: [],
            datasets: [{
                data: [],
                backgroundColor: [
                    'rgba(255, 230, 141, 1)',
                    'rgba(255, 201, 120, 1)',
                    'rgba(255, 188, 87, 1)',
                    'rgba(255, 158, 87, 1)',
                    'rgba(247, 128, 95, 1)',
                    'rgba(243, 98, 118, 1)',
                    'rgba(232, 84, 104, 1)',
                    'rgba(233, 88, 149, 1)',
                ],
                borderColor: [
                    'rgba(255, 230, 141, 1)',
                    'rgba(255, 201, 120, 1)',
                    'rgba(255, 188, 87, 1)',
                    'rgba(255, 158, 87, 1)',
                    'rgba(247, 128, 95, 1)',
                    'rgba(243, 98, 118, 1)',
                    'rgba(232, 84, 104, 1)',
                    'rgba(233, 88, 149, 1)',
                ],
                borderWidth: 1
            }]
        },
        options: {
            aspectRatio: 1.3,
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
                    padding: 10,
                    callbacks: {
                        label: function (context) {
                            var value = context.parsed;
                            return 'Sprzedaż: ' + parseFloat(value).toFixed(2) + ' zł';
                        }
                    }
                }
            }
        }
    });
    var categoryOrdersCtx = document.getElementById('categoryOrdersChart').getContext('2d');
    var categoryOrdersChart = new Chart(categoryOrdersCtx, {
        type: 'doughnut',
        data: {
            labels: [],
            datasets: [{
                data: [],
                backgroundColor: [
                    'rgba(135, 206, 235, 1)',
                    'rgba(100, 149, 237, 1)',
                    'rgba(65, 105, 225, 1)',
                    'rgba(30, 144, 255, 1)',
                    'rgba(0, 191, 255, 1)',
                    'rgba(173, 216, 230, 1)',
                    'rgba(70, 130, 180, 1)',
                    'rgba(176, 224, 230, 1)'
                ],
                borderColor: [
                    'rgba(135, 206, 235, 1)',
                    'rgba(100, 149, 237, 1)',
                    'rgba(65, 105, 225, 1)',
                    'rgba(30, 144, 255, 1)',
                    'rgba(0, 191, 255, 1)',
                    'rgba(173, 216, 230, 1)',
                    'rgba(70, 130, 180, 1)',
                    'rgba(176, 224, 230, 1)'
                ],
                borderWidth: 1
            }]
        },
        options: {
            aspectRatio: 1.3,
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
                            return 'Zamówienia: ' + (value);
                        }
                    }
                }
            }
        }
    });

  

    function updateClicksChart(modelTimeLabels, managerClicks) {
        clicksChart.data.labels = modelTimeLabels;
        clicksChart.data.datasets[0].data = managerClicks;
        clicksChart.update();
    }

    function updateEarningsChart(modelTimeLabels, managerEarnings) {
        earningsChart.data.labels = modelTimeLabels;
        earningsChart.data.datasets[0].data = managerEarnings;
        earningsChart.update();
    }

    function updateOrdersChart(modelTimeLabels, managerOrders) {
        ordersChart.data.labels = modelTimeLabels;
        ordersChart.data.datasets[0].data = managerOrders;
        ordersChart.update();
    }

    function updateSalesChart(modelTimeLabels, managerSales) {
        salesChart.data.labels = modelTimeLabels;
        salesChart.data.datasets[0].data = managerSales;
        salesChart.update();
    }

    function updateCategoryTable(categoryClicks) {
        var categoryTableBody = $("#categoryTableBody");
        categoryTableBody.empty();

        $.each(categoryClicks, function (index, item) {
            categoryTableBody.append(
                "<tr><td>" + item.categoryName + "</td><td>" + formatNumber(parseFloat(item.categoryClickCount).toLocaleString()) + "</td></tr>"
            );
        });
    }
    function updateEarningsTable(categoryEarnings) {
        var earningsTableBody = $("#earningsTableBody");
        earningsTableBody.empty();

        $.each(categoryEarnings, function (index, item) {
            earningsTableBody.append(
                "<tr><td>" + item.category + "</td><td>" + formatNumber(parseFloat(item.earnings).toFixed(2)) + " zł" + "</td></tr>"

            );
        });
    }
    function updateOrdersTable(categoryOrders) {
        var ordersTableBody = $("#ordersTableBody");
        ordersTableBody.empty();

        $.each(categoryOrders, function (index, item) {
            ordersTableBody.append(
                "<tr><td>" + item.category + "</td><td>" + item.orders + "</td></tr>"

            );
        });
    }
    function updatecategoryClicksChart(categoryClicks) {
        var labels = [];
        var data = [];

        $.each(categoryClicks, function (index, item) {
            labels.push(item.categoryName);
            data.push(item.categoryClickCount);
        });

        categoryClicksChart.data.labels = labels;
        categoryClicksChart.data.datasets[0].data = data;
        categoryClicksChart.update();
    }
    function updatecategoryEarningsChart(categoryEarnings) {
        var labels = [];
        var data = [];

        $.each(categoryEarnings, function (index, item) {
            labels.push(item.category);
            data.push(item.earnings);
        });

        categoryEarningsChart.data.labels = labels;
        categoryEarningsChart.data.datasets[0].data = data;
        categoryEarningsChart.update();
    }
    function updatecategoryOrdersChart(categoryOrders) {
        var labels = [];
        var data = [];

        $.each(categoryOrders, function (index, item) {
            labels.push(item.category);
            data.push(item.orders);
        });

        categoryOrdersChart.data.labels = labels;
        categoryOrdersChart.data.datasets[0].data = data;
        categoryOrdersChart.update();
    }

    function setDateRange(start, end) {
        $('input[name="StartDate"]').val(moment(start).format('YYYY-MM-DD'));
        $('input[name="EndDate"]').val(moment(end).format('YYYY-MM-DD'));
        $("#dateRangeForm").submit();
    }
    $(".dateShortcut").click(function () {
        switch ($(this).data('range')) {
            case "today":
                setDateRange(moment(), moment());
                break;
            case "yesterday":
                setDateRange(moment().subtract(1, 'days'), moment().subtract(1, 'days'));
                break;
            case "this-week":
                setDateRange(moment().startOf('isoWeek'), moment().endOf('isoWeek'));
                break;
            case "last-week":
                setDateRange(moment().subtract(1, 'week').startOf('isoWeek'), moment().subtract(1, 'week').endOf('isoWeek'));
                break;
            case "this-month":
                setDateRange(moment().startOf('month'), moment().endOf('month'));
                break;
        }
    });

    function onDateChange() {
        var startDate = $('input[name="StartDate"]').val();
        var endDate = $('input[name="EndDate"]').val();

        $('.dateShortcut').removeClass('selected');

        if (startDate && endDate) {
            $("#dateRangeForm").submit();
        }
    }

    $('input[name="StartDate"], input[name="EndDate"]').change(onDateChange);

    $("#dateRangeForm").submit(function (event) {
        event.preventDefault();

        var formData = $(this).serialize();

        $.ajax({
            type: "POST",
            url: "/ManagerPanel/Index",
            data: formData,
            dataType: "json",
            success: function (response) {
                updateClicksChart(response.modelTimeLabels, response.managerClicks);
                let formattedEarnings = response.managerEarnings.map(value => parseFloat(value).toFixed(2));
                updateEarningsChart(response.modelTimeLabels, formattedEarnings);
                updateOrdersChart(response.modelTimeLabels, response.managerOrders);
                let formattedSales = response.managerSales.map(value => parseFloat(value).toFixed(2));
                updateSalesChart(response.modelTimeLabels, response.managerSales);
                updateCategoryTable(response.categoryClicks);
                updatecategoryClicksChart(response.categoryClicks);
                updateEarningsTable(response.categoryEarnings)
                updatecategoryEarningsChart(response.categoryEarnings);
                updateOrdersTable(response.categoryOrders)
                updatecategoryOrdersChart(response.categoryOrders);
                $("#totalClicks").text(formatNumber(response.totalClicksDuringPeriod));
                $("#totalEarnings").text(formatNumber(parseFloat(response.totalEarningsDuringPeriod).toFixed(2)) + " zł");
                $("#totalSales").text(formatNumber(parseFloat(response.totalSalesDuringPeriod).toFixed(2)) + " zł");
                $("#totalOrders").text(formatNumber(response.totalOrdersDuringPeriod));
                $("#totalLinks").text(formatNumber(response.totalLinksDuringPeriod));
                updateCostToSalesRatio(parseFloat(response.totalEarningsDuringPeriod), parseFloat(response.totalSalesDuringPeriod));
                updateOrdersToClicksRatio(response.totalOrdersDuringPeriod, response.totalClicksDuringPeriod);
                updateAvrSaleValue(response.totalOrdersDuringPeriod, response.totalSalesDuringPeriod);
           
            },
            error: function (error) {
                console.error("Błąd podczas aktualizacji danych", error);
            }
        });
    });
    setDateRange(moment().startOf('month'), moment().endOf('month'));
});

document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.agregationShortcut').forEach(button => {
        button.addEventListener('click', function () {
            document.querySelectorAll('.agregationShortcut').forEach(innerButton => {
                innerButton.classList.remove('selected');
            });

            button.classList.add('selected');
        });
    });

    document.querySelectorAll('.categoryShortcut').forEach(button => {
        button.addEventListener('click', function () {
            document.querySelectorAll('.categoryShortcut').forEach(innerButton => {
                innerButton.classList.remove('selected');
            });

            button.classList.add('selected');
        });
    });

    document.querySelectorAll('.dateShortcut').forEach(button => {
        button.addEventListener('click', function () {
            document.querySelectorAll('.dateShortcut').forEach(innerButton => {
                innerButton.classList.remove('selected');
            });

            button.classList.add('selected');
        });
    });
});

function updateCostToSalesRatio(totalEarnings, totalSales) {
    var ratio = 0;
    if (totalSales > 0) {
        ratio = (totalEarnings / totalSales) * 100;
    }
    $("#costToSalesRatio").text(ratio.toFixed(2) + ' %');
}
function updateOrdersToClicksRatio(totalOrders, totalClicks) {
    var ratio = 0;
    if (totalClicks > 0) {
        ratio = (totalOrders / totalClicks) * 100;
    }
    $("#ordersToClicksRatio").text(ratio.toFixed(2) + ' %');
}
function updateAvrSaleValue(totalOrders, totalSales) {
    var ratio = 0;
    if (totalOrders > 0) {
        ratio = (totalSales / totalOrders);
    }
    $("#avrSaleValue").text(ratio.toFixed(2) + ' zł');
}