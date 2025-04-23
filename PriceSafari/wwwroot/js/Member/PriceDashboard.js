
const hub = new signalR.HubConnectionBuilder()
    .withUrl("/dashboardProgressHub")
    .build();

let hubConnectionId = null;

hub.start()
    .then(() => hub.invoke("GetConnectionId"))
    .then(id => { hubConnectionId = id; load(30); })   // pierwotne pierwsze ładowanie
    .catch(err => console.error(err));

/* ---- 2.1  obsługa postępu z huba ---- */
hub.on("ReceiveProgress", (_msg, percent) => {          // _msg ignorujemy
    document.getElementById("progressBar").style.width = percent + "%";
    document.getElementById("progressText").innerText = `Ładowanie… ${percent}%`;

    if (percent === 100) setTimeout(hideLoadingOverlay, 800);
});

/* ---- 2.2  pokaż nakładkę ---- */
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
        Mon: "Poniedziałek", Tue: "Wtorek", Wed: "Środa", Thu: "Czwartek",
        Fri: "Piątek", Sat: "Sobota", Sun: "Niedziela",
        "pon.": "Poniedziałek", "wt.": "Wtorek", "śr.": "Środa", "czw.": "Czwartek",
        "pt.": "Piątek", "sob.": "Sobota", "niedz.": "Niedziela"
    }[d] || d;
}


let chart;                
let tooltipDates = [];    
let tooltipDays = [];  
let openedId = null;    


function drawChart(data) {
    tooltipDates = data.map(r => r.date);
    tooltipDays = data.map(r => r.day);

    const ctx = document.getElementById("priceAnaliseChart").getContext("2d");
    const labels = data.map(r => r.date.slice(5)); 
    const lowered = data.map(r => -r.lowered);
    const raised = data.map(r => r.raised);

    if (chart) {                   
        chart.data.labels = labels;
        chart.data.datasets[0].data = lowered;
        chart.data.datasets[1].data = raised;
        chart.update();
        return;
    }

    chart = new Chart(ctx, {
        type: "bar",
        data: {
            labels,
            datasets: [
                {
                    label: "Obniżki",
                    data: lowered,
                    backgroundColor: "rgba(0,128,0,.6)",
                    borderColor: "rgba(0,128,0,1)",
                    borderWidth: 2,
                    borderRadius: 4
                },
                {
                    label: "Podwyżki",
                    data: raised,
                    backgroundColor: "rgba(255,0,0,.6)",
                    borderColor: "rgba(255,0,0,1)",
                    borderWidth: 2,
                    borderRadius: 4
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: { mode: "index", intersect: false },
            scales: {
                x: { stacked: true },
                y: {
                    stacked: true,
                    beginAtZero: true,
                    ticks: { callback: v => Math.abs(v) },
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
                            return `${tooltipDates[i]} (${mapDayFull(tooltipDays[i])})`;
                        },
                        label: c => `${c.dataset.label}: ${Math.abs(c.parsed.y)}`
                    }
                }
            }
        }
    });
}





function buildTable(rows) {
    const rowsDesc = [...rows].reverse();
    const tb = document.querySelector(".table-price tbody");
    tb.innerHTML = "";

    const fullDayNames = [
        "Nd.", "Pon.", "Wt.", "Śrd.", "Czw.", "Pt.", "Sbt."
    ];

    rowsDesc.forEach((r, i) => {
        const detailId = `detail_${i}`;
        const dateObj = new Date(r.date);
        const dayIndex = dateObj.getDay();
        const fullDay = fullDayNames[dayIndex];
        const isWeekend = (dayIndex === 0 || dayIndex === 6);

        const renderNameCell = d => {
            const url = d.productImage || "";
            return `
                <td>
                  <div style="
                           width: 64px;
                           height: 64px;
                           padding:4px;
                           border-radius:4px;
                           object-fit: cover;
                           flex-shrink: 0;
                           background: #fff;
                           margin-right: 8px;
                           border:1px solid #ddd;
                           ">
                    ${url
                    ? `<img src="${url}"
                                   style="width:100%;height:100%;object-fit:cover;"
                                   onerror="this.style.display='none';" />`
                    : ``
                }
                  </div>
                  <a href="/PriceHistory/Details?productId=${d.productId}&scrapId=${d.scrapId}"
                     target="_blank">
                    ${d.productName}
                  </a>
            </td>`;
        };

        const loweredRows = r.loweredDetails.length
            ? r.loweredDetails.map(d => `
                <tr>
                  ${renderNameCell(d)}
                  <td>${d.oldPrice.toFixed(2)} PLN</td>
                  <td>${d.newPrice.toFixed(2)} PLN</td>
                  <td style="color:green;">${d.priceDifference.toFixed(2)} PLN</td>
                </tr>
            `).join("")
            : ``;

        const loweredTable = `
        <table class="table table-sm inner-table">
              <thead>
                <tr>
                  <th>Produkt <span class="text-success font-weight-normal">(Obniżki: ${r.lowered})</span></th>
                  <th>Poprzednia cena</th>
                  <th>Nowa cena</th>
                  <th>Zmiana ceny</th>
                </tr>
              </thead>
              <tbody>
                ${loweredRows || '<tr><td colspan="4" class="text-center text-muted py-3">Brak obniżek tego dnia</td></tr>'}
              </tbody>
        </table>`;

        const raisedRows = r.raisedDetails.length
            ? r.raisedDetails.map(d => `
                <tr>
                  ${renderNameCell(d)}
                  <td>${d.oldPrice.toFixed(2)} PLN</td>
                  <td>${d.newPrice.toFixed(2)} PLN</td>
                  <td style="color:red;">+${d.priceDifference.toFixed(2)} PLN</td>
                </tr>
            `).join("")
            : ``;

        const raisedTable = `
        <table class="table table-sm inner-table">
              <thead>
                <tr>
                  <th>Produkt <span class="text-danger font-weight-normal">(Podwyżki: ${r.raised})</span></th>
                  <th>Poprzednia cena</th>
                  <th>Nowa cena</th>
                  <th>Zmiana ceny</th>
                </tr>
              </thead>
              <tbody>
                ${raisedRows || '<tr><td colspan="4" class="text-center text-muted py-3">Brak podwyżek tego dnia</td></tr>'}
              </tbody>
        </table>`;

        let loweredCellContent = '';
        let raisedCellContent = '';

        const downArrow = '<span style="color: green;">&#9660;</span>';
        const upArrow = '<span style="color: red;">&#9650;</span>';
        const grayCircle = '<span style="color: gray;">&#9679;</span>';

      
        if (r.lowered > 0) {
            loweredCellContent = `${downArrow} ${r.lowered}`;
        } else {
            loweredCellContent = `${grayCircle} 0`; 
        }


        if (r.raised > 0) {
            raisedCellContent = `${upArrow} ${r.raised}`;
        } else {
            raisedCellContent = `${grayCircle} 0`; 
        }

        tb.insertAdjacentHTML("beforeend", `
        <tr class="parent-row" data-target="${detailId}" style="cursor: pointer;">
          <td>
            <div class="${isWeekend ? "weekend-box" : "week-box"}">${fullDay}</div>
            ${r.date}
          </td>
          <td class="text-start">${loweredCellContent}</td>
          <td class="text-start">${raisedCellContent}</td>
        </tr>
        <tr id="${detailId}" class="details-row">
          <td colspan="3" class="p-0" style="border: none;">
             <div class="details-content">
               <div class="row no-gutters details-inner-row">
                 <div class="col-md-6" style="padding: 6px 3px 6px 12px;">
                   ${loweredTable}
                 </div>
                 <div class="col-md-6" style="padding: 6px 12px 6px 3px;">
                   ${raisedTable}
                 </div>
               </div>
             </div>
          </td>
        </tr>`);
    });

    tb.querySelectorAll("tr.parent-row").forEach(tr => {
        tr.addEventListener("click", () => toggleRowSmooth(tr.dataset.target));
    });
}





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


async function load(count) {
    if (!hubConnectionId) return;       

    showLoadingOverlay("Ładowanie danych…");

    const res = await fetch(
        `/Dashboard/GetDashboardData?storeId=${STORE_ID}&scraps=${count}&connectionId=${hubConnectionId}`
    );
    if (!res.ok) { console.error("fetch error"); hideLoadingOverlay(); return; }

    const data = await res.json();
    drawChart(data);
    buildTable(data);

  
    setTimeout(hideLoadingOverlay, 200);
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
