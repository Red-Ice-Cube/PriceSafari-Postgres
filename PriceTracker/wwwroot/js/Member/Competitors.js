document.addEventListener('DOMContentLoaded', function () {
    const canvases = document.querySelectorAll('canvas[id^="chart-"]');
    const searchInput = document.getElementById('storeSearch');
    const competitorTableRows = document.querySelectorAll('#competitors-table tbody tr');
    const scrapableCountSpan = document.getElementById('scrapable-count');


    canvases.forEach(canvas => {
        const ctx = canvas.getContext('2d');
        const samePriceCount = parseInt(canvas.getAttribute('data-same'));
        const higherPriceCount = parseInt(canvas.getAttribute('data-higher'));
        const lowerPriceCount = parseInt(canvas.getAttribute('data-lower'));
        const totalCount = parseInt(canvas.getAttribute('data-total'));

        new Chart(ctx, {
            type: 'bar',
            data: {
                labels: ['Produkty'],
                datasets: [

                    {
                        label: 'Wyższa cena',
                        data: [higherPriceCount],
                        backgroundColor: 'rgba(0, 156, 42, 0.7)',
                        borderColor: 'rgba(255, 255, 255, 1)',
                        borderWidth: 1,
                        barThickness: 18
                    },
                    {
                        label: 'Taka sama cena',
                        data: [samePriceCount],
                        backgroundColor: 'rgba(14, 126, 135, 0.7)',
                        borderColor: 'rgba(255, 255, 255, 1)',
                        borderWidth: 1,
                        barThickness: 18
                    },
                    {
                        label: 'Niższa cena',
                        data: [lowerPriceCount],
                        backgroundColor: 'rgba(238, 17, 17, 0.7)',
                        borderColor: 'rgba(255, 255, 255, 1)',
                        borderWidth: 1,
                        barThickness: 18
                    }
                ]
            },
            options: {
                indexAxis: 'y',
                scales: {
                    x: {
                        beginAtZero: true,
                        stacked: true,
                        max: totalCount,
                        display: false,
                        grid: {
                            display: false
                        }
                    },
                    y: {
                        stacked: true,
                        display: false,
                        grid: {
                            display: false
                        }
                    }
                },
                plugins: {
                    tooltip: {
                        callbacks: {
                            label: function (context) {
                                let label = context.dataset.label || '';
                                if (label) {
                                    label += ': ';
                                }
                                if (context.raw !== null) {
                                    label += context.raw;
                                }
                                return label;
                            }
                        }
                    }
                },
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        display: false
                    }
                }
            }
        });
    });


    searchInput.addEventListener('input', function () {
        const searchTerm = searchInput.value.toLowerCase();
        let visibleCount = 0;

        competitorTableRows.forEach(row => {
            const competitorName = row.querySelector('.competitor-name').textContent.toLowerCase();

            if (competitorName.includes(searchTerm)) {
                row.style.display = '';
                visibleCount++;
            } else {
                row.style.display = 'none';
            }
        });

        scrapableCountSpan.textContent = visibleCount;
    });


    scrapableCountSpan.textContent = competitorTableRows.length;


    competitorTableRows.forEach(row => {
        row.addEventListener('click', function () {
            const url = row.getAttribute('data-url');
            if (url) {
                window.location.href = url;
            }
        });
    });
});