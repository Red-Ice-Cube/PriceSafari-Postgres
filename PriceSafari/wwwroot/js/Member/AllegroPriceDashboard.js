const hub = new signalR.HubConnectionBuilder()
    .withUrl("/dashboardProgressHub")
    .build();

let hubConnectionId = null;

hub.start()
    .then(() => hub.invoke("GetConnectionId"))
    .then(id => { hubConnectionId = id; load(7); })
    .catch(err => console.error(err));

hub.on("ReceiveProgress", (_msg, percent) => {
    document.getElementById("progressBar").style.width = percent + "%";
    document.getElementById("progressText").innerText = `Analiza... ${percent}%`;
    if (percent === 100) setTimeout(hideLoadingOverlay, 500);
});

function showLoadingOverlay() {
    document.getElementById("loadingOverlay").style.display = "block";
    document.getElementById("progressBar").style.width = "0%";
}
function hideLoadingOverlay() {
    document.getElementById("loadingOverlay").style.display = "none";
}

let chart;

function drawChart(dailyData) {
    const ctx = document.getElementById("priceAnaliseChart").getContext("2d");

    let allScrapsFlat = [];
    dailyData.forEach(day => {
        day.scraps.forEach(scrap => {

            const dayPart = day.date.slice(5).replace('-', '.');
            const label = `${day.dayShort} ${dayPart} ${scrap.time}`;

            allScrapsFlat.push({
                label: label,
                lowered: -scrap.lowered,
                raised: scrap.raised,
                rawDate: scrap.fullDate
            });
        });
    });

    const labels = allScrapsFlat.map(s => s.label);
    const loweredData = allScrapsFlat.map(s => s.lowered);
    const raisedData = allScrapsFlat.map(s => s.raised);

    if (chart) chart.destroy();

    chart = new Chart(ctx, {
        type: "bar",
        data: {
            labels: labels,
            datasets: [
                {
                    label: "Obniżki",
                    data: loweredData,
                    backgroundColor: "#00a65a",
                    barPercentage: 0.6,
                    categoryPercentage: 0.8
                },
                {
                    label: "Podwyżki",
                    data: raisedData,
                    backgroundColor: "#dd4b39",
                    barPercentage: 0.6,
                    categoryPercentage: 0.8
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            scales: {
                x: {
                    ticks: {
                        maxRotation: 45,
                        minRotation: 45,
                        font: { size: 11 }
                    },
                    grid: { display: false }
                },
                y: {
                    beginAtZero: true,
                    ticks: {
                        callback: v => Math.abs(v),
                        precision: 0
                    }
                }
            },
            plugins: {
                legend: { display: true, position: 'top' },
                tooltip: {
                    callbacks: {
                        label: c => `${c.dataset.label}: ${Math.abs(c.raw)}`
                    }
                }
            }
        }
    });
}

function buildTable(dailyData) {
    const tb = document.querySelector(".table-price tbody");
    tb.innerHTML = "";

    const daysDesc = [...dailyData].reverse();

    daysDesc.forEach((day, dayIdx) => {
        const dayRowId = `day_${dayIdx}`;

        const dayRowHtml = `
            <tr class="day-row" data-target="${dayRowId}" style="cursor: pointer; background-color: #f9f9f9; border-bottom: 2px solid #eee;">
                <td>
                   <div style="display:flex; align-items:center; gap:10px;">
                     <span class="week-box" style="width:auto; padding: 4px 8px;">${day.dayShort}</span>
                     <span style="font-weight:500;">${day.date}</span>
                   </div>
                </td>
                <td class="text-start">
                    ${day.totalLowered > 0 ? `<span style="color:#00a65a; font-weight:bold;">&#9660; ${day.totalLowered}</span>` : `<span style="color:#ccc;">0</span>`}
                </td>
                <td class="text-start">
                    ${day.totalRaised > 0 ? `<span style="color:#dd4b39; font-weight:bold;">&#9650; ${day.totalRaised}</span>` : `<span style="color:#ccc;">0</span>`}
                </td>
            </tr>
        `;
        tb.insertAdjacentHTML("beforeend", dayRowHtml);

        let scrapsHtml = '';

        [...day.scraps].reverse().forEach((scrap, scrapIdx) => {
            scrapsHtml += buildScrapHtml(scrap, dayRowId, scrapIdx);
        });

        const dayDetailsRow = `
            <tr id="${dayRowId}" class="details-row day-details" style="display:none;">
                <td colspan="3" style="padding: 0; border-top: none;">
                    <div class="day-details-content" style="background: #fff; padding-left: 20px;">
                        ${scrapsHtml}
                    </div>
                </td>
            </tr>
        `;
        tb.insertAdjacentHTML("beforeend", dayDetailsRow);
    });

    tb.querySelectorAll("tr.day-row").forEach(tr => {
        tr.addEventListener("click", () => {
            const targetId = tr.dataset.target;
            const detailsRow = document.getElementById(targetId);
            const isHidden = detailsRow.style.display === "none";

            detailsRow.style.display = isHidden ? "table-row" : "none";
            tr.style.backgroundColor = isHidden ? "#eef" : "#f9f9f9";
        });
    });

    tb.querySelectorAll(".scrap-header").forEach(header => {
        header.addEventListener("click", (e) => {

            const details = header.nextElementSibling;
            if (details && details.classList.contains("scrap-details")) {
                const isHidden = details.style.display === "none";
                details.style.display = isHidden ? "block" : "none";
                header.querySelector(".toggle-icon").innerText = isHidden ? "−" : "+";
            }
            e.stopPropagation();
        });
    });
}

function buildScrapHtml(scrap, dayRowId, scrapIdx) {
    const loweredTable = buildChangesTable(scrap.loweredDetails, "green", "Obniżki");
    const raisedTable = buildChangesTable(scrap.raisedDetails, "red", "Podwyżki");

    return `
        <div class="scrap-container" style="border-left: 4px solid #ddd; margin: 10px 0;">
            <div class="scrap-header" style="padding: 10px; background: #f4f4f4; cursor: pointer; display: flex; align-items: center;">
                <span class="toggle-icon" style="font-family: monospace; font-size: 16px; margin-right: 10px; width: 20px; text-align: center;">+</span>
                <strong style="margin-right: 15px;">Godzina: ${scrap.time}</strong>
                <span style="color: ${scrap.lowered > 0 ? '#00a65a' : '#ccc'}; margin-right: 15px;">
                   &#9660; ${scrap.lowered}
                </span>
                <span style="color: ${scrap.raised > 0 ? '#dd4b39' : '#ccc'};">
                   &#9650; ${scrap.raised}
                </span>
            </div>

            <div class="scrap-details" style="display: none; padding: 10px;">
                <div style="display: flex; gap: 20px; flex-wrap: wrap;">
                    <div style="flex: 1; min-width: 300px;">${loweredTable}</div>
                    <div style="flex: 1; min-width: 300px;">${raisedTable}</div>
                </div>
            </div>
        </div>
    `;
}

function buildChangesTable(details, color, title) {
    if (!details || details.length === 0) {
        return `<div style="padding: 10px; color: #999; font-style: italic;">Brak zmian (${title.toLowerCase()})</div>`;
    }

    const rows = details.map(d => `
        <tr>
            <td style="vertical-align: middle;">
                <a href="/AllegroPriceHistory/Details?storeId=${STORE_ID}&productId=${d.productId}" target="_blank" style="text-decoration:none; color:#333; font-weight:500;">
                    ${d.productName}
                </a>
            </td>
            <td style="text-align:right;">${d.oldPrice.toFixed(2)} zł</td>
            <td style="text-align:right;">${d.newPrice.toFixed(2)} zł</td>
            <td style="text-align:right; color:${color === 'green' ? '#00a65a' : '#dd4b39'}; font-weight:bold;">
                ${d.priceDifference > 0 ? '+' : ''}${d.priceDifference.toFixed(2)} zł
            </td>
        </tr>
    `).join("");

    return `
        <h6 style="color: ${color === 'green' ? '#00a65a' : '#dd4b39'}; margin-bottom: 5px;">${title} (${details.length})</h6>
        <table class="table table-sm" style="font-size: 13px; background: #fff;">
            <thead>
                <tr style="background: #eee;">
                    <th>Produkt</th>
                    <th style="text-align:right;">Było</th>
                    <th style="text-align:right;">Jest</th>
                    <th style="text-align:right;">Różnica</th>
                </tr>
            </thead>
            <tbody>${rows}</tbody>
        </table>
    `;
}

async function load(days) {
    if (!hubConnectionId) return;
    showLoadingOverlay();

    try {
        const res = await fetch(`/AllegroDashboard/GetDashboardData?storeId=${STORE_ID}&days=${days}&connectionId=${hubConnectionId}`);
        if (!res.ok) throw new Error("Błąd pobierania danych");
        const data = await res.json();

        drawChart(data);
        buildTable(data);
    } catch (e) {
        console.error(e);
        document.getElementById("progressText").innerText = "Wystąpił błąd!";
    } finally {

    }
}

document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll(".dateShortcut").forEach(btn => {
        btn.addEventListener("click", () => {
            document.querySelectorAll(".dateShortcut").forEach(b => b.classList.remove("active"));
            btn.classList.add("active");
            load(+btn.dataset.count);
        });
    });
});