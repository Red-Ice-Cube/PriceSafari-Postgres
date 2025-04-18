
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
    const tb = document.querySelector(".table-price tbody");
    tb.innerHTML = "";

    rows.forEach((r, i) => {
        const detailId = `detail_${i}`;
        const weekend = ["Sat", "Sun", "sob.", "niedz."].includes(r.day);
        const dayBg = weekend ? "#FFC0CB" : "#D3D3D3";

        // helper do renderowania jednej komórki z obrazkiem + nazwą
        const renderNameCell = d => {
            const url = d.productImage || "";
            // jeśli url nie istnieje, to <img> nie zostanie wstrzyknięte
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

        /* --- wiersze obniżek --- */
        const loweredRows = r.loweredDetails.length
            ? r.loweredDetails.map(d => `
                <tr>
                  ${renderNameCell(d)}
                  <td>${d.oldPrice.toFixed(2)}</td>
                  <td>${d.newPrice.toFixed(2)}</td>
                  <td style="color:green;">${d.priceDifference.toFixed(2)}</td>
                </tr>
            `).join("")
            : `<tr><td colspan="4" class="text-center">Brak obniżek cen</td></tr>`;

        const loweredTable = `
            <table class="table table-sm inner-table">
              <thead>
                <tr>
                  <th>Produkt</th>
                  <th>Poprzednia cena</th>
                  <th>Nowa cena</th>
                  <th>Różnica</th>
                </tr>
              </thead>
              <tbody>
                ${loweredRows}
              </tbody>
            </table>`;

        /* --- wiersze podwyżek --- */
        const raisedRows = r.raisedDetails.length
            ? r.raisedDetails.map(d => `
                <tr>
                  ${renderNameCell(d)}
                  <td>${d.oldPrice.toFixed(2)}</td>
                  <td>${d.newPrice.toFixed(2)}</td>
                  <td style="color:red;">+${d.priceDifference.toFixed(2)}</td>
                </tr>
            `).join("")
            : `<tr><td colspan="4" class="text-center">Brak podwyżek cen</td></tr>`;

        const raisedTable = `
            <table class="table table-sm inner-table">
              <thead>
                <tr>
                  <th>Produkt</th>
                  <th>Poprzednia cena</th>
                  <th>Nowa cena</th>
                  <th>Różnica</th>
                </tr>
              </thead>
              <tbody>
                ${raisedRows}
              </tbody>
            </table>`;

        /* --- wiersze tabeli głównej --- */
        tb.insertAdjacentHTML("beforeend", `
<tr class="parent-row" data-target="${detailId}">
  <td>
    ${r.date}
    <span style="background:${dayBg};padding:2px 5px;">${r.day}</span>
  </td>
  <td>${r.lowered}</td>
  <td>${r.raised}</td>
</tr>
<tr id="${detailId}" class="details-row">
  <td colspan="4" class="p-0">
     <div class="details-content">
       <div class="row details-inner-row">
         <div class="col-md-6" style="margin-left:12px;">
           ${loweredTable}
         </div>
         <div class="col-md-6" style="margin-right:12px;">
           ${raisedTable}
         </div>
       </div>
     </div>
  </td>
</tr>`);
    });

    /* jedno wspólne zdarzenie */
    tb.querySelectorAll("tr.parent-row").forEach(tr => {
        tr.addEventListener("click", () => toggleRowSmooth(tr.dataset.target));
    });
}




/* ════════════════  ANIMACJA WYIERSZY  ════════════════ */
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

    // usuń podświetlenie od razu
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
    const res = await fetch(`/Dashboard/GetDashboardData?storeId=${STORE_ID}&scraps=${count}`);
    if (!res.ok) { console.error("fetch error"); return; }
    const data = await res.json();
    drawChart(data);
    buildTable(data);
}

document.addEventListener("DOMContentLoaded", () => {
    load(30);                                        
    document.querySelectorAll(".dateShortcut").forEach(btn => {
        btn.addEventListener("click", () => {
            document.querySelectorAll(".dateShortcut").forEach(b => b.classList.remove("active"));
            btn.classList.add("active");
            load(+btn.dataset.count);
        });
    });
});
