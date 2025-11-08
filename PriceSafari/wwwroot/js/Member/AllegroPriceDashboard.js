const hub = new signalR.HubConnectionBuilder()
    .withUrl("/dashboardProgressHub")
    .build();

let hubConnectionId = null;
let currentView = 'analysis';

hub.start()
    .then(() => hub.invoke("GetConnectionId"))
    .then(id => {
        hubConnectionId = id;

        load(7);
    })
    .catch(err => console.error(err));

hub.on("ReceiveProgress", (_msg, percent) => {

    const progressBar = document.getElementById("progressBar");
    const progressText = document.getElementById("progressText");
    if (progressBar && progressText) {
        progressBar.style.width = percent + "%";
        progressText.innerText = `Ładowanie... ${percent}%`;
    }
    if (percent === 100) setTimeout(hideLoadingOverlay, 800);
});

function showLoadingOverlay() {
    const progressBar = document.getElementById("progressBar");
    const progressText = document.getElementById("progressText");
    const loadingOverlay = document.getElementById("loadingOverlay");

    if (progressBar) progressBar.style.width = "0%";
    if (progressText) progressText.innerText = "Ładowanie… 0%";
    if (loadingOverlay) loadingOverlay.style.display = "block";
}

function hideLoadingOverlay() {
    const loadingOverlay = document.getElementById("loadingOverlay");
    if (loadingOverlay) loadingOverlay.style.display = "none";
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
    const ctxElement = document.getElementById("priceAnaliseChart");
    if (!ctxElement) return;
    const ctx = ctxElement.getContext("2d");

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
    const tb = document.querySelector("#analysisTable tbody");
    if (!tb) return;
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

    if (row && parentRow && contentBox) {
        row.classList.add('open');
        parentRow.classList.add('active');
        contentBox.style.maxHeight = contentBox.scrollHeight + 'px';
        openedId = id;
    }
}

function closeRow(id) {
    const row = document.getElementById(id);
    if (!row) return;

    const parentRow = document.querySelector(`tr.parent-row[data-target="${id}"]`);
    const contentBox = row.querySelector('.details-content');

    if (parentRow && contentBox) {
        parentRow.classList.remove('active');
        contentBox.style.maxHeight = contentBox.scrollHeight + 'px';

        contentBox.offsetHeight;
        contentBox.style.maxHeight = '0';

        const transitionEndHandler = function (e) {
            if (e.propertyName !== 'max-height') return;
            row.classList.remove('open');
            contentBox.style.maxHeight = '';
            row.removeEventListener('transitionend', transitionEndHandler);
        };
        row.addEventListener('transitionend', transitionEndHandler);
    }
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
        alert("Wystąpił błąd podczas pobierania danych analizy.");
    } finally {

    }
}

async function loadHistory(days) {
    showLoadingOverlay();
    const progressText = document.getElementById("progressText");
    const progressBarContainer = document.getElementById("progressBarContainer");

    if (progressText) progressText.innerText = "Ładowanie historii zmian...";

    if (progressBarContainer) progressBarContainer.style.display = 'none';

    try {
        const res = await fetch(`/AllegroDashboard/GetPriceBridgeHistory?storeId=${STORE_ID}&days=${days}`);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const data = await res.json();
        buildHistoryTable(data);
    } catch (e) {
        console.error("Błąd ładowania historii:", e);
        alert("Wystąpił błąd podczas pobierania historii zmian.");
    } finally {
        hideLoadingOverlay();

        if (progressBarContainer) progressBarContainer.style.display = 'block';
    }
}

function buildHistoryTable(dailyData) {
    const tb = document.querySelector("#historyTable tbody");
    if (!tb) return;
    tb.innerHTML = "";

    if (!dailyData || dailyData.length === 0) {
        tb.innerHTML = '<tr><td colspan="3" class="text-center" style="padding: 20px;">Brak historii zmian w wybranym okresie.</td></tr>';
        return;
    }

    dailyData.forEach((day, i) => {
        const detailId = `detail_history_${i}`;
        const dLow = day.dayShort.toLowerCase();
        const isWeekend = dLow.includes("s") || dLow.includes("niedz") || dLow === "nd";

        const batchesHtml = day.batches.map(batch => buildBatchSection(batch)).join("");

        tb.insertAdjacentHTML("beforeend", `
        <tr class="parent-row" data-target="${detailId}" style="cursor: pointer;">
          <td>
             <div class="${isWeekend ? "weekend-box" : "week-box"}">${day.dayShort}</div>
             ${day.date}
          </td>
          <td class="text-start"><span style="color: green; font-weight:bold;">${day.totalItemsChanged}</span></td>
          <td class="text-start"><span style="color: red; font-weight:bold;">${day.totalErrors}</span></td>
        </tr>
        <tr id="${detailId}" class="details-row">
          <td colspan="3" class="p-0" style="border: none;">
             <div class="details-content">
                ${batchesHtml}
             </div>
          </td>
        </tr>`);
    });

    tb.querySelectorAll("tr.parent-row").forEach(tr => {
        tr.addEventListener("click", () => toggleRowSmooth(tr.dataset.target));
    });
}

function buildBatchSection(batch) {
    return `
    <div class="scrap-section" style="background: #f9f9f9;">
        <div class="scrap-header" style="background: #eee;">
            <span class="time-badge" style="background: #555;">${batch.executionTime}</span>
            <span style="margin-left: 10px;">Użytkownik: <strong>${batch.userName}</strong></span>
            <span style="margin-left: auto; display: flex; gap: 20px;">
                 <span style="color: green;">Sukces: ${batch.successfulCount}</span>
                 <span style="color: red;">Błędy: ${batch.failedCount}</span>
            </span>
        </div>
        <div style="padding: 10px;">
            ${buildBatchItemsTable(batch.items)}
        </div>
    </div>
    `;
}

function buildBatchItemsTable(items) {
    if (!items || items.length === 0) return '<p class="text-muted">Brak szczegółów.</p>';

    const rows = items.map(item => {
        const priceDiffStr = item.priceDiff > 0 ? `+${item.priceDiff.toFixed(2)}` : item.priceDiff.toFixed(2);
        const priceColor = item.priceDiff > 0 ? 'red' : (item.priceDiff < 0 ? 'green' : 'gray');

        const errorTitle = item.errorMessage ? item.errorMessage.replace(/"/g, '&quot;') : 'Błąd';
        const statusIcon = item.success
            ? '<span style="color:green;">✔</span>'
            : `<span style="color:red; cursor:help;" title="${errorTitle}">✖</span>`;

        return `
        <tr>
            <td>
                 <a href="/AllegroPriceHistory/Details?storeId=${STORE_ID}&productId=${item.productId}" target="_blank">
                    ${item.productName}
                 </a>
                 <br/><small style="color:#999;">${item.offerId}</small>
            </td>
            <td>${item.priceBefore.toFixed(2)} PLN</td>
            <td><strong>${item.priceAfter.toFixed(2)} PLN</strong></td>
            <td style="color: ${priceColor};">${priceDiffStr} PLN</td>
            <td class="text-center">${statusIcon}</td>
        </tr>
        `;
    }).join("");

    return `
    <table class="table table-sm inner-table" style="background: #fff;">
        <thead>
            <tr>
                <th>Produkt / Oferta ID</th>
                <th>Cena przed</th>
                <th>Cena po</th>
                <th>Zmiana</th>
                <th class="text-center">Status</th>
            </tr>
        </thead>
        <tbody>
            ${rows}
        </tbody>
    </table>
    `;
}

document.addEventListener("DOMContentLoaded", () => {
    const analysisShortcuts = document.getElementById('analysisDateShortcuts');
    const historyShortcuts = document.getElementById('historyDateShortcuts');
    const viewAnalysisBtn = document.getElementById('viewAnalysisBtn');
    const viewHistoryBtn = document.getElementById('viewHistoryBtn');
    const analysisContainer = document.getElementById('analysisViewContainer');
    const historyContainer = document.getElementById('historyViewContainer');

    if (analysisShortcuts) {
        analysisShortcuts.querySelectorAll(".dateShortcut").forEach(btn => {
            btn.addEventListener("click", () => {
                analysisShortcuts.querySelectorAll(".dateShortcut").forEach(b => b.classList.remove("active"));
                btn.classList.add("active");
                load(+btn.dataset.count);
            });
        });
    }

    if (historyShortcuts) {
        historyShortcuts.querySelectorAll(".dateShortcut").forEach(btn => {
            btn.addEventListener("click", () => {
                historyShortcuts.querySelectorAll(".dateShortcut").forEach(b => b.classList.remove("active"));
                btn.classList.add("active");
                loadHistory(+btn.dataset.count);
            });
        });
    }

    if (viewAnalysisBtn && viewHistoryBtn && analysisContainer && historyContainer) {
        viewAnalysisBtn.addEventListener('click', () => {
            if (currentView === 'analysis') return;
            currentView = 'analysis';
            viewAnalysisBtn.classList.add('active');
            viewHistoryBtn.classList.remove('active');
            analysisContainer.style.display = 'block';
            historyContainer.style.display = 'none';
            if (analysisShortcuts) analysisShortcuts.style.display = 'flex';
            if (historyShortcuts) historyShortcuts.style.display = 'none';

            const activeBtn = analysisShortcuts.querySelector(".dateShortcut.active");
            load(activeBtn ? +activeBtn.dataset.count : 7);
        });

        viewHistoryBtn.addEventListener('click', () => {
            if (currentView === 'history') return;
            currentView = 'history';
            viewHistoryBtn.classList.add('active');
            viewAnalysisBtn.classList.remove('active');
            analysisContainer.style.display = 'none';
            historyContainer.style.display = 'block';
            if (analysisShortcuts) analysisShortcuts.style.display = 'none';
            if (historyShortcuts) historyShortcuts.style.display = 'flex';

            const activeBtn = historyShortcuts.querySelector(".dateShortcut.active");

            loadHistory(activeBtn ? +activeBtn.dataset.count : 30);
        });
    }
});