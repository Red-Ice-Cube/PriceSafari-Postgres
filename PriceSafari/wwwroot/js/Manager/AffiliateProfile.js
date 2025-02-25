//$(document).ready(function () {
//    $('#productSearch').on('keyup', function () {
//        var searchText = $(this).val().toLowerCase();

//        $('.table-orders tbody tr').each(function () {
//            var productName = $(this).find('td:nth-child(1)').text().toLowerCase();
//            $(this).toggle(productName.indexOf(searchText) > -1);
//        });
//    });
//});

//$(document).ready(function () {
//    $('#orderSearch').on('keyup', function () {
//        var searchText = $(this).val().toLowerCase();

//        $('.table-orders tbody tr').each(function () {
//            var orderNumber = $(this).find('td:nth-child(2)').text().toLowerCase();
//            $(this).toggle(orderNumber.indexOf(searchText) > -1);
//        });
//    });
//});

//$(document).ready(function () {
//    $('#showAccepted, #showInValidation').on('change', function () {
//        if (this.id == 'showAccepted' && this.checked) {
//            $('#showInValidation').prop('checked', false);
//        } else if (this.id == 'showInValidation' && this.checked) {
//            $('#showAccepted').prop('checked', false);
//        }
//        filterRows();
//    });

//    function filterRows() {
//        var showAccepted = $('#showAccepted').is(':checked');
//        var showInValidation = $('#showInValidation').is(':checked');

//        $('.table-orders tbody tr').each(function () {
//            var isAccepted = $(this).find('.Validation').hasClass('Validation-accepted');
//            var isInValidation = !isAccepted;

//            if (showAccepted || showInValidation) {
//                $(this).toggle((isAccepted && showAccepted) || (isInValidation && showInValidation));
//            } else {
//                $(this).show();
//            }
//        });
//    }

//    $('.sortable').on('click', function () {
//        var table = $(this).parents('table').eq(0);
//        var index = $(this).index();
//        var rows = table.find('tr:gt(0)').toArray().sort(comparer(index));
//        this.asc = !this.asc;

//        if (!this.asc) { rows = rows.reverse(); }
//        for (var i = 0; i < rows.length; i++) { table.append(rows[i]); }

//        $('.sortable .sort-icon').not($(this).find('.sort-icon')).removeClass('asc').html('&#9660;');
//        $(this).find('.sort-icon').toggleClass('asc', this.asc).html(this.asc ? '&#9650;' : '&#9660;');
//    });

//    function comparer(index) {
//        return function (a, b) {
//            var valA = getCellValue(a, index), valB = getCellValue(b, index);

//            if (index == 4 || index == 5) {
//                valA = parseFloat(valA.replace(' zł', '')) || valA;
//                valB = parseFloat(valB.replace(' zł', '')) || valB;
//            } else if (index == 6) {
//                valA = new Date(valA.replace(' ', 'T')).getTime();
//                valB = new Date(valB.replace(' ', 'T')).getTime();
//            } else if (index == 7) {
//                return valA === valB ? 0 : valA ? -1 : 1;
//            }

//            return $.isNumeric(valA) && $.isNumeric(valB) ? valA - valB : valA.toString().localeCompare(valB.toString());
//        };
//    }

//    function getCellValue(row, index) {
//        return $(row).children('td').eq(index).text();
//    }
//});

//function showClicksChart() {
//    $('#earningsChart').hide();
//    $('#ordersChart').hide();
//    $('#salesChart').hide();
//    $('#clicksChart').show();
//}

//function showEarningsChart() {
//    $('#clicksChart').hide();
//    $('#ordersChart').hide();
//    $('#salesChart').hide();
//    $('#earningsChart').show();
//}

//function showOrdersChart() {
//    $('#clicksChart').hide();
//    $('#earningsChart').hide();
//    $('#salesChart').hide();
//    $('#ordersChart').show();
//}

//function showSalesChart() {
//    $('#clicksChart').hide();
//    $('#ordersChart').hide();
//    $('#earningsChart').hide();
//    $('#salesChart').show();
//}

//function showcategoryClicksChart() {
//    $('#categoryEarningsChart').hide();
//    $('#tableCategoryEarningsChart').hide();
//    $('#categoryClicksChart').show();
//    $('#tableCategoryClicksChart').show();
//}

//function showcategoryEarningsChart() {
//    $('#categoryClicksChart').hide();
//    $('#tableCategoryClicksChart').hide();
//    $('#categoryEarningsChart').show();
//    $('#tableCategoryEarningsChart').show();
//}

//$(document).ready(function () {
//    var clicksCtx = document.getElementById('clicksChart').getContext('2d');
//    var clicksChart = new Chart(clicksCtx, {
//        type: 'bar',
//        data: {
//            labels: 'modelTimeLabels',
//            datasets: [{
//                label: 'Wejść na strone',
//                data: 'clicks',
//                backgroundColor: 'rgba(0, 69, 82, 0.7)',
//                tension: 0.1,
//                fill: 'origin',
//                minBarLength: 3,
//                borderWidth: 2,
//                borderRadius: 4,
//                fill: true,
//                borderColor: 'rgba(0, 69, 82, 1)',
//            }]
//        },
//        options: {
//            maintainAspectRatio: false,
//            scales: {
//                x: {
//                    grid: {
//                        color: 'rgba(40, 40, 40, 0)',
//                    },

//                    title: {
//                        display: false,
//                        text: 'Data',
//                        color: 'rgba(0, 0, 0, 0.2)',
//                        font: {
//                            size: 18,
//                            weight: 'regular'
//                        }
//                    },
//                },
//                y: {
//                    beginAtZero: true,
//                    suggestedMax: 50,
//                    grid: {
//                        color: 'rgba(40, 40, 40, 0.2)',
//                    },
//                    title: {
//                        display: false,
//                        text: 'Kliknięcia',
//                        color: 'rgba(0, 69, 82, 1)',
//                        font: {
//                            size: 18,
//                            weight: 'regular'
//                        }
//                    },
//                }
//            },
//            elements: {
//                point: {
//                    radius: 0,
//                    hoverRadius: 6,
//                    hoverBorderWidth: 2
//                }
//            },
//            plugins: {
//                legend: {
//                    display: false,
//                    labels: {
//                        color: 'rgba(0, 69, 82, 1)',
//                        font: {
//                            size: 15
//                        }
//                    }
//                },
//                tooltip: {
//                    mode: 'index',
//                    intersect: false
//                }
//            }
//        }
//    });

//    var earningsCtx = document.getElementById('earningsChart').getContext('2d');
//    var earningsChart = new Chart(earningsCtx, {
//        type: 'bar',
//        data: {
//            labels: 'modelTimeLabels',
//            datasets: [{
//                label: 'Prowizja Partnera',
//                data: 'earnings',
//                backgroundColor: 'rgba(40, 160, 151, 0.8)',
//                tension: 0.1,
//                fill: 'origin',
//                minBarLength: 3,
//                borderWidth: 2,
//                borderRadius: 4,
//                fill: true,
//                borderColor: 'rgba(40, 160, 151, 1)',
//            }]
//        },
//        options: {
//            maintainAspectRatio: false,
//            scales: {
//                x: {
//                    grid: {
//                        color: 'rgba(40, 40, 40, 0)',
//                    },

//                    title: {
//                        display: false,
//                        text: 'Data',
//                        color: 'rgba(0, 0, 0, 0.2)',
//                        font: {
//                            size: 18,
//                            weight: 'regular'
//                        }
//                    },
//                },
//                y: {
//                    beginAtZero: true,
//                    suggestedMax: 100,
//                    ticks: {
//                        callback: function (value, index, values) {
//                            return value.toFixed(2) + " zł";
//                        }
//                    },
//                    grid: {
//                        color: 'rgba(40, 40, 40, 0.2)',
//                    },
//                    title: {
//                        display: false,
//                        text: 'Zarobki',
//                        color: 'rgba(0, 69, 82, 1)',
//                        font: {
//                            size: 18,
//                            weight: 'regular'
//                        }
//                    },
//                }
//            },
//            elements: {
//                point: {
//                    radius: 0,
//                    hoverRadius: 6,
//                    hoverBorderWidth: 2
//                }
//            },
//            plugins: {
//                legend: {
//                    display: false,
//                    labels: {
//                        color: 'rgba(0, 69, 82, 1)',
//                        font: {
//                            size: 15
//                        }
//                    }
//                },
//                tooltip: {
//                    mode: 'index',
//                    intersect: false,
//                    callbacks: {
//                        label: function (context) {
//                            var value = context.parsed.y;
//                            return 'Prowizja Partnera: ' + parseFloat(value).toFixed(2) + ' zł';
//                        }
//                    }
//                }
//            }
//        }
//    });

//    var ordersCtx = document.getElementById('ordersChart').getContext('2d');
//    var ordersChart = new Chart(ordersCtx, {
//        type: 'bar',
//        data: {
//            labels: 'modelTimeLabels',
//            datasets: [{
//                label: 'Zamówienia',
//                data: 'orders',
//                backgroundColor: 'rgba(6, 99, 108, 0.8)',
//                tension: 0.1,
//                fill: 'origin',
//                minBarLength: 3,
//                borderWidth: 2,
//                borderRadius: 4,
//                fill: true,
//                borderColor: 'rgba(6, 99, 108, 1)',
//            }]
//        },
//        options: {
//            maintainAspectRatio: false,
//            scales: {
//                x: {
//                    grid: {
//                        color: 'rgba(40, 40, 40, 0)',
//                    },

//                    title: {
//                        display: false,
//                        text: 'Data',
//                        color: 'rgba(0, 0, 0, 0.2)',
//                        font: {
//                            size: 18,
//                            weight: 'regular'
//                        }
//                    },
//                },
//                y: {
//                    beginAtZero: true,
//                    suggestedMax: 10,
//                    grid: {
//                        color: 'rgba(40, 40, 40, 0.2)',
//                    },
//                    title: {
//                        display: false,
//                        text: 'Zamówienia',
//                        color: 'rgba(0, 69, 82, 1)',
//                        font: {
//                            size: 18,
//                            weight: 'regular'
//                        }
//                    },
//                }
//            },
//            elements: {
//                point: {
//                    radius: 0,
//                    hoverRadius: 6,
//                    hoverBorderWidth: 2
//                }
//            },
//            plugins: {
//                legend: {
//                    display: false,
//                    labels: {
//                        color: 'rgba(0, 69, 82, 1)',
//                        font: {
//                            size: 15
//                        }
//                    }
//                },
//                tooltip: {
//                    mode: 'index',
//                    intersect: false
//                }
//            }
//        }
//    });

//    var salesCtx = document.getElementById('salesChart').getContext('2d');
//    var salesChart = new Chart(salesCtx, {
//        type: 'bar',
//        data: {
//            labels: 'modelTimeLabels',
//            datasets: [{
//                label: 'Wartość sprzedaży',
//                data: 'sales',
//                backgroundColor: 'rgba(14, 126, 135, 0.8)',
//                tension: 0.1,
//                fill: 'origin',
//                minBarLength: 3,
//                borderWidth: 2,
//                borderRadius: 4,
//                fill: true,
//                borderColor: 'rgba(14, 126, 135, 1)',
//            }]
//        },
//        options: {
//            maintainAspectRatio: false,
//            scales: {
//                x: {
//                    grid: {
//                        color: 'rgba(40, 40, 40, 0)',
//                    },

//                    title: {
//                        display: false,
//                        text: 'Data',
//                        color: 'rgba(0, 0, 0, 0.2)',
//                        font: {
//                            size: 18,
//                            weight: 'regular'
//                        }
//                    },
//                },
//                y: {
//                    beginAtZero: true,
//                    suggestedMax: 1000,
//                    ticks: {
//                        callback: function (value, index, values) {
//                            return value.toFixed(2) + " zł";
//                        }
//                    },
//                    grid: {
//                        color: 'rgba(40, 40, 40, 0.2)',
//                    },
//                    title: {
//                        display: false,
//                        text: 'Zamówienia',
//                        color: 'rgba(0, 69, 82, 1)',
//                        font: {
//                            size: 18,
//                            weight: 'regular'
//                        }
//                    },
//                }
//            },
//            elements: {
//                point: {
//                    radius: 0,
//                    hoverRadius: 6,
//                    hoverBorderWidth: 2
//                }
//            },
//            plugins: {
//                legend: {
//                    display: false,
//                    labels: {
//                        color: 'rgba(0, 69, 82, 1)',
//                        font: {
//                            size: 15
//                        }
//                    }
//                },
//                tooltip: {
//                    mode: 'index',
//                    intersect: false,
//                    callbacks: {
//                        label: function (context) {
//                            var value = context.parsed.y;
//                            return 'Wartość sprzedaży: ' + parseFloat(value).toFixed(2) + ' zł';
//                        }
//                    }
//                }
//            }
//        }
//    });

//    $('#showClicksChart').click(showClicksChart);
//    $('#showEarningsChart').click(showEarningsChart);
//    $('#showOrdersChart').click(showOrdersChart);
//    $('#showSalesChart').click(showSalesChart);
//    $('#showcategoryClicksChart').click(showcategoryClicksChart);
//    $('#showcategoryEarningsChart').click(showcategoryEarningsChart);

//    var categoryClicksCtx = document.getElementById('categoryClicksChart').getContext('2d');
//    var categoryClicksChart = new Chart(categoryClicksCtx, {
//        type: 'doughnut',
//        data: {
//            labels: [],
//            datasets: [{
//                data: [],
//                backgroundColor: [
//                    'rgba(68, 240, 240, 1)',
//                    'rgba(65, 199, 199, 1)',
//                    'rgba(72, 151, 151, 1)',
//                    'rgba(68, 182, 154, 1)',
//                    'rgba(76, 131, 117, 1)',
//                    'rgba(71, 117, 117, 1)',
//                    'rgba(80, 110, 128, 1)',
//                    'rgba(72, 87, 125, 1)'
//                ],
//                borderColor: [
//                    'rgba(68, 240, 240, 1)',
//                    'rgba(65, 199, 199, 1)',
//                    'rgba(72, 151, 151, 1)',
//                    'rgba(68, 182, 154, 1)',
//                    'rgba(76, 131, 117, 1)',
//                    'rgba(71, 117, 117, 1)',
//                    'rgba(80, 110, 128, 1)',
//                    'rgba(72, 87, 125, 1)'
//                ],
//                borderWidth: 1
//            }]
//        },
//        options: {
//            aspectRatio: 1.3,

//            plugins: {
//                legend: {
//                    display: false,
//                    position: 'right',
//                    labels: {
//                        usePointStyle: true,
//                        padding: 16,
//                        generateLabels: function (chart) {
//                            const original = Chart.overrides.doughnut.plugins.legend.labels.generateLabels;
//                            const labels = original.call(this, chart);
//                            labels.forEach(label => {
//                                label.text = '   ' + label.text;
//                            });
//                            return labels;
//                        }
//                    }
//                },
//                tooltip: {
//                    callbacks: {
//                        label: function (context) {
//                            var value = context.parsed;
//                            return 'Kliki: ' + (value);
//                        }
//                    }
//                }
//            }
//        }
//    });
//    var categoryEarningsCtx = document.getElementById('categoryEarningsChart').getContext('2d');
//    var categoryEarningsChart = new Chart(categoryEarningsCtx, {
//        type: 'doughnut',
//        data: {
//            labels: [],
//            datasets: [{
//                data: [],
//                backgroundColor: [
//                    'rgba(255, 230, 141, 1)',
//                    'rgba(255, 201, 120, 1)',
//                    'rgba(255, 188, 87, 1)',
//                    'rgba(255, 158, 87, 1)',
//                    'rgba(247, 128, 95, 1)',
//                    'rgba(243, 98, 118, 1)',
//                    'rgba(232, 84, 104, 1)',
//                    'rgba(233, 88, 149, 1)',
//                ],
//                borderColor: [
//                    'rgba(255, 230, 141, 1)',
//                    'rgba(255, 201, 120, 1)',
//                    'rgba(255, 188, 87, 1)',
//                    'rgba(255, 158, 87, 1)',
//                    'rgba(247, 128, 95, 1)',
//                    'rgba(243, 98, 118, 1)',
//                    'rgba(232, 84, 104, 1)',
//                    'rgba(233, 88, 149, 1)',
//                ],
//                borderWidth: 1
//            }]
//        },
//        options: {
//            aspectRatio: 1.3,
//            plugins: {
//                legend: {
//                    display: false,
//                    position: 'right',
//                    labels: {
//                        usePointStyle: true,
//                        padding: 16,
//                        generateLabels: function (chart) {
//                            const original = Chart.overrides.doughnut.plugins.legend.labels.generateLabels;
//                            const labels = original.call(this, chart);
//                            labels.forEach(label => {
//                                label.text = '   ' + label.text;
//                            });
//                            return labels;
//                        }
//                    }
//                },
//                tooltip: {
//                    padding: 10,
//                    callbacks: {
//                        label: function (context) {
//                            var value = context.parsed;
//                            return 'Sprzedaż: ' + parseFloat(value).toFixed(2) + ' zł';
//                        }
//                    }
//                }
//            }
//        }
//    });
//    function updateClicksChart(modelTimeLabels, clicks) {
//        clicksChart.data.labels = modelTimeLabels;
//        clicksChart.data.datasets[0].data = clicks;
//        clicksChart.update();
//    }

//    function updateEarningsChart(modelTimeLabels, earnings) {
//        earningsChart.data.labels = modelTimeLabels;
//        earningsChart.data.datasets[0].data = earnings;
//        earningsChart.update();
//    }

//    function updateOrdersChart(modelTimeLabels, orders) {
//        ordersChart.data.labels = modelTimeLabels;
//        ordersChart.data.datasets[0].data = orders;
//        ordersChart.update();
//    }

//    function updateSalesChart(modelTimeLabels, sales) {
//        salesChart.data.labels = modelTimeLabels;
//        salesChart.data.datasets[0].data = sales;
//        salesChart.update();
//    }

//    function updateCategoryTable(categoryClicks) {
//        var categoryTableBody = $("#categoryTableBody");
//        categoryTableBody.empty();

//        $.each(categoryClicks, function (index, item) {
//            categoryTableBody.append(
//                "<tr><td>" + item.categoryName + "</td><td>" + item.categoryClickCount + "</td></tr>"
//            );
//        });
//    }
//    function updateEarningsTable(categoryEarnings) {
//        var earningsTableBody = $("#earningsTableBody");
//        earningsTableBody.empty();

//        $.each(categoryEarnings, function (index, item) {
//            earningsTableBody.append(
//                "<tr><td>" + item.category + "</td><td>" + parseFloat(item.earnings).toFixed(2) + " zł" + "</td></tr>"

//            );
//        });
//    }
//    function updatecategoryClicksChart(categoryClicks) {
//        var labels = [];
//        var data = [];

//        $.each(categoryClicks, function (index, item) {
//            labels.push(item.categoryName);
//            data.push(item.categoryClickCount);
//        });

//        categoryClicksChart.data.labels = labels;
//        categoryClicksChart.data.datasets[0].data = data;
//        categoryClicksChart.update();
//    }
//    function updatecategoryEarningsChart(categoryEarnings) {
//        var labels = [];
//        var data = [];

//        $.each(categoryEarnings, function (index, item) {
//            labels.push(item.category);
//            data.push(item.earnings);
//        });

//        categoryEarningsChart.data.labels = labels;
//        categoryEarningsChart.data.datasets[0].data = data;
//        categoryEarningsChart.update();
//    }

//    var walletChart = null;

//    function updateWalletChart(walletData) {
//        var walletCtx = document.getElementById('walletChart').getContext('2d');


//        if (walletChart && typeof walletChart.destroy === 'function') {
//            walletChart.destroy();
//        }

//        walletChart = new Chart(walletCtx, {
//            type: 'line',

//            data: {
//                labels: walletData.map(data => data.date),
//                datasets: [{
//                    label: 'Prowizja w walidacji',
//                    data: walletData.map(data => data.inValidationEarnings),
//                    backgroundColor: 'rgba(0, 255, 240, 0.4)',
//                    borderColor: 'rgba(14, 126, 135, 1)',
//                    borderWidth: 2,
//                    tension: 0.4,
//                    fill: true
//                }, {
//                    label: 'Prowizja do wypłaty',
//                    data: walletData.map(data => data.acceptedEarnings),
//                    backgroundColor: 'rgba(14, 126, 135, 1)',
//                    borderColor: 'rgba(14, 126, 135, 1)',
//                    tension: 0.4,
//                    borderWidth: 3,
//                    fill: true
//                }]
//            },
//            options: {
//                maintainAspectRatio: false,
//                scales: {
//                    x: {
//                        display: false,
//                        offset: false,
//                        grid: {
//                            color: 'rgba(40, 40, 40, 0.7)',
//                        }
//                    },
//                    y: {
//                        display: false,
//                        beginAtZero: true,
//                        grid: {
//                            color: 'rgba(255, 255, 255, 0.1)',
//                        },
//                        suggestedMax: 50,
//                        ticks: {
//                            display: false,
//                            offset: false,
//                            callback: function (value) {
//                                return value.toFixed(2) + " zł";
//                            }
//                        }
//                    }
//                },
//                elements: {
//                    point: {
//                        radius: 0,
//                        hoverRadius: 6,
//                        hoverBorderWidth: 2
//                    }
//                },
//                plugins: {
//                    legend: {
//                        display: false,
//                    },
//                    tooltip: {
//                        mode: 'index',
//                        intersect: false,
//                        callbacks: {
//                            label: function (context) {
//                                var label = context.dataset.label || '';
//                                if (label) {
//                                    label += ': ';
//                                }
//                                if (context.parsed.y !== null) {
//                                    label += parseFloat(context.parsed.y).toFixed(2) + ' zł';
//                                }
//                                return label;
//                            }
//                        }
//                    }
//                }
//            }
//        });
//    }

//    function setDateRange(start, end) {
//        $('input[name="StartDate"]').val(moment(start).format('YYYY-MM-DD'));
//        $('input[name="EndDate"]').val(moment(end).format('YYYY-MM-DD'));
//        $("#dateRangeForm").submit();
//    }
//    $(".dateShortcut").click(function () {
//        switch ($(this).data('range')) {
//            case "today":
//                setDateRange(moment(), moment());
//                break;
//            case "yesterday":
//                setDateRange(moment().subtract(1, 'days'), moment().subtract(1, 'days'));
//                break;
//            case "this-week":
//                setDateRange(moment().startOf('isoWeek'), moment().endOf('isoWeek'));
//                break;
//            case "last-week":
//                setDateRange(moment().subtract(1, 'week').startOf('isoWeek'), moment().subtract(1, 'week').endOf('isoWeek'));
//                break;
//            case "this-month":
//                setDateRange(moment().startOf('month'), moment().endOf('month'));
//                break;
//        }
//    });

//    function onDateChange() {
//        var startDate = $('input[name="StartDate"]').val();
//        var endDate = $('input[name="EndDate"]').val();

//        $('.dateShortcut').removeClass('selected');

//        if (startDate && endDate) {
//            $("#dateRangeForm").submit();
//        }
//    }

//    $('input[name="StartDate"], input[name="EndDate"]').change(onDateChange);

//    $("#dateRangeForm").submit(function (event) {
//        event.preventDefault();

//        var formData = $(this).serialize();

//        var codePAR = new URLSearchParams(window.location.search).get('codePAR');

//        formData += "&codePAR=" + codePAR;

//        $.ajax({
//            type: "POST",
//            url: "/ManagerAffiliateProfile/Profile",
//            data: formData,
//            dataType: "json",
//            success: function (response) {
//                // Aktualizuj wykres i inne elementy za pomocą odpowiedzi
//                updateClicksChart(response.modelTimeLabels, response.clicks);
//                let formattedEarnings = response.earnings.map(value => parseFloat(value).toFixed(2));
//                updateEarningsChart(response.modelTimeLabels, formattedEarnings);
//                updateOrdersChart(response.modelTimeLabels, response.orders);
//                let formattedSales = response.sales.map(value => parseFloat(value).toFixed(2));
//                updateSalesChart(response.modelTimeLabels, response.sales);
//                updateCategoryTable(response.categoryClicks);
//                updatecategoryClicksChart(response.categoryClicks);
//                updateEarningsTable(response.categoryEarnings)
//                updatecategoryEarningsChart(response.categoryEarnings);
//                $("#totalClicks").text(response.totalClicksDuringPeriod);
//                $("#totalEarnings").text(parseFloat(response.totalEarningsDuringPeriod).toFixed(2) + " zł");
//                $("#totalSales").text(parseFloat(response.totalSalesDuringPeriod).toFixed(2) + " zł");
//                $("#totalOrders").text(response.totalOrdersDuringPeriod);
//                $("#acceptedEarnings").text(parseFloat(response.acceptedEarningsDuringPeriod).toFixed(2) + " zł");
//                $("#acceptedSales").text(parseFloat(response.acceptedSalesDuringPeriod).toFixed(2) + " zł");
//                $("#acceptedOrders").text(response.acceptedOrdersDuringPeriod);
//                $("#canceledEarnings").text(parseFloat(response.canceledEarningsDuringPeriod).toFixed(2) + " zł");
//                $("#canceledSales").text(parseFloat(response.canceledSalesDuringPeriod).toFixed(2) + " zł");
//                $("#canceledOrders").text(response.canceledOrdersDuringPeriod);
//                updateEPC(parseFloat(response.totalEarningsDuringPeriod), parseFloat(response.totalClicksDuringPeriod));
//                updateCR(response.totalOrdersDuringPeriod, response.totalClicksDuringPeriod);
//                updateWalletChart(response.walletData);
//            },
//            error: function (error) {
//                console.error("Błąd podczas aktualizacji danych", error);
//            }
//        });
//    });

//    setDateRange(moment().startOf('month'), moment().endOf('month'));
//});

//document.addEventListener('DOMContentLoaded', function () {
//    document.querySelectorAll('.agregationShortcut').forEach(button => {
//        button.addEventListener('click', function () {
//            document.querySelectorAll('.agregationShortcut').forEach(innerButton => {
//                innerButton.classList.remove('selected');
//            });

//            button.classList.add('selected');
//        });
//    });

//    document.querySelectorAll('.categoryShortcut').forEach(button => {
//        button.addEventListener('click', function () {
//            document.querySelectorAll('.categoryShortcut').forEach(innerButton => {
//                innerButton.classList.remove('selected');
//            });

//            button.classList.add('selected');
//        });
//    });

//    document.querySelectorAll('.dateShortcut').forEach(button => {
//        button.addEventListener('click', function () {
//            document.querySelectorAll('.dateShortcut').forEach(innerButton => {
//                innerButton.classList.remove('selected');
//            });

//            button.classList.add('selected');
//        });
//    });
//});

//function updateEPC(totalEarnings, totalClicks) {
//    var ratio = 0;
//    if (totalEarnings > 0) {
//        ratio = (totalEarnings / totalClicks);
//    }
//    $("#EPC").text(ratio.toFixed(2) + ' zł');
//}
//function updateCR(totalOrders, totalClicks) {
//    var ratio = 0;
//    if (totalClicks > 0) {
//        ratio = (totalOrders / totalClicks) * 100;
//    }
//    $("#CR").text(ratio.toFixed(2) + ' %');
//}