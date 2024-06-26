$(document).ready(function () {
    $('#campaignSearch').on('keyup', function () {
        var searchText = $(this).val().toLowerCase();

        $('.campaign-wrapper').filter(function () {
            $(this).toggle($(this).find('.campaign-box-head-name').text().toLowerCase().indexOf(searchText) > -1);
        });
    });
});

function deleteCampaign(campaignId) {
    if (confirm('Czy na pewno chcesz usunąć tę kampanię?')) {
        fetch(`/Campaign/DeleteCampaign?campaignId=${campaignId}`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
            },
            body: JSON.stringify({ campaignId: campaignId })
        })
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    alert('Kampania została usunięta.');

                    location.reload();
                } else {
                    alert(data.message);
                }
            })
            .catch(error => {
                console.error('Wystąpił błąd:', error);
            });
    }
}

let currentCampaignId = null;

function editCampaignName(button, campaignId) {
    currentCampaignId = campaignId;
    document.getElementById('editCampaignModal').style.display = 'block';
}

function closeModal() {
    document.getElementById('editCampaignModal').style.display = 'none';
}

function saveNewCampaignName() {
    let newName = document.getElementById('newCampaignName').value;

    fetch(`/Campaign/UpdateCampaignName?campaignId=${currentCampaignId}&campaignName=${encodeURIComponent(newName)}`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
        }
    })
        .then(response => response.json())
        .then(data => {
            if (data.success) {
                alert('Nazwa kampanii została zaktualizowana.');
                location.reload();
            } else {
                alert(data.message);
            }
        })
        .catch(error => {
            console.error('Wystąpił błąd:', error);
        });

    closeModal();
}

function calculateCampaigns() {
    let campaignCount = 0;
    document.querySelectorAll('.campaign-wrapper').forEach(row => {
        campaignCount++;
    });
    const maxCampaigns = 20;
    document.getElementById('campaignCount').innerText = `${campaignCount}/${maxCampaigns}`;
}
window.onload = calculateCampaigns;

function toggleTable(element) {
    const tableContainer = element.nextElementSibling;
    const toggleText = element.querySelector('.toggle-text');
    if (tableContainer.style.display === "none") {
        tableContainer.style.display = "block";
        element.classList.add("expanded");
        element.classList.add("campaign-box-expanded");
        toggleText.textContent = 'Zwiń';
    } else {
        tableContainer.style.display = "none";
        element.classList.remove("expanded");
        element.classList.remove("campaign-box-expanded");
        toggleText.textContent = 'Rozwiń';
    }
}

let draggedItem = null;

function drag(event) {
    draggedItem = event.target.closest('.campaign-wrapper');
    setTimeout(() => {
        draggedItem.classList.add('dragged-item');
    }, 0);
}

function dragOver(event) {
    event.preventDefault();

    let closestCampaignWrapper = event.target.closest('.campaign-wrapper');
    if (draggedItem !== closestCampaignWrapper) {
        if (isBefore(draggedItem, closestCampaignWrapper)) {
            closestCampaignWrapper.before(draggedItem);
        } else {
            closestCampaignWrapper.after(draggedItem);
        }
    }
}

function isBefore(el1, el2) {
    if (el1.parentNode === el2.parentNode) {
        for (let cur = el1.previousSibling; cur; cur = cur.previousSibling) {
            if (cur === el2) return true;
        }
    }
    return false;
}

function dragLeave(event) {
    event.target.closest('.campaign-wrapper').classList.remove('over');
}

function endDrag(event) {
    draggedItem.classList.remove('dragged-item');
    let overItems = document.querySelectorAll('.over');
    overItems.forEach(item => item.classList.remove('over'));
}

function drop(event) {
    event.preventDefault();

    let targetWrapper = event.target.closest('.campaign-wrapper');
    targetWrapper.style.transform = '';
    targetWrapper.after(draggedItem);
    saveOrder();

    let overItems = document.querySelectorAll('.over');
    overItems.forEach(item => {
        item.style.transform = '';
        item.classList.remove('over');
    });
}

function allowDrop(event) {
    event.preventDefault();
}

function saveOrder() {
    const campaignOrder = Array.from(document.querySelectorAll('.campaign-wrapper')).map(wrapper => wrapper.getAttribute('data-campaign-id'));
    fetch('/Campaign/UpdateOrder', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
        },
        body: JSON.stringify(campaignOrder)
    })
        .then(response => response.json())
        .then(data => {
            if (!data.success) {
                alert('Wystąpił błąd podczas aktualizacji kolejności kampanii.');
            }
        })
        .catch(error => {
            console.error('Wystąpił błąd:', error);
        });
}

function loadOrder() {
    const storedOrder = JSON.parse(localStorage.getItem('campaignOrder'));
    if (storedOrder) {
        const campaignContainer = document.getElementById('campaign-container');
        const campaignWrappers = document.querySelectorAll('.campaign-wrapper');
        const campaignArray = Array.from(campaignWrappers);
        campaignContainer.innerHTML = '';

        storedOrder.forEach(campaignName => {
            const matchingCampaign = campaignArray.find(wrapper => wrapper.querySelector('.campaign-box span').textContent === campaignName);
            if (matchingCampaign) {
                campaignContainer.appendChild(matchingCampaign);
            }
        });

        campaignArray.forEach(campaign => {
            if (!campaignContainer.contains(campaign)) {
                campaignContainer.appendChild(campaign);
            }
        });
    }
}

document.querySelectorAll('.campaign-wrapper').forEach(wrapper => {
    wrapper.addEventListener('dragover', dragOver);
    wrapper.addEventListener('dragleave', dragLeave);
});

document.addEventListener("DOMContentLoaded", function () {
    fetch(campaignsDataUrl)
        .then(response => response.json())
        .then(data => {
            data.forEach(campaign => {
                updateCampaignRowAndChart(campaign);
            });
        });
});

function updateCampaignRowAndChart(campaign) {
    var wrapper = document.querySelector(`.campaign-wrapper[data-campaign-id='${campaign.campaignId}']`);
    if (wrapper) {
        const campaignName = wrapper.querySelector('.campaign-box-head-name');
        wrapper.querySelector('.click-data').textContent = campaign.click;
        wrapper.querySelector('.full-click-data').textContent = campaign.fullClick;
        wrapper.querySelector('.orders-data').textContent = campaign.orders;
        wrapper.querySelector('.full-orders-data').textContent = campaign.fullOrders;
        wrapper.querySelector('.earnings-data').textContent = campaign.earnings.toFixed(2) + ' zł';
        wrapper.querySelector('.full-earnings-data').textContent = campaign.fullEarnings.toFixed(2) + ' zł';
        if (campaignName) campaignName.textContent = campaign.campaignName;
        wrapper.setAttribute('data-creation-date', campaign.creationDate);
        var creationDate = new Date(campaign.creationDate);
        var formattedDate = creationDate.toLocaleDateString('pl-PL', { year: 'numeric', month: 'long', day: 'numeric' });
        wrapper.querySelector('.creationDate-data').textContent = formattedDate;

        var ordersToClicksRatio = updateOrdersToClicksRatio(campaign.orders, campaign.click);
        wrapper.querySelector('.ordersToClicksRatio').textContent = ordersToClicksRatio;
        var fullOrdersToClicksRatio = updateFullOrdersToClicksRatio(campaign.fullOrders, campaign.fullClick);
        wrapper.querySelector('.fullOrdersToClicksRatio').textContent = fullOrdersToClicksRatio;

        var labels = campaign.clicks.map(click => click.clickTime.substring(0, 10));
        var clicks = campaign.clicks.map(click => click.count);
        var sales = campaign.sales.map(sale => sale.amount);

        var ctx = wrapper.querySelector('.chart-container-cam canvas').getContext('2d');
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
                label: 'Zarobki',
                data: sales,
                barThickness: 10,
                backgroundColor: 'rgba(255, 255, 255, 0.6)',
                borderColor: 'rgba(255, 255, 255, 0.6)',
                borderWidth: 1,
                borderRadius: 2,
                yAxisID: 'yEarnings'
            }, {
                label: 'Ruch na stronę',
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
                yEarnings: {
                    display: true,
                    type: 'linear',
                    position: 'right',
                    color: 'rgba(255, 255, 255, 0.8)',
                    beginAtZero: false,
                    suggestedMax: 100,
                    grid: {
                        drawOnChartArea: false,
                    },
                    title: {
                        display: false,
                        text: 'Zarobki',
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

                            if (label === 'Zarobki') {
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

function updateOrdersToClicksRatio(orders, click) {
    var ratio = 0;
    if (click > 0) {
        ratio = (orders / click) * 100;
    }
    return ratio.toFixed(2) + ' %';
}
function updateFullOrdersToClicksRatio(fullOrders, fullClick) {
    var ratio = 0;
    if (fullClick > 0) {
        ratio = (fullOrders / fullClick) * 100;
    }
    return ratio.toFixed(2) + ' %';
}

function copyToClipboard(textToCopy) {
    var textArea = document.createElement("textarea");
    textArea.value = textToCopy;
    textArea.style.position = 'fixed';
    textArea.style.left = '0';
    textArea.style.top = '0';
    textArea.style.opacity = '0';
    document.body.appendChild(textArea);
    textArea.focus();
    textArea.select();

    try {
        var successful = document.execCommand('copy');
        var msg = successful ? 'Skopiowano: ' + textToCopy : 'Nie udało się skopiować.';
        showCopyAlert(msg, successful ? 'success' : 'danger');
    } catch (err) {
        showCopyAlert('Nie udało się skopiować. ' + err, 'danger');
    }

    document.body.removeChild(textArea);
}

function showCopyAlert(message, alertType) {
    const alertPlaceholder = document.getElementById('alertContainer');

    alertPlaceholder.innerHTML = '';
    const wrapper = document.createElement('div');
    wrapper.innerHTML = [
        `<div class="alert alert-${alertType} d-flex align-items-center justify-content-center" role="alert" style="min-height: 50px; width: auto; color: #FFF; background: rgba(14, 126, 135, 0.76);  padding: 0.4rem 1.2rem;">`,
        `   <div class="text-center">${message}</div>`,
        '</div>'
    ].join('');

    alertPlaceholder.append(wrapper);

    alertPlaceholder.style.position = 'fixed';
    alertPlaceholder.style.left = '50%';
    alertPlaceholder.style.bottom = '50px';
    alertPlaceholder.style.transform = 'translateX(-40%)';
    alertPlaceholder.style.display = 'flex';
    alertPlaceholder.style.justifyContent = 'center';
    alertPlaceholder.style.alignItems = 'center';

    setTimeout(() => {
        wrapper.remove();
    }, 3000);
}









