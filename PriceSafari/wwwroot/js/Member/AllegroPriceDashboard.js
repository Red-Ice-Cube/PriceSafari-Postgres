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
    document.getElementById("progressText").innerText = `Ładowanie... ${percent}%`;
    if (percent === 100) setTimeout(hideLoadingOverlay, 800);
});

function showLoadingOverlay() {
    document.getElementById("progressBar").style.width = "0%";
    document.getElementById("progressText").innerText = "Ładowanie… 0%";
    document.getElementById("loadingOverlay").style.display = "block";
}

function hideLoadingOverlay() {
    document.getElementById("loadingOverlay").style.display = "none";
}

function mapDayFull(d) {
    return {
        "Pn": "Poniedziałek", "Wt": "Wtorek", "Śr": "Środa", "Cz": "Czwartek",
        "Pt": "Piątek", "So": "Sobota", "Nd": "Niedziela",
        "pon.": "Poniedziałek", "wt.": "Wtorek", "śr.": "Środa", "czw.": "Czwartek",
        "pt.": "Piątek", "sob.": "Sobota", "niedz.": "Niedziela"
    }[d] || d;
}

let chart;
let tooltipMeta = [];

function drawChart(dailyData) {
    const ctx = document.getElementById("priceAnaliseChart").getContext("2d");

    const sortedDays = [...dailyData].sort((a, b) => new Date(a.date) - new Date(b.date));

    const labels = sortedDays.map(d => d.date.slice(5));
    tooltipMeta = sortedDays.map(d => `${d.date} (${mapDayFull(d.dayShort)})`);
    const loweredData = sortedDays.map(d => -d.totalLowered);
    const raisedData = sortedDays.map(d => d.totalRaised);

    if (chart) {
        chart.destroy();
    }

    chart = new Chart(ctx, {
        type: "bar",
        data: {
            labels: labels,
            datasets: [
                {
                    label: "Obniżki",
                    data: loweredData,
                    backgroundColor: "rgba(0,128,0,.6)",
                    borderColor: "rgba(0,128,0,1)",
                    borderWidth: 2,
                    borderRadius: 4,
                    barPercentage: 0.6,
                    categoryPercentage: 0.8
                },
                {
                    label: "Podwyżki",
                    data: raisedData,
                    backgroundColor: "rgba(255,0,0,.6)",
                    borderColor: "rgba(255,0,0,1)",
                    borderWidth: 2,
                    borderRadius: 4,
                    barPercentage: 0.6,
                    categoryPercentage: 0.8
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: { mode: "index", intersect: false },
            scales: {
                x: {
                    stacked: true,
                    ticks: {
                        maxRotation: 45,
                        minRotation: 45,
                        font: { size: 11 }
                    }
                },
                y: {
                    stacked: true,
                    beginAtZero: true,
                    ticks: {
                        callback: v => Math.abs(v),
                        precision: 0
                    },
                    title: { display: true, text: "Liczba zmian" }
                }
            },
            plugins: {
                legend: { display: false },
                tooltip: {
                    itemSort: (a, b) => a.dataset.label === "Podwyżki" ? -1 : 1,
                    callbacks: {
                        title: c => {
                            const i = c[0].dataIndex;
                            return tooltipMeta[i] || c[0].label;
                        },
                        label: c => `${c.dataset.label}: ${Math.abs(c.parsed.y)}`
                    }
                }
            }
        }
    });
}

function buildTable(dailyData) {
    const tb = document.querySelector(".table-price tbody");
    tb.innerHTML = "";

    const daysDesc = [...dailyData].sort((a, b) => new Date(b.date) - new Date(a.date));

    const downArrow = '<span style="color: green;">&#9660;</span>';
    const upArrow = '<span style="color: red;">&#9650;</span>';
    const grayCircle = '<span style="color: gray;">&#9679;</span>';

    daysDesc.forEach((day, i) => {
        const detailId = `detail_allegro_${i}`;
        const dLow = day.dayShort.toLowerCase();
        const isWeekend = dLow.includes("s") || dLow.includes("niedz") || dLow === "nd";

        const scrapsDesc = [...day.scraps].sort((a, b) => b.time.localeCompare(a.time));
        const scrapsHtml = scrapsDesc.map(scrap => buildScrapSection(scrap)).join("");

        let loweredCell = day.totalLowered > 0 ? `${downArrow} ${day.totalLowered}` : `${grayCircle} 0`;
        let raisedCell = day.totalRaised > 0 ? `${upArrow} ${day.totalRaised}` : `${grayCircle} 0`;

        tb.insertAdjacentHTML("beforeend", `
        <tr class="parent-row" data-target="${detailId}" style="cursor: pointer;">
          <td>
            <div class="${isWeekend ? "weekend-box" : "week-box"}">${day.dayShort}</div>
            ${day.date}
          </td>
          <td class="text-start">${loweredCell}</td>
          <td class="text-start">${raisedCell}</td>
        </tr>
        <tr id="${detailId}" class="details-row">
          <td colspan="3" class="p-0" style="border: none;">
             <div class="details-content">
                ${scrapsHtml}
             </div>
          </td>
        </tr>`);
    });

    tb.querySelectorAll("tr.parent-row").forEach(tr => {
        tr.addEventListener("click", () => toggleRowSmooth(tr.dataset.target));
    });
}

function buildScrapSection(scrap) {
    const downArrow = '<span style="color: green;">&#9660;</span>';
    const upArrow = '<span style="color: red;">&#9650;</span>';
    const grayCircle = '<span style="color: gray;">&#9679;</span>';

    const loweredSummary = scrap.lowered > 0 ? `${downArrow} ${scrap.lowered}` : `${grayCircle} 0`;
    const raisedSummary = scrap.raised > 0 ? `${upArrow} ${scrap.raised}` : `${grayCircle} 0`;

    return `
    <div class="scrap-section">
        <div class="scrap-header">
            <span class="time-badge">${scrap.time}</span>
            <span style="margin-left: auto; display: flex; gap: 20px;">
                 <span>Obniżki: ${loweredSummary}</span>
                 <span>Podwyżki: ${raisedSummary}</span>
            </span>
        </div>
        <div class="row no-gutters details-inner-row">
            <div class="col-md-6" style="padding: 6px 3px 6px 12px;">
                ${buildInnerTable(scrap.loweredDetails, "green", scrap.lowered)}
            </div>
            <div class="col-md-6" style="padding: 6px 12px 6px 3px;">
                ${buildInnerTable(scrap.raisedDetails, "red", scrap.raised)}
            </div>
        </div>
    </div>
    `;
}

function buildInnerTable(details, colorType, count) {
    const isGreen = colorType === "green";
    const headerText = isGreen ? "Obniżki" : "Podwyżki";
    const headerClass = isGreen ? "text-success" : "text-danger";

    const rows = details && details.length > 0
        ? details.map(d => `
            <tr>
                <td>
                    <a href="/AllegroPriceHistory/Details?storeId=${STORE_ID}&productId=${d.productId}" target="_blank">
                        ${d.productName}
                    </a>
                </td>
                <td>${d.oldPrice.toFixed(2)} PLN</td>
                <td>${d.newPrice.toFixed(2)} PLN</td>
                <td style="color:${isGreen ? 'green' : 'red'};">
                    ${isGreen ? '' : '+'}${d.priceDifference.toFixed(2)} PLN
                </td>
            </tr>
        `).join("")
        : `<tr><td colspan="4" class="text-center text-muted py-3">Brak zmian (${headerText.toLowerCase()})</td></tr>`;

    return `
    <table class="table table-sm inner-table">
          <thead>
            <tr>
              <th>Produkt <span class="${headerClass} font-weight-normal">(${headerText}: ${count})</span></th>
              <th>Poprzednia cena</th>
              <th>Nowa cena</th>
              <th>Zmiana ceny</th>
            </tr>
          </thead>
          <tbody>
            ${rows}
          </tbody>
    </table>`;
}

let openedId = null;
function toggleRowSmooth(id) {
    if (openedId && openedId !== id) closeRow(openedId);
    if (openedId === id) { closeRow(id); openedId = null; return; }

    const row = document.getElementById(id);
    if (!row) return;

    const parentRow = document.querySelector(`tr.parent-row[data-target="${id}"]`);
    const contentBox = row.querySelector('.details-content');

    row.classList.add('open');
    parentRow.classList.add('active');
    contentBox.style.maxHeight = contentBox.scrollHeight + 'px';

    openedId = id;
}

function closeRow(id) {
    const row = document.getElementById(id);
    if (!row) return;

    const parentRow = document.querySelector(`tr.parent-row[data-target="${id}"]`);
    const contentBox = row.querySelector('.details-content');

    parentRow.classList.remove('active');
    contentBox.style.maxHeight = contentBox.scrollHeight + 'px';
    contentBox.offsetHeight;
    contentBox.style.maxHeight = '0';

    row.addEventListener('transitionend', function h(e) {
        if (e.propertyName !== 'max-height') return;
        row.classList.remove('open');
        contentBox.style.maxHeight = '';
        row.removeEventListener('transitionend', h);
    });
}

async function load(days) {
    if (!hubConnectionId) return;
    showLoadingOverlay();

    try {
        const res = await fetch(`/AllegroDashboard/GetDashboardData?storeId=${STORE_ID}&days=${days}&connectionId=${hubConnectionId}`);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const data = await res.json();

        drawChart(data);
        buildTable(data);
    } catch (e) {
        console.error("Błąd:", e);
        alert("Wystąpił błąd podczas pobierania danych.");
    } finally {
        setTimeout(hideLoadingOverlay, 200);
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